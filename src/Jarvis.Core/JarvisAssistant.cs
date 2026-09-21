namespace Jarvis.Core;

public sealed class JarvisAssistant
{
    private readonly ToolContext _toolContext;
    private readonly IMemoryStore _memoryStore;
    private readonly ITranscriptStore _transcriptStore;
    private readonly IAgentStateStore _agentStateStore;
    private readonly IReadOnlyList<IAssistantTool> _tools;
    private readonly IAssistantPlanner _planner;
    private readonly IPendingApprovalStore _pendingApprovalStore;
    private readonly ToolSafetyService _toolSafety;
    private readonly IUserStateProvider _userStateProvider;
    private readonly ShortTermConversationState _conversationState = new();

    public JarvisAssistant(
        JarvisOptions options,
        IMemoryStore memoryStore,
        IDesktopAutomationService desktopAutomation,
        IWeatherService weatherService,
        IUserStateProvider userStateProvider,
        IWebResearchService webResearchService,
        IVisualContextService visualContextService,
        ITranscriptStore transcriptStore,
        IAgentStateStore agentStateStore,
        IAssistantPlanner planner,
        IPendingApprovalStore pendingApprovalStore,
        ToolSafetyService toolSafety,
        string workspaceRoot)
    {
        Options = options;
        _agentStateStore = agentStateStore;
        _planner = planner;
        _memoryStore = memoryStore;
        _pendingApprovalStore = pendingApprovalStore;
        _toolSafety = toolSafety;
        _userStateProvider = userStateProvider;
        _toolContext = new ToolContext(
            options,
            memoryStore,
            transcriptStore,
            desktopAutomation,
            weatherService,
            userStateProvider,
            webResearchService,
            visualContextService,
            workspaceRoot,
            planner.IsAvailable,
            planner.Status,
            ExecuteAutomationCommandFromContextAsync);
        _transcriptStore = transcriptStore;

        var tools = new List<IAssistantTool>();
        tools.Add(new HelpTool(() => tools));
        tools.Add(new StatusTool());
        tools.Add(new TimeTool());
        tools.Add(new RememberTool());
        tools.Add(new RecallTool());
        tools.Add(new ListMemoryTool());
        tools.Add(new ReminderTool());
        tools.Add(new ForgetMemoryTool());
        tools.Add(new MemoryProfileTool());
        tools.Add(new MemoryTool());
        tools.Add(new PrivacyTool());
        tools.Add(new ExportMemoriesTool());
        tools.Add(new ImportMemoriesTool());
        tools.Add(new ExportTranscriptsTool());
        tools.Add(new ImportTranscriptsTool());
        tools.Add(new WeatherTool());
        tools.Add(new SystemStateTool());
        tools.Add(new ComputerContextTool());
        tools.Add(new NetworkTool());
        tools.Add(new ApplicationsTool());
        tools.Add(new OpenApplicationTool());
        tools.Add(new OpenPathTool());
        tools.Add(new ListFilesTool());
        tools.Add(new ReadFileTool());
        tools.Add(new FindFilesTool());
        tools.Add(new WriteFileTool());
        tools.Add(new AppendFileTool());
        tools.Add(new ReplaceInFileTool());
        tools.Add(new MovePathTool());
        tools.Add(new CopyPathTool());
        tools.Add(new DeletePathTool());
        tools.Add(new BrowseTool());
        tools.Add(new SearchWebTool());
        tools.Add(new SpotifyTool());
        tools.Add(new CodeTool());
        tools.Add(new ChromeTool());
        tools.Add(new ExplorerAppTool());
        tools.Add(new OfficeTool());
        tools.Add(new DiscordTool());
        tools.Add(new SlackTool());
        tools.Add(new WindowsTool());
        tools.Add(new AppStateTool());
        tools.Add(new FocusAppTool());
        tools.Add(new CloseAppTool());
        tools.Add(new WindowStateTool());
        tools.Add(new MoveWindowTool());
        tools.Add(new ResizeWindowTool());
        tools.Add(new SnapWindowTool());
        tools.Add(new SendHotkeyTool());
        tools.Add(new ClickElementTool());
        tools.Add(new TypeIntoElementTool());
        tools.Add(new GetElementTextTool());
        tools.Add(new ScrollWindowTool());
        tools.Add(new MediaTool());
        tools.Add(new ClipboardTool());
        tools.Add(new NotifyTool());
        tools.Add(new MacroTool());
        tools.Add(new WaitTool());
        tools.Add(new EmailTool());
        tools.Add(new TasksTool());
        tools.Add(new ResearchTool());
        tools.Add(new ScreenTool());
        tools.Add(new AmbientTool());
        tools.Add(new AnalyzeImageTool());
        tools.Add(new AnalyzeDocumentTool());
        tools.Add(new ShellTool());
        tools.Add(new ExitTool());
        _tools = tools;
    }

    public JarvisOptions Options { get; }

    public event EventHandler<AssistantActivityEventArgs>? Activity;

    public Task<PendingApprovalAction?> GetPendingApprovalAsync(CancellationToken cancellationToken) =>
        _pendingApprovalStore.GetAsync(cancellationToken);

    public Task<PersistedAgentSession?> GetPendingClarificationSessionAsync(CancellationToken cancellationToken) =>
        _agentStateStore.GetPendingClarificationAsync(cancellationToken);

    public Task<PersistedAgentSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        _agentStateStore.GetSessionAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<PersistedAgentSession>> GetBackgroundSessionsAsync(CancellationToken cancellationToken) =>
        _agentStateStore.GetBackgroundSessionsAsync(cancellationToken);

    public Task<IReadOnlyList<MemoryNote>> GetMemoriesAsync(CancellationToken cancellationToken) =>
        _memoryStore.GetRecentAsync(200, cancellationToken);

    public Task<IReadOnlyList<MemoryNote>> GetPendingRemindersAsync(CancellationToken cancellationToken) =>
        _memoryStore.GetPendingRemindersAsync(32, cancellationToken);

    public Task<MemoryNote?> GetMemoryByIdAsync(string id, CancellationToken cancellationToken) =>
        _memoryStore.GetByIdAsync(id, cancellationToken);

    public Task<MemoryNote?> UpdateMemoryAsync(MemoryUpdateRequest request, CancellationToken cancellationToken) =>
        _memoryStore.UpdateAsync(request, cancellationToken);

    public Task<bool> ForgetMemoryAsync(string id, string reason, CancellationToken cancellationToken) =>
        _memoryStore.ForgetAsync(id, reason, cancellationToken);

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_planner.IsAvailable)
        {
            return Task.CompletedTask;
        }

        return _planner.InitializeAsync(ExecutePlannerToolAsync, CreateActivityProgress(), cancellationToken);
    }

    public async Task<AssistantTurn> HandleAsync(string input, CancellationToken cancellationToken)
    {
        var persistencePlan = MemoryAutomation.PrepareTurn(input);
        var normalizedInput = NormalizeInput(persistencePlan.EffectiveInput);
        ToolResult result;
        AgentIntentKind intent = AgentIntentKind.DirectTool;
        string sessionId = string.Empty;
        bool isFollowUpQuestion = false;
        AssistantToolExecution[] toolResults = [];
        AssistantCitation[] citations = [];
        MemoryProfileSnapshot? memoryProfile = null;

        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            result = new ToolResult($"{Options.AssistantName} online. Use `help` to inspect the current command surface.");
        }
        else if (!persistencePlan.PersistAutomaticMemory && LooksLikeRememberCommand(normalizedInput))
        {
            result = new ToolResult(
                "Noted privately. I did not persist that memory.",
                VerificationText: "The user marked the turn as off the record, so the memory was not stored.",
                SummaryText: "Private memory was not persisted.");
        }
        else
        {
            var pendingApprovalOutcome = await TryHandlePendingApprovalAsync(normalizedInput, cancellationToken);

            if (pendingApprovalOutcome is not null)
            {
                result = pendingApprovalOutcome.Result;
                intent = pendingApprovalOutcome.Intent;
                sessionId = pendingApprovalOutcome.SessionId;
                isFollowUpQuestion = pendingApprovalOutcome.IsFollowUpQuestion;
                toolResults = pendingApprovalOutcome.ToolResults;
                citations = pendingApprovalOutcome.Citations;
            }
            else
            {
                var tool = _tools.FirstOrDefault(candidate => candidate.CanHandle(normalizedInput));
                if (tool is null)
                {
                    memoryProfile = await _memoryStore.GetProfileAsync(cancellationToken);
                    var preferredUserAddress = AssistantPersonaConfiguration.ResolvePreferredUserAddress(Options, memoryProfile);

                    if (AssistantPresenceStyle.TryBuildSocialReply(normalizedInput, Options, preferredUserAddress, out var socialReply))
                    {
                        intent = AgentIntentKind.SimpleReply;
                        result = new ToolResult(
                            socialReply,
                            SummaryText: TrimForSummary(socialReply),
                            VerificationText: "The runtime handled the input as a direct social-presence reply without tool execution.");

                        EmitActivity(new AssistantActivityEvent(
                            AssistantActivityKind.Status,
                            "Handled as a direct presence reply.",
                            Detail: socialReply));
                    }
                    else
                    {
                        var planningOutcome = await HandleWithPlannerAsync(normalizedInput, memoryProfile, cancellationToken);
                        result = planningOutcome.Result;
                        intent = planningOutcome.Intent;
                        sessionId = planningOutcome.SessionId;
                        isFollowUpQuestion = planningOutcome.IsFollowUpQuestion;
                        toolResults = planningOutcome.ToolResults;
                        citations = planningOutcome.Citations;
                    }
                }
                else
                {
                    var toolInput = ToolInputHelper.GetCommandTail(normalizedInput, tool.Name);

                    EmitActivity(new AssistantActivityEvent(
                        AssistantActivityKind.Classification,
                        $"Matched the direct `{tool.Name}` tool route.",
                        Detail: string.IsNullOrWhiteSpace(toolInput) ? tool.Description : toolInput));

                    EmitActivity(new AssistantActivityEvent(
                        AssistantActivityKind.ToolStart,
                        $"Running {tool.Name}.",
                        Detail: toolInput));

                    result = await ExecuteToolWithSafetyAsync(tool, normalizedInput, input, cancellationToken);
                    toolResults = [BuildToolExecution(tool.Name, toolInput, result, result.ResponseText)];

                    if (!LooksLikeSafetyIntervention(result))
                    {
                        EmitActivity(new AssistantActivityEvent(
                            result.Succeeded && !InferToolError(result.ResponseText)
                                ? AssistantActivityKind.ToolComplete
                                : AssistantActivityKind.Error,
                            $"{tool.Name}: {toolResults[0].Summary}",
                            Detail: toolResults[0].VerificationText));
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(result.ResponseText))
        {
            memoryProfile ??= await _memoryStore.GetProfileAsync(cancellationToken);
            var preferredUserAddress = AssistantPersonaConfiguration.ResolvePreferredUserAddress(Options, memoryProfile);
            var presentedResponse = AssistantPresenceStyle.ApplyResponsePresence(
                persistencePlan.EffectiveInput,
                result.ResponseText,
                Options,
                intent,
                isFollowUpQuestion,
                preferredUserAddress);

            if (!string.Equals(presentedResponse, result.ResponseText, StringComparison.Ordinal))
            {
                result = result with { ResponseText = presentedResponse };
            }
        }

        var interactionContext = await CaptureInteractionContextAsync(
            persistencePlan.EffectiveInput,
            toolResults,
            cancellationToken);

        var turn = new AssistantTurn(
            DateTimeOffset.UtcNow,
            input,
            result.ResponseText,
            result.ShouldExit,
            intent.ToString(),
            sessionId,
            isFollowUpQuestion,
            toolResults,
            citations,
            persistencePlan.PrivacyMode,
            interactionContext);

        _conversationState.AddTurn(turn);

        if (persistencePlan.PersistTranscript)
        {
            await _transcriptStore.AppendAsync(turn, cancellationToken);
        }

        if (persistencePlan.PersistAutomaticMemory)
        {
            await LearnFromTurnAsync(persistencePlan.EffectiveInput, toolResults, interactionContext, cancellationToken);

            if (persistencePlan.PersistTranscript)
            {
                await TryCreateConversationSummaryAsync(cancellationToken);
            }
        }

        return turn;
    }

    private string NormalizeInput(string input)
    {
        var value = StripWakeAddressing(input.Trim());
        return JarvisCommandCatalog.NormalizeToCommand(value);
    }

    private string StripWakeAddressing(string value)
    {
        var current = value;
        current = StripGreetingAddressPrefix(current, Options.AssistantName);
        current = StripGreetingAddressPrefix(current, Options.WakePhrase);
        current = StripPrefix(current, Options.AssistantName);
        current = StripPrefix(current, Options.WakePhrase);
        return current;
    }

    private static string StripGreetingAddressPrefix(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return value;
        }

        var current = value.Trim();

        foreach (var greeting in new[] { "hey", "hi", "hello" })
        {
            foreach (var candidate in new[]
            {
                $"{greeting} {name}",
                $"{greeting}, {name}",
                $"{greeting} {name},",
                $"{greeting}, {name},"
            })
            {
                if (!current.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var remainder = current[candidate.Length..].TrimStart(' ', ',', ':', ';', '-');
                return string.IsNullOrWhiteSpace(remainder) ? greeting : remainder;
            }
        }

        return current;
    }

    private async Task<PlanningOutcome> HandleWithPlannerAsync(
        string originalInput,
        MemoryProfileSnapshot? memoryProfile,
        CancellationToken cancellationToken)
    {
        if (!_planner.IsAvailable)
        {
            return new PlanningOutcome(
                new ToolResult(BuildFallbackResponse()),
                AgentIntentKind.Planner,
                string.Empty,
                false,
                [],
                []);
        }

        var recentTurns = await _transcriptStore.GetRecentAsync(
            Math.Max(0, Options.PlannerRecentTurnCount),
            cancellationToken);
        var userState = await _userStateProvider.GetSnapshotAsync(cancellationToken);
        memoryProfile ??= await _memoryStore.GetProfileAsync(cancellationToken);
        var recentToolResults = recentTurns
            .SelectMany(turn => turn.ToolResults ?? Array.Empty<AssistantToolExecution>())
            .TakeLast(Math.Max(0, Options.PlannerRecentToolResultCount))
            .ToArray();

        string computerContext;
        DesktopContextSnapshot desktopContext;

        try
        {
            desktopContext = await _toolContext.DesktopAutomation.GetContextSnapshotAsync(cancellationToken);
            computerContext = await _toolContext.DesktopAutomation.GetComputerContextAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            desktopContext = new DesktopContextSnapshot(DateTimeOffset.Now, string.Empty, string.Empty, [], [], 0, string.Empty);
            computerContext = $"Computer context unavailable: {TrimErrorMessage(exception.Message)}";
        }

        var memoryQueryContext = BuildMemoryQueryContext(originalInput, desktopContext);
        var relevantMemories = await _memoryStore.SearchAsync(
            originalInput,
            memoryQueryContext,
            Math.Max(0, Options.PlannerRecentMemoryCount),
            cancellationToken);

        if (relevantMemories.Count < Math.Max(0, Options.PlannerRecentMemoryCount))
        {
            var recentFallback = await _memoryStore.GetRecentAsync(
                Math.Max(0, Options.PlannerRecentMemoryCount * 2),
                cancellationToken);
            relevantMemories = relevantMemories
                .Concat(recentFallback.Where(note => relevantMemories.All(existing => !string.Equals(existing.Id, note.Id, StringComparison.OrdinalIgnoreCase))))
                .Take(Math.Max(0, Options.PlannerRecentMemoryCount))
                .ToArray();
        }

        var request = new AssistantPlanningRequest(
            Options.AssistantName,
            originalInput,
            _toolContext.WorkspaceRoot,
            computerContext,
            Options.ShellExecutionEnabled,
            _tools.Select(BuildPlannerToolDefinition).ToArray(),
            relevantMemories,
            recentTurns,
            recentToolResults,
            _conversationState.GetSnapshot(Math.Max(4, Options.PlannerRecentTurnCount)),
            userState,
            memoryProfile);

        AssistantPlanningResult plan;

        try
        {
            plan = await _planner.ExecuteTurnAsync(
                request,
                ExecutePlannerToolAsync,
                CreateActivityProgress(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new PlanningOutcome(
                new ToolResult(BuildPlannerErrorResponse(exception), Succeeded: false),
                AgentIntentKind.Planner,
                string.Empty,
                false,
                [],
                []);
        }

        if (!string.IsNullOrWhiteSpace(plan.Response))
        {
            return new PlanningOutcome(
                new ToolResult(plan.Response.Trim(), plan.ShouldExit, SummaryText: TrimForSummary(plan.Response)),
                plan.Intent,
                plan.SessionId,
                plan.RequiresClarification,
                plan.ToolResults.Select(ToToolExecution).ToArray(),
                plan.Citations);
        }

        if (plan.ToolResults.Length > 0)
        {
            var lastToolResult = plan.ToolResults[^1];
            return new PlanningOutcome(
                new ToolResult(
                    lastToolResult.Output,
                    lastToolResult.ShouldExit,
                    Succeeded: !lastToolResult.IsError,
                    VerificationText: lastToolResult.Verification,
                    SummaryText: lastToolResult.Summary),
                plan.Intent,
                plan.SessionId,
                plan.RequiresClarification,
                plan.ToolResults.Select(ToToolExecution).ToArray(),
                plan.Citations);
        }

        return new PlanningOutcome(
            new ToolResult(BuildFallbackResponse()),
            plan.Intent,
            plan.SessionId,
            plan.RequiresClarification,
            [],
            plan.Citations);
    }

    private async Task<PlanningOutcome?> TryHandlePendingApprovalAsync(
        string normalizedInput,
        CancellationToken cancellationToken)
    {
        var pendingApproval = await _pendingApprovalStore.GetAsync(cancellationToken);

        if (pendingApproval is null)
        {
            return null;
        }

        if (_toolSafety.LooksLikeDenialResponse(normalizedInput))
        {
            await _pendingApprovalStore.ClearAsync(cancellationToken);

            EmitActivity(new AssistantActivityEvent(
                AssistantActivityKind.Warning,
                "Canceled the pending major-action approval.",
                Detail: pendingApproval.Summary));

            return new PlanningOutcome(
                new ToolResult(
                    string.Join(
                        Environment.NewLine,
                        "Canceled the pending action.",
                        pendingApproval.Summary),
                    SummaryText: "Canceled the pending major-action approval.",
                    VerificationText: "The pending approved action was canceled before execution."),
                AgentIntentKind.Clarification,
                string.Empty,
                false,
                [],
                []);
        }

        if (!_toolSafety.LooksLikeApprovalResponse(normalizedInput))
        {
            return null;
        }

        await _pendingApprovalStore.ClearAsync(cancellationToken);

        var tool = _tools.FirstOrDefault(candidate => string.Equals(candidate.Name, pendingApproval.ToolName, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
        {
            return new PlanningOutcome(
                new ToolResult(
                    $"The pending action could not be completed because the `{pendingApproval.ToolName}` tool is no longer available.",
                    Succeeded: false,
                    SummaryText: "Pending approval could not be executed.",
                    VerificationText: "The pending action referred to a missing tool."),
                AgentIntentKind.Clarification,
                string.Empty,
                false,
                [],
                []);
        }

        EmitActivity(new AssistantActivityEvent(
            AssistantActivityKind.Warning,
            $"Approval received. Running the pending `{tool.Name}` action.",
            Detail: pendingApproval.Summary));

        var result = await ExecuteToolWithSafetyAsync(
            tool,
            pendingApproval.NormalizedCommand,
            pendingApproval.OriginalInput,
            cancellationToken,
            bypassApproval: true);

        return new PlanningOutcome(
            result,
            AgentIntentKind.Clarification,
            string.Empty,
            false,
            [BuildToolExecution(tool.Name, ToolInputHelper.GetCommandTail(pendingApproval.NormalizedCommand, tool.Name), result, result.ResponseText)],
            []);
    }

    private static string StripPrefix(string value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var stripped = value[prefix.Length..];
        return stripped.TrimStart(' ', ',', ':', ';', '-');
    }

    private string BuildFallbackResponse()
    {
        if (_planner.IsAvailable)
        {
            return string.Join(
                Environment.NewLine,
                "Agent runtime is active, but I could not confidently complete that request.",
                "Try rephrasing it naturally or use `help` to inspect the current command surface.",
                "I currently handle memory, memory diagnostics and maintenance, live computer context, app discovery and launch, app adapters for Spotify, VS Code, Chrome, Explorer, Discord, and Slack, window management, clipboard history and media controls, Win32 UI automation, file create/edit/move/copy/delete actions, browser, form, and selector-aware Chrome DOM workflows, notifications, macros, web research, user-state context, visual analysis for screens and documents, system state, and optional shell execution.");
        }

        return string.Join(
            Environment.NewLine,
            "Bootstrap mode is active.",
            "I do not have a general-purpose reasoning model wired in yet.",
            "Use `help` to inspect commands for memory, live computer context, app discovery, desktop automation, browser and file workflows, and system state.",
            "The next major layers are planner access, stronger voice handling, deeper app-specific automation, and a richer HUD.");
    }

    private string BuildPlannerErrorResponse(Exception exception)
    {
        return string.Join(
            Environment.NewLine,
            $"Planner request failed: {TrimErrorMessage(exception.Message)}",
            "Check the planner settings, model, and API key, or use `help` to fall back to direct commands.");
    }

    private async Task<PlannerToolResult> ExecutePlannerToolAsync(PlannerToolCall toolCall, CancellationToken cancellationToken)
    {
        var tool = _tools.FirstOrDefault(candidate => string.Equals(candidate.Name, toolCall.ApiName, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
        {
            return new PlannerToolResult(
                toolCall.ApiName,
                string.Empty,
                toolCall.Input,
                $"Unsupported internal tool: {toolCall.ApiName}",
                IsError: true,
                Verification: "The requested tool was not registered in the current runtime.",
                Summary: $"Unsupported tool `{toolCall.ApiName}`.",
                Purpose: toolCall.Purpose,
                ExpectedEvidence: toolCall.ExpectedEvidence);
        }

        var commandText = string.IsNullOrWhiteSpace(toolCall.Input)
            ? tool.Name
            : $"{tool.Name} {toolCall.Input}";

        var normalizedCommand = NormalizeInput(commandText);
        var toolResult = await ExecuteToolWithSafetyAsync(tool, normalizedCommand, commandText, cancellationToken);
        var isError = !toolResult.Succeeded || InferToolError(toolResult.ResponseText);
        var summary = string.IsNullOrWhiteSpace(toolResult.SummaryText)
            ? TrimForSummary(toolResult.ResponseText)
            : toolResult.SummaryText.Trim();
        var verification = string.IsNullOrWhiteSpace(toolResult.VerificationText)
            ? BuildVerificationText(tool.Name, toolCall.Input, toolResult.ResponseText, isError)
            : toolResult.VerificationText.Trim();

        return new PlannerToolResult(
            toolCall.ApiName,
            tool.Name,
            toolCall.Input,
            toolResult.ResponseText,
            isError,
            toolResult.ShouldExit,
            verification,
            summary,
            toolCall.Purpose,
            toolCall.ExpectedEvidence);
    }

    private async Task<ToolResult> ExecuteAutomationCommandFromContextAsync(
        string command,
        bool bypassApproval,
        CancellationToken cancellationToken)
    {
        var normalizedCommand = NormalizeInput(command);

        if (string.IsNullOrWhiteSpace(normalizedCommand))
        {
            return new ToolResult(
                "Automation command was empty.",
                Succeeded: false,
                VerificationText: "A workflow step resolved to an empty command.",
                SummaryText: "Workflow step was empty.");
        }

        var tool = _tools.FirstOrDefault(candidate => candidate.CanHandle(normalizedCommand));

        if (tool is null)
        {
            return new ToolResult(
                $"Unsupported automation command: {command}",
                Succeeded: false,
                VerificationText: "The workflow step did not match a registered tool.",
                SummaryText: "Unsupported workflow step.");
        }

        return await ExecuteToolWithSafetyAsync(
            tool,
            normalizedCommand,
            command,
            cancellationToken,
            bypassApproval);
    }

    private async Task<ToolResult> ExecuteToolWithSafetyAsync(
        IAssistantTool tool,
        string normalizedCommand,
        string originalInput,
        CancellationToken cancellationToken,
        bool bypassApproval = false)
    {
        var safetyDecision = _toolSafety.Assess(tool.Name, normalizedCommand);

        if (safetyDecision.Kind == SafetyDecisionKind.Block)
        {
            EmitActivity(new AssistantActivityEvent(
                AssistantActivityKind.Warning,
                safetyDecision.SummaryText,
                Detail: safetyDecision.ResponseText));

            return new ToolResult(
                safetyDecision.ResponseText,
                Succeeded: false,
                VerificationText: safetyDecision.VerificationText,
                SummaryText: safetyDecision.SummaryText);
        }

        if (!bypassApproval && safetyDecision.Kind == SafetyDecisionKind.RequireApproval)
        {
            await _pendingApprovalStore.SaveAsync(
                new PendingApprovalAction(
                    tool.Name,
                    normalizedCommand,
                    originalInput,
                    safetyDecision.SummaryText,
                    safetyDecision.ResponseText,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            EmitActivity(new AssistantActivityEvent(
                AssistantActivityKind.Warning,
                safetyDecision.SummaryText,
                Detail: safetyDecision.ResponseText));

            return new ToolResult(
                safetyDecision.ResponseText,
                Succeeded: false,
                VerificationText: safetyDecision.VerificationText,
                SummaryText: safetyDecision.SummaryText);
        }

        try
        {
            return await tool.ExecuteAsync(normalizedCommand, _toolContext, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ToolResult(
                $"{tool.Name} failed: {TrimErrorMessage(exception.Message)}",
                Succeeded: false,
                VerificationText: $"The `{tool.Name}` tool threw an unhandled exception that was converted into a failure result.",
                SummaryText: $"{tool.Name} failed.");
        }
    }

    private void EmitActivity(AssistantActivityEvent activity)
    {
        Activity?.Invoke(this, new AssistantActivityEventArgs(activity));
    }

    private IProgress<AssistantActivityEvent> CreateActivityProgress()
    {
        return new Progress<AssistantActivityEvent>(EmitActivity);
    }

    private static AssistantToolExecution BuildToolExecution(string toolName, string input, ToolResult result, string fallbackText)
    {
        var failed = !result.Succeeded || InferToolError(result.ResponseText);
        var summary = string.IsNullOrWhiteSpace(result.SummaryText)
            ? TrimForSummary(fallbackText)
            : result.SummaryText.Trim();
        var verification = string.IsNullOrWhiteSpace(result.VerificationText)
            ? BuildVerificationText(toolName, input, result.ResponseText, failed)
            : result.VerificationText.Trim();
        var verificationStatus = BuildVerificationStatus(result.ResponseText, summary, verification, failed);
        var verificationSummary = BuildVerificationSummary(summary, verification, verificationStatus);

        return new AssistantToolExecution(
            toolName,
            input,
            summary,
            !failed,
            verification,
            result.ResponseText,
            verificationStatus,
            verificationSummary);
    }

    private static AssistantToolExecution ToToolExecution(PlannerToolResult result)
    {
        var verification = string.IsNullOrWhiteSpace(result.Verification)
            ? BuildVerificationText(result.Command, result.Input, result.Output, result.IsError)
            : result.Verification.Trim();
        var verificationStatus = string.IsNullOrWhiteSpace(result.VerificationStatus)
            ? BuildVerificationStatus(result.Output, result.Summary, verification, result.IsError)
            : result.VerificationStatus.Trim();
        var verificationSummary = string.IsNullOrWhiteSpace(result.VerificationSummary)
            ? BuildVerificationSummary(result.Summary, verification, verificationStatus)
            : result.VerificationSummary.Trim();

        return new AssistantToolExecution(
            result.Command,
            result.Input,
            string.IsNullOrWhiteSpace(result.Summary) ? TrimForSummary(result.Output) : result.Summary.Trim(),
            !result.IsError,
            verification,
            result.Output,
            verificationStatus,
            verificationSummary);
    }

    private static bool InferToolError(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return true;
        }

        var errorPrefixes = new[]
        {
            "usage:",
            "unable ",
            "path not found",
            "file not found",
            "directory not found",
            "no matching",
            "shell execution is disabled",
            "command failed",
            "speech recognition unavailable",
            "microphone startup failed"
        };

        return errorPrefixes.Any(prefix => responseText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            || responseText.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildVerificationText(string toolName, string input, string responseText, bool isError)
    {
        if (isError)
        {
            return $"The `{toolName}` tool reported a failure signal for input `{input}`.";
        }

        return $"The `{toolName}` tool returned a success-shaped response for input `{input}`.";
    }

    private static string BuildVerificationStatus(string responseText, string summary, string verificationText, bool isError)
    {
        if (LooksLikeApprovalGate(responseText, summary, verificationText))
        {
            return "awaiting_approval";
        }

        return isError ? "failed" : "observed";
    }

    private static string BuildVerificationSummary(string summary, string verificationText, string verificationStatus)
    {
        if (string.Equals(verificationStatus, "awaiting_approval", StringComparison.OrdinalIgnoreCase))
        {
            return "Awaiting user approval before the requested evidence can be confirmed.";
        }

        if (!string.IsNullOrWhiteSpace(verificationText))
        {
            return verificationText.Trim();
        }

        return string.IsNullOrWhiteSpace(summary) ? string.Empty : summary.Trim();
    }

    private static bool LooksLikeApprovalGate(string responseText, string summary, string verificationText)
    {
        return responseText.StartsWith("Approval required", StringComparison.OrdinalIgnoreCase)
            || summary.Contains("approval required", StringComparison.OrdinalIgnoreCase)
            || verificationText.Contains("approval", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimForSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        const int maxLength = 160;
        var flattened = text.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..maxLength] + "...";
    }

    private static string TrimErrorMessage(string message)
    {
        const int maxLength = 320;
        var value = message.Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static bool LooksLikeSafetyIntervention(ToolResult result)
    {
        return result.SummaryText.Contains("approval required", StringComparison.OrdinalIgnoreCase)
            || result.SummaryText.Contains("blocked", StringComparison.OrdinalIgnoreCase)
            || result.ResponseText.StartsWith("Approval required", StringComparison.OrdinalIgnoreCase)
            || result.ResponseText.StartsWith("Blocked for safety", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<InteractionContextSnapshot> CaptureInteractionContextAsync(
        string effectiveInput,
        IReadOnlyList<AssistantToolExecution> toolResults,
        CancellationToken cancellationToken)
    {
        try
        {
            var desktopContext = await _toolContext.DesktopAutomation.GetContextSnapshotAsync(cancellationToken);
            return MemoryAutomation.BuildInteractionContext(
                desktopContext,
                ResolveLocationContext(),
                BuildRecentActivity(effectiveInput, toolResults));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new InteractionContextSnapshot(
                DateTimeOffset.UtcNow,
                string.Empty,
                ResolveLocationContext(),
                string.Empty,
                string.Empty,
                [],
                BuildRecentActivity(effectiveInput, toolResults).ToArray());
        }
    }

    private async Task LearnFromTurnAsync(
        string effectiveInput,
        IReadOnlyList<AssistantToolExecution> toolResults,
        InteractionContextSnapshot interactionContext,
        CancellationToken cancellationToken)
    {
        foreach (var preference in MemoryAutomation.ExtractLearnedPreferences(effectiveInput, interactionContext))
        {
            await _memoryStore.AddAsync(preference, cancellationToken);
        }

        var episodicMemory = MemoryAutomation.BuildEpisodicMemory(effectiveInput, toolResults, interactionContext);

        if (episodicMemory is not null)
        {
            await _memoryStore.AddAsync(episodicMemory, cancellationToken);
        }
    }

    private async Task TryCreateConversationSummaryAsync(CancellationToken cancellationToken)
    {
        var turns = await _transcriptStore.GetAllAsync(cancellationToken);
        var notes = await _memoryStore.GetAllAsync(cancellationToken);
        var summary = MemoryAutomation.BuildConversationSummary(turns, notes);

        if (summary is not null)
        {
            await _memoryStore.AddAsync(summary, cancellationToken);
        }
    }

    private MemoryQueryContext BuildMemoryQueryContext(string query, DesktopContextSnapshot desktopContext)
    {
        var interactionContext = MemoryAutomation.BuildInteractionContext(
            desktopContext,
            ResolveLocationContext(),
            [query]);

        return new MemoryQueryContext(
            query,
            interactionContext.TimeOfDay,
            interactionContext.Location,
            interactionContext.ActiveApp,
            interactionContext.ActiveWindow,
            interactionContext.SafeRecentActivity);
    }

    private IEnumerable<string> BuildRecentActivity(string userInput, IReadOnlyList<AssistantToolExecution> toolResults)
    {
        if (!string.IsNullOrWhiteSpace(userInput))
        {
            yield return TrimForSummary(userInput);
        }

        foreach (var toolResult in toolResults.Where(result => result.Succeeded).Take(3))
        {
            yield return $"{toolResult.ToolName}: {TrimForSummary(toolResult.Summary)}";
        }
    }

    private string ResolveLocationContext()
    {
        return string.IsNullOrWhiteSpace(Options.DefaultWeatherLocation)
            ? string.Empty
            : Options.DefaultWeatherLocation.Trim();
    }

    private static bool LooksLikeRememberCommand(string normalizedInput)
    {
        return normalizedInput.StartsWith("remember ", StringComparison.OrdinalIgnoreCase)
            || normalizedInput.StartsWith("note ", StringComparison.OrdinalIgnoreCase);
    }

    private static PlannerToolDefinition BuildPlannerToolDefinition(IAssistantTool tool)
    {
        return tool.Name switch
        {
            "computer" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Read live desktop context including the active window, visible apps, workspace entries, and system state. This is read-only.",
                "Leave empty.",
                "Inspect the current desktop state before answering a question about what the user is doing or what is open.",
                "Current active window, visible apps, workspace entries, or a clear context-unavailable result."),
            "email" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Read-only email snapshot from the local user-state data. Use this for inbox summaries and planning context. It does not send, draft, or reply to email.",
                "Leave empty.",
                "Inspect the current inbox snapshot before answering an email-related question.",
                "A list of email items or a clear no-email-context result."),
            "tasks" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Read-only task snapshot from the local user-state data. Use this for task summaries and planning context. It does not create or complete tasks in an external service.",
                "Leave empty.",
                "Inspect the current task snapshot before answering a task-related question.",
                "A list of task items or a clear no-task-context result."),
            "research" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Collect cited public-web sources for open-domain questions. Prefer this over browser search when the user needs sourced information.",
                "Open-domain research query only.",
                "Gather public web evidence with citations before answering a research question.",
                "One or more citations with titles, URLs, and snippets, or a clear no-sources result."),
            "screen" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Analyze the current screen, capture a screenshot, extract screen text, or detect UI elements.",
                "Optional target or question. Examples: `capture`, `ocr browser`, `ui settings`, or a question about the visible screen.",
                "Inspect the visible screen to answer a question about on-screen content or UI state.",
                "Screen analysis, extracted text, detected UI elements, or a fresh capture path."),
            "analyze image" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Analyze an image file. This is for existing local image files.",
                "Format: `<path>` or `<path> :: <question>`.",
                "Inspect a specific image file before answering a question about it.",
                "An image-grounded summary or answer tied to the provided file."),
            "analyze document" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Analyze a text, PDF, or image-based document file.",
                "Format: `<path>` or `<path> :: <question>`.",
                "Inspect a specific document before answering a question about its contents.",
                "A document-grounded summary, extracted text, or answer tied to the provided file."),
            "open app" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Focus a running app or launch a discovered application.",
                "Application name only.",
                "Open or focus the target application.",
                "Confirmation that the app was focused or launched, or a clear failure to resolve it."),
            "open path" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Open a file or folder path.",
                "File or folder path only.",
                "Open the target file or folder for the user.",
                "Confirmation that the path opened, or a clear not-found result."),
            "list files" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "List files in a directory.",
                "Directory path only.",
                "Inspect the contents of a folder before choosing a file or next action.",
                "A directory listing or a clear path-not-found result."),
            "read file" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Preview a text file from disk.",
                "File path only.",
                "Read a local text file to answer a question about its contents.",
                "A file preview or a clear read failure."),
            "find files" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Search the workspace for matching files.",
                "Filename fragment, wildcard, or search term only.",
                "Find candidate files before opening or reading one.",
                "A set of matching file paths or a clear no-match result."),
            "memory" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Inspect, tune, or maintain stored memories including routines, retention, importance, diagnostics, and manual merges.",
                "Memory subcommand only.",
                "Use this when the user explicitly wants to review or maintain memory quality, personalization, or retention behavior.",
                "A memory review, diagnostics report, or a clear maintenance outcome."),
            "write file" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Create or overwrite a text file on disk. Approval may be required.",
                "Format: `<path> :: <content>`.",
                "Create a new text file or replace the full contents of an existing text file when the user explicitly wants a file written.",
                "Confirmation that the file was written, or a clear approval gate/block/failure."),
            "append file" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Append text to an existing text file or create it if needed. Approval may be required.",
                "Format: `<path> :: <content>`.",
                "Add content to the end of a text file as part of a file workflow.",
                "Confirmation that the file was appended, or a clear approval gate/block/failure."),
            "replace in file" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Replace one text fragment with another inside a text file. Approval may be required.",
                "Format: `<path> :: <find> :: <replace>`.",
                "Edit a local text file without dropping into shell commands.",
                "Confirmation that replacements were made, or a clear no-match/approval/block/failure result."),
            "move path" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Move a file or folder. Approval may be required.",
                "Format: `<source> :: <destination>`.",
                "Relocate a file or folder during a desktop workflow.",
                "Confirmation that the path moved, or a clear approval gate/block/failure."),
            "copy path" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Copy a file or folder. Approval may be required.",
                "Format: `<source> :: <destination>`.",
                "Duplicate a file or folder during a desktop workflow.",
                "Confirmation that the path copied, or a clear approval gate/block/failure."),
            "delete path" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Delete a file or folder. Approval may be required.",
                "Target path only.",
                "Remove a file or folder only when the user explicitly asks for deletion.",
                "Confirmation that the path was deleted, or a clear approval gate/block/failure."),
            "browse" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Open a site in the browser.",
                "URL or site target only.",
                "Navigate the browser to the requested site.",
                "Confirmation that the target site or URL was opened."),
            "search web" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Launch a browser search without collecting citations.",
                "Search query only.",
                "Open a quick browser search when the user wants navigation rather than sourced research.",
                "Confirmation that the browser search was opened."),
            "spotify" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Spotify adapter for focus, media transport, and search.",
                "Spotify action only.",
                "Use Spotify-specific commands instead of only generic media keys when the task is clearly about Spotify.",
                "Confirmation that Spotify was opened, controlled, or searched."),
            "code" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "VS Code adapter for opening files/folders, goto targets, and in-app search.",
                "Code action only.",
                "Open or search code workspaces without falling back to raw shell commands when the task is clearly about VS Code.",
                "Confirmation that VS Code opened, focused, or received the requested action."),
            "chrome" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Chrome adapter for navigation, tabs, history/downloads/bookmarks, find-on-page, form/login workflows, and selector-aware DevTools-backed DOM automation specifically in Chrome.",
                "Chrome action only.",
                "Route browser navigation and real web-task automation into Chrome when the user asks for Chrome explicitly, including DOM reads, clicks, typing, waits, and verification.",
                "Confirmation that Chrome opened, focused, navigated, searched, manipulated a DOM target, or verified the requested browser state."),
            "explorer" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Explorer adapter for opening folders and revealing files.",
                "Explorer action only.",
                "Use File Explorer-specific actions such as reveal and open-folder navigation.",
                "Confirmation that Explorer opened, focused, or revealed the requested path."),
            "office" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Office adapter for Word, Excel, PowerPoint, and Outlook launch/status actions plus opening files directly in Office apps.",
                "Office action only.",
                "Use this when the user explicitly wants an Office app instead of a generic app launch.",
                "Confirmation that an Office app was opened, focused, or given a file to open."),
            "discord" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Discord adapter for focus, status, quick-jump, and in-app search.",
                "Discord action only.",
                "Use this when the user explicitly wants Discord navigation instead of generic window control.",
                "Confirmation that Discord was opened, focused, or routed to a search/jump target."),
            "slack" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Slack adapter for focus, status, and quick-jump routing.",
                "Slack action only.",
                "Use this when the user explicitly wants Slack navigation instead of generic window control.",
                "Confirmation that Slack was opened, focused, or routed to a jump target."),
            "windows" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "List visible top-level desktop windows.",
                "Optional filter only.",
                "Inspect the currently open windows before focusing, resizing, snapping, or pasting into one.",
                "A list of visible windows or a clear no-match result."),
            "app state" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Inspect richer state for a specific application, including visibility, process details, and launch metadata.",
                "Application name only.",
                "Use this before or after a workflow when the user wants more than a simple open/not-open answer.",
                "A structured application-state summary or a clear no-match result."),
            "focus app" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Bring a running app window to the foreground.",
                "Window target only.",
                "Focus the requested desktop window before interacting with it.",
                "Confirmation that the requested window was focused, or a clear not-found result."),
            "close app" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Send a close request to a running window.",
                "Window target only.",
                "Close the requested app window when the user explicitly asks for it.",
                "Confirmation that a close request was sent, or a clear approval gate/not-found result."),
            "window state" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Minimize, maximize, or restore a window.",
                "Target window plus state payload.",
                "Change a window's visibility state without moving it.",
                "Confirmation that the window state changed, or a clear failure."),
            "move window" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Move a visible window to explicit screen coordinates.",
                "Target window and X/Y coordinates.",
                "Reposition a window on screen as part of a desktop workflow.",
                "Confirmation that the window moved, or a clear failure."),
            "resize window" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Resize a visible window to the requested dimensions.",
                "Target window and width/height payload.",
                "Resize a desktop window before reading, comparing, or arranging apps.",
                "Confirmation that the window resized, or a clear failure."),
            "snap window" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Snap a visible window into a desktop region such as left, right, or top-right.",
                "Target window and snap position.",
                "Arrange windows into common desktop layouts quickly.",
                "Confirmation that the window snapped, or a clear failure."),
            "send hotkey" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Send a keyboard shortcut to a target window. Approval may be required.",
                "Target window and hotkey payload.",
                "Use the app's own shortcuts when clicking or typing is not the right fit.",
                "Confirmation that the hotkey was sent, or a clear approval gate/failure."),
            "click element" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Click a named or indexed UI element in the active app.",
                "UI target only.",
                "Activate a visible control in the active UI.",
                "Confirmation that the target element was clicked, or a clear not-found result."),
            "type into" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Type text into a named UI element or the focused field.",
                "Target and text payload expected by the existing tool.",
                "Enter the requested text into the right control.",
                "Confirmation that text was entered, or a clear failure/approval gate."),
            "get element text" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Read text from a named UI element.",
                "UI target only.",
                "Inspect a control's visible text before deciding the next step.",
                "The requested element text or a clear not-found result."),
            "scroll" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Scroll the active window or target area.",
                "Direction or target payload only.",
                "Reveal more content in the active UI.",
                "Confirmation that the UI was scrolled, or a clear failure."),
            "media" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Send system media controls such as play/pause, next track, or volume adjustments.",
                "Media action only.",
                "Control playback or system volume without switching apps first.",
                "Confirmation that the media key command was sent, or a clear failure."),
            "clipboard" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Inspect or manipulate the clipboard, including history, restore, and paste into a target window.",
                "Clipboard subcommand payload only.",
                "Use the clipboard for lightweight copy/paste workflows and text handoff.",
                "Clipboard contents, confirmation of an update, or a clear approval gate/failure."),
            "notify" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Send or inspect Jarvis-managed notifications and toast attempts.",
                "Format: `<title> :: <message>`, `list`, or `clear`.",
                "Use this for local notification history or best-effort toast delivery without dropping to shell.",
                "Confirmation that the notification was recorded or shown, or a clear usage/failure result."),
            "macro" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Store or run repeatable multi-step automation macros with optional rollback steps. Running a macro requires approval.",
                "Macro action only.",
                "Use this to save, inspect, or run a reusable desktop workflow instead of repeating the same manual sequence each time.",
                "A saved macro definition, macro execution transcript, or a clear approval gate/failure result."),
            "wait" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Pause briefly to let UI state settle.",
                "Duration payload only when needed.",
                "Wait for the UI or a process to finish before verifying the next step.",
                "A completed wait interval."),
            "run" => new PlannerToolDefinition(
                tool.Name,
                tool.Name,
                "Run a PowerShell command when needed. Read-only commands run directly; major changes may require approval.",
                "PowerShell command only.",
                "Use shell execution only when no safer built-in tool fits the task.",
                "Concrete command output, or a clear approval gate, block, or failure."),
            _ => new PlannerToolDefinition(tool.Name, tool.Name, tool.Description)
        };
    }

    private sealed record PlanningOutcome(
        ToolResult Result,
        AgentIntentKind Intent,
        string SessionId,
        bool IsFollowUpQuestion,
        AssistantToolExecution[] ToolResults,
        AssistantCitation[] Citations);
}
