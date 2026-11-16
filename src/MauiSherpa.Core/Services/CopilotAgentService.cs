using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using GitHub.Copilot;
using MauiSherpa.Core.Interfaces;
using Microsoft.Extensions.AI;
using Shiny.Controls.AgentUI;
using Shiny.Controls.AgentUI.A2UI;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Connects the Copilot session to the Shiny AgentView. It owns the AG-UI conversation, answers the
/// session's permission and ask_user callbacks with inline prompts, and gives the agent picker tools and
/// a generated-UI (A2UI) tool.
/// </summary>
public sealed class CopilotAgentService : ICopilotAgentService
{
    const string ShinyCatalogId = "https://shinylib.net/a2ui/shiny.controls/v1.json";

    readonly ILoggingService _logger;
    readonly CopilotAgentSession _session;
    readonly HashSet<string> _uiToolNames;
    readonly HashSet<string> _answeredQuestionCalls = new(StringComparer.Ordinal);
    readonly List<JsonNode> _renderedOperations = new();
    readonly object _renderGate = new();

    public CopilotAgentService(ICopilotService copilot, ILoggingService logger)
    {
        _logger = logger;
        _session = new CopilotAgentSession(copilot, logger);
        Catalogs = new A2UICatalogRegistry(includeBasic: true).Add(A2UICatalog.CreateShiny());
        Prompts = new AgentPromptQueue();
        Runner = new AgentRunner(_session);
        Runner.Thread.CustomEventReceived += OnCustomEvent;

        var uiTools = CreateUiTools();
        _uiToolNames = uiTools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        copilot.AdditionalTools = uiTools;
        copilot.PermissionHandler = HandlePermissionAsync;
        copilot.UserInputHandler = HandleUserInputAsync;
    }

    public AgentRunner Runner { get; }
    public AgentPromptQueue Prompts { get; }
    public A2UICatalogRegistry Catalogs { get; }

    public void Reset()
    {
        Runner.Cancel();
        Runner.Thread.Clear();
        _session.Reset();
        _answeredQuestionCalls.Clear();
        lock (_renderGate)
            _renderedOperations.Clear();
    }

    void OnCustomEvent(object? sender, BaseEvent e)
    {
        if (e is CustomEvent { Name: CopilotAgentSession.ToolFailedEventName, Value: { } value })
        {
            var toolCallId = value.GetProperty("toolCallId").GetString();
            if (toolCallId != null)
                Runner.Thread.SetToolResult(toolCallId, value.GetProperty("result").GetString(), value.GetProperty("error").GetString());
        }
    }

    async Task<ToolPermissionResult> HandlePermissionAsync(ToolPermissionRequest request)
    {
        // Read-only tools and the UI tools (which only ask the user something) run without asking
        if (request.IsReadOnly || _uiToolNames.Contains(request.ToolName))
            return new ToolPermissionResult(true);

        var toolCallId = string.IsNullOrEmpty(request.ToolCallId) ? NewId() : request.ToolCallId;
        var message = string.IsNullOrWhiteSpace(request.ToolDescription)
            ? $"Copilot wants to run {FormatToolName(request.ToolName)}."
            : request.ToolDescription;
        if (!string.IsNullOrWhiteSpace(request.Command))
            message += $"\n\n$ {request.Command}";
        else if (!string.IsNullOrWhiteSpace(request.Path))
            message += $"\n\nPath: {request.Path}";

        var result = await _session.AskAsync(Prompts, toolCallId, request.ToolName, AgentPromptKinds.Confirm, new JsonObject
        {
            ["title"] = $"Allow {FormatToolName(request.ToolName)}?",
            ["message"] = message,
            ["confirmText"] = "Allow",
            ["cancelText"] = "Deny"
        });

        var confirmed = result.Error == null && ReadResult(result)?["confirmed"]?.GetValue<bool>() == true;
        _logger.LogDebug($"Permission for {request.ToolName} ({toolCallId}): {(confirmed ? "allowed" : "denied")}");
        return confirmed
            ? new ToolPermissionResult(true)
            : new ToolPermissionResult(false, "The user denied permission.");
    }

    async Task<CopilotUserInputResponse> HandleUserInputAsync(CopilotUserInputRequest request)
    {
        // The question belongs under the agent's ask_user card; fall back to a card of our own
        var toolCallId = _session.FindToolCall((id, name) =>
                name.Contains("ask", StringComparison.OrdinalIgnoreCase) && !_answeredQuestionCalls.Contains(id))
            ?? NewId();
        _answeredQuestionCalls.Add(toolCallId);

        if (request.Choices.Count > 0)
        {
            var choice = await _session.AskAsync(Prompts, toolCallId, "ask_user", AgentPromptKinds.ChooseOne, new JsonObject
            {
                ["title"] = request.Question,
                ["options"] = new JsonArray(request.Choices.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray())
            });
            return choice.Error == null
                ? new CopilotUserInputResponse(ReadResult(choice)?["choice"]?.GetValue<string>() ?? "", false)
                : new CopilotUserInputResponse("The user declined to answer.", true);
        }

        var text = await _session.AskAsync(Prompts, toolCallId, "ask_user", AgentPromptKinds.Text, new JsonObject
        {
            ["title"] = request.Question,
            ["multiline"] = true
        });
        return new CopilotUserInputResponse(
            text.Error == null ? ReadResult(text)?["text"]?.GetValue<string>() ?? "" : "The user declined to answer.",
            true);
    }

    List<AIFunction> CreateUiTools() =>
    [
        AIFunctionFactory.Create(
            ([Description("The question, shown above the options")] string title,
             [Description("The options to choose from")] string[] options,
             AIFunctionArguments arguments) =>
                AskToolAsync(arguments, AgentPromptKinds.ChooseOne, new JsonObject
                {
                    ["title"] = title,
                    ["options"] = ToJsonArray(options)
                }),
            "choose_one",
            "Ask the user to pick exactly one of a few options with buttons. Returns {\"choice\": string}."),

        AIFunctionFactory.Create(
            ([Description("The question, shown above the options")] string title,
             [Description("The options to choose from")] string[] options,
             [Description("The fewest options the user must pick")] int? min,
             [Description("The most options the user may pick")] int? max,
             AIFunctionArguments arguments) =>
                AskToolAsync(arguments, AgentPromptKinds.ChooseMany, new JsonObject
                {
                    ["title"] = title,
                    ["options"] = ToJsonArray(options),
                    ["min"] = min,
                    ["max"] = max
                }),
            "choose_many",
            "Ask the user to pick any number of options with chips (devices, certificates, packages). Returns {\"choices\": [string]}."),

        AIFunctionFactory.Create(
            ([Description("What the date is for")] string title,
             [Description("The earliest date allowed, YYYY-MM-DD")] string? min,
             [Description("The latest date allowed, YYYY-MM-DD")] string? max,
             AIFunctionArguments arguments) =>
                AskToolAsync(arguments, AgentPromptKinds.Date, new JsonObject { ["title"] = title, ["min"] = min, ["max"] = max }),
            "pick_date",
            "Ask the user to pick a date on a calendar. Returns {\"date\": \"YYYY-MM-DD\"}."),

        AIFunctionFactory.Create(
            ([Description("What the range is for")] string title,
             [Description("The earliest date allowed, YYYY-MM-DD")] string? min,
             [Description("The latest date allowed, YYYY-MM-DD")] string? max,
             AIFunctionArguments arguments) =>
                AskToolAsync(arguments, AgentPromptKinds.DateRange, new JsonObject { ["title"] = title, ["min"] = min, ["max"] = max }),
            "pick_date_range",
            "Ask the user to choose a date range. Returns {\"start\": \"YYYY-MM-DD\", \"end\": \"YYYY-MM-DD\"}."),

        AIFunctionFactory.Create(
            GetUiCatalog,
            "get_ui_catalog",
            "Returns the A2UI catalog (JSON Schema) of the components render_ui can draw. Read it once before your first render_ui call."),

        AIFunctionFactory.Create(
            ([Description("An array of A2UI v1.0 messages (createSurface, updateComponents, updateDataModel, deleteSurface), each with \"version\": \"1.0\"")] JsonElement messages,
             AIFunctionArguments arguments) =>
                RenderUi(messages, arguments),
            "render_ui",
            "Draws native UI in the chat from A2UI v1.0 messages, using the components from get_ui_catalog. " +
            "Validates the messages first and reports any errors to fix. Button actions come back as [UI action] messages."),
    ];

    async Task<string> AskToolAsync(AIFunctionArguments arguments, string kind, JsonObject promptArguments)
    {
        var invocation = arguments.Context?.TryGetValue(typeof(ToolInvocation), out var value) == true ? value as ToolInvocation : null;
        var toolCallId = invocation?.ToolCallId is { Length: > 0 } id ? id : NewId();

        var result = await _session.AskAsync(Prompts, toolCallId, invocation?.ToolName ?? kind, kind, promptArguments);
        return result.Error == null
            ? result.Content
            : new JsonObject { ["declined"] = true, ["reason"] = result.Error }.ToJsonString();
    }

    string GetUiCatalog() =>
        Catalogs.TryGet(ShinyCatalogId, out var catalog) ? catalog.Definition?.ToJsonString() ?? "{}" : "{}";

    string RenderUi(JsonElement messages, AIFunctionArguments arguments)
    {
        JsonNode? parsed;
        try
        {
            parsed = messages.ValueKind == JsonValueKind.String
                ? JsonNode.Parse(messages.GetString() ?? "")
                : JsonNode.Parse(messages.GetRawText());
        }
        catch (JsonException ex)
        {
            return $"Nothing was rendered: the messages aren't valid JSON ({ex.Message}).";
        }

        var operations = parsed as JsonArray ?? new JsonArray(parsed?.DeepClone());
        var errors = ValidateSurfaceOperations(operations);
        if (errors.Count > 0)
            return "Nothing was rendered. Fix these A2UI errors and call render_ui again:\n- " + string.Join("\n- ", errors);

        var invocation = arguments.Context?.TryGetValue(typeof(ToolInvocation), out var value) == true ? value as ToolInvocation : null;
        var content = new JsonObject { [A2UIAgentBinding.OperationsKey] = operations.DeepClone() };
        if (!_session.EmitActivity(A2UIAgentBinding.DefaultActivityType, content, invocation?.ToolCallId))
            return "Nothing was rendered: the Copilot chat isn't open.";

        lock (_renderGate)
            _renderedOperations.AddRange(operations.Select(o => o!.DeepClone()));

        var surfaces = operations
            .Select(o => o?["createSurface"]?["surfaceId"]?.GetValue<string>())
            .OfType<string>()
            .ToList();
        return surfaces.Count > 0
            ? $"Rendered surface(s) {string.Join(", ", surfaces)} in the chat. The user can see and interact with them now; " +
              "button presses come back to you as [UI action] messages."
            : "Applied the update to the existing surface(s).";
    }

    /// <summary>
    /// Applies the operations to a scratch host (after replaying what was already rendered, so updates to an
    /// existing surface resolve) and returns every error the engine reports, plus surfaces that can't draw.
    /// </summary>
    List<string> ValidateSurfaceOperations(JsonArray operations)
    {
        var host = new A2UISurfaceHost(Catalogs);
        lock (_renderGate)
        {
            foreach (var previous in _renderedOperations)
                host.Apply(previous!.DeepClone());
        }

        var errors = new List<string>();
        host.Outbound += (_, message) =>
        {
            if (message.Kind == A2UIOutboundKind.Error)
                errors.Add(message.Message["error"]?.ToJsonString() ?? message.Message.ToJsonString());
        };

        foreach (var operation in operations)
            host.Apply(operation?.DeepClone());

        var touchedSurfaces = operations
            .Select(o => (o as JsonObject)?.Where(p => p.Key != "version").Select(p => p.Value?["surfaceId"]?.GetValue<string>()).FirstOrDefault())
            .OfType<string>()
            .Distinct();

        foreach (var surface in touchedSurfaces.Select(host.GetSurface).OfType<A2UISurface>().Where(s => !s.IsDeleted))
        {
            if (surface.Root == null)
                errors.Add($"Surface '{surface.SurfaceId}' has no component with the id 'root', so nothing would show.");

            errors.AddRange(surface.Components.Values
                .Where(c => surface.GetDefinition(c) == null)
                .Select(c => $"Component '{c.Id}' has the unknown type '{c.Type}'. Use only the components from get_ui_catalog."));
        }
        return errors;
    }

    static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

    static JsonNode? ReadResult(AgentToolResult result)
    {
        try
        {
            return JsonNode.Parse(result.Content);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string FormatToolName(string toolName) =>
        string.Join(" ", toolName.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    static string NewId() => Guid.NewGuid().ToString("N");
}
