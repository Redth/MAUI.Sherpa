using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using AGUI.Abstractions;
using GitHub.Copilot;
using MauiSherpa.Core.Interfaces;
using Shiny.Controls.AgentUI;

namespace MauiSherpa.Core.Services;

/// <summary>
/// An AG-UI session over the Copilot SDK session that <see cref="ICopilotService"/> owns. Each run sends one
/// prompt and translates the SDK's session events into AG-UI events until the session goes idle.
/// </summary>
/// <remarks>
/// The Copilot session keeps its own history, so only the newest user message (or a UI action) is sent and
/// the history the runner puts in <see cref="RunAgentInput.Messages"/> is ignored. SDK events arrive on
/// background threads; they are written to a channel the runner reads on the UI thread.
/// </remarks>
public sealed class CopilotAgentSession : IAgentSession
{
    /// <summary>
    /// CUSTOM event raised when a tool call fails, so the thread can mark its card failed
    /// (AG-UI's TOOL_CALL_RESULT has no error field).
    /// </summary>
    public const string ToolFailedEventName = "sherpa.tool_failed";

    // Internal bookkeeping tools the old chat UI also hid
    static readonly HashSet<string> HiddenTools = new(StringComparer.Ordinal) { "report_intent", "skill", "update_todo" };

    readonly ICopilotService _copilot;
    readonly ILoggingService _logger;
    readonly object _gate = new();
    readonly HashSet<string> _sentMessageIds = new(StringComparer.Ordinal);
    RunState? _run;
    string? _lastPrompt;

    public CopilotAgentSession(ICopilotService copilot, ILoggingService logger)
    {
        _copilot = copilot;
        _logger = logger;
        _copilot.OnSessionEvent += OnSessionEvent;
    }

    public string ThreadId => "copilot";

    public async IAsyncEnumerable<BaseEvent> RunAsync(RunAgentInput input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The runner calls this on the UI thread, so this is the context prompts must be raised on
        var run = new RunState(ThreadId, input.RunId, SynchronizationContext.Current, cancellationToken);
        lock (_gate)
        {
            _run?.Complete();
            _run = run;
        }
        run.Emit(new RunStartedEvent { ThreadId = ThreadId, RunId = input.RunId });

        var prompt = BuildPrompt(input);
        if (prompt == null)
        {
            lock (_gate)
            {
                run.Finish();
                _run = null;
            }
        }
        else
        {
            _ = SendAsync(run, prompt);
        }

        using var abort = cancellationToken.Register(() => _ = _copilot.AbortAsync());
        try
        {
            await foreach (var e in run.Reader.ReadAllAsync(cancellationToken))
                yield return e;
        }
        finally
        {
            lock (_gate)
            {
                if (_run == run)
                    _run = null;
            }
        }
    }

    /// <summary>
    /// Clears what the session remembers about the conversation (after the chat is cleared).
    /// </summary>
    public void Reset()
    {
        lock (_gate)
        {
            _sentMessageIds.Clear();
            _lastPrompt = null;
        }
    }

    /// <summary>
    /// Asks the user through an inline prompt drawn under a tool call's card, creating the card when the
    /// agent hasn't announced the call yet. Returns a failed result when no chat is running to answer it.
    /// </summary>
    public async Task<AgentToolResult> AskAsync(AgentPromptQueue prompts, string toolCallId, string toolName, string kind, JsonObject arguments)
    {
        RunState? run;
        lock (_gate)
        {
            run = _run;
            if (run != null)
                EnsureToolCard(run, toolCallId, toolName, null, null);
        }

        if (run == null)
            return AgentToolResult.Failure("The Copilot chat isn't open, so the user can't answer.");

        try
        {
            return await OnUiAsync(run.UiContext, () => prompts.AskAsync(toolCallId, kind, arguments, run.Token).AsTask());
        }
        catch (OperationCanceledException)
        {
            return AgentToolResult.Failure("The user cancelled.");
        }
    }

    /// <summary>
    /// The id of the most recent tool call in the current run that matches (by id and name), or null.
    /// </summary>
    public string? FindToolCall(Func<string, string, bool> matches)
    {
        lock (_gate)
        {
            return _run?.ToolNames.LastOrDefault(t => matches(t.Id, t.Name)).Id;
        }
    }

    /// <summary>
    /// Emits an activity (an A2UI surface) into the current run. Returns false when nothing is running.
    /// </summary>
    public bool EmitActivity(string activityType, JsonNode content, string? parentToolCallId)
    {
        lock (_gate)
        {
            var run = _run;
            if (run == null)
                return false;

            run.Emit(new ActivitySnapshotEvent
            {
                MessageId = NewId(),
                ActivityType = activityType,
                Content = JsonSerializer.SerializeToElement(content),
                SubagentRunId = run.SubagentFor(parentToolCallId)
            });
            return true;
        }
    }

    async Task SendAsync(RunState run, string prompt)
    {
        try
        {
            await _copilot.SendMessageAsync(prompt);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Copilot send failed: {ex.Message}", ex);
            lock (_gate)
            {
                run.Fail(ex.Message, ex.GetType().Name);
                if (_run == run)
                    _run = null;
            }
        }
    }

    string? BuildPrompt(RunAgentInput input)
    {
        var forwarded = input.ForwardedProperties is { ValueKind: JsonValueKind.Object } props
            ? JsonNode.Parse(props.GetRawText()) as JsonObject
            : null;

        if (forwarded?["a2uiAction"]?["userAction"] is JsonObject action)
            return _lastPrompt = DescribeAction(action, forwarded["a2uiRendererDataModel"]);

        if (forwarded?["a2uiClientMessages"] is JsonArray { Count: > 0 } clientMessages)
            return "The generated UI reported these errors. Fix the surface with render_ui:\n" + clientMessages.ToJsonString();

        var user = input.Messages.OfType<AGUIUserMessage>().LastOrDefault();
        lock (_gate)
        {
            if (user != null && _sentMessageIds.Add(user.Id))
                return _lastPrompt = TextOf(user.Content);
        }

        // No new message: a Retry, so send the last prompt again
        return _lastPrompt;
    }

    static string DescribeAction(JsonObject action, JsonNode? dataModel)
    {
        var text = $"[UI action] The user triggered \"{action["name"]}\" on generated UI surface \"{action["surfaceId"]}\" " +
                   $"(component \"{action["sourceComponentId"]}\").";
        if (action["context"] is JsonObject { Count: > 0 } context)
            text += "\nAction context: " + context.ToJsonString();
        if (dataModel != null)
            text += "\nSurface data model: " + dataModel.ToJsonString();
        return text + "\nCarry out what the user asked for, using your tools where needed.";
    }

    static string TextOf(AGUIContent content)
    {
        if (content.Value is string text)
            return text;
        return string.Join("\n", content.OfType<AGUITextInputContent>().Select(p => p.Text));
    }

    void OnSessionEvent(SessionEvent evt)
    {
        lock (_gate)
        {
            var run = _run;
            if (run == null)
                return;

            switch (evt)
            {
                case UserMessageEvent:
                case AssistantTurnStartEvent:
                    run.HasStarted = true;
                    break;

                case AssistantMessageDeltaEvent delta:
                    run.HasStarted = true;
                    AppendText(run, delta.Data.MessageId, delta.Data.DeltaContent, delta.Data.ParentToolCallId);
                    break;

                case AssistantMessageEvent message:
                    run.HasStarted = true;
                    CompleteText(run, message.Data.MessageId, message.Data.Content, message.Data.ParentToolCallId);
                    AnnounceToolRequests(run, message.Data.ToolRequests, message.Data.ParentToolCallId);
                    break;

                case AssistantReasoningDeltaEvent reasoningDelta:
                    AppendReasoning(run, reasoningDelta.Data.ReasoningId, reasoningDelta.Data.DeltaContent);
                    break;

                case AssistantReasoningEvent reasoning:
                    CompleteReasoning(run, reasoning.Data.ReasoningId, reasoning.Data.Content);
                    break;

                case AssistantIntentEvent intent:
                    SetStep(run, intent.Data?.Intent);
                    break;

                case ToolExecutionStartEvent toolStart:
                    StartTool(run, toolStart.Data);
                    break;

                case ToolExecutionCompleteEvent toolComplete:
                    CompleteTool(run, toolComplete.Data);
                    break;

                case GitHub.Copilot.SubagentStartedEvent subagentStarted:
                    run.Subagents[subagentStarted.Data.ToolCallId] = subagentStarted.Data.ToolCallId;
                    run.Emit(new AGUI.Abstractions.SubagentStartedEvent
                    {
                        SubagentRunId = subagentStarted.Data.ToolCallId,
                        Name = subagentStarted.Data.AgentDisplayName,
                        Description = subagentStarted.Data.AgentDescription,
                        ParentToolCallId = subagentStarted.Data.ToolCallId
                    });
                    break;

                case SubagentCompletedEvent subagentCompleted:
                    run.Emit(new SubagentFinishedEvent
                    {
                        SubagentRunId = subagentCompleted.Data.ToolCallId,
                        Outcome = new SubagentFinishedSuccessOutcome()
                    });
                    break;

                case SubagentFailedEvent subagentFailed:
                    run.Emit(new SubagentErrorEvent
                    {
                        SubagentRunId = subagentFailed.Data.ToolCallId,
                        Message = subagentFailed.Data.Error
                    });
                    break;

                case AssistantUsageEvent usage:
                    run.InputTokens += usage.Data.InputTokens ?? 0;
                    run.OutputTokens += usage.Data.OutputTokens ?? 0;
                    run.Model = usage.Data.Model ?? run.Model;
                    break;

                case SessionErrorEvent error:
                    run.Fail(error.Data?.Message ?? "Unknown error", error.Data?.ErrorType);
                    _run = null;
                    break;

                // An idle left over from an aborted turn can arrive before this run's turn starts
                case SessionIdleEvent when run.HasStarted:
                    run.Finish();
                    _run = null;
                    break;
            }
        }
    }

    static void AppendText(RunState run, string messageId, string? delta, string? parentToolCallId)
    {
        if (string.IsNullOrEmpty(delta) || run.ClosedMessages.Contains(messageId))
            return;

        run.CloseReasoning();
        var subagent = run.SubagentFor(parentToolCallId);
        if (run.OpenMessages.Add(messageId))
            run.Emit(new TextMessageStartEvent { MessageId = messageId, Role = AGUIRoles.Assistant, SubagentRunId = subagent });
        run.Emit(new TextMessageContentEvent { MessageId = messageId, Delta = delta, SubagentRunId = subagent });
    }

    static void CompleteText(RunState run, string messageId, string? content, string? parentToolCallId)
    {
        // The final message carries the whole text; only use it when nothing was streamed
        if (!run.OpenMessages.Contains(messageId))
            AppendText(run, messageId, content, parentToolCallId);

        if (run.OpenMessages.Remove(messageId))
            run.Emit(new TextMessageEndEvent { MessageId = messageId, SubagentRunId = run.SubagentFor(parentToolCallId) });
        run.ClosedMessages.Add(messageId);
    }

    static void AnnounceToolRequests(RunState run, AssistantMessageToolRequest[]? requests, string? parentToolCallId)
    {
        // Draw the cards (with their arguments) as soon as the model asks for them, so a permission prompt
        // that arrives before the tool starts has a card to sit under
        foreach (var request in (requests ?? []).Where(r => !HiddenTools.Contains(r.Name)))
            EnsureToolCard(run, request.ToolCallId, request.Name, request.Arguments?.GetRawText(), run.SubagentFor(parentToolCallId));
    }

    static void AppendReasoning(RunState run, string reasoningId, string? delta)
    {
        if (string.IsNullOrEmpty(delta) || run.ClosedReasoning.Contains(reasoningId))
            return;

        if (run.OpenReasoning.Add(reasoningId))
        {
            run.Emit(new ReasoningStartEvent { MessageId = reasoningId });
            run.Emit(new ReasoningMessageStartEvent { MessageId = reasoningId, Role = AGUIRoles.Reasoning });
        }
        run.Emit(new ReasoningMessageContentEvent { MessageId = reasoningId, Delta = delta });
    }

    static void CompleteReasoning(RunState run, string reasoningId, string? content)
    {
        if (!run.OpenReasoning.Contains(reasoningId))
            AppendReasoning(run, reasoningId, content);
        run.EndReasoning(reasoningId);
    }

    static void SetStep(RunState run, string? intent)
    {
        if (string.IsNullOrWhiteSpace(intent) || intent == run.Step)
            return;

        if (run.Step != null)
            run.Emit(new StepFinishedEvent { StepName = run.Step });
        run.Emit(new StepStartedEvent { StepName = intent });
        run.Step = intent;
    }

    static void StartTool(RunState run, ToolExecutionStartData data)
    {
        if (HiddenTools.Contains(data.ToolName))
        {
            run.HiddenToolCalls.Add(data.ToolCallId);
            return;
        }

        run.CloseReasoning();
        EnsureToolCard(run, data.ToolCallId, data.ToolName, data.Arguments?.GetRawText(), run.SubagentFor(data.ParentToolCallId));
    }

    static void CompleteTool(RunState run, ToolExecutionCompleteData data)
    {
        if (run.HiddenToolCalls.Contains(data.ToolCallId))
            return;

        var subagent = run.SubagentFor(data.ParentToolCallId);
        EnsureToolCard(run, data.ToolCallId, "tool", null, subagent);

        var content = data.Result?.Content ?? data.Error?.Message ?? "";
        run.Emit(new ToolCallResultEvent
        {
            MessageId = NewId(),
            ToolCallId = data.ToolCallId,
            Content = content,
            Role = AGUIRoles.Tool,
            SubagentRunId = subagent
        });

        if (!data.Success)
        {
            run.Emit(new CustomEvent
            {
                Name = ToolFailedEventName,
                Value = JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["toolCallId"] = data.ToolCallId,
                    ["result"] = content,
                    ["error"] = data.Error?.Message ?? "The tool failed."
                })
            });
        }
    }

    static void EnsureToolCard(RunState run, string toolCallId, string toolName, string? argumentsJson, string? subagent)
    {
        if (!run.ToolCards.Add(toolCallId))
            return;

        run.ToolNames.Add((toolCallId, toolName));
        run.Emit(new ToolCallStartEvent { ToolCallId = toolCallId, ToolCallName = toolName, SubagentRunId = subagent });
        if (!string.IsNullOrEmpty(argumentsJson))
            run.Emit(new ToolCallArgsEvent { ToolCallId = toolCallId, Delta = argumentsJson, SubagentRunId = subagent });
        run.Emit(new ToolCallEndEvent { ToolCallId = toolCallId, SubagentRunId = subagent });
    }

    static Task<T> OnUiAsync<T>(SynchronizationContext? context, Func<Task<T>> work)
    {
        if (context == null || SynchronizationContext.Current == context)
            return work();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ =>
        {
            try
            {
                tcs.TrySetResult(await work());
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// What one run has streamed so far, so events can be opened and closed in order.
    /// </summary>
    sealed class RunState(string threadId, string runId, SynchronizationContext? uiContext, CancellationToken token)
    {
        readonly Channel<BaseEvent> _channel = Channel.CreateUnbounded<BaseEvent>(new UnboundedChannelOptions { SingleReader = true });

        public SynchronizationContext? UiContext { get; } = uiContext;
        public CancellationToken Token { get; } = token;
        public ChannelReader<BaseEvent> Reader => _channel.Reader;

        public bool HasStarted { get; set; }
        public string? Step { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public string? Model { get; set; }

        public HashSet<string> OpenMessages { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ClosedMessages { get; } = new(StringComparer.Ordinal);
        public HashSet<string> OpenReasoning { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ClosedReasoning { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ToolCards { get; } = new(StringComparer.Ordinal);
        public List<(string Id, string Name)> ToolNames { get; } = new();
        public HashSet<string> HiddenToolCalls { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Subagents { get; } = new(StringComparer.Ordinal);

        public void Emit(BaseEvent e) => _channel.Writer.TryWrite(e);

        public void Complete() => _channel.Writer.TryComplete();

        public string? SubagentFor(string? parentToolCallId) =>
            parentToolCallId != null && Subagents.TryGetValue(parentToolCallId, out var subagent) ? subagent : null;

        public void EndReasoning(string reasoningId)
        {
            if (OpenReasoning.Remove(reasoningId))
            {
                Emit(new ReasoningMessageEndEvent { MessageId = reasoningId });
                Emit(new ReasoningEndEvent { MessageId = reasoningId });
            }
            ClosedReasoning.Add(reasoningId);
        }

        public void CloseReasoning()
        {
            foreach (var reasoningId in OpenReasoning.ToList())
                EndReasoning(reasoningId);
        }

        public void Finish()
        {
            CloseAll();
            Emit(new RunFinishedEvent
            {
                ThreadId = threadId,
                RunId = runId,
                Outcome = new RunFinishedSuccessOutcome(),
                Usage = InputTokens + OutputTokens > 0
                    ? [new TokenUsage { Model = Model, InputTokens = InputTokens, OutputTokens = OutputTokens, TotalTokens = InputTokens + OutputTokens }]
                    : null
            });
            Complete();
        }

        public void Fail(string message, string? code)
        {
            CloseAll();
            Emit(new RunErrorEvent { Message = message, Code = code });
            Complete();
        }

        void CloseAll()
        {
            CloseReasoning();
            foreach (var messageId in OpenMessages.ToList())
                Emit(new TextMessageEndEvent { MessageId = messageId });
            OpenMessages.Clear();
            if (Step != null)
                Emit(new StepFinishedEvent { StepName = Step });
            Step = null;
        }
    }
}
