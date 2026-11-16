using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using FluentAssertions;
using GitHub.Copilot;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Microsoft.Extensions.AI;
using Moq;
using Shiny.Controls.AgentUI;

namespace MauiSherpa.Core.Tests.Services;

public class CopilotAgentSessionTests
{
    readonly Mock<ICopilotService> _copilot = new();
    readonly Mock<ILoggingService> _logger = new();
    readonly List<string> _sentPrompts = new();

    CopilotAgentSession CreateSession(params SessionEvent[] events)
    {
        _copilot
            .Setup(c => c.SendMessageAsync(It.IsAny<string>()))
            .Returns((string prompt) =>
            {
                _sentPrompts.Add(prompt);
                foreach (var e in events)
                    _copilot.Raise(c => c.OnSessionEvent += null, e);
                return Task.CompletedTask;
            });
        return new CopilotAgentSession(_copilot.Object, _logger.Object);
    }

    static RunAgentInput UserTurn(string id, string text) => new()
    {
        ThreadId = "copilot",
        RunId = Guid.NewGuid().ToString("N"),
        Messages = [new AGUIUserMessage { Id = id, Content = text }]
    };

    static async Task<List<BaseEvent>> CollectAsync(CopilotAgentSession session, RunAgentInput input)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new List<BaseEvent>();
        await foreach (var e in session.RunAsync(input, timeout.Token))
            events.Add(e);
        return events;
    }

    static SessionEvent TurnStart() => new AssistantTurnStartEvent { Data = new AssistantTurnStartData { TurnId = "t1" } };
    static SessionEvent Idle() => new SessionIdleEvent { Data = new SessionIdleData() };

    [Fact]
    public async Task StreamedReply_BecomesOneTextMessageInsideTheRun()
    {
        var session = CreateSession(
            TurnStart(),
            new AssistantMessageDeltaEvent { Data = new AssistantMessageDeltaData { MessageId = "m1", DeltaContent = "Hel" } },
            new AssistantMessageDeltaEvent { Data = new AssistantMessageDeltaData { MessageId = "m1", DeltaContent = "lo" } },
            new AssistantMessageEvent { Data = new AssistantMessageData { MessageId = "m1", Content = "Hello" } },
            Idle());

        var events = await CollectAsync(session, UserTurn("u1", "Hi there"));

        _sentPrompts.Should().Equal("Hi there");
        events.Select(e => e.GetType().Name).Should().Equal(
            nameof(RunStartedEvent),
            nameof(TextMessageStartEvent),
            nameof(TextMessageContentEvent),
            nameof(TextMessageContentEvent),
            nameof(TextMessageEndEvent),
            nameof(RunFinishedEvent));
        string.Concat(events.OfType<TextMessageContentEvent>().Select(e => e.Delta)).Should().Be("Hello");
    }

    [Fact]
    public async Task FinalMessageWithoutDeltas_IsStillShown()
    {
        var session = CreateSession(
            TurnStart(),
            new AssistantMessageEvent { Data = new AssistantMessageData { MessageId = "m1", Content = "Done." } },
            Idle());

        var events = await CollectAsync(session, UserTurn("u1", "Go"));

        events.OfType<TextMessageContentEvent>().Single().Delta.Should().Be("Done.");
        events.OfType<TextMessageEndEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task ToolCalls_AreCardsWithArgumentsAndResults_AndFailuresAreFlagged()
    {
        var session = CreateSession(
            TurnStart(),
            new ToolExecutionStartEvent { Data = new ToolExecutionStartData { ToolCallId = "intent", ToolName = "report_intent" } },
            new ToolExecutionCompleteEvent { Data = new ToolExecutionCompleteData { ToolCallId = "intent", Success = true } },
            new ToolExecutionStartEvent
            {
                Data = new ToolExecutionStartData
                {
                    ToolCallId = "c1",
                    ToolName = "revoke_certificate",
                    Arguments = JsonSerializer.SerializeToElement(new { serial = "123" })
                }
            },
            new ToolExecutionCompleteEvent
            {
                Data = new ToolExecutionCompleteData
                {
                    ToolCallId = "c1",
                    Success = false,
                    Error = new ToolExecutionCompleteError { Message = "Not allowed" }
                }
            },
            Idle());

        var events = await CollectAsync(session, UserTurn("u1", "Revoke it"));

        events.OfType<ToolCallStartEvent>().Should().ContainSingle().Which.ToolCallName.Should().Be("revoke_certificate");
        events.OfType<ToolCallArgsEvent>().Single().Delta.Should().Contain("123");
        events.OfType<ToolCallResultEvent>().Single().ToolCallId.Should().Be("c1");

        var failed = events.OfType<CustomEvent>().Single();
        failed.Name.Should().Be(CopilotAgentSession.ToolFailedEventName);
        failed.Value!.Value.GetProperty("error").GetString().Should().Be("Not allowed");
    }

    [Fact]
    public async Task Reasoning_IsClosedBeforeTheReplyStarts()
    {
        var session = CreateSession(
            TurnStart(),
            new AssistantReasoningDeltaEvent { Data = new AssistantReasoningDeltaData { ReasoningId = "r1", DeltaContent = "Thinking" } },
            new AssistantMessageDeltaEvent { Data = new AssistantMessageDeltaData { MessageId = "m1", DeltaContent = "Answer" } },
            Idle());

        var names = (await CollectAsync(session, UserTurn("u1", "Why?"))).Select(e => e.GetType().Name).ToList();

        names.IndexOf(nameof(ReasoningEndEvent)).Should().BeLessThan(names.IndexOf(nameof(TextMessageStartEvent)));
        names.IndexOf(nameof(TextMessageEndEvent)).Should().BeLessThan(names.IndexOf(nameof(RunFinishedEvent)));
    }

    [Fact]
    public async Task SessionError_FailsTheRun()
    {
        var session = CreateSession(
            TurnStart(),
            new SessionErrorEvent { Data = new SessionErrorData { ErrorType = "rate_limit", Message = "Slow down" } });

        var events = await CollectAsync(session, UserTurn("u1", "Hi"));

        var error = events.OfType<RunErrorEvent>().Single();
        error.Message.Should().Be("Slow down");
        error.Code.Should().Be("rate_limit");
        events.Last().Should().BeOfType<RunErrorEvent>();
    }

    [Fact]
    public async Task Retry_ResendsTheLastPrompt()
    {
        var session = CreateSession(TurnStart(), Idle());
        var input = UserTurn("u1", "Check my setup");

        await CollectAsync(session, input);
        await CollectAsync(session, new RunAgentInput { ThreadId = "copilot", RunId = "retry", Messages = input.Messages });

        _sentPrompts.Should().Equal("Check my setup", "Check my setup");
    }

    [Fact]
    public async Task A2UIAction_IsSentAsAUiActionPrompt()
    {
        var session = CreateSession(TurnStart(), Idle());
        var forwarded = new JsonObject
        {
            ["a2uiAction"] = new JsonObject
            {
                ["userAction"] = new JsonObject
                {
                    ["name"] = "install_package",
                    ["surfaceId"] = "sdk",
                    ["sourceComponentId"] = "installBtn",
                    ["context"] = new JsonObject { ["path"] = "platforms;android-35" }
                }
            }
        };

        await CollectAsync(session, new RunAgentInput
        {
            ThreadId = "copilot",
            RunId = "action",
            ForwardedProperties = JsonSerializer.SerializeToElement(forwarded)
        });

        _sentPrompts.Should().ContainSingle()
            .Which.Should().StartWith("[UI action]").And.Contain("install_package").And.Contain("platforms;android-35");
    }

    [Fact]
    public async Task CardOpenedForAPrompt_IsFilledByTheLaterAnnouncement()
    {
        var prompts = new AgentPromptQueue();
        prompts.Changed += (_, _) => prompts.Pending.FirstOrDefault()?.Submit(new JsonObject { ["confirmed"] = true });
        CopilotAgentSession? session = null;
        _copilot
            .Setup(c => c.SendMessageAsync(It.IsAny<string>()))
            .Returns(async () =>
            {
                _copilot.Raise(c => c.OnSessionEvent += null, TurnStart());
                // Permission is asked before the tool announces itself
                await session!.AskAsync(prompts, "c1", "delete_bundle_id", AgentPromptKinds.Confirm, new JsonObject { ["message"] = "Delete?" });
                _copilot.Raise(c => c.OnSessionEvent += null, (SessionEvent)new ToolExecutionStartEvent
                {
                    Data = new ToolExecutionStartData
                    {
                        ToolCallId = "c1",
                        ToolName = "delete_bundle_id",
                        Arguments = JsonSerializer.SerializeToElement(new { identifier = "com.example.app" })
                    }
                });
                _copilot.Raise(c => c.OnSessionEvent += null, Idle());
            });
        session = new CopilotAgentSession(_copilot.Object, _logger.Object);

        var events = await CollectAsync(session, UserTurn("u1", "Delete it"));

        events.OfType<ToolCallStartEvent>().Should().ContainSingle();
        events.OfType<ToolCallArgsEvent>().Single().Delta.Should().Contain("com.example.app");
        var names = events.Select(e => e.GetType().Name).ToList();
        names.IndexOf(nameof(ToolCallArgsEvent)).Should().BeLessThan(names.IndexOf(nameof(ToolCallEndEvent)));
        events.OfType<ToolCallEndEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task StaleIdleBeforeTheTurnStarts_DoesNotEndTheRun()
    {
        var session = CreateSession(
            Idle(),
            TurnStart(),
            new AssistantMessageEvent { Data = new AssistantMessageData { MessageId = "m1", Content = "Still here" } },
            Idle());

        var events = await CollectAsync(session, UserTurn("u1", "Hi"));

        events.OfType<TextMessageContentEvent>().Single().Delta.Should().Be("Still here");
    }
}

public class CopilotAgentServiceTests
{
    readonly Mock<ICopilotService> _copilot = new();
    readonly Mock<ILoggingService> _logger = new();

    AIFunction GetTool(string name)
    {
        _copilot.SetupProperty(c => c.AdditionalTools, []);
        _ = new CopilotAgentService(_copilot.Object, _logger.Object);
        return _copilot.Object.AdditionalTools.Single(t => t.Name == name);
    }

    static async Task<string> InvokeAsync(AIFunction tool, string messagesJson)
    {
        var result = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["messages"] = JsonDocument.Parse(messagesJson).RootElement.Clone()
        });
        return result?.ToString() ?? "";
    }

    [Fact]
    public void RegistersPickerAndGeneratedUiTools()
    {
        _copilot.SetupProperty(c => c.AdditionalTools, []);
        _ = new CopilotAgentService(_copilot.Object, _logger.Object);

        _copilot.Object.AdditionalTools.Select(t => t.Name).Should().BeEquivalentTo(
            "choose_one", "choose_many", "pick_date", "pick_date_range", "get_ui_catalog", "render_ui");
        _copilot.VerifySet(c => c.PermissionHandler = It.IsNotNull<Func<ToolPermissionRequest, Task<ToolPermissionResult>>>());
        _copilot.VerifySet(c => c.UserInputHandler = It.IsNotNull<Func<CopilotUserInputRequest, Task<CopilotUserInputResponse>>>());
    }

    [Fact]
    public async Task AskUser_WithChoicesAndFreeform_LetsTheUserTypeTheirOwnAnswer()
    {
        _copilot.SetupProperty(c => c.AdditionalTools, []);
        _copilot.SetupProperty(c => c.UserInputHandler);
        var service = new CopilotAgentService(_copilot.Object, _logger.Object);
        service.Prompts.Changed += (_, _) =>
        {
            var prompt = service.Prompts.Pending.FirstOrDefault();
            if (prompt?.Kind == AgentPromptKinds.ChooseOne)
            {
                prompt.List("options").Should().EndWith("Something else…");
                prompt.Submit(new JsonObject { ["choice"] = "Something else…" });
            }
            else
            {
                prompt?.Submit(new JsonObject { ["text"] = "Pixel 9 Pro" });
            }
        };

        CopilotUserInputResponse? answer = null;
        _copilot
            .Setup(c => c.SendMessageAsync(It.IsAny<string>()))
            .Returns(async () =>
            {
                _copilot.Raise(c => c.OnSessionEvent += null, (SessionEvent)new AssistantTurnStartEvent { Data = new AssistantTurnStartData { TurnId = "t1" } });
                answer = await _copilot.Object.UserInputHandler!(new CopilotUserInputRequest("Which device?", ["Pixel 8", "Pixel Tablet"], true));
                _copilot.Raise(c => c.OnSessionEvent += null, (SessionEvent)new SessionIdleEvent { Data = new SessionIdleData() });
            });

        await service.Runner.SendAsync("Create an emulator");

        answer.Should().Be(new CopilotUserInputResponse("Pixel 9 Pro", true));
    }

    [Fact]
    public async Task RenderUi_ReportsSurfacesWithoutARootAndUnknownComponents()
    {
        var result = await InvokeAsync(GetTool("render_ui"), """
            [
              {"version": "1.0", "createSurface": {"surfaceId": "s1", "catalogId": "https://shinylib.net/a2ui/shiny.controls/v1.json"}},
              {"version": "1.0", "updateComponents": {"surfaceId": "s1", "components": [
                {"id": "main", "component": "Sparkline", "values": [1, 2, 3]}
              ]}}
            ]
            """);

        result.Should().StartWith("Nothing was rendered");
        result.Should().Contain("'root'");
        result.Should().Contain("Sparkline");
    }

    [Fact]
    public async Task RenderUi_ReportsTheWrongProtocolVersion()
    {
        var result = await InvokeAsync(GetTool("render_ui"), """
            [{"version": "v1.0", "createSurface": {"surfaceId": "s1", "catalogId": "https://shinylib.net/a2ui/shiny.controls/v1.json"}}]
            """);

        result.Should().StartWith("Nothing was rendered").And.Contain("version");
    }

    [Fact]
    public async Task RenderUi_WithValidMessagesButNoOpenChat_SaysSo()
    {
        var result = await InvokeAsync(GetTool("render_ui"), """
            [
              {"version": "1.0", "createSurface": {"surfaceId": "s1", "catalogId": "https://shinylib.net/a2ui/shiny.controls/v1.json"}},
              {"version": "1.0", "updateComponents": {"surfaceId": "s1", "components": [
                {"id": "root", "component": "Text", "text": "Hello"}
              ]}}
            ]
            """);

        result.Should().Contain("isn't open");
    }

    [Fact]
    public async Task GetUiCatalog_ReturnsTheShinyCatalog()
    {
        var result = await GetTool("get_ui_catalog").InvokeAsync(new AIFunctionArguments());

        result!.ToString().Should().Contain("DataGrid").And.Contain("https://shinylib.net/a2ui/shiny.controls/v1.json");
    }
}
