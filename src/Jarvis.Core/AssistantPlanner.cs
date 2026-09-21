using System.Collections.Concurrent;

namespace Jarvis.Core;

public sealed record PlannerToolDefinition(
    string ApiName,
    string CommandName,
    string Description,
    string InputDescription = "",
    string PurposeDescription = "",
    string ExpectedEvidenceDescription = "");

public sealed record PlannerToolCall(
    string ApiName,
    string Input,
    string Purpose = "",
    string ExpectedEvidence = "");

public sealed record PlannerToolResult(
    string ApiName,
    string Command,
    string Input,
    string Output,
    bool IsError,
    bool ShouldExit = false,
    string Verification = "",
    string Summary = "",
    string Purpose = "",
    string ExpectedEvidence = "",
    string VerificationStatus = "",
    string VerificationSummary = "");

public sealed record AssistantPlanningRequest(
    string AssistantName,
    string UserInput,
    string WorkspaceRoot,
    string ComputerContext,
    bool ShellExecutionEnabled,
    IReadOnlyList<PlannerToolDefinition> Tools,
    IReadOnlyList<MemoryNote> RecentMemories,
    IReadOnlyList<AssistantTurn> RecentTurns,
    IReadOnlyList<AssistantToolExecution> RecentToolResults,
    ConversationStateSnapshot ConversationState,
    UserStateSnapshot UserState,
    MemoryProfileSnapshot MemoryProfile);

public sealed record AssistantPlanningResult(
    string Response,
    PlannerToolResult[] ToolResults,
    AssistantCitation[] Citations,
    AgentIntentKind Intent,
    bool ShouldExit = false,
    bool RequiresClarification = false,
    string ClarificationQuestion = "",
    bool StartedBackgroundTask = false,
    string SessionId = "");

public interface IAssistantPlanner
{
    bool IsAvailable { get; }

    string Status { get; }

    Task InitializeAsync(
        Func<PlannerToolCall, CancellationToken, Task<PlannerToolResult>> executeToolAsync,
        IProgress<AssistantActivityEvent> activity,
        CancellationToken cancellationToken);

    Task<AssistantPlanningResult> ExecuteTurnAsync(
        AssistantPlanningRequest request,
        Func<PlannerToolCall, CancellationToken, Task<PlannerToolResult>> executeToolAsync,
        IProgress<AssistantActivityEvent> activity,
        CancellationToken cancellationToken);
}

public static class AssistantPlannerFactory
{
    public static IAssistantPlanner Create(
        JarvisOptions options,
        IModelGateway modelGateway,
        IAgentStateStore stateStore,
        IWebResearchService webResearchService)
    {
        if (!options.PlannerEnabled)
        {
            return new DisabledAssistantPlanner("disabled in settings");
        }

        return new StructuredAssistantPlanner(options, modelGateway, stateStore, webResearchService);
    }
}

internal sealed class DisabledAssistantPlanner : IAssistantPlanner
{
    public DisabledAssistantPlanner(string reason)
    {
        Status = $"disabled ({reason})";
    }

    public bool IsAvailable => false;

    public string Status { get; }

    public Task InitializeAsync(
        Func<PlannerToolCall, CancellationToken, Task<PlannerToolResult>> executeToolAsync,
        IProgress<AssistantActivityEvent> activity,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<AssistantPlanningResult> ExecuteTurnAsync(
        AssistantPlanningRequest request,
        Func<PlannerToolCall, CancellationToken, Task<PlannerToolResult>> executeToolAsync,
        IProgress<AssistantActivityEvent> activity,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Planner is not available.");
    }
}

internal sealed partial class StructuredAssistantPlanner : IAssistantPlanner
{
    private readonly JarvisOptions _options;
    private readonly IModelGateway _modelGateway;
    private readonly IAgentStateStore _stateStore;
    private readonly IWebResearchService _webResearchService;
    private readonly ConcurrentDictionary<string, Task> _backgroundTasks = new(StringComparer.OrdinalIgnoreCase);

    public StructuredAssistantPlanner(
        JarvisOptions options,
        IModelGateway modelGateway,
        IAgentStateStore stateStore,
        IWebResearchService webResearchService)
    {
        _options = options;
        _modelGateway = modelGateway;
        _stateStore = stateStore;
        _webResearchService = webResearchService;
    }

    public bool IsAvailable => true;

    public string Status =>
        _modelGateway.IsAvailable
            ? $"agent runtime enabled ({_options.PlannerProvider}, routes fast/reasoning/vision, retries {_options.PlannerRetryCount})"
            : "agent runtime enabled in degraded mode (heuristics, direct tools, and optional web research)";

    public async Task InitializeAsync(
        Func<PlannerToolCall, CancellationToken, Task<PlannerToolResult>> executeToolAsync,
        IProgress<AssistantActivityEvent> activity,
        CancellationToken cancellationToken)
    {
        var queuedBackgroundSessions = await _stateStore.GetBackgroundSessionsAsync(cancellationToken);
        var latestResumableSession = await _stateStore.GetLatestResumableSessionAsync(cancellationToken);

        if (queuedBackgroundSessions.Count > 0)
        {
            activity.Report(new AssistantActivityEvent(
                AssistantActivityKind.Background,
                $"{queuedBackgroundSessions.Count} background workflow{(queuedBackgroundSessions.Count == 1 ? string.Empty : "s")} can be resumed. Say `resume` to continue the latest interrupted task.",
                Detail: string.Join(Environment.NewLine, queuedBackgroundSessions.Select(session => $"{session.Status}: {session.OriginalRequest}"))));
        }

        if (latestResumableSession is not null
            && queuedBackgroundSessions.All(session => !string.Equals(session.SessionId, latestResumableSession.SessionId, StringComparison.OrdinalIgnoreCase)))
        {
            activity.Report(new AssistantActivityEvent(
                AssistantActivityKind.Planning,
                "A persisted workflow can be resumed. Say `resume` to continue it.",
                latestResumableSession.SessionId,
                Detail: $"{latestResumableSession.Status}: {latestResumableSession.OriginalRequest}"));
        }
    }

    public async Task<AssistantPlanningResult> ExecuteTurnAsync(
        AssistantPlanningRequest request,
        Func<PlannerToolCall, CancellationToken, Task<PlannerToolResult>> executeToolAsync,
        IProgress<AssistantActivityEvent> activity,
        CancellationToken cancellationToken)
    {
        var pendingSession = await _stateStore.GetPendingClarificationAsync(cancellationToken);
        PersistedAgentSession? resumedSession = null;
        var trimmedInput = request.UserInput.Trim();

        if (pendingSession is null && LooksLikeResumeRequest(trimmedInput))
        {
            resumedSession = await _stateStore.GetLatestResumableSessionAsync(cancellationToken);

            if (resumedSession is null)
            {
                return new AssistantPlanningResult(
                    "There is no interrupted workflow to resume.",
                    [],
                    [],
                    AgentIntentKind.SimpleReply);
            }

            if (_backgroundTasks.ContainsKey(resumedSession.SessionId))
            {
                return new AssistantPlanningResult(
                    "That workflow is already running in the background.",
                    [],
                    resumedSession.Citations,
                    AgentIntentKind.Background,
                    SessionId: resumedSession.SessionId);
            }
        }

        var sessionId = pendingSession?.SessionId ?? resumedSession?.SessionId ?? Guid.NewGuid().ToString("n");
        var effectiveInput = pendingSession is null
            ? resumedSession is null
                ? trimmedInput
                : BuildResumedInput(trimmedInput, resumedSession)
            : $"{pendingSession.OriginalRequest}{Environment.NewLine}Clarification from the user: {trimmedInput}";
        var effectiveRequest = request with { UserInput = effectiveInput };

        if (pendingSession is not null)
        {
            activity.Report(new AssistantActivityEvent(
                AssistantActivityKind.Clarification,
                "Continuing the pending workflow with the user's follow-up.",
                sessionId,
                Detail: trimmedInput));

            if (TryResolveClarificationReference(pendingSession.OriginalRequest, trimmedInput, request, out var clarifiedInput, out var clarificationDetail))
            {
                effectiveInput = clarifiedInput;
                effectiveRequest = request with { UserInput = effectiveInput };

                activity.Report(new AssistantActivityEvent(
                    AssistantActivityKind.Classification,
                    "Resolved the user's clarification from recent context.",
                    sessionId,
                    IsTransient: true,
                    Detail: clarificationDetail));
            }
        }
        else if (resumedSession is not null)
        {
            activity.Report(new AssistantActivityEvent(
                AssistantActivityKind.Background,
                "Resuming the most recent interrupted workflow.",
                sessionId,
                Detail: resumedSession.OriginalRequest));
        }
        else if (TryResolveContextualReference(effectiveInput, effectiveRequest, out var resolvedInput, out var resolutionDetail))
        {
            effectiveInput = resolvedInput;
            effectiveRequest = request with { UserInput = effectiveInput };

            activity.Report(new AssistantActivityEvent(
                AssistantActivityKind.Classification,
                "Resolved a short follow-up reference from recent context.",
                sessionId,
                IsTransient: true,
                Detail: resolutionDetail));
        }

        var intentDecision = await ClassifyIntentAsync(effectiveInput, effectiveRequest, pendingSession, resumedSession, sessionId, activity, cancellationToken);

        if (intentDecision.AskFollowUp)
        {
            await SaveClarificationSessionAsync(sessionId, effectiveInput, intentDecision, pendingSession ?? resumedSession, cancellationToken);

            return new AssistantPlanningResult(
                intentDecision.FollowUpQuestion,
                [],
                [],
                AgentIntentKind.Clarification,
                RequiresClarification: true,
                ClarificationQuestion: intentDecision.FollowUpQuestion,
                SessionId: sessionId);
        }

        if (pendingSession is not null)
        {
            await _stateStore.RemoveSessionAsync(pendingSession.SessionId, cancellationToken);
        }

        if (intentDecision.RunInBackground && _options.PlannerBackgroundTasksEnabled)
        {
            return await QueueBackgroundTaskAsync(
                sessionId,
                effectiveRequest,
                intentDecision,
                executeToolAsync,
                activity,
                resumedSession,
                cancellationToken);
        }

        return intentDecision.Intent switch
        {
            AgentIntentKind.DirectTool or AgentIntentKind.Visual => await ExecuteDirectToolIntentAsync(
                intentDecision,
                executeToolAsync,
                sessionId,
                cancellationToken),
            AgentIntentKind.SimpleReply => await ExecuteSimpleReplyAsync(
                effectiveRequest,
                sessionId,
                resumedSession,
                cancellationToken),
            AgentIntentKind.Research => await ExecuteResearchIntentAsync(
                effectiveInput,
                sessionId,
                cancellationToken),
            _ => await ExecutePlannedSessionAsync(
                effectiveRequest,
                sessionId,
                intentDecision,
                executeToolAsync,
                activity,
                resumedSession,
                false,
                cancellationToken)
        };
    }

    private async Task<AssistantPlanningResult> QueueBackgroundTaskAsync(
        string sessionId,
        AssistantPlanningRequest request,
        IntentDecision decision,
        Func<PlannerToolCall, CancellationToken, Task<PlannerToolResult>> executeToolAsync,
        IProgress<AssistantActivityEvent> activity,
        PersistedAgentSession? existingSession,
        CancellationToken cancellationToken)
    {
        var queuedStep = new PlanStepState("Queued", "queued", "Waiting for the background worker to start.");
        var session = new PersistedAgentSession(
            sessionId,
            "background-queued",
            AgentIntentKind.Background,
            existingSession?.OriginalRequest ?? request.UserInput,
            request.UserInput,
            string.Empty,
            existingSession?.LatestResponse ?? string.Empty,
            RunInBackground: true,
            existingSession?.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            existingSession is { Steps.Length: > 0 }
                ? existingSession.Steps.Concat([queuedStep]).ToArray()
                : [queuedStep],
            existingSession?.ToolResults ?? [],
            existingSession?.Citations ?? []);

        await _stateStore.UpsertSessionAsync(session, cancellationToken);

        activity.Report(new AssistantActivityEvent(
            AssistantActivityKind.Background,
            "Queued a background workflow.",
            sessionId,
            Detail: request.UserInput));

        var backgroundTask = Task.Run(async () =>
        {
            try
            {
                var backgroundRequest = request with
                {
                    UserInput = request.UserInput.Replace("in the background", string.Empty, StringComparison.OrdinalIgnoreCase).Trim()
                };

                await _stateStore.UpsertSessionAsync(
                    session with
                    {
                        Status = "background-running",
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Steps = session.Steps.Concat([new PlanStepState("Running", "in_progress", "Processing the background workflow.")]).ToArray()
                    },
                    CancellationToken.None);

                activity.Report(new AssistantActivityEvent(
                    AssistantActivityKind.Background,
                    "Background workflow started.",
                    sessionId,
                    Detail: backgroundRequest.UserInput));

                var result = await ExecutePlannedSessionAsync(
                    backgroundRequest,
                    sessionId,
                    decision with { RunInBackground = false, Intent = AgentIntentKind.Planner },
                    executeToolAsync,
                    activity,
                    session,
                    true,
                    CancellationToken.None);

                await _stateStore.UpsertSessionAsync(
                    session with
                    {
                        Status = "background-complete",
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        LatestResponse = result.Response,
                        ToolResults = result.ToolResults.Select(ToToolExecution).ToArray(),
                        Citations = result.Citations,
                        Steps = session.Steps.Concat([new PlanStepState("Completed", "completed", "Background workflow finished.")]).ToArray()
                    },
                    CancellationToken.None);

                activity.Report(new AssistantActivityEvent(
                    AssistantActivityKind.Background,
                    $"Background workflow completed. {TrimForFeed(result.Response)}",
                    sessionId,
                    Detail: result.Response));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _stateStore.UpsertSessionAsync(
                    session with
                    {
                        Status = "background-failed",
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        LatestResponse = exception.Message,
                        Steps = session.Steps.Concat([new PlanStepState("Failed", "failed", TrimError(exception.Message))]).ToArray()
                    },
                    CancellationToken.None);

                activity.Report(new AssistantActivityEvent(
                    AssistantActivityKind.Error,
                    $"Background workflow failed: {TrimError(exception.Message)}",
                    sessionId,
                    Detail: exception.Message));
            }
            finally
            {
                _backgroundTasks.TryRemove(sessionId, out _);
            }
        });

        _backgroundTasks[sessionId] = backgroundTask;

        return new AssistantPlanningResult(
            "I queued that as a background task and will keep working on it while the app stays open.",
            [],
            [],
            AgentIntentKind.Background,
            StartedBackgroundTask: true,
            SessionId: sessionId);
    }

    private async Task<AssistantPlanningResult> ExecutePlannedSessionAsync(
        AssistantPlanningRequest request,
        string sessionId,
        IntentDecision intentDecision,
        Func<PlannerToolCall, CancellationToken, Task<PlannerToolResult>> executeToolAsync,
        IProgress<AssistantActivityEvent> activity,
        PersistedAgentSession? existingSession,
        bool runInBackground,
        CancellationToken cancellationToken)
    {
        var toolResults = existingSession?.ToolResults.Select(ToPlannerToolResult).ToList() ?? new List<PlannerToolResult>();
        var citations = existingSession?.Citations.ToList() ?? new List<AssistantCitation>();
        var steps = existingSession?.Steps.ToList() ?? new List<PlanStepState>();

        if (existingSession is not null)
        {
            steps.Add(new PlanStepState(
                "Resume context",
                "completed",
                $"Resuming persisted workflow from `{existingSession.Status}` with {existingSession.ToolResults.Length} saved tool result{(existingSession.ToolResults.Length == 1 ? string.Empty : "s")} and {existingSession.Steps.Length} saved step{(existingSession.Steps.Length == 1 ? string.Empty : "s")}"));
        }

        var messages = BuildConversationMessages(request, existingSession);
        var toolDefinitions = request.Tools
            .Select(tool => new ChatToolDefinition(
                tool.ApiName,
                $"{tool.Description} Pass only the argument tail in `input`.",
                string.IsNullOrWhiteSpace(tool.InputDescription)
                    ? $"Optional argument tail for `{tool.CommandName}`."
                    : tool.InputDescription,
                string.IsNullOrWhiteSpace(tool.PurposeDescription)
                    ? $"Why `{tool.CommandName}` is the right next step for the user's request."
                    : tool.PurposeDescription,
                string.IsNullOrWhiteSpace(tool.ExpectedEvidenceDescription)
                    ? $"What result from `{tool.CommandName}` would confirm success or reveal the next gap."
                    : tool.ExpectedEvidenceDescription))
            .ToArray();

        await PersistActiveSessionAsync(
            sessionId,
            request.UserInput,
            intentDecision.Intent,
            steps,
            toolResults,
            citations,
            existingSession,
            runInBackground,
            cancellationToken);

        for (var iteration = 0; iteration < Math.Max(1, _options.PlannerMaxIterations); iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stepTitle = $"Iteration {iteration + 1}";
            steps.Add(new PlanStepState(stepTitle, "in_progress", "Planning the next action."));

            activity.Report(new AssistantActivityEvent(
                AssistantActivityKind.Planning,
                $"Planning step {iteration + 1} of {Math.Max(1, _options.PlannerMaxIterations)}.",
                sessionId,
                IsTransient: true,
                Detail: steps[^1].Detail));

            ChatCompletionResponse response;

            try
            {
                response = _modelGateway.IsAvailable
                    ? await ExecuteModelCallAsync(
                        new ChatCompletionRequest(messages.ToArray(), toolDefinitions, Temperature: 0.2, User: BuildModelSessionKey(sessionId, "planner")),
                        "planner",
                        sessionId,
                        activity,
                        ModelRoute.Reasoning,
                        cancellationToken)
                    : new ChatCompletionResponse(string.Empty, [], string.Empty, "offline");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                steps[^1] = steps[^1] with { Status = "failed", Detail = TrimError(exception.Message) };
                return await BuildFallbackPlannedResultAsync(request, sessionId, toolResults, citations, exception.Message, cancellationToken);
            }

            steps[^1] = steps[^1] with
            {
                Status = "completed",
                Detail = response.ToolCalls.Length == 0
                    ? "The model finalized the response without another tool call."
                    : $"Selected {response.ToolCalls.Length} tool step{(response.ToolCalls.Length == 1 ? string.Empty : "s")} for this iteration."
            };

            if (response.ToolCalls.Length == 0)
            {
                var finalResponse = string.IsNullOrWhiteSpace(response.Content)
                    ? await BuildPlannedSummaryAsync(request.UserInput, toolResults, citations.ToArray(), sessionId, cancellationToken)
                    : AppendCitations(response.Content.Trim(), citations);

                await _stateStore.RemoveSessionAsync(sessionId, cancellationToken);

                return new AssistantPlanningResult(
                    finalResponse,
                    toolResults.ToArray(),
                    citations.ToArray(),
                    intentDecision.Intent,
                    ShouldExit: toolResults.LastOrDefault()?.ShouldExit ?? false,
                    SessionId: sessionId);
            }

            messages.Add(BuildAssistantToolCallMessage(response));

            foreach (var toolCall in response.ToolCalls)
            {
                var plannerToolCall = ParsePlannerToolCall(toolCall);
                var toolStepIndex = steps.Count;
                steps.Add(new PlanStepState(
                    $"Tool {plannerToolCall.ApiName}",
                    "in_progress",
                    BuildToolStepDetail(plannerToolCall)));

                activity.Report(new AssistantActivityEvent(
                    AssistantActivityKind.ToolStart,
                    $"Running {plannerToolCall.ApiName}.",
                    sessionId,
                    IsTransient: true,
                    Detail: BuildToolStepDetail(plannerToolCall)));

                var rawResult = await executeToolAsync(plannerToolCall, cancellationToken);
                var verificationReport = BuildToolVerificationReport(plannerToolCall, rawResult);
                var result = rawResult with
                {
                    Purpose = plannerToolCall.Purpose,
                    ExpectedEvidence = plannerToolCall.ExpectedEvidence,
                    VerificationStatus = verificationReport.Status,
                    VerificationSummary = verificationReport.Summary
                };
                toolResults.Add(result);

                var summarizedOutput = string.IsNullOrWhiteSpace(result.Summary)
                    ? TrimForFeed(result.Output)
                    : result.Summary;

                steps[toolStepIndex] = steps[toolStepIndex] with
                {
                    Status = result.IsError ? "needs_correction" : "completed",
                    Detail = $"{BuildToolStepDetail(plannerToolCall)} | observed: {summarizedOutput}"
                };

                steps.Add(new PlanStepState(
                    $"Verify {plannerToolCall.ApiName}",
                    verificationReport.PlanStepStatus,
                    verificationReport.Detail));

                activity.Report(new AssistantActivityEvent(
                    AssistantActivityKind.ToolComplete,
                    $"{plannerToolCall.ApiName}: {summarizedOutput}",
                    sessionId,
                    Detail: verificationReport.Detail));

                activity.Report(new AssistantActivityEvent(
                    ResolveVerificationActivityKind(verificationReport.Status),
                    $"Verification for {plannerToolCall.ApiName}: {verificationReport.Summary}",
                    sessionId,
                    IsTransient: true,
                    Detail: verificationReport.Detail));

                messages.Add(new ChatMessage(
                    "tool",
                    [ChatMessageContentPart.FromText(BuildToolMessageContent(result))],
                    ToolCallId: toolCall.Id));

                if (string.Equals(result.ApiName, "research", StringComparison.OrdinalIgnoreCase))
                {
                    citations.AddRange(ParseCitationsFromOutput(result.Output));
                }

                await PersistActiveSessionAsync(
                    sessionId,
                    request.UserInput,
                    intentDecision.Intent,
                    steps,
                    toolResults,
                    citations,
                    existingSession,
                    runInBackground,
                    cancellationToken);

                if (result.ShouldExit)
                {
                    await _stateStore.RemoveSessionAsync(sessionId, cancellationToken);

                    return new AssistantPlanningResult(
                        result.Output,
                        toolResults.ToArray(),
                        citations.ToArray(),
                        intentDecision.Intent,
                        ShouldExit: true,
                        SessionId: sessionId);
                }
            }

            var reflection = await ReflectAsync(request.UserInput, toolResults, sessionId, activity, cancellationToken);

            if (reflection.AskFollowUp)
            {
                await _stateStore.UpsertSessionAsync(
                    new PersistedAgentSession(
                        sessionId,
                        "awaiting-clarification",
                        AgentIntentKind.Clarification,
                        existingSession?.OriginalRequest ?? request.UserInput,
                        request.UserInput,
                        reflection.FollowUpQuestion,
                        string.Empty,
                        RunInBackground: runInBackground,
                        existingSession?.CreatedAtUtc ?? DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        steps.ToArray(),
                        toolResults.Select(ToToolExecution).ToArray(),
                        citations.ToArray()),
                    cancellationToken);

                activity.Report(new AssistantActivityEvent(
                    AssistantActivityKind.Clarification,
                    reflection.FollowUpQuestion,
                    sessionId,
                    Detail: request.UserInput));

                return new AssistantPlanningResult(
                    reflection.FollowUpQuestion,
                    toolResults.ToArray(),
                    citations.ToArray(),
                    AgentIntentKind.Clarification,
                    RequiresClarification: true,
                    ClarificationQuestion: reflection.FollowUpQuestion,
                    SessionId: sessionId);
            }

            if (!reflection.GoalSatisfied && !reflection.AskFollowUp)
            {
                var continuationGuidance = BuildReflectionContinuationGuidance(reflection);

                if (!string.IsNullOrWhiteSpace(continuationGuidance))
                {
                    activity.Report(new AssistantActivityEvent(
                        AssistantActivityKind.Reflection,
                        reflection.RetryRecommended
                            ? "Reflection recommended another step before finishing."
                            : "Reflection kept the workflow open.",
                        sessionId,
                        IsTransient: true,
                        Detail: continuationGuidance));

                    messages.Add(new ChatMessage(
                        "system",
                        [ChatMessageContentPart.FromText(
                            $"Reflection: {continuationGuidance}. Continue the workflow, gather any missing evidence, and avoid repeating clearly completed steps unless fresh evidence is needed.")]));                
                }
            }

            if (reflection.GoalSatisfied)
            {
                activity.Report(new AssistantActivityEvent(
                    AssistantActivityKind.Summary,
                    "Reflection determined the current evidence is sufficient to finish.",
                    sessionId,
                    IsTransient: true,
                    Detail: string.IsNullOrWhiteSpace(reflection.Summary)
                        ? "The latest verified tool evidence satisfied the request."
                        : reflection.Summary));

                var finalResponse = await BuildPlannedSummaryAsync(
                    request.UserInput,
                    toolResults,
                    citations.ToArray(),
                    sessionId,
                    cancellationToken);

                await _stateStore.RemoveSessionAsync(sessionId, cancellationToken);

                return new AssistantPlanningResult(
                    finalResponse,
                    toolResults.ToArray(),
                    citations.ToArray(),
                    intentDecision.Intent,
                    ShouldExit: toolResults.LastOrDefault()?.ShouldExit ?? false,
                    SessionId: sessionId);
            }
        }

        await _stateStore.RemoveSessionAsync(sessionId, cancellationToken);

        return new AssistantPlanningResult(
            await BuildPlannedSummaryAsync(request.UserInput, toolResults, citations.ToArray(), sessionId, cancellationToken),
            toolResults.ToArray(),
            citations.ToArray(),
            intentDecision.Intent,
            ShouldExit: toolResults.LastOrDefault()?.ShouldExit ?? false,
            SessionId: sessionId);
    }

    private async Task PersistActiveSessionAsync(
        string sessionId,
        string latestInput,
        AgentIntentKind intent,
        IReadOnlyList<PlanStepState> steps,
        IReadOnlyList<PlannerToolResult> toolResults,
        IReadOnlyList<AssistantCitation> citations,
        PersistedAgentSession? existingSession,
        bool runInBackground,
        CancellationToken cancellationToken)
    {
        var latestToolResult = toolResults.LastOrDefault();
        var latestResponse = latestToolResult is null
            ? existingSession?.LatestResponse ?? string.Empty
            : string.IsNullOrWhiteSpace(latestToolResult.Summary)
                ? latestToolResult.Output
                : latestToolResult.Summary;

        await _stateStore.UpsertSessionAsync(
            new PersistedAgentSession(
                sessionId,
                runInBackground ? "background-running" : "active",
                runInBackground ? AgentIntentKind.Background : intent,
                existingSession?.OriginalRequest ?? latestInput,
                latestInput,
                string.Empty,
                latestResponse,
                RunInBackground: runInBackground,
                existingSession?.CreatedAtUtc ?? DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                steps.ToArray(),
                toolResults.Select(ToToolExecution).ToArray(),
                citations.ToArray()),
            cancellationToken);
    }

    private async Task SaveClarificationSessionAsync(
        string sessionId,
        string originalRequest,
        IntentDecision decision,
        PersistedAgentSession? existingSession,
        CancellationToken cancellationToken)
    {
        var createdAt = existingSession?.CreatedAtUtc ?? DateTimeOffset.UtcNow;

        await _stateStore.UpsertSessionAsync(
            new PersistedAgentSession(
                sessionId,
                "awaiting-clarification",
                AgentIntentKind.Clarification,
                existingSession?.OriginalRequest ?? originalRequest,
                originalRequest,
                decision.FollowUpQuestion,
                string.Empty,
                RunInBackground: existingSession?.RunInBackground ?? false,
                createdAt,
                DateTimeOffset.UtcNow,
                existingSession?.Steps ?? [],
                existingSession?.ToolResults ?? [],
                existingSession?.Citations ?? []),
            cancellationToken);
    }

    private static string BuildResumedInput(string latestUserInput, PersistedAgentSession session)
    {
        if (string.IsNullOrWhiteSpace(latestUserInput) || LooksLikeResumeRequest(latestUserInput))
        {
            return session.OriginalRequest;
        }

        return string.Join(
            Environment.NewLine,
            session.OriginalRequest,
            $"Additional instruction for the resumed workflow: {latestUserInput.Trim()}");
    }

    private static AssistantActivityKind ResolveVerificationActivityKind(string verificationStatus) =>
        verificationStatus switch
        {
            "verified" or "observed" => AssistantActivityKind.Summary,
            _ => AssistantActivityKind.Warning
        };

    private static string BuildReflectionContinuationGuidance(ReflectionDecision reflection)
    {
        if (!string.IsNullOrWhiteSpace(reflection.CorrectionNote))
        {
            return reflection.CorrectionNote.Trim();
        }

        if (!string.IsNullOrWhiteSpace(reflection.Summary))
        {
            return reflection.Summary.Trim();
        }

        return reflection.RetryRecommended
            ? "The current evidence is not complete yet"
            : string.Empty;
    }
}
