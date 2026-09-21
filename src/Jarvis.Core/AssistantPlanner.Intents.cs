using System.Globalization;
using System.Text.Json;

namespace Jarvis.Core;

internal sealed partial class StructuredAssistantPlanner
{
    private Task<IntentDecision> ClassifyIntentAsync(
        string input,
        AssistantPlanningRequest request,
        PersistedAgentSession? pendingSession,
        PersistedAgentSession? resumedSession,
        string sessionId,
        IProgress<AssistantActivityEvent> activity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ClassifyIntentCoreAsync(input, request, pendingSession, resumedSession, sessionId, activity, cancellationToken);
    }

    private async Task<IntentDecision> ClassifyIntentCoreAsync(
        string input,
        AssistantPlanningRequest request,
        PersistedAgentSession? pendingSession,
        PersistedAgentSession? resumedSession,
        string sessionId,
        IProgress<AssistantActivityEvent> activity,
        CancellationToken cancellationToken)
    {
        IntentDecision decision;

        if (resumedSession is not null)
        {
            decision = new IntentDecision(
                ShouldRunInBackground(request.UserInput) ? AgentIntentKind.Background : AgentIntentKind.Planner,
                $"resuming persisted {resumedSession.Status} workflow",
                ToolHint: string.Empty,
                ToolInput: string.Empty,
                AskFollowUp: false,
                FollowUpQuestion: string.Empty,
                RunInBackground: ShouldRunInBackground(request.UserInput));
        }
        else
        {
            var heuristicDecision = ClassifyHeuristically(input, request, pendingSession);
            decision = heuristicDecision;

            if (ShouldUseModelIntentClassification(input, heuristicDecision, pendingSession))
            {
                var modelDecision = await TryClassifyIntentWithModelAsync(
                    input,
                    request,
                    sessionId,
                    activity,
                    heuristicDecision,
                    cancellationToken);

                if (modelDecision is not null)
                {
                    decision = modelDecision;
                }
            }
        }

        activity.Report(new AssistantActivityEvent(
            AssistantActivityKind.Classification,
            $"Intent classified as {decision.Intent.ToString().ToLowerInvariant()} ({decision.Reason}).",
            sessionId,
            Detail: input));

        return decision;
    }

    private IntentDecision ClassifyHeuristically(string input, AssistantPlanningRequest request, PersistedAgentSession? pendingSession)
    {
        if (pendingSession is not null)
        {
            return new IntentDecision(
                AgentIntentKind.Planner,
                "continuing pending clarification",
                ToolHint: string.Empty,
                ToolInput: string.Empty,
                AskFollowUp: false,
                FollowUpQuestion: string.Empty,
                RunInBackground: false);
        }

        if (LooksAmbiguous(input))
        {
            return new IntentDecision(
                AgentIntentKind.Clarification,
                "request is ambiguous",
                ToolHint: string.Empty,
                ToolInput: string.Empty,
                AskFollowUp: true,
                FollowUpQuestion: BuildClarificationQuestion(input, request),
                RunInBackground: false);
        }

        if (TryInferToolHint(input, out var toolHint))
        {
            var intent = string.Equals(toolHint, "screen", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolHint, "ambient", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolHint, "analyze image", StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolHint, "analyze document", StringComparison.OrdinalIgnoreCase)
                ? AgentIntentKind.Visual
                : AgentIntentKind.DirectTool;

            return new IntentDecision(
                intent,
                "matched a cheap local tool route",
                toolHint,
                ExtractNaturalToolInput(input, toolHint),
                AskFollowUp: false,
                FollowUpQuestion: string.Empty,
                RunInBackground: false);
        }

        if (LooksLikeSimpleReply(input))
        {
            return new IntentDecision(
                AgentIntentKind.SimpleReply,
                "small conversational request",
                ToolHint: string.Empty,
                ToolInput: string.Empty,
                AskFollowUp: false,
                FollowUpQuestion: string.Empty,
                RunInBackground: false);
        }

        if (LooksLikeResearchRequest(input))
        {
            return new IntentDecision(
                AgentIntentKind.Research,
                "open-domain research request",
                ToolHint: string.Empty,
                ToolInput: string.Empty,
                AskFollowUp: false,
                FollowUpQuestion: string.Empty,
                RunInBackground: ShouldRunInBackground(input));
        }

        if (LooksLikePlannerRequest(input))
        {
            return new IntentDecision(
                ShouldRunInBackground(input) ? AgentIntentKind.Background : AgentIntentKind.Planner,
                "explicit action workflow",
                ToolHint: string.Empty,
                ToolInput: string.Empty,
                AskFollowUp: false,
                FollowUpQuestion: string.Empty,
                RunInBackground: ShouldRunInBackground(input));
        }

        return new IntentDecision(
            AgentIntentKind.SimpleReply,
            "assistant conversational reply",
            ToolHint: string.Empty,
            ToolInput: string.Empty,
            AskFollowUp: false,
            FollowUpQuestion: string.Empty,
            RunInBackground: false);
    }

    private bool ShouldUseModelIntentClassification(
        string input,
        IntentDecision heuristicDecision,
        PersistedAgentSession? pendingSession)
    {
        if (pendingSession is not null || !_modelGateway.IsAvailable)
        {
            return false;
        }

        if (heuristicDecision.Intent is AgentIntentKind.DirectTool
            or AgentIntentKind.Visual
            or AgentIntentKind.Research
            or AgentIntentKind.Planner
            or AgentIntentKind.Background)
        {
            return false;
        }

        return heuristicDecision.Intent != AgentIntentKind.SimpleReply
            || !LooksLikeSimpleReply(input)
            || input.Trim().Length > 24
            || LooksLikeEmailContextQuestion(input)
            || LooksLikeTaskContextQuestion(input);
    }

    private async Task<IntentDecision?> TryClassifyIntentWithModelAsync(
        string input,
        AssistantPlanningRequest request,
        string sessionId,
        IProgress<AssistantActivityEvent> activity,
        IntentDecision heuristicDecision,
        CancellationToken cancellationToken)
    {
        try
        {
            activity.Report(new AssistantActivityEvent(
                AssistantActivityKind.Classification,
                "Checking a fast structured classifier before planning.",
                sessionId,
                IsTransient: true,
                Detail: input));

            var response = await ExecuteModelCallAsync(
                new ChatCompletionRequest(
                    BuildIntentClassificationMessages(request, input, heuristicDecision),
                    Temperature: 0,
                    User: BuildModelSessionKey(sessionId, "classify")),
                "intent classification",
                sessionId,
                activity,
                ModelRoute.Fast,
                cancellationToken);

            var classification = ParseJson<IntentClassificationContract>(response.Content);
            return classification is null
                ? null
                : BuildIntentDecisionFromModelClassification(input, request, heuristicDecision, classification);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity.Report(new AssistantActivityEvent(
                AssistantActivityKind.Warning,
                "Falling back to heuristic intent classification.",
                sessionId,
                Detail: TrimError(exception.Message)));

            return null;
        }
    }

    private ChatMessage[] BuildIntentClassificationMessages(
        AssistantPlanningRequest request,
        string input,
        IntentDecision heuristicDecision)
    {
        var toolHints = string.Join(", ", request.Tools.Select(tool => tool.ApiName));

        return
        [
            new ChatMessage("system", [ChatMessageContentPart.FromText(BuildPersonaScopedSystemPrompt(
                "Classify the user's request for routing. Return only JSON with keys intent, reason, toolHint, toolInput, askFollowUp, followUpQuestion, runInBackground. Allowed intent values: direct_tool, simple_reply, planner, research, visual. Use direct_tool only when one immediate tool call is enough. Use visual for screen, ambient, image, or document understanding requests. Use research only for open-domain web questions. Use planner for local computer actions or multi-step workflows. Use simple_reply for conversational answers that can be grounded in the supplied context without taking an action. Ask a follow-up only when a specific missing detail blocks the next step. Email and task snapshot questions are usually simple_reply unless the user explicitly wants to send, draft, create, edit, or update something. toolHint must be one of the allowed tool names or empty. toolInput must contain only the argument tail, not the command name."))]),
            new ChatMessage("user", [ChatMessageContentPart.FromText(string.Join(
                Environment.NewLine,
                $"User input: {input}",
                $"Heuristic guess: intent={MapIntentToClassifierValue(heuristicDecision.Intent)}; reason={heuristicDecision.Reason}; toolHint={heuristicDecision.ToolHint}; runInBackground={heuristicDecision.RunInBackground}",
                $"Allowed tool hints: {toolHints}",
                "Relevant user state:",
                TruncateForContext(request.UserState.ToPromptText(), 900),
                "Recent conversation:",
                TruncateForContext(BuildReplyRecentTurnBlock(request.RecentTurns), 900),
                "Recent tool evidence:",
                BuildRecentToolResultBlock(request.RecentToolResults, Math.Min(3, Math.Max(1, _options.PlannerRecentToolResultCount))),
                "Live computer context:",
                TruncateForContext(request.ComputerContext, 900)))])
        ];
    }

    private IntentDecision? BuildIntentDecisionFromModelClassification(
        string input,
        AssistantPlanningRequest request,
        IntentDecision heuristicDecision,
        IntentClassificationContract classification)
    {
        if (!TryMapClassifierIntent(classification.Intent, out var intent))
        {
            return null;
        }

        var askFollowUp = classification.AskFollowUp ?? false;
        var followUpQuestion = askFollowUp
            ? string.IsNullOrWhiteSpace(classification.FollowUpQuestion)
                ? BuildClarificationQuestion(input)
                : classification.FollowUpQuestion.Trim()
            : string.Empty;
        var toolHint = NormalizeClassifierToolHint(classification.ToolHint, request.Tools);
        var runInBackground = (classification.RunInBackground ?? false) || ShouldRunInBackground(input);

        if (intent == AgentIntentKind.Clarification)
        {
            askFollowUp = true;
            if (string.IsNullOrWhiteSpace(followUpQuestion))
            {
                followUpQuestion = BuildClarificationQuestion(input);
            }
        }

        if (askFollowUp)
        {
            intent = AgentIntentKind.Clarification;
            toolHint = string.Empty;
        }
        else if (intent is AgentIntentKind.DirectTool or AgentIntentKind.Visual)
        {
            if (string.IsNullOrWhiteSpace(toolHint) && TryInferToolHint(input, out var inferredToolHint))
            {
                toolHint = inferredToolHint;
            }

            if (string.IsNullOrWhiteSpace(toolHint))
            {
                return null;
            }

            intent = IsVisualTool(toolHint) ? AgentIntentKind.Visual : AgentIntentKind.DirectTool;
        }
        else
        {
            toolHint = string.Empty;
        }

        var toolInput = string.IsNullOrWhiteSpace(toolHint)
            ? string.Empty
            : string.IsNullOrWhiteSpace(classification.ToolInput)
                ? ExtractNaturalToolInput(input, toolHint)
                : classification.ToolInput.Trim();
        var reason = string.IsNullOrWhiteSpace(classification.Reason)
            ? heuristicDecision.Reason
            : $"model classifier: {classification.Reason.Trim()}";

        return new IntentDecision(
            intent,
            reason,
            toolHint,
            toolInput,
            askFollowUp,
            followUpQuestion,
            runInBackground);
    }

    private async Task<AssistantPlanningResult> ExecuteDirectToolIntentAsync(
        IntentDecision decision,
        Func<PlannerToolCall, CancellationToken, Task<PlannerToolResult>> executeToolAsync,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var result = await executeToolAsync(new PlannerToolCall(decision.ToolHint, decision.ToolInput), cancellationToken);

        return new AssistantPlanningResult(
            result.Output,
            [result],
            [],
            decision.Intent,
            result.ShouldExit,
            SessionId: sessionId);
    }

    private async Task<AssistantPlanningResult> ExecuteSimpleReplyAsync(
        AssistantPlanningRequest request,
        string sessionId,
        PersistedAgentSession? resumedSession,
        CancellationToken cancellationToken)
    {
        var offlineFallback = BuildOfflineSimpleReply(request);

        if (!_modelGateway.IsAvailable)
        {
            return new AssistantPlanningResult(
                offlineFallback,
                [],
                [],
                AgentIntentKind.SimpleReply,
                SessionId: sessionId);
        }

        try
        {
            var response = await ExecuteModelCallAsync(
                new ChatCompletionRequest(BuildContextualReplyMessages(request, resumedSession), Temperature: 0.25, User: BuildModelSessionKey(sessionId, "simple")),
                "simple reply",
                sessionId,
                null,
                ModelRoute.Fast,
                cancellationToken);

            return new AssistantPlanningResult(
                string.IsNullOrWhiteSpace(response.Content) ? offlineFallback : response.Content.Trim(),
                [],
                [],
                AgentIntentKind.SimpleReply,
                SessionId: sessionId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AssistantPlanningResult(
                offlineFallback,
                [],
                [],
                AgentIntentKind.SimpleReply,
                SessionId: sessionId);
        }
    }

    private async Task<AssistantPlanningResult> ExecuteResearchIntentAsync(
        string input,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!_options.PlannerWebResearchEnabled)
        {
            return new AssistantPlanningResult(
                "Web research is disabled in settings.",
                [],
                [],
                AgentIntentKind.Research,
                SessionId: sessionId);
        }

        var research = await _webResearchService.ResearchAsync(
            input,
            _options.PlannerWebResearchMaxResults,
            cancellationToken);

        if (!_modelGateway.IsAvailable || research.Citations.Length == 0)
        {
            return new AssistantPlanningResult(
                BuildOfflineResearchSummary(research),
                [],
                research.Citations,
                AgentIntentKind.Research,
                SessionId: sessionId);
        }

        var sourceText = string.Join(
            Environment.NewLine,
            research.Citations.Select((citation, index) =>
                $"[{index + 1}] {citation.Title}{Environment.NewLine}URL: {citation.Url}{Environment.NewLine}Snippet: {citation.Snippet}"));

        var messages = new[]
        {
            new ChatMessage("system", [ChatMessageContentPart.FromText(
                BuildPersonaScopedSystemPrompt("Answer the question using only the supplied web citations. If the evidence is thin, say so plainly. End with a short Sources section that references the numbered citations."))]),
            new ChatMessage("user", [ChatMessageContentPart.FromText($"Question: {input}{Environment.NewLine}{Environment.NewLine}Sources:{Environment.NewLine}{sourceText}")])
        };

        try
        {
            var response = await ExecuteModelCallAsync(
                new ChatCompletionRequest(messages, Temperature: 0.2, User: BuildModelSessionKey(sessionId, "research")),
                "research summary",
                sessionId,
                null,
                ModelRoute.Reasoning,
                cancellationToken);

            var content = string.IsNullOrWhiteSpace(response.Content)
                ? BuildOfflineResearchSummary(research)
                : response.Content.Trim();

            return new AssistantPlanningResult(
                content,
                [],
                research.Citations,
                AgentIntentKind.Research,
                SessionId: sessionId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AssistantPlanningResult(
                BuildOfflineResearchSummary(research) + Environment.NewLine + $"Model fallback: {TrimError(exception.Message)}",
                [],
                research.Citations,
                AgentIntentKind.Research,
                SessionId: sessionId);
        }
    }

    private async Task<AssistantPlanningResult> BuildFallbackPlannedResultAsync(
        AssistantPlanningRequest request,
        string sessionId,
        IReadOnlyList<PlannerToolResult> toolResults,
        IReadOnlyList<AssistantCitation> citations,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var contextualFallback = toolResults.Count == 0
            ? await TryBuildContextualFallbackReplyAsync(request, sessionId, cancellationToken)
            : string.Empty;
        var response = toolResults.Count == 0
            ? (string.IsNullOrWhiteSpace(contextualFallback)
                ? string.Join(
                    Environment.NewLine,
                    $"The planner could not complete that request: {TrimError(errorMessage)}",
                    "You can try a more direct command, or rephrase the request once connectivity is available.")
                : contextualFallback)
            : await BuildPlannedSummaryAsync(request.UserInput, toolResults, citations.ToArray(), sessionId, cancellationToken);
        var intent = string.IsNullOrWhiteSpace(contextualFallback) ? AgentIntentKind.Planner : AgentIntentKind.SimpleReply;

        await _stateStore.RemoveSessionAsync(sessionId, cancellationToken);

        return new AssistantPlanningResult(
            response,
            toolResults.ToArray(),
            citations.ToArray(),
            intent,
            ShouldExit: toolResults.LastOrDefault()?.ShouldExit ?? false,
            SessionId: sessionId);
    }

    private async Task<string> TryBuildContextualFallbackReplyAsync(
        AssistantPlanningRequest request,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (!_modelGateway.IsAvailable)
        {
            return string.Empty;
        }

        try
        {
            var response = await ExecuteModelCallAsync(
                new ChatCompletionRequest(BuildContextualReplyMessages(request), Temperature: 0.2, User: BuildModelSessionKey(sessionId, "fallback")),
                "fallback reply",
                sessionId,
                null,
                ModelRoute.Fast,
                cancellationToken);

            return response.Content?.Trim() ?? string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private async Task<string> BuildPlannedSummaryAsync(
        string userInput,
        IReadOnlyList<PlannerToolResult> toolResults,
        AssistantCitation[] citations,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (toolResults.Count == 0)
        {
            return AppendCitations("I could not complete that request with the available tools.", citations);
        }

        if (!_modelGateway.IsAvailable)
        {
            return AppendCitations(BuildOfflinePlanSummary(toolResults), citations);
        }

        var toolText = string.Join(
            Environment.NewLine,
            toolResults.Select(result =>
                $"- {result.ApiName}({result.Input}): purpose={result.Purpose}; expectedEvidence={result.ExpectedEvidence}; success={!result.IsError}; verificationStatus={result.VerificationStatus}; verificationSummary={result.VerificationSummary}; verification={result.Verification}; summary={result.Summary}; output={TrimForFeed(result.Output)}"));

        var messages = new[]
        {
            new ChatMessage("system", [ChatMessageContentPart.FromText(
                BuildPersonaScopedSystemPrompt("Summarize the finished workflow for the user. Do not dump raw tool output. Mention whether the action actually succeeded based on the verification evidence."))]),
            new ChatMessage("user", [ChatMessageContentPart.FromText(
                $"User request: {userInput}{Environment.NewLine}Tool results:{Environment.NewLine}{toolText}")])
        };

        try
        {
            var response = await ExecuteModelCallAsync(
                new ChatCompletionRequest(messages, Temperature: 0.2, User: BuildModelSessionKey(sessionId, "summary")),
                "plan summary",
                sessionId,
                null,
                ModelRoute.Fast,
                cancellationToken);

            var content = string.IsNullOrWhiteSpace(response.Content)
                ? BuildOfflinePlanSummary(toolResults)
                : response.Content.Trim();

            return AppendCitations(content, citations);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return AppendCitations(
                BuildOfflinePlanSummary(toolResults) + Environment.NewLine + $"Model fallback: {TrimError(exception.Message)}",
                citations);
        }
    }

    private async Task<ReflectionDecision> ReflectAsync(
        string userInput,
        IReadOnlyList<PlannerToolResult> toolResults,
        string sessionId,
        IProgress<AssistantActivityEvent> activity,
        CancellationToken cancellationToken)
    {
        if (toolResults.Count == 0)
        {
            return new ReflectionDecision(false, string.Empty, false, string.Empty, false, string.Empty);
        }

        if (!_options.PlannerReflectionEnabled || !_modelGateway.IsAvailable)
        {
            return BuildHeuristicReflectionDecision(toolResults);
        }

        var toolText = string.Join(
            Environment.NewLine,
            toolResults.TakeLast(Math.Max(1, _options.PlannerRecentToolResultCount)).Select(result =>
                $"- tool={result.ApiName}; input={result.Input}; purpose={result.Purpose}; expectedEvidence={result.ExpectedEvidence}; success={!result.IsError}; verificationStatus={result.VerificationStatus}; verificationSummary={result.VerificationSummary}; verification={result.Verification}; summary={result.Summary}; output={TrimForFeed(result.Output)}"));

        var messages = new[]
        {
            new ChatMessage("system", [ChatMessageContentPart.FromText(
                "You are a critic for a tool-using assistant. Return only JSON with keys goalSatisfied (bool), askFollowUp (bool), followUpQuestion (string), retryRecommended (bool), correctionNote (string), summary (string).")]),
            new ChatMessage("user", [ChatMessageContentPart.FromText(
                $"User request: {userInput}{Environment.NewLine}Recent tool results:{Environment.NewLine}{toolText}")])
        };

        try
        {
            activity.Report(new AssistantActivityEvent(
                AssistantActivityKind.Reflection,
                "Reviewing the last tool results before deciding on the next step.",
                sessionId,
                IsTransient: true,
                Detail: toolResults[^1].Summary));

            var response = await ExecuteModelCallAsync(
                new ChatCompletionRequest(messages, Temperature: 0, User: BuildModelSessionKey(sessionId, "reflection")),
                "reflection",
                sessionId,
                activity,
                ModelRoute.Fast,
                cancellationToken);

            var reflection = ParseJson<ReflectionDecisionContract>(response.Content);

            if (reflection is null)
            {
                throw new InvalidOperationException("The reflection response did not contain valid JSON.");
            }

            return NormalizeReflectionDecision(
                toolResults,
                new ReflectionDecision(
                reflection.GoalSatisfied,
                reflection.Summary ?? string.Empty,
                reflection.AskFollowUp,
                reflection.FollowUpQuestion ?? string.Empty,
                reflection.RetryRecommended,
                reflection.CorrectionNote ?? string.Empty));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var heuristic = BuildHeuristicReflectionDecision(toolResults);
            var correctionNote = FirstNonEmpty(
                heuristic.CorrectionNote,
                $"Reflection fallback: {TrimError(exception.Message)}");

            return heuristic with { CorrectionNote = correctionNote };
        }
    }

    private static ReflectionDecision BuildHeuristicReflectionDecision(IReadOnlyList<PlannerToolResult> toolResults)
    {
        if (toolResults.Count == 0)
        {
            return new ReflectionDecision(false, string.Empty, false, string.Empty, false, string.Empty);
        }

        var last = toolResults[^1];
        var verificationStatus = ResolveVerificationStatus(last);
        var summary = FirstNonEmpty(last.VerificationSummary, last.Summary, TrimForFeed(last.Output));

        if (last.IsError)
        {
            return new ReflectionDecision(
                false,
                summary,
                false,
                string.Empty,
                true,
                FirstNonEmpty(
                    last.VerificationSummary,
                    "The last tool failed, so a different approach or tool is needed."));
        }

        if (string.Equals(verificationStatus, "awaiting_approval", StringComparison.OrdinalIgnoreCase))
        {
            return new ReflectionDecision(
                true,
                FirstNonEmpty(summary, "The workflow is paused pending approval."),
                false,
                string.Empty,
                false,
                string.Empty);
        }

        if (string.Equals(verificationStatus, "unconfirmed", StringComparison.OrdinalIgnoreCase))
        {
            return new ReflectionDecision(
                false,
                summary,
                false,
                string.Empty,
                true,
                FirstNonEmpty(
                    last.VerificationSummary,
                    "The expected evidence is still unconfirmed, so gather more evidence before finishing."));
        }

        return new ReflectionDecision(
            true,
            summary,
            false,
            string.Empty,
            false,
            string.Empty);
    }

    private static ReflectionDecision NormalizeReflectionDecision(
        IReadOnlyList<PlannerToolResult> toolResults,
        ReflectionDecision decision)
    {
        if (toolResults.Count == 0)
        {
            return decision;
        }

        var last = toolResults[^1];
        var verificationStatus = ResolveVerificationStatus(last);
        var summary = FirstNonEmpty(decision.Summary, last.VerificationSummary, last.Summary, TrimForFeed(last.Output));

        if (last.IsError)
        {
            return decision with
            {
                GoalSatisfied = false,
                Summary = summary,
                RetryRecommended = true,
                CorrectionNote = FirstNonEmpty(
                    decision.CorrectionNote,
                    last.VerificationSummary,
                    "The last tool failed, so a different approach or tool is needed.")
            };
        }

        if (string.Equals(verificationStatus, "awaiting_approval", StringComparison.OrdinalIgnoreCase))
        {
            return decision with
            {
                GoalSatisfied = true,
                AskFollowUp = false,
                FollowUpQuestion = string.Empty,
                RetryRecommended = false,
                CorrectionNote = string.Empty,
                Summary = FirstNonEmpty(summary, "The workflow is paused pending approval.")
            };
        }

        if (string.Equals(verificationStatus, "unconfirmed", StringComparison.OrdinalIgnoreCase))
        {
            if (decision.AskFollowUp)
            {
                return decision with
                {
                    GoalSatisfied = false,
                    Summary = summary
                };
            }

            return decision with
            {
                GoalSatisfied = false,
                Summary = summary,
                RetryRecommended = true,
                CorrectionNote = FirstNonEmpty(
                    decision.CorrectionNote,
                    last.VerificationSummary,
                    "The expected evidence is still unconfirmed, so gather more evidence before finishing.")
            };
        }

        if (!decision.GoalSatisfied
            && !decision.AskFollowUp
            && !decision.RetryRecommended
            && string.IsNullOrWhiteSpace(decision.CorrectionNote))
        {
            return decision with
            {
                Summary = summary,
                RetryRecommended = true,
                CorrectionNote = FirstNonEmpty(
                    last.VerificationSummary,
                    "The evidence is incomplete, so continue the workflow.")
            };
        }

        return decision with { Summary = summary };
    }

    private sealed record IntentDecision(
        AgentIntentKind Intent,
        string Reason,
        string ToolHint,
        string ToolInput,
        bool AskFollowUp,
        string FollowUpQuestion,
        bool RunInBackground);

    private sealed record ReflectionDecision(
        bool GoalSatisfied,
        string Summary,
        bool AskFollowUp,
        string FollowUpQuestion,
        bool RetryRecommended,
        string CorrectionNote);

    private sealed record IntentClassificationContract(
        string? Intent,
        string? Reason,
        string? ToolHint,
        string? ToolInput,
        bool? AskFollowUp,
        string? FollowUpQuestion,
        bool? RunInBackground);

    private sealed record ReflectionDecisionContract(
        bool GoalSatisfied,
        bool AskFollowUp,
        string? FollowUpQuestion,
        bool RetryRecommended,
        string? CorrectionNote,
        string? Summary);

    private static string BuildOfflineSimpleReply(AssistantPlanningRequest request)
    {
        if (TryBuildOfflineUserStateReply(request.UserInput, request.UserState, out var contextualReply))
        {
            return contextualReply;
        }

        return BuildOfflineSimpleReply(request.UserInput);
    }

    private static bool TryBuildOfflineUserStateReply(string input, UserStateSnapshot snapshot, out string reply)
    {
        if (LooksLikeUserStatePlanningQuestion(input))
        {
            reply = BuildOfflineUserStatePlanningReply(input, snapshot);
            return true;
        }

        if (LooksLikeEmailContextQuestion(input))
        {
            reply = BuildOfflineEmailReply(snapshot);
            return true;
        }

        if (LooksLikeTaskContextQuestion(input))
        {
            reply = BuildOfflineTaskReply(snapshot);
            return true;
        }

        reply = string.Empty;
        return false;
    }

    private static string BuildOfflineEmailReply(UserStateSnapshot snapshot)
    {
        if (snapshot.EmailItems.Length == 0)
        {
            return "No email context is available in the local user-state snapshot.";
        }

        var emailAttention = BuildAttentionItems(snapshot)
            .Where(item => string.Equals(item.Kind, "email", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToArray();
        var unreadCount = snapshot.EmailItems.Count(item => item.IsUnread);
        var items = emailAttention.Length > 0
            ? emailAttention
            : snapshot.EmailItems
                .Take(3)
                .Select(item => new UserStateAttentionItem(
                    "email",
                    item.Title,
                    item.Detail,
                    item.IsUnread ? 45 : 20,
                    item.IsUnread ? "unread" : "email item",
                    Unread: item.IsUnread))
                .ToArray();

        var lines = items.Select(item =>
            $"- {item.Title}: {item.Detail}{(item.Unread ? " | unread" : string.Empty)} | {item.Reason}");

        return string.Join(
            Environment.NewLine,
            unreadCount > 0
                ? $"Email snapshot: {unreadCount} unread item(s) out of {snapshot.EmailItems.Length} total."
                : $"Email snapshot: {snapshot.EmailItems.Length} item(s) are available.",
            string.Join(Environment.NewLine, lines));
    }

    private static string BuildOfflineTaskReply(UserStateSnapshot snapshot)
    {
        if (snapshot.TaskItems.Length == 0)
        {
            return "No task context is available in the local user-state snapshot.";
        }

        var items = BuildAttentionItems(snapshot)
            .Where(item => string.Equals(item.Kind, "task", StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToArray();
        var lines = items.Select(item =>
            $"- {item.Title}: {item.Detail}{(string.IsNullOrWhiteSpace(item.DueAt) ? string.Empty : $" | due {item.DueAt}")} | {item.Reason}");

        return string.Join(
            Environment.NewLine,
            $"Task snapshot: {snapshot.TaskItems.Length} task item(s) are available.",
            string.Join(Environment.NewLine, lines));
    }

    private static string BuildOfflineUserStatePlanningReply(string input, UserStateSnapshot snapshot)
    {
        var attentionItems = BuildAttentionItems(snapshot)
            .Where(item => ShouldIncludeAttentionItemForQuestion(item, input))
            .Take(4)
            .ToArray();

        if (attentionItems.Length == 0)
        {
            return snapshot.EmailItems.Length == 0 && snapshot.TaskItems.Length == 0
                ? "No email or task context is available in the local user-state snapshot."
                : "The local user-state snapshot does not contain enough urgent email or task detail to prioritize confidently.";
        }

        var lines = attentionItems.Select(item =>
            $"- [{item.Kind}] {item.Title}: {item.Detail}{(string.IsNullOrWhiteSpace(item.DueAt) ? string.Empty : $" | due {item.DueAt}")} | {item.Reason}");
        var recommended = attentionItems[0];

        return string.Join(
            Environment.NewLine,
            $"Attention snapshot: {snapshot.TaskItems.Length} task item(s), {snapshot.EmailItems.Length} email item(s).",
            $"Suggested next focus: {BuildAttentionRecommendation(recommended)}",
            "Top attention items:",
            string.Join(Environment.NewLine, lines));
    }

    private static string BuildUserStateDecisionSupportBlock(UserStateSnapshot snapshot)
    {
        if (snapshot.EmailItems.Length == 0 && snapshot.TaskItems.Length == 0)
        {
            return "No email or task state is available.";
        }

        var attentionItems = BuildAttentionItems(snapshot).Take(5).ToArray();
        var lines = new List<string>
        {
            $"Counts: {snapshot.TaskItems.Length} task item(s), {snapshot.EmailItems.Length} email item(s)."
        };

        if (attentionItems.Length == 0)
        {
            lines.Add("No urgent attention items were derived from the current snapshot.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add("Derived attention priorities:");
        lines.AddRange(attentionItems.Select(item =>
            $"- [{item.Kind}] {item.Title} | priority={item.Priority} | {item.Reason}{(string.IsNullOrWhiteSpace(item.DueAt) ? string.Empty : $" | due {item.DueAt}")}"));
        lines.Add($"Suggested next focus: {BuildAttentionRecommendation(attentionItems[0])}");
        return string.Join(Environment.NewLine, lines);
    }

    private static UserStateAttentionItem[] BuildAttentionItems(UserStateSnapshot snapshot)
    {
        var items = new List<UserStateAttentionItem>();
        items.AddRange(snapshot.TaskItems.Select(BuildTaskAttentionItem));
        items.AddRange(snapshot.EmailItems.Select(BuildEmailAttentionItem));

        return items
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UserStateAttentionItem BuildTaskAttentionItem(UserStateItem item)
    {
        var priority = 48;
        var reasons = new List<string>();

        if (TryParseDueAt(item.DueAt, out var dueAt))
        {
            var delta = dueAt - DateTimeOffset.Now;

            if (delta < TimeSpan.Zero)
            {
                priority += 55;
                reasons.Add("overdue");
            }
            else if (delta <= TimeSpan.FromHours(24))
            {
                priority += 42;
                reasons.Add("due within 24 hours");
            }
            else if (delta <= TimeSpan.FromDays(3))
            {
                priority += 24;
                reasons.Add("due within 3 days");
            }
            else
            {
                priority += 8;
                reasons.Add("has a due date");
            }
        }

        if (ContainsUrgencySignal(item.Title, item.Detail, item.Category))
        {
            priority += 20;
            reasons.Add("urgent wording");
        }

        if (ContainsActionSignal(item.Title, item.Detail, item.Category))
        {
            priority += 10;
            reasons.Add("action-oriented");
        }

        return new UserStateAttentionItem(
            "task",
            item.Title,
            item.Detail,
            priority,
            FirstNonEmpty(string.Join(", ", reasons), "active task"),
            item.DueAt);
    }

    private static UserStateAttentionItem BuildEmailAttentionItem(UserStateItem item)
    {
        var priority = item.IsUnread ? 42 : 18;
        var reasons = new List<string>();

        if (item.IsUnread)
        {
            reasons.Add("unread");
        }

        if (ContainsUrgencySignal(item.Title, item.Detail, item.Category))
        {
            priority += 24;
            reasons.Add("urgent wording");
        }

        if (ContainsActionSignal(item.Title, item.Detail, item.Category))
        {
            priority += 18;
            reasons.Add("may need a reply or action");
        }

        return new UserStateAttentionItem(
            "email",
            item.Title,
            item.Detail,
            priority,
            FirstNonEmpty(string.Join(", ", reasons), item.IsUnread ? "unread email" : "email item"),
            Unread: item.IsUnread);
    }

    private static bool ShouldIncludeAttentionItemForQuestion(UserStateAttentionItem item, string input)
    {
        var value = input.Trim().ToLowerInvariant();
        var mentionsEmail = value.Contains("email", StringComparison.OrdinalIgnoreCase)
            || value.Contains("emails", StringComparison.OrdinalIgnoreCase)
            || value.Contains("inbox", StringComparison.OrdinalIgnoreCase)
            || value.Contains("mail", StringComparison.OrdinalIgnoreCase);
        var mentionsTask = value.Contains("task", StringComparison.OrdinalIgnoreCase)
            || value.Contains("tasks", StringComparison.OrdinalIgnoreCase)
            || value.Contains("todo", StringComparison.OrdinalIgnoreCase)
            || value.Contains("to-do", StringComparison.OrdinalIgnoreCase)
            || value.Contains("to do", StringComparison.OrdinalIgnoreCase);

        if (mentionsEmail && !mentionsTask)
        {
            return string.Equals(item.Kind, "email", StringComparison.OrdinalIgnoreCase);
        }

        if (mentionsTask && !mentionsEmail)
        {
            return string.Equals(item.Kind, "task", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static string BuildAttentionRecommendation(UserStateAttentionItem item)
    {
        return item.Kind switch
        {
            "task" => $"Start with task \"{item.Title}\" because it is {item.Reason}.",
            "email" => $"Check email \"{item.Title}\" first because it is {item.Reason}.",
            _ => item.Title
        };
    }

    private static bool LooksLikeUserStatePlanningQuestion(string input)
    {
        var value = input.Trim().ToLowerInvariant();

        if (value.StartsWith("send ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("reply ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("compose ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("draft ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("add task", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("create task", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("set task", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var mentionsUserStateSurface = value.Contains("email", StringComparison.OrdinalIgnoreCase)
            || value.Contains("emails", StringComparison.OrdinalIgnoreCase)
            || value.Contains("inbox", StringComparison.OrdinalIgnoreCase)
            || value.Contains("mail", StringComparison.OrdinalIgnoreCase)
            || value.Contains("task", StringComparison.OrdinalIgnoreCase)
            || value.Contains("tasks", StringComparison.OrdinalIgnoreCase)
            || value.Contains("todo", StringComparison.OrdinalIgnoreCase)
            || value.Contains("to-do", StringComparison.OrdinalIgnoreCase)
            || value.Contains("to do", StringComparison.OrdinalIgnoreCase);
        var planningSignals = value.Contains("priority", StringComparison.OrdinalIgnoreCase)
            || value.Contains("prioritize", StringComparison.OrdinalIgnoreCase)
            || value.Contains("triage", StringComparison.OrdinalIgnoreCase)
            || value.Contains("urgent", StringComparison.OrdinalIgnoreCase)
            || value.Contains("attention", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what should i work on", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what should i do first", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what should i tackle", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what needs", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what do i need", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what's next", StringComparison.OrdinalIgnoreCase)
            || value.Contains("whats next", StringComparison.OrdinalIgnoreCase)
            || value.Contains("next up", StringComparison.OrdinalIgnoreCase)
            || value.Contains("focus on", StringComparison.OrdinalIgnoreCase);

        return planningSignals && (mentionsUserStateSurface
            || value.Contains("work on", StringComparison.OrdinalIgnoreCase)
            || value.Contains("do first", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsUrgencySignal(params string[] values)
    {
        var signals = new[]
        {
            "urgent",
            "asap",
            "today",
            "tonight",
            "immediately",
            "deadline",
            "critical",
            "high priority"
        };

        return values.Any(value => signals.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ContainsActionSignal(params string[] values)
    {
        var signals = new[]
        {
            "reply",
            "respond",
            "action required",
            "follow up",
            "follow-up",
            "review",
            "approve",
            "send",
            "finish",
            "complete"
        };

        return values.Any(value => signals.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool TryParseDueAt(string dueAt, out DateTimeOffset value)
    {
        if (DateTimeOffset.TryParse(dueAt, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out value))
        {
            return true;
        }

        return DateTimeOffset.TryParse(dueAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out value);
    }

    private static string ResolveVerificationStatus(PlannerToolResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.VerificationStatus))
        {
            return result.VerificationStatus.Trim();
        }

        return result.IsError ? "failed" : "observed";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private sealed record UserStateAttentionItem(
        string Kind,
        string Title,
        string Detail,
        int Priority,
        string Reason,
        string DueAt = "",
        bool Unread = false);

    private string BuildPersonaScopedSystemPrompt(string taskInstruction)
    {
        return string.Join(
            Environment.NewLine,
            $"You are {_options.AssistantName}, a Windows desktop assistant.",
            $"Configured persona: {AssistantPersonaConfiguration.Resolve(_options)}",
            AssistantPersonaConfiguration.BuildStabilityInstruction(),
            "Presence guidance:",
            AssistantPersonaConfiguration.BuildPresenceGuidance(_options),
            taskInstruction);
    }

    private static string NormalizeClassifierLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(
            ' ',
            value
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Trim()
            .ToLowerInvariant();
    }

    private static string MapIntentToClassifierValue(AgentIntentKind intent) =>
        intent switch
        {
            AgentIntentKind.DirectTool => "direct_tool",
            AgentIntentKind.Clarification => "clarification",
            AgentIntentKind.SimpleReply => "simple_reply",
            AgentIntentKind.Planner => "planner",
            AgentIntentKind.Research => "research",
            AgentIntentKind.Visual => "visual",
            AgentIntentKind.Background => "planner",
            _ => "simple_reply"
        };

    private static bool TryMapClassifierIntent(string? value, out AgentIntentKind intent)
    {
        switch (NormalizeClassifierLabel(value))
        {
            case "direct tool":
                intent = AgentIntentKind.DirectTool;
                return true;
            case "clarification":
                intent = AgentIntentKind.Clarification;
                return true;
            case "simple reply":
                intent = AgentIntentKind.SimpleReply;
                return true;
            case "planner":
                intent = AgentIntentKind.Planner;
                return true;
            case "research":
                intent = AgentIntentKind.Research;
                return true;
            case "visual":
                intent = AgentIntentKind.Visual;
                return true;
            default:
                intent = AgentIntentKind.SimpleReply;
                return false;
        }
    }

    private static string NormalizeClassifierToolHint(string? toolHint, IReadOnlyList<PlannerToolDefinition> tools)
    {
        var normalized = NormalizeClassifierLabel(toolHint);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        foreach (var tool in tools)
        {
            if (NormalizeClassifierLabel(tool.ApiName) == normalized
                || NormalizeClassifierLabel(tool.CommandName) == normalized)
            {
                return tool.ApiName;
            }
        }

        return string.Empty;
    }

    private static bool IsVisualTool(string toolHint) =>
        string.Equals(toolHint, "screen", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolHint, "ambient", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolHint, "analyze image", StringComparison.OrdinalIgnoreCase)
        || string.Equals(toolHint, "analyze document", StringComparison.OrdinalIgnoreCase);
}
