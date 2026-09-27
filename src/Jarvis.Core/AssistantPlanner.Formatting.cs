using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.Core;

internal sealed partial class StructuredAssistantPlanner
{
    private List<ChatMessage> BuildConversationMessages(AssistantPlanningRequest request, PersistedAgentSession? resumedSession = null)
    {
        return
        [
            new ChatMessage("system", [ChatMessageContentPart.FromText(BuildSystemPrompt(request))]),
            new ChatMessage("system", [ChatMessageContentPart.FromText(string.Join(
                Environment.NewLine,
                $"Current local time: {DateTimeOffset.Now:dddd, MMMM d, yyyy h:mm tt zzz}",
                $"Workspace root: {request.WorkspaceRoot}",
                $"Shell execution: {(request.ShellExecutionEnabled ? "enabled" : "disabled")}",
                "Live computer context:",
                request.ComputerContext))]),
            new ChatMessage("system", [ChatMessageContentPart.FromText("Short-term session state:" + Environment.NewLine + request.ConversationState.ToPromptText())]),
            new ChatMessage("system", [ChatMessageContentPart.FromText("User state:" + Environment.NewLine + request.UserState.ToPromptText())]),
            new ChatMessage("system", [ChatMessageContentPart.FromText("User state decision support:" + Environment.NewLine + BuildUserStateDecisionSupportBlock(request.UserState))]),
            new ChatMessage("system", [ChatMessageContentPart.FromText("Memory profile:" + Environment.NewLine + request.MemoryProfile.ToPromptText())]),
            new ChatMessage("system", [ChatMessageContentPart.FromText("Recent memory:" + Environment.NewLine + BuildRecentMemoryBlock(request.RecentMemories))]),
            new ChatMessage("system", [ChatMessageContentPart.FromText("Recent turns:" + Environment.NewLine + BuildRecentTurnBlock(request.RecentTurns))]),
            new ChatMessage("system", [ChatMessageContentPart.FromText("Recent tool evidence:" + Environment.NewLine + BuildRecentToolResultBlock(request.RecentToolResults, Math.Max(1, _options.PlannerRecentToolResultCount)))]),
            new ChatMessage("system", [ChatMessageContentPart.FromText("Persisted workflow state:" + Environment.NewLine + BuildPersistedSessionBlock(resumedSession))]),
            new ChatMessage("user", [ChatMessageContentPart.FromText(request.UserInput)])
        ];
    }

    private ChatMessage[] BuildContextualReplyMessages(AssistantPlanningRequest request, PersistedAgentSession? resumedSession = null)
    {
        return
        [
            new ChatMessage("system", [ChatMessageContentPart.FromText(BuildReplySystemPrompt(request))]),
            new ChatMessage("system", [ChatMessageContentPart.FromText(string.Join(
                Environment.NewLine,
                $"Current local time: {DateTimeOffset.Now:dddd, MMMM d, yyyy h:mm tt zzz}",
                "Live computer context:",
                TruncateForContext(request.ComputerContext, 1200),
                "Short-term session state:",
                TruncateForContext(request.ConversationState.ToPromptText(), 900),
                "User state:",
                TruncateForContext(request.UserState.ToPromptText(), 900),
                "User state decision support:",
                BuildUserStateDecisionSupportBlock(request.UserState),
                "Memory profile:",
                TruncateForContext(request.MemoryProfile.ToPromptText(), 900),
                "Recent conversation:",
                BuildReplyRecentTurnBlock(request.RecentTurns),
                "Recent tool evidence:",
                BuildRecentToolResultBlock(request.RecentToolResults, Math.Max(1, _options.PlannerRecentToolResultCount)),
                "Persisted workflow state:",
                BuildPersistedSessionBlock(resumedSession)))]),
            new ChatMessage("user", [ChatMessageContentPart.FromText(request.UserInput)])
        ];
    }

    private string BuildSystemPrompt(AssistantPlanningRequest request)
    {
        var persona = AssistantPersonaConfiguration.Resolve(_options);

        var lines = new List<string>
        {
            $"You are {request.AssistantName}, a Windows desktop assistant.",
            $"Configured persona: {persona}",
            AssistantPersonaConfiguration.BuildStabilityInstruction(),
            "Presence guidance:",
            AssistantPersonaConfiguration.BuildPresenceGuidance(_options, request.MemoryProfile),
            "You are running inside a structured tool-using agent runtime.",
            "Reason in short steps, use tools when you need evidence, and reflect after each tool result.",
            "When you call a tool, populate `input`, `purpose`, and `expectedEvidence`. Keep `purpose` specific to the current step, and use `expectedEvidence` to state what result would confirm success or expose the next gap.",
            "After each tool result, inspect the returned `verificationStatus`, `verificationSummary`, and `verification` fields before deciding the next step.",
            "Do not claim an action succeeded unless the tool verification evidence supports it.",
            "If a request is ambiguous, ask a concise follow-up question instead of guessing.",
            "If a tool reports approval is required, do not try to bypass it with another tool or a rephrased shell command. Explain the planned change and wait for the user's approval.",
            "Protected system paths and dangerous shell operations are hard-blocked. Do not attempt to work around those restrictions.",
            // Tools are sent as native definitions; repeating them here can exceed local-model context limits.
            "Keep the final answer concise and proactive. Summarize what happened rather than dumping raw tool output."
        };

        if (!request.ShellExecutionEnabled)
        {
            lines.Add("Shell execution may exist as a tool, but it is currently disabled. Prefer other tools.");
        }

        if (!string.IsNullOrWhiteSpace(_options.PlannerInstructions))
        {
            lines.Add("Additional planner instructions:");
            lines.Add(_options.PlannerInstructions.Trim());
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string BuildReplySystemPrompt(AssistantPlanningRequest request)
    {
        var persona = AssistantPersonaConfiguration.Resolve(_options);

        return string.Join(
            Environment.NewLine,
            $"You are {request.AssistantName}, a Windows desktop assistant.",
            $"Configured persona: {persona}",
            AssistantPersonaConfiguration.BuildStabilityInstruction(),
            "Presence guidance:",
            AssistantPersonaConfiguration.BuildPresenceGuidance(_options, request.MemoryProfile),
            "Reply naturally, directly, and with low latency.",
            "Use the provided live computer context and user state when they are relevant to the user's question.",
            "When the user asks what needs attention, what to work on next, or how to prioritize inbox and tasks, synthesize a recommendation from the supplied user-state snapshot instead of listing raw items only.",
            "Honor the learned memory profile when it includes stable preferences for tone, response style, phrasing, address, conversation habits, apps, locations, or automations, but do not let it override the configured persona.",
            "When a preferred user address is known, use it sparingly and naturally rather than in every reply.",
            "If the user is clearly making small talk, answer naturally within the configured presence style; otherwise stay focused and concise.",
            "If the user asks what is open, what they are doing, or what is happening on the computer, ground the answer in that context.",
            "If the request would require taking an action on the computer, do not pretend the action already happened. State the next step briefly or ask for a direct instruction.",
            "Do not mention internal planner, routing, or tool mechanics unless the user explicitly asks.");
    }

    private static ChatMessage BuildAssistantToolCallMessage(ChatCompletionResponse response)
    {
        return new ChatMessage(
            "assistant",
            [ChatMessageContentPart.FromText(response.Content ?? string.Empty)],
            ToolCalls: response.ToolCalls);
    }

    private static string BuildModelSessionKey(string sessionId, string lane)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return string.Empty;
        }

        return $"{sessionId}:{lane}";
    }

    private static string BuildToolMessageContent(PlannerToolResult result)
    {
        return JsonSerializer.Serialize(new
        {
            tool = result.ApiName,
            command = result.Command,
            input = result.Input,
            purpose = result.Purpose,
            expectedEvidence = result.ExpectedEvidence,
            succeeded = !result.IsError,
            verificationStatus = result.VerificationStatus,
            verificationSummary = result.VerificationSummary,
            verification = result.Verification,
            summary = result.Summary,
            output = TrimForFeed(result.Output)
        });
    }

    private static AssistantToolExecution ToToolExecution(PlannerToolResult result)
    {
        return new AssistantToolExecution(
            result.ApiName,
            result.Input,
            string.IsNullOrWhiteSpace(result.Summary) ? TrimForFeed(result.Output) : result.Summary,
            !result.IsError,
            result.Verification,
            result.Output,
            result.VerificationStatus,
            result.VerificationSummary);
    }

    private static PlannerToolResult ToPlannerToolResult(AssistantToolExecution result)
    {
        return new PlannerToolResult(
            result.ToolName,
            result.ToolName,
            result.Input,
            result.OutputText,
            !result.Succeeded,
            Verification: result.VerificationText,
            Summary: result.Summary,
            VerificationStatus: string.IsNullOrWhiteSpace(result.VerificationStatus)
                ? result.Succeeded ? "observed" : "failed"
                : result.VerificationStatus,
            VerificationSummary: string.IsNullOrWhiteSpace(result.VerificationSummary)
                ? result.VerificationText
                : result.VerificationSummary);
    }

    private static string BuildRecentMemoryBlock(IReadOnlyList<MemoryNote> recentMemories)
    {
        if (recentMemories.Count == 0)
        {
            return "No recent saved memories.";
        }

        return string.Join(
            Environment.NewLine,
            recentMemories
                .OrderByDescending(note => note.CreatedAtUtc)
                .Select(note => $"- {note.CreatedAtUtc:yyyy-MM-dd HH:mm}Z | {note.Content}"));
    }

    private static string BuildRecentTurnBlock(IReadOnlyList<AssistantTurn> recentTurns)
    {
        if (recentTurns.Count == 0)
        {
            return "No recent turns.";
        }

        var lines = new List<string>();

        foreach (var turn in recentTurns.TakeLast(8))
        {
            lines.Add($"- User: {TrimForFeed(turn.UserInput)}");
            lines.Add($"  Assistant: {TrimForFeed(turn.ResponseText)}");

            if (turn.ToolResults is not { Length: > 0 })
            {
                continue;
            }

            foreach (var tool in turn.ToolResults.TakeLast(3))
            {
                lines.Add($"  Tool: {tool.ToolName} | success={tool.Succeeded} | verificationStatus={ResolveVerificationStatus(tool)} | summary={TrimForFeed(tool.Summary)}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildReplyRecentTurnBlock(IReadOnlyList<AssistantTurn> recentTurns)
    {
        if (recentTurns.Count == 0)
        {
            return "No recent conversation.";
        }

        var lines = new List<string>();

        foreach (var turn in recentTurns.TakeLast(3))
        {
            lines.Add($"User: {TruncateForContext(turn.UserInput, 160)}");
            lines.Add($"Assistant: {TruncateForContext(turn.ResponseText, 200)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildRecentToolResultBlock(IReadOnlyList<AssistantToolExecution> recentToolResults, int maxResults)
    {
        if (recentToolResults.Count == 0)
        {
            return "No recent tool evidence.";
        }

        return string.Join(
            Environment.NewLine,
            recentToolResults
                .TakeLast(Math.Max(1, maxResults))
                .Select(result =>
                    $"- {result.ToolName} | success={result.Succeeded} | verificationStatus={ResolveVerificationStatus(result)} | summary={TrimForFeed(result.Summary)} | verification={TrimForFeed(ResolveVerificationSummary(result))}"));
    }

    private static string BuildPersistedSessionBlock(PersistedAgentSession? session)
    {
        if (session is null)
        {
            return "No persisted workflow is being resumed.";
        }

        var lines = new List<string>
        {
            $"Status: {session.Status}",
            $"Original request: {TrimForFeed(session.OriginalRequest)}",
            "Resume guidance: continue from the persisted evidence when it still applies, and avoid repeating clearly completed steps unless fresh evidence is needed."
        };

        if (!string.IsNullOrWhiteSpace(session.LatestInput)
            && !string.Equals(session.LatestInput, session.OriginalRequest, StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"Latest input: {TrimForFeed(session.LatestInput)}");
        }

        if (!string.IsNullOrWhiteSpace(session.LatestResponse))
        {
            lines.Add($"Latest response: {TrimForFeed(session.LatestResponse)}");
        }

        if (session.Steps.Length > 0)
        {
            lines.Add("Saved plan steps:");
            lines.AddRange(session.Steps.TakeLast(6).Select(step =>
                $"- [{step.Status}] {step.Title}: {TrimForFeed(step.Detail)}"));
        }

        if (session.ToolResults.Length > 0)
        {
            lines.Add("Saved tool evidence:");
            lines.AddRange(session.ToolResults.TakeLast(6).Select(result =>
                $"- {result.ToolName} | success={result.Succeeded} | verificationStatus={ResolveVerificationStatus(result)} | summary={TrimForFeed(result.Summary)} | verification={TrimForFeed(ResolveVerificationSummary(result))}"));
        }

        if (session.Citations.Length > 0)
        {
            lines.Add($"Saved citations: {session.Citations.Length}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool LooksAmbiguous(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        var trimmed = input.Trim().ToLowerInvariant();
        var ambiguousPhrases = new[]
        {
            "open it",
            "close it",
            "use that",
            "search that",
            "open that",
            "check it",
            "look at it",
            "use the file",
            "open the app",
            "do it",
            "that one",
            "this one",
            "the first one",
            "the second one",
            "the third one",
            "the earlier one",
            "the previous one",
            "the other one"
        };

        return ambiguousPhrases.Any(phrase => trimmed.Equals(phrase, StringComparison.OrdinalIgnoreCase))
            || TryParseContextualReferenceRequest(trimmed, out _, out _)
            || TryParseReferenceSelectorText(trimmed, out _)
            || (trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4
                && new[] { "it", "that", "them", "there", "this" }.Any(token =>
                    trimmed.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    private static string BuildClarificationQuestion(string input)
    {
        var value = input.ToLowerInvariant();

        if (value.Contains("open", StringComparison.OrdinalIgnoreCase))
        {
            return "What specific app, site, file, or folder do you want me to open?";
        }

        if (value.Contains("search", StringComparison.OrdinalIgnoreCase) || value.Contains("research", StringComparison.OrdinalIgnoreCase))
        {
            return "What exact topic should I search for?";
        }

        if (value.Contains("read", StringComparison.OrdinalIgnoreCase) || value.Contains("document", StringComparison.OrdinalIgnoreCase))
        {
            return "Which file or document should I inspect?";
        }

        return "What specific app, file, site, or task do you want me to act on?";
    }

    private static string BuildClarificationQuestion(string input, AssistantPlanningRequest request)
    {
        if (TryBuildContextualClarificationQuestion(input, request, out var question))
        {
            return question;
        }

        return BuildClarificationQuestion(input);
    }

    private static bool TryResolveContextualReference(
        string input,
        AssistantPlanningRequest request,
        out string resolvedInput,
        out string detail)
    {
        resolvedInput = string.Empty;
        detail = string.Empty;

        if (TryParseTypeReferenceRequest(input, out var typedText, out var typedSelector))
        {
            var typedCandidates = GetRecentReferenceCandidates(request);
            if (TrySelectReferenceCandidate(typedCandidates, ReferenceAction.TypeInto, typedSelector, out var typedCandidate)
                && TryBuildResolvedTypeCommand(typedCandidate, typedText, out resolvedInput))
            {
                detail = $"Resolved \"{input.Trim()}\" to `{resolvedInput}` from recent {typedCandidate.Kind} context: {typedCandidate.DisplayLabel}.";
                return true;
            }
        }

        if (!TryParseContextualReferenceRequest(input, out var action, out var selector))
        {
            return false;
        }

        var candidates = GetRecentReferenceCandidates(request);
        if (!TrySelectReferenceCandidate(candidates, action, selector, out var candidate))
        {
            return false;
        }

        if (!TryBuildResolvedReferenceCommand(action, candidate, out resolvedInput))
        {
            return false;
        }

        detail = $"Resolved \"{input.Trim()}\" to `{resolvedInput}` from recent {candidate.Kind} context: {candidate.DisplayLabel}.";
        return true;
    }

    private static bool TryResolveClarificationReference(
        string originalInput,
        string clarificationInput,
        AssistantPlanningRequest request,
        out string resolvedInput,
        out string detail)
    {
        resolvedInput = string.Empty;
        detail = string.Empty;

        if (TryParseTypeReferenceRequest(originalInput, out var typedText, out _)
            && TryParseReferenceSelectorText(clarificationInput, out var typedSelector))
        {
            var typedCandidates = GetRecentReferenceCandidates(request);
            if (TrySelectReferenceCandidate(typedCandidates, ReferenceAction.TypeInto, typedSelector, out var typedCandidate)
                && TryBuildResolvedTypeCommand(typedCandidate, typedText, out resolvedInput))
            {
                detail = $"Resolved the clarification \"{clarificationInput.Trim()}\" to `{resolvedInput}` using recent {typedCandidate.Kind} context: {typedCandidate.DisplayLabel}.";
                return true;
            }
        }

        if (!TryGetReferenceActionAndTail(originalInput, out var action, out _)
            || !TryParseReferenceSelectorText(clarificationInput, out var selector))
        {
            return false;
        }

        var candidates = GetRecentReferenceCandidates(request);
        if (!TrySelectReferenceCandidate(candidates, action, selector, out var candidate)
            || !TryBuildResolvedReferenceCommand(action, candidate, out resolvedInput))
        {
            return false;
        }

        detail = $"Resolved the clarification \"{clarificationInput.Trim()}\" to `{resolvedInput}` using recent {candidate.Kind} context: {candidate.DisplayLabel}.";
        return true;
    }

    private static bool TryBuildContextualClarificationQuestion(
        string input,
        AssistantPlanningRequest request,
        out string question)
    {
        question = string.Empty;

        if (TryParseTypeReferenceRequest(input, out _, out var typedSelector))
        {
            var typedCandidates = GetClarificationReferenceCandidates(
                GetRecentReferenceCandidates(request),
                ReferenceAction.TypeInto,
                typedSelector);

            if (typedCandidates.Count > 0)
            {
                question = typedCandidates.Count == 1
                    ? BuildSingleCandidateClarificationQuestion(ReferenceAction.TypeInto, typedCandidates[0])
                    : BuildMultiCandidateClarificationQuestion(ReferenceAction.TypeInto, typedCandidates);
                return true;
            }
        }

        if (!TryParseContextualReferenceRequest(input, out var action, out var selector))
        {
            return false;
        }

        var candidates = GetClarificationReferenceCandidates(GetRecentReferenceCandidates(request), action, selector);
        if (candidates.Count == 0)
        {
            return false;
        }

        if (candidates.Count == 1)
        {
            question = BuildSingleCandidateClarificationQuestion(action, candidates[0]);
            return true;
        }

        question = BuildMultiCandidateClarificationQuestion(action, candidates);
        return true;
    }

    private static IReadOnlyList<ReferenceCandidate> GetRecentReferenceCandidates(
        AssistantPlanningRequest request,
        int maxCandidates = 6)
    {
        var candidates = new List<ReferenceCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var turn in request.RecentTurns.Reverse())
        {
            AppendReferenceCandidates(turn, candidates, seen, maxCandidates);

            if (candidates.Count >= maxCandidates)
            {
                return candidates;
            }
        }

        AppendReferenceCandidate(
            request.ConversationState.CurrentFocus,
            request.ConversationState.Entries.LastOrDefault()?.TimestampUtc,
            candidates,
            seen,
            maxCandidates);
        return candidates;
    }

    private static void AppendReferenceCandidates(
        AssistantTurn turn,
        List<ReferenceCandidate> candidates,
        HashSet<string> seen,
        int maxCandidates)
    {
        foreach (var tool in (turn.ToolResults ?? Array.Empty<AssistantToolExecution>()).Reverse())
        {
            if (candidates.Count >= maxCandidates)
            {
                return;
            }

            if (!tool.Succeeded || string.IsNullOrWhiteSpace(tool.Input))
            {
                continue;
            }

            AppendToolReferenceCandidates(tool, turn.TimestampUtc, candidates, seen, maxCandidates);
        }

        if (candidates.Count >= maxCandidates)
        {
            return;
        }

        AppendReferenceCandidate(
            TryBuildReferenceCandidate(turn.UserInput, out var userCandidate)
                ? userCandidate with { ObservedAtUtc = turn.TimestampUtc }
                : null,
            candidates,
            seen,
            maxCandidates);
    }

    private static void AppendToolReferenceCandidates(
        AssistantToolExecution tool,
        DateTimeOffset observedAtUtc,
        List<ReferenceCandidate> candidates,
        HashSet<string> seen,
        int maxCandidates)
    {
        foreach (var candidate in BuildToolReferenceCandidates(tool, observedAtUtc))
        {
            AppendReferenceCandidate(candidate, candidates, seen, maxCandidates);

            if (candidates.Count >= maxCandidates)
            {
                return;
            }
        }
    }

    private static IEnumerable<ReferenceCandidate> BuildToolReferenceCandidates(
        AssistantToolExecution tool,
        DateTimeOffset observedAtUtc)
    {
        if (TryBuildScreenUiReferenceCandidates(tool, observedAtUtc, out var uiCandidates))
        {
            foreach (var candidate in uiCandidates)
            {
                yield return candidate;
            }
        }

        if (TryBuildReferenceCandidate(tool.ToolName, tool.Input, out var toolCandidate))
        {
            yield return toolCandidate with { ObservedAtUtc = observedAtUtc };
        }
    }

    private static bool TryBuildScreenUiReferenceCandidates(
        AssistantToolExecution tool,
        DateTimeOffset observedAtUtc,
        out IReadOnlyList<ReferenceCandidate> candidates)
    {
        candidates = Array.Empty<ReferenceCandidate>();

        if (string.IsNullOrWhiteSpace(tool.OutputText)
            || !tool.OutputText.Contains("Structured UI detection", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lines = tool.OutputText
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return false;
        }

        var windowTarget = string.Empty;
        var built = new List<ReferenceCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();

            if (trimmed.StartsWith("- Application guess:", StringComparison.OrdinalIgnoreCase))
            {
                windowTarget = NormalizeScreenUiValue(trimmed["- Application guess:".Length..]);

                if (string.IsNullOrWhiteSpace(windowTarget))
                {
                    windowTarget = NormalizeScreenUiValue(tool.Input);
                }

                continue;
            }

            if (!trimmed.StartsWith("- ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryParseScreenUiElementLine(trimmed[2..], out var role, out var label, out var text))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(windowTarget))
            {
                continue;
            }

            var elementName = !string.IsNullOrWhiteSpace(label) ? label : text;
            if (string.IsNullOrWhiteSpace(elementName))
            {
                continue;
            }

            var candidate = BuildUiElementReferenceCandidate(windowTarget, elementName, role, observedAtUtc);
            var dedupeKey = $"{candidate.Kind}|{candidate.Value}";

            if (seen.Add(dedupeKey))
            {
                built.Add(candidate);
            }
        }

        candidates = built;
        return built.Count > 0;
    }

    private static bool TryParseScreenUiElementLine(
        string line,
        out string role,
        out string label,
        out string text)
    {
        role = string.Empty;
        label = string.Empty;
        text = string.Empty;

        var parts = line
            .Split('|', StringSplitOptions.TrimEntries)
            .Select(part => part.Trim())
            .ToArray();
        if (parts.Length < 3)
        {
            return false;
        }

        role = NormalizeScreenUiValue(parts[1]);
        label = NormalizeScreenUiValue(parts[2]);

        foreach (var part in parts.Skip(3))
        {
            if (part.StartsWith("text=", StringComparison.OrdinalIgnoreCase))
            {
                text = NormalizeScreenUiValue(part["text=".Length..]);
                break;
            }
        }

        return !string.IsNullOrWhiteSpace(role)
            || !string.IsNullOrWhiteSpace(label)
            || !string.IsNullOrWhiteSpace(text);
    }

    private static string NormalizeScreenUiValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().Trim('"');
        return trimmed switch
        {
            "(none)" or "Unknown" or "unknown" or "unnamed" or "unknown region" => string.Empty,
            _ => trimmed
        };
    }

    private static void AppendReferenceCandidate(
        string sourceText,
        DateTimeOffset? observedAtUtc,
        List<ReferenceCandidate> candidates,
        HashSet<string> seen,
        int maxCandidates)
    {
        if (candidates.Count >= maxCandidates
            || !TryBuildReferenceCandidate(sourceText, out var candidate))
        {
            return;
        }

        AppendReferenceCandidate(candidate with { ObservedAtUtc = observedAtUtc }, candidates, seen, maxCandidates);
    }

    private static void AppendReferenceCandidate(
        ReferenceCandidate? candidate,
        List<ReferenceCandidate> candidates,
        HashSet<string> seen,
        int maxCandidates)
    {
        if (candidate is null || candidates.Count >= maxCandidates)
        {
            return;
        }

        var dedupeKey = $"{candidate.Kind}|{candidate.Value.Trim()}";
        if (!seen.Add(dedupeKey))
        {
            return;
        }

        candidates.Add(candidate);
    }

    private static bool TryBuildReferenceCandidate(string sourceText, out ReferenceCandidate candidate)
    {
        candidate = default!;

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var normalized = sourceText.Trim();

        if (UiCommandParsing.TryParseClickTarget(normalized, out var clickWindow, out var clickElement)
            && !string.IsNullOrWhiteSpace(clickElement))
        {
            candidate = BuildUiElementReferenceCandidate(clickWindow, clickElement, "button");
            return true;
        }

        if (UiCommandParsing.TryParseGetTextTarget(normalized, out var textWindow, out var textElement)
            && !string.IsNullOrWhiteSpace(textElement))
        {
            candidate = BuildUiElementReferenceCandidate(textWindow, textElement, "text");
            return true;
        }

        if (UiCommandParsing.TryParseTypeInto(normalized, out var typeWindow, out var typeElement, out _)
            && !string.IsNullOrWhiteSpace(typeElement))
        {
            candidate = BuildUiElementReferenceCandidate(typeWindow, typeElement, "field");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "open app")
            || ToolInputHelper.StartsWithCommand(normalized, "launch")
            || ToolInputHelper.StartsWithCommand(normalized, "focus app")
            || ToolInputHelper.StartsWithCommand(normalized, "focus window")
            || ToolInputHelper.StartsWithCommand(normalized, "switch to")
            || ToolInputHelper.StartsWithCommand(normalized, "activate app")
            || ToolInputHelper.StartsWithCommand(normalized, "bring to front")
            || ToolInputHelper.StartsWithCommand(normalized, "close app")
            || ToolInputHelper.StartsWithCommand(normalized, "close window")
            || ToolInputHelper.StartsWithCommand(normalized, "quit app"))
        {
            var app = ToolInputHelper.GetCommandTail(normalized, "open app", "launch", "focus app", "focus window", "switch to", "activate app", "bring to front", "close app", "close window", "quit app");

            if (!string.IsNullOrWhiteSpace(app))
            {
                candidate = new ReferenceCandidate("app", app.Trim(), $"the app `{TruncateForContext(app, 56)}`");
                return true;
            }
        }

        if (UiCommandParsing.TryParseClickTarget(normalized, out var clickTarget)
            || UiCommandParsing.TryParseGetTextTarget(normalized, out clickTarget)
            || UiCommandParsing.TryParseTypeInto(normalized, out clickTarget, out _)
            || UiCommandParsing.TryParseScrollCommand(normalized, out clickTarget, out _, out _)
            || UiCommandParsing.TryParseMoveWindow(normalized, out clickTarget, out _, out _)
            || UiCommandParsing.TryParseResizeWindow(normalized, out clickTarget, out _, out _)
            || UiCommandParsing.TryParseSnapWindow(normalized, out clickTarget, out _)
            || UiCommandParsing.TryParseWindowState(normalized, out clickTarget, out _))
        {
            candidate = new ReferenceCandidate("app", clickTarget.Trim(), $"the window `{TruncateForContext(clickTarget, 56)}`");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "browse")
            || ToolInputHelper.StartsWithCommand(normalized, "go to")
            || ToolInputHelper.StartsWithCommand(normalized, "visit"))
        {
            var url = ToolInputHelper.GetCommandTail(normalized, "browse", "go to", "visit");

            if (LooksLikeUrl(url))
            {
                candidate = new ReferenceCandidate("url", url.Trim(), $"the site `{TruncateForContext(url, 72)}`");
                return true;
            }
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "open path")
            || ToolInputHelper.StartsWithCommand(normalized, "open file")
            || ToolInputHelper.StartsWithCommand(normalized, "open folder")
            || ToolInputHelper.StartsWithCommand(normalized, "read file")
            || ToolInputHelper.StartsWithCommand(normalized, "show file")
            || ToolInputHelper.StartsWithCommand(normalized, "preview file")
            || ToolInputHelper.StartsWithCommand(normalized, "analyze document")
            || ToolInputHelper.StartsWithCommand(normalized, "inspect document")
            || ToolInputHelper.StartsWithCommand(normalized, "analyze image")
            || ToolInputHelper.StartsWithCommand(normalized, "inspect image"))
        {
            var tail = ToolInputHelper.GetCommandTail(normalized, "open path", "open file", "open folder", "read file", "show file", "preview file", "analyze document", "inspect document", "analyze image", "inspect image");

            if (TryBuildPathReferenceCandidate(tail, out candidate))
            {
                return true;
            }
        }

        if (LooksLikeUrl(normalized))
        {
            candidate = new ReferenceCandidate("url", normalized, $"the site `{TruncateForContext(normalized, 72)}`");
            return true;
        }

        if (TryBuildPathReferenceCandidate(normalized, out candidate))
        {
            return true;
        }

        return false;
    }

    private static ReferenceCandidate BuildUiElementReferenceCandidate(
        string windowTarget,
        string elementName,
        string role,
        DateTimeOffset? observedAtUtc = null)
    {
        var trimmedWindow = windowTarget.Trim().Trim('"');
        var trimmedElement = elementName.Trim().Trim('"');
        var kind = ResolveUiCandidateKind(role, trimmedElement);
        var roleLabel = DescribeUiCandidate(kind, role);
        var value = $"{trimmedWindow} :: {trimmedElement}";

        return new ReferenceCandidate(
            kind,
            value,
            $"the {roleLabel} `{TruncateForContext(trimmedElement, 48)}` in `{TruncateForContext(trimmedWindow, 48)}`",
            observedAtUtc);
    }

    private static string ResolveUiCandidateKind(string role, string elementName)
    {
        var combined = string.Join(" ", new[] { role, elementName }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (combined.Contains("checkbox", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("toggle", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("switch", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_toggle";
        }

        if (combined.Contains("tab", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_tab";
        }

        if (combined.Contains("link", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_link";
        }

        if (combined.Contains("button", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("menu", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_button";
        }

        if (combined.Contains("field", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("input", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("textbox", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("text box", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("search", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("edit", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_field";
        }

        if (combined.Contains("label", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("text", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("heading", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("caption", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("status", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_text";
        }

        return "ui_element";
    }

    private static string DescribeUiCandidate(string kind, string role)
    {
        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalizedRole = role.Trim().ToLowerInvariant();

            if (normalizedRole.Contains("text box", StringComparison.OrdinalIgnoreCase))
            {
                return "text box";
            }

            if (normalizedRole.Contains("search box", StringComparison.OrdinalIgnoreCase))
            {
                return "search box";
            }

            if (normalizedRole.Contains("menu item", StringComparison.OrdinalIgnoreCase))
            {
                return "menu item";
            }

            if (normalizedRole.Contains("button", StringComparison.OrdinalIgnoreCase)
                || normalizedRole.Contains("field", StringComparison.OrdinalIgnoreCase)
                || normalizedRole.Contains("input", StringComparison.OrdinalIgnoreCase)
                || normalizedRole.Contains("textbox", StringComparison.OrdinalIgnoreCase)
                || normalizedRole.Contains("checkbox", StringComparison.OrdinalIgnoreCase)
                || normalizedRole.Contains("toggle", StringComparison.OrdinalIgnoreCase)
                || normalizedRole.Contains("tab", StringComparison.OrdinalIgnoreCase)
                || normalizedRole.Contains("link", StringComparison.OrdinalIgnoreCase)
                || normalizedRole.Contains("label", StringComparison.OrdinalIgnoreCase)
                || normalizedRole.Contains("text", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedRole;
            }
        }

        return kind switch
        {
            "ui_button" => "button",
            "ui_field" => "field",
            "ui_toggle" => "toggle",
            "ui_tab" => "tab",
            "ui_link" => "link",
            "ui_text" => "text",
            _ => "control"
        };
    }

    private static bool TryParseContextualReferenceRequest(
        string input,
        out ReferenceAction action,
        out ReferenceSelector selector)
    {
        selector = default!;

        if (!TryGetReferenceActionAndTail(input, out action, out var tail))
        {
            return false;
        }

        return TryParseReferenceSelectorText(tail, out selector);
    }

    private static bool TryParseTypeReferenceRequest(
        string input,
        out string text,
        out ReferenceSelector selector)
    {
        text = string.Empty;
        selector = default!;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = input.Trim();

        if (UiCommandParsing.TryParseTypeInto(normalized, out var target, out _, out var parsedText)
            && TryParseReferenceSelectorText(target, out selector)
            && !string.IsNullOrWhiteSpace(parsedText))
        {
            text = parsedText.Trim();
            return true;
        }

        foreach (var prefix in new[] { "type", "types", "enter", "input", "write" })
        {
            if (!ToolInputHelper.StartsWithCommand(normalized, prefix))
            {
                continue;
            }

            var remainder = ToolInputHelper.GetCommandTail(normalized, prefix);
            var separatorIndex = remainder.LastIndexOf(" into ", StringComparison.OrdinalIgnoreCase);
            if (separatorIndex <= 0)
            {
                return false;
            }

            var candidateText = remainder[..separatorIndex].Trim().Trim('"');
            var selectorText = remainder[(separatorIndex + " into ".Length)..].Trim();

            if (string.IsNullOrWhiteSpace(candidateText)
                || !TryParseReferenceSelectorText(selectorText, out selector))
            {
                return false;
            }

            text = candidateText;
            return true;
        }

        return false;
    }

    private static bool TryGetReferenceActionAndTail(
        string input,
        out ReferenceAction action,
        out string tail)
    {
        action = ReferenceAction.Unknown;
        tail = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = input.Trim();

        if (ToolInputHelper.StartsWithCommand(normalized, "open")
            || ToolInputHelper.StartsWithCommand(normalized, "browse")
            || ToolInputHelper.StartsWithCommand(normalized, "visit")
            || ToolInputHelper.StartsWithCommand(normalized, "go to"))
        {
            action = ReferenceAction.Open;
            tail = ToolInputHelper.GetCommandTail(normalized, "open", "browse", "visit", "go to");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "focus app")
            || ToolInputHelper.StartsWithCommand(normalized, "focus window")
            || ToolInputHelper.StartsWithCommand(normalized, "switch to")
            || ToolInputHelper.StartsWithCommand(normalized, "activate app")
            || ToolInputHelper.StartsWithCommand(normalized, "bring to front"))
        {
            action = ReferenceAction.Focus;
            tail = ToolInputHelper.GetCommandTail(normalized, "focus app", "focus window", "switch to", "activate app", "bring to front");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "click element")
            || ToolInputHelper.StartsWithCommand(normalized, "click")
            || ToolInputHelper.StartsWithCommand(normalized, "tap"))
        {
            action = ReferenceAction.Click;
            tail = ToolInputHelper.GetCommandTail(normalized, "click element", "click", "tap");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "get element text")
            || ToolInputHelper.StartsWithCommand(normalized, "read element")
            || ToolInputHelper.StartsWithCommand(normalized, "element text")
            || ToolInputHelper.StartsWithCommand(normalized, "what text")
            || ToolInputHelper.StartsWithCommand(normalized, "get text from")
            || ToolInputHelper.StartsWithCommand(normalized, "read text from"))
        {
            action = ReferenceAction.GetText;
            tail = ToolInputHelper.GetCommandTail(normalized, "get element text", "read element", "element text", "what text", "get text from", "read text from");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "read")
            || ToolInputHelper.StartsWithCommand(normalized, "look at")
            || ToolInputHelper.StartsWithCommand(normalized, "check")
            || ToolInputHelper.StartsWithCommand(normalized, "inspect")
            || ToolInputHelper.StartsWithCommand(normalized, "show")
            || ToolInputHelper.StartsWithCommand(normalized, "preview")
            || ToolInputHelper.StartsWithCommand(normalized, "use"))
        {
            action = ReferenceAction.Inspect;
            tail = ToolInputHelper.GetCommandTail(normalized, "read", "look at", "check", "inspect", "show", "preview", "use");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "search")
            || ToolInputHelper.StartsWithCommand(normalized, "research"))
        {
            action = ReferenceAction.Search;
            tail = ToolInputHelper.GetCommandTail(normalized, "search", "research");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "close"))
        {
            action = ReferenceAction.Close;
            tail = ToolInputHelper.GetCommandTail(normalized, "close");
            return true;
        }

        return false;
    }

    private static bool TryParseReferenceSelectorText(string input, out ReferenceSelector selector)
    {
        selector = default!;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = NormalizeReferenceText(input);
        if (string.IsNullOrWhiteSpace(normalized)
            || LooksLikeUrl(normalized)
            || TryBuildPathReferenceCandidate(normalized, out _))
        {
            return false;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var allowedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a",
            "an",
            "app",
            "box",
            "button",
            "checkbox",
            "control",
            "document",
            "earlier",
            "element",
            "file",
            "field",
            "first",
            "folder",
            "from",
            "former",
            "image",
            "input",
            "it",
            "last",
            "label",
            "latest",
            "link",
            "menu",
            "most",
            "newest",
            "one",
            "ones",
            "page",
            "path",
            "pdf",
            "photo",
            "picture",
            "please",
            "previous",
            "prior",
            "program",
            "query",
            "recent",
            "screenshot",
            "second",
            "site",
            "that",
            "text",
            "textbox",
            "the",
            "third",
            "today",
            "toggle",
            "this",
            "tab",
            "topic",
            "url",
            "website",
            "window",
            "other"
        };

        if (tokens.Any(token => !allowedTokens.Contains(token)))
        {
            return false;
        }

        var kindHint = ResolveReferenceKindHint(normalized);
        var hasKindHint = !string.IsNullOrWhiteSpace(kindHint);
        var mode = ReferenceSelectorMode.Default;
        var candidateIndex = 0;

        if (tokens.Contains("other", StringComparer.OrdinalIgnoreCase))
        {
            mode = ReferenceSelectorMode.Other;
        }
        else if (tokens.Contains("today", StringComparer.OrdinalIgnoreCase)
            && (tokens.Contains("earlier", StringComparer.OrdinalIgnoreCase)
                || tokens.Contains("previous", StringComparer.OrdinalIgnoreCase)
                || tokens.Contains("prior", StringComparer.OrdinalIgnoreCase)
                || tokens.Contains("former", StringComparer.OrdinalIgnoreCase)))
        {
            mode = ReferenceSelectorMode.EarlierToday;
        }
        else if (tokens.Contains("second", StringComparer.OrdinalIgnoreCase))
        {
            candidateIndex = 1;
            mode = ReferenceSelectorMode.Indexed;
        }
        else if (tokens.Contains("third", StringComparer.OrdinalIgnoreCase))
        {
            candidateIndex = 2;
            mode = ReferenceSelectorMode.Indexed;
        }
        else if (tokens.Contains("first", StringComparer.OrdinalIgnoreCase))
        {
            candidateIndex = 0;
            mode = ReferenceSelectorMode.Indexed;
        }
        else if (tokens.Contains("earlier", StringComparer.OrdinalIgnoreCase)
            || tokens.Contains("previous", StringComparer.OrdinalIgnoreCase)
            || tokens.Contains("prior", StringComparer.OrdinalIgnoreCase)
            || tokens.Contains("former", StringComparer.OrdinalIgnoreCase))
        {
            candidateIndex = 1;
            mode = ReferenceSelectorMode.Indexed;
        }
        else if (hasKindHint
            && (tokens.Contains("last", StringComparer.OrdinalIgnoreCase)
                || tokens.Contains("latest", StringComparer.OrdinalIgnoreCase)
                || tokens.Contains("recent", StringComparer.OrdinalIgnoreCase)
                || tokens.Contains("newest", StringComparer.OrdinalIgnoreCase)
                || normalized.Contains("most recent", StringComparison.OrdinalIgnoreCase)))
        {
            candidateIndex = 0;
            mode = ReferenceSelectorMode.Indexed;
        }

        var hasReferenceSignal = hasKindHint
            || tokens.Any(token =>
                token is "it" or "that" or "this" or "one" or "ones"
                or "first" or "second" or "third"
                or "earlier" or "previous" or "prior" or "former"
                or "last" or "latest" or "recent" or "newest"
                or "other");
        if (!hasReferenceSignal)
        {
            return false;
        }

        selector = new ReferenceSelector(kindHint, mode, candidateIndex);
        return true;
    }

    private static string NormalizeReferenceText(string input)
    {
        return string.Join(
            " ",
            input
                .Trim()
                .TrimEnd('.', '?', '!', ',')
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ResolveReferenceKindHint(string input)
    {
        if (input.Contains("button", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_button";
        }

        if (input.Contains("field", StringComparison.OrdinalIgnoreCase)
            || input.Contains("input", StringComparison.OrdinalIgnoreCase)
            || input.Contains("textbox", StringComparison.OrdinalIgnoreCase)
            || input.Contains("text box", StringComparison.OrdinalIgnoreCase)
            || input.Contains("search box", StringComparison.OrdinalIgnoreCase)
            || input.Contains("search field", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_field";
        }

        if (input.Contains("checkbox", StringComparison.OrdinalIgnoreCase)
            || input.Contains("toggle", StringComparison.OrdinalIgnoreCase)
            || input.Contains("switch", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_toggle";
        }

        if (input.Contains("tab", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_tab";
        }

        if (input.Contains("link", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_link";
        }

        if (input.Contains("label", StringComparison.OrdinalIgnoreCase)
            || input.Contains("text", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_text";
        }

        if (input.Contains("element", StringComparison.OrdinalIgnoreCase)
            || input.Contains("control", StringComparison.OrdinalIgnoreCase))
        {
            return "ui_element";
        }

        if (input.Contains("app", StringComparison.OrdinalIgnoreCase)
            || input.Contains("window", StringComparison.OrdinalIgnoreCase)
            || input.Contains("program", StringComparison.OrdinalIgnoreCase))
        {
            return "app";
        }

        if (input.Contains("site", StringComparison.OrdinalIgnoreCase)
            || input.Contains("website", StringComparison.OrdinalIgnoreCase)
            || input.Contains("url", StringComparison.OrdinalIgnoreCase)
            || input.Contains("page", StringComparison.OrdinalIgnoreCase))
        {
            return "url";
        }

        if (input.Contains("image", StringComparison.OrdinalIgnoreCase)
            || input.Contains("photo", StringComparison.OrdinalIgnoreCase)
            || input.Contains("picture", StringComparison.OrdinalIgnoreCase)
            || input.Contains("screenshot", StringComparison.OrdinalIgnoreCase))
        {
            return "image";
        }

        if (input.Contains("document", StringComparison.OrdinalIgnoreCase)
            || input.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "document";
        }

        if (input.Contains("folder", StringComparison.OrdinalIgnoreCase)
            || input.Contains("path", StringComparison.OrdinalIgnoreCase))
        {
            return "path";
        }

        if (input.Contains("file", StringComparison.OrdinalIgnoreCase))
        {
            return "file";
        }

        if (input.Contains("topic", StringComparison.OrdinalIgnoreCase)
            || input.Contains("query", StringComparison.OrdinalIgnoreCase))
        {
            return "query";
        }

        return string.Empty;
    }

    private static List<ReferenceCandidate> GetCompatibleReferenceCandidates(
        IReadOnlyList<ReferenceCandidate> candidates,
        ReferenceAction action,
        ReferenceSelector selector)
    {
        return candidates
            .Where(candidate => IsCandidateCompatible(candidate, action))
            .Where(candidate => MatchesReferenceKindHint(candidate, selector.KindHint))
            .ToList();
    }

    private static List<ReferenceCandidate> GetClarificationReferenceCandidates(
        IReadOnlyList<ReferenceCandidate> candidates,
        ReferenceAction action,
        ReferenceSelector selector)
    {
        var compatibleCandidates = GetCompatibleReferenceCandidates(candidates, action, selector);

        return selector.Mode switch
        {
            ReferenceSelectorMode.Other => compatibleCandidates.Skip(1).ToList(),
            ReferenceSelectorMode.EarlierToday => GetEarlierTodayCandidates(compatibleCandidates),
            _ => compatibleCandidates
        };
    }

    private static bool TrySelectReferenceCandidate(
        IReadOnlyList<ReferenceCandidate> candidates,
        ReferenceAction action,
        ReferenceSelector selector,
        out ReferenceCandidate candidate)
    {
        candidate = default!;
        var compatibleCandidates = GetCompatibleReferenceCandidates(candidates, action, selector);

        if (compatibleCandidates.Count == 0)
        {
            return false;
        }

        switch (selector.Mode)
        {
            case ReferenceSelectorMode.Indexed:
                if (selector.CandidateIndex >= compatibleCandidates.Count)
                {
                    return false;
                }

                candidate = compatibleCandidates[selector.CandidateIndex];
                return true;
            case ReferenceSelectorMode.Other:
                if (compatibleCandidates.Count == 2)
                {
                    candidate = compatibleCandidates[1];
                    return true;
                }

                return false;
            case ReferenceSelectorMode.EarlierToday:
                var earlierTodayCandidates = GetEarlierTodayCandidates(compatibleCandidates);
                if (earlierTodayCandidates.Count == 0)
                {
                    return false;
                }

                candidate = earlierTodayCandidates[0];
                return true;
            default:
                if (compatibleCandidates.Count != 1)
                {
                    return false;
                }

                candidate = compatibleCandidates[0];
                return true;
        }
    }

    private static bool IsCandidateCompatible(ReferenceCandidate candidate, ReferenceAction action)
    {
        return action switch
        {
            ReferenceAction.Open => candidate.Kind is "app" or "url" or "file" or "document" or "image" or "path",
            ReferenceAction.Focus => candidate.Kind is "app",
            ReferenceAction.Click => candidate.Kind is "app" or "ui_button" or "ui_field" or "ui_link" or "ui_tab" or "ui_toggle" or "ui_element",
            ReferenceAction.GetText => candidate.Kind is "app" or "ui_text" or "ui_field" or "ui_link" or "ui_element",
            ReferenceAction.TypeInto => candidate.Kind is "app" or "ui_field" or "ui_element",
            ReferenceAction.Inspect => candidate.Kind is "url" or "file" or "document" or "image",
            ReferenceAction.Search => candidate.Kind is "query",
            ReferenceAction.Close => candidate.Kind is "app",
            _ => false
        };
    }

    private static List<ReferenceCandidate> GetEarlierTodayCandidates(IReadOnlyList<ReferenceCandidate> compatibleCandidates)
    {
        var today = DateTimeOffset.Now.Date;
        var todayCandidates = compatibleCandidates
            .Where(candidate => candidate.ObservedAtUtc.HasValue
                && candidate.ObservedAtUtc.Value.ToLocalTime().Date == today)
            .ToList();

        return todayCandidates.Count <= 1
            ? []
            : todayCandidates.Skip(1).ToList();
    }

    private static bool MatchesReferenceKindHint(ReferenceCandidate candidate, string kindHint)
    {
        if (string.IsNullOrWhiteSpace(kindHint))
        {
            return true;
        }

        return kindHint switch
        {
            "ui_button" => candidate.Kind is "ui_button",
            "ui_field" => candidate.Kind is "ui_field",
            "ui_toggle" => candidate.Kind is "ui_toggle",
            "ui_tab" => candidate.Kind is "ui_tab",
            "ui_link" => candidate.Kind is "ui_link",
            "ui_text" => candidate.Kind is "ui_text",
            "ui_element" => candidate.Kind.StartsWith("ui_", StringComparison.OrdinalIgnoreCase),
            "app" => candidate.Kind is "app",
            "url" => candidate.Kind is "url",
            "query" => candidate.Kind is "query",
            "document" => candidate.Kind is "document",
            "image" => candidate.Kind is "image",
            "path" => candidate.Kind is "path",
            "file" => candidate.Kind is "file" or "document" or "image" or "path",
            _ => true
        };
    }

    private static bool TryBuildReferenceCandidate(string toolName, string input, out ReferenceCandidate candidate)
    {
        candidate = default!;
        var normalizedTool = toolName.Trim().ToLowerInvariant();
        var trimmedInput = input.Trim();

        if (string.IsNullOrWhiteSpace(trimmedInput))
        {
            return false;
        }

        if (normalizedTool is "open app" or "apps" or "focus app" or "close app")
        {
            candidate = new ReferenceCandidate("app", trimmedInput, $"the app `{TruncateForContext(trimmedInput, 56)}`");
            return true;
        }

        if (normalizedTool is "click element")
        {
            if (UiCommandParsing.TryParseClickTarget($"click element {trimmedInput}", out var clickWindow, out var clickElement)
                && !string.IsNullOrWhiteSpace(clickElement))
            {
                candidate = BuildUiElementReferenceCandidate(clickWindow, clickElement, "button");
                return true;
            }

            candidate = new ReferenceCandidate("app", trimmedInput, $"the window `{TruncateForContext(trimmedInput, 56)}`");
            return true;
        }

        if (normalizedTool is "get element text")
        {
            if (UiCommandParsing.TryParseGetTextTarget($"get element text {trimmedInput}", out var textWindow, out var textElement)
                && !string.IsNullOrWhiteSpace(textElement))
            {
                candidate = BuildUiElementReferenceCandidate(textWindow, textElement, "text");
                return true;
            }

            candidate = new ReferenceCandidate("app", trimmedInput, $"the window `{TruncateForContext(trimmedInput, 56)}`");
            return true;
        }

        if (normalizedTool is "type into")
        {
            if (UiCommandParsing.TryParseTypeInto($"type into {trimmedInput}", out var typeWindow, out var typeElement, out _)
                && !string.IsNullOrWhiteSpace(typeElement))
            {
                candidate = BuildUiElementReferenceCandidate(typeWindow, typeElement, "field");
                return true;
            }

            if (UiCommandParsing.TryParseTypeInto($"type into {trimmedInput}", out var typeTarget, out _))
            {
                candidate = new ReferenceCandidate("app", typeTarget, $"the window `{TruncateForContext(typeTarget, 56)}`");
                return true;
            }

            return false;
        }

        if (normalizedTool is "scroll")
        {
            if (UiCommandParsing.TryParseScrollCommand($"scroll {trimmedInput}", out var scrollTarget, out _, out _))
            {
                candidate = new ReferenceCandidate("app", scrollTarget, $"the window `{TruncateForContext(scrollTarget, 56)}`");
                return true;
            }

            return false;
        }

        if (normalizedTool is "window state")
        {
            if (UiCommandParsing.TryParseWindowState($"window state {trimmedInput}", out var windowTarget, out _))
            {
                candidate = new ReferenceCandidate("app", windowTarget, $"the window `{TruncateForContext(windowTarget, 56)}`");
                return true;
            }

            return false;
        }

        if (normalizedTool is "move window")
        {
            if (UiCommandParsing.TryParseMoveWindow($"move window {trimmedInput}", out var moveTarget, out _, out _))
            {
                candidate = new ReferenceCandidate("app", moveTarget, $"the window `{TruncateForContext(moveTarget, 56)}`");
                return true;
            }

            return false;
        }

        if (normalizedTool is "resize window")
        {
            if (UiCommandParsing.TryParseResizeWindow($"resize window {trimmedInput}", out var resizeTarget, out _, out _))
            {
                candidate = new ReferenceCandidate("app", resizeTarget, $"the window `{TruncateForContext(resizeTarget, 56)}`");
                return true;
            }

            return false;
        }

        if (normalizedTool is "snap window")
        {
            if (UiCommandParsing.TryParseSnapWindow($"snap window {trimmedInput}", out var snapTarget, out _))
            {
                candidate = new ReferenceCandidate("app", snapTarget, $"the window `{TruncateForContext(snapTarget, 56)}`");
                return true;
            }

            return false;
        }

        if (normalizedTool is "send hotkey")
        {
            if (UiCommandParsing.TryParseHotkeyCommand($"send hotkey {trimmedInput}", out var hotkeyTarget, out _))
            {
                candidate = new ReferenceCandidate("app", hotkeyTarget, $"the window `{TruncateForContext(hotkeyTarget, 56)}`");
                return true;
            }

            return false;
        }

        if (normalizedTool is "clipboard")
        {
            if (UiCommandParsing.TryParseClipboardCommand($"clipboard {trimmedInput}".Trim(), out var clipboardAction, out var clipboardPayload)
                && string.Equals(clipboardAction, "paste", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(clipboardPayload))
            {
                candidate = new ReferenceCandidate("app", clipboardPayload, $"the window `{TruncateForContext(clipboardPayload, 56)}`");
                return true;
            }

            return false;
        }

        if (normalizedTool is "browse" && LooksLikeUrl(trimmedInput))
        {
            candidate = new ReferenceCandidate("url", trimmedInput, $"the site `{TruncateForContext(trimmedInput, 72)}`");
            return true;
        }

        if (normalizedTool is "search web" or "research")
        {
            candidate = new ReferenceCandidate("query", trimmedInput, $"the topic `{TruncateForContext(trimmedInput, 72)}`");
            return true;
        }

        if (normalizedTool is "open path" or "read file" or "analyze document" or "analyze image")
        {
            return TryBuildPathReferenceCandidate(trimmedInput, out candidate);
        }

        return false;
    }

    private static bool TryBuildPathReferenceCandidate(string value, out ReferenceCandidate candidate)
    {
        candidate = default!;
        var trimmed = value.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (LooksLikeImagePath(trimmed))
        {
            candidate = new ReferenceCandidate("image", trimmed, $"the image `{TruncateForContext(trimmed, 72)}`");
            return true;
        }

        if (LooksLikeAnalyzableDocumentPath(trimmed))
        {
            candidate = new ReferenceCandidate("document", trimmed, $"the document `{TruncateForContext(trimmed, 72)}`");
            return true;
        }

        if (LooksLikeTextualFilePath(trimmed))
        {
            candidate = new ReferenceCandidate("file", trimmed, $"the file `{TruncateForContext(trimmed, 72)}`");
            return true;
        }

        if (trimmed.Contains('\\')
            || trimmed.StartsWith(".\\", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("..\\", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/", StringComparison.OrdinalIgnoreCase)
            || (trimmed.Length >= 3
                && char.IsLetter(trimmed[0])
                && trimmed[1] == ':'
                && (trimmed[2] == '\\' || trimmed[2] == '/')))
        {
            candidate = new ReferenceCandidate("path", trimmed, $"the path `{TruncateForContext(trimmed, 72)}`");
            return true;
        }

        return false;
    }

    private static bool TryBuildResolvedReferenceCommand(
        ReferenceAction action,
        ReferenceCandidate candidate,
        out string command)
    {
        command = string.Empty;

        return action switch
        {
            ReferenceAction.Open => TryBuildResolvedOpenCommand(candidate, out command),
            ReferenceAction.Focus => TryBuildResolvedFocusCommand(candidate, out command),
            ReferenceAction.Click => TryBuildResolvedClickCommand(candidate, out command),
            ReferenceAction.GetText => TryBuildResolvedGetTextCommand(candidate, out command),
            ReferenceAction.Inspect => TryBuildResolvedInspectCommand(candidate, out command),
            ReferenceAction.Search => TryBuildResolvedSearchCommand(candidate, out command),
            ReferenceAction.Close => TryBuildResolvedCloseCommand(candidate, out command),
            _ => false
        };
    }

    private static bool TryBuildResolvedOpenCommand(ReferenceCandidate candidate, out string command)
    {
        command = candidate.Kind switch
        {
            "app" => $"open app {candidate.Value}",
            "url" => $"browse {candidate.Value}",
            "file" or "document" or "image" or "path" => $"open path {candidate.Value}",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(command);
    }

    private static bool TryBuildResolvedTypeCommand(ReferenceCandidate candidate, string text, out string command)
    {
        command = string.Empty;
        var trimmedText = text.Trim();

        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            return false;
        }

        if (string.Equals(candidate.Kind, "app", StringComparison.OrdinalIgnoreCase))
        {
            command = $"type into {candidate.Value} {trimmedText}";
            return true;
        }

        if (candidate.Kind.StartsWith("ui_", StringComparison.OrdinalIgnoreCase))
        {
            command = $"type into {candidate.Value} :: {trimmedText}";
            return true;
        }

        return false;
    }

    private static bool TryBuildResolvedInspectCommand(ReferenceCandidate candidate, out string command)
    {
        command = candidate.Kind switch
        {
            "url" => $"browse {candidate.Value}",
            "file" => $"read file {candidate.Value}",
            "document" => $"analyze document {candidate.Value}",
            "image" => $"analyze image {candidate.Value}",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(command);
    }

    private static bool TryBuildResolvedFocusCommand(ReferenceCandidate candidate, out string command)
    {
        command = string.Equals(candidate.Kind, "app", StringComparison.OrdinalIgnoreCase)
            ? $"focus app {candidate.Value}"
            : string.Empty;

        return !string.IsNullOrWhiteSpace(command);
    }

    private static bool TryBuildResolvedClickCommand(ReferenceCandidate candidate, out string command)
    {
        command = string.Equals(candidate.Kind, "app", StringComparison.OrdinalIgnoreCase)
            || candidate.Kind.StartsWith("ui_", StringComparison.OrdinalIgnoreCase)
            ? $"click element {candidate.Value}"
            : string.Empty;

        return !string.IsNullOrWhiteSpace(command);
    }

    private static bool TryBuildResolvedGetTextCommand(ReferenceCandidate candidate, out string command)
    {
        command = string.Equals(candidate.Kind, "app", StringComparison.OrdinalIgnoreCase)
            || candidate.Kind.StartsWith("ui_", StringComparison.OrdinalIgnoreCase)
            ? $"get element text {candidate.Value}"
            : string.Empty;

        return !string.IsNullOrWhiteSpace(command);
    }

    private static bool TryBuildResolvedSearchCommand(ReferenceCandidate candidate, out string command)
    {
        command = string.Equals(candidate.Kind, "query", StringComparison.OrdinalIgnoreCase)
            ? $"research {candidate.Value}"
            : string.Empty;

        return !string.IsNullOrWhiteSpace(command);
    }

    private static bool TryBuildResolvedCloseCommand(ReferenceCandidate candidate, out string command)
    {
        command = string.Equals(candidate.Kind, "app", StringComparison.OrdinalIgnoreCase)
            ? $"close app {candidate.Value}"
            : string.Empty;

        return !string.IsNullOrWhiteSpace(command);
    }

    private static string BuildSingleCandidateClarificationQuestion(
        ReferenceAction action,
        ReferenceCandidate candidate)
    {
        return action switch
        {
            ReferenceAction.Open => $"Do you want me to open {candidate.DisplayLabel}, or something else?",
            ReferenceAction.Focus => $"Do you want me to switch to {candidate.DisplayLabel}?",
            ReferenceAction.Click => $"Do you want me to click {candidate.DisplayLabel}?",
            ReferenceAction.GetText => $"Do you want me to read text from {candidate.DisplayLabel}?",
            ReferenceAction.TypeInto => $"Do you want me to type into {candidate.DisplayLabel}?",
            ReferenceAction.Inspect => $"Do you want me to inspect {candidate.DisplayLabel}?",
            ReferenceAction.Search => $"Do you want me to research {candidate.DisplayLabel}?",
            ReferenceAction.Close => $"Do you want me to close {candidate.DisplayLabel}?",
            _ => string.Empty
        };
    }

    private static string BuildMultiCandidateClarificationQuestion(
        ReferenceAction action,
        IReadOnlyList<ReferenceCandidate> candidates)
    {
        var verb = action switch
        {
            ReferenceAction.Open => "open",
            ReferenceAction.Focus => "switch to",
            ReferenceAction.Click => "click",
            ReferenceAction.GetText => "read text from",
            ReferenceAction.TypeInto => "type into",
            ReferenceAction.Inspect => "inspect",
            ReferenceAction.Search => "research",
            ReferenceAction.Close => "close",
            _ => "use"
        };
        var labels = candidates
            .Take(3)
            .Select(candidate => candidate.DisplayLabel)
            .ToArray();

        return $"Do you want me to {verb} {JoinWithOr(labels)}, or something else?";
    }

    private static string JoinWithOr(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        if (values.Count == 1)
        {
            return values[0];
        }

        if (values.Count == 2)
        {
            return $"{values[0]} or {values[1]}";
        }

        return string.Join(", ", values.Take(values.Count - 1)) + $", or {values[^1]}";
    }

    private static bool LooksLikeUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || value.Contains(".com/", StringComparison.OrdinalIgnoreCase)
            || value.Contains(".org/", StringComparison.OrdinalIgnoreCase)
            || value.Contains(".net/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTextualFilePath(string input)
    {
        return input.Contains(".txt", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".md", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".json", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".cs", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".xml", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".yml", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".yaml", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".csv", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".log", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeAnalyzableDocumentPath(string input)
    {
        return input.Contains(".pdf", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".tif", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private enum ReferenceAction
    {
        Unknown,
        Open,
        Focus,
        Click,
        GetText,
        TypeInto,
        Inspect,
        Search,
        Close
    }

    private enum ReferenceSelectorMode
    {
        Default,
        Indexed,
        Other,
        EarlierToday
    }

    private sealed record ReferenceSelector(
        string KindHint,
        ReferenceSelectorMode Mode,
        int CandidateIndex = 0);

    private sealed record ReferenceCandidate(
        string Kind,
        string Value,
        string DisplayLabel,
        DateTimeOffset? ObservedAtUtc = null);

    private static bool TryInferToolHint(string input, out string toolName)
    {
        var value = input.Trim().ToLowerInvariant();

        if (value.Contains("weather", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "weather";
            return true;
        }

        if (value.Contains("preference", StringComparison.OrdinalIgnoreCase)
            || value.Contains("profile", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "preferences";
            return true;
        }

        if (value.Contains("privacy", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "privacy";
            return true;
        }

        if (value.Contains("reminder", StringComparison.OrdinalIgnoreCase)
            || value.Contains("remind me", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "reminders";
            return true;
        }

        // Require a time or date question to avoid matching unrelated phrases such as "every time".
        if (Regex.IsMatch(value, @"\bwhat(?:'s| is)?(?: the)? (?:time|date|day)\b|\btime is it\b|\bcurrent (?:time|date)\b|\btoday'?s date\b"))
        {
            toolName = "time";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "open app")
            || ToolInputHelper.StartsWithCommand(value, "launch app")
            || ToolInputHelper.StartsWithCommand(value, "launch")
            || ToolInputHelper.StartsWithCommand(value, "start app"))
        {
            toolName = "open app";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "focus app")
            || ToolInputHelper.StartsWithCommand(value, "focus window")
            || ToolInputHelper.StartsWithCommand(value, "switch to")
            || ToolInputHelper.StartsWithCommand(value, "activate app")
            || ToolInputHelper.StartsWithCommand(value, "bring to front"))
        {
            toolName = "focus app";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "windows")
            || ToolInputHelper.StartsWithCommand(value, "list windows")
            || ToolInputHelper.StartsWithCommand(value, "show windows")
            || ToolInputHelper.StartsWithCommand(value, "visible windows"))
        {
            toolName = "windows";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "click element")
            || ToolInputHelper.StartsWithCommand(value, "click")
            || ToolInputHelper.StartsWithCommand(value, "tap"))
        {
            toolName = "click element";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "get element text")
            || ToolInputHelper.StartsWithCommand(value, "read element")
            || ToolInputHelper.StartsWithCommand(value, "element text")
            || ToolInputHelper.StartsWithCommand(value, "what text")
            || ToolInputHelper.StartsWithCommand(value, "get text from")
            || ToolInputHelper.StartsWithCommand(value, "read text from"))
        {
            toolName = "get element text";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "type into")
            || ToolInputHelper.StartsWithCommand(value, "types into")
            || ToolInputHelper.StartsWithCommand(value, "enter into")
            || ToolInputHelper.StartsWithCommand(value, "input into")
            || ToolInputHelper.StartsWithCommand(value, "write into"))
        {
            toolName = "type into";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "close app")
            || ToolInputHelper.StartsWithCommand(value, "close window")
            || ToolInputHelper.StartsWithCommand(value, "quit app"))
        {
            toolName = "close app";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "window state")
            || ToolInputHelper.StartsWithCommand(value, "minimize app")
            || ToolInputHelper.StartsWithCommand(value, "maximize app")
            || ToolInputHelper.StartsWithCommand(value, "restore app")
            || ToolInputHelper.StartsWithCommand(value, "minimize window")
            || ToolInputHelper.StartsWithCommand(value, "maximize window")
            || ToolInputHelper.StartsWithCommand(value, "restore window"))
        {
            toolName = "window state";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "move window")
            || ToolInputHelper.StartsWithCommand(value, "move app")
            || ToolInputHelper.StartsWithCommand(value, "position window"))
        {
            toolName = "move window";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "resize window")
            || ToolInputHelper.StartsWithCommand(value, "resize app"))
        {
            toolName = "resize window";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "snap window")
            || ToolInputHelper.StartsWithCommand(value, "snap app")
            || ToolInputHelper.StartsWithCommand(value, "dock window"))
        {
            toolName = "snap window";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "send hotkey")
            || ToolInputHelper.StartsWithCommand(value, "press key")
            || ToolInputHelper.StartsWithCommand(value, "send shortcut")
            || ToolInputHelper.StartsWithCommand(value, "shortcut"))
        {
            toolName = "send hotkey";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "open path")
            || ToolInputHelper.StartsWithCommand(value, "open file")
            || ToolInputHelper.StartsWithCommand(value, "open folder"))
        {
            toolName = "open path";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "read file")
            || ToolInputHelper.StartsWithCommand(value, "show file")
            || ToolInputHelper.StartsWithCommand(value, "preview file"))
        {
            toolName = "read file";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "browse")
            || ToolInputHelper.StartsWithCommand(value, "go to")
            || ToolInputHelper.StartsWithCommand(value, "visit")
            || ToolInputHelper.StartsWithCommand(value, "open website"))
        {
            toolName = "browse";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "spotify"))
        {
            toolName = "spotify";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "code")
            || ToolInputHelper.StartsWithCommand(value, "vscode")
            || ToolInputHelper.StartsWithCommand(value, "vs code"))
        {
            toolName = "code";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "chrome"))
        {
            toolName = "chrome";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "office")
            || ToolInputHelper.StartsWithCommand(value, "word")
            || ToolInputHelper.StartsWithCommand(value, "excel")
            || ToolInputHelper.StartsWithCommand(value, "powerpoint")
            || ToolInputHelper.StartsWithCommand(value, "power point")
            || ToolInputHelper.StartsWithCommand(value, "ppt")
            || ToolInputHelper.StartsWithCommand(value, "outlook"))
        {
            toolName = "office";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "explorer")
            || ToolInputHelper.StartsWithCommand(value, "file explorer"))
        {
            toolName = "explorer";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "discord"))
        {
            toolName = "discord";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "slack"))
        {
            toolName = "slack";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "search web")
            || ToolInputHelper.StartsWithCommand(value, "web search"))
        {
            toolName = "search web";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "media")
            || ToolInputHelper.StartsWithCommand(value, "play media")
            || ToolInputHelper.StartsWithCommand(value, "pause media")
            || ToolInputHelper.StartsWithCommand(value, "resume media")
            || ToolInputHelper.StartsWithCommand(value, "toggle media")
            || ToolInputHelper.StartsWithCommand(value, "stop media")
            || ToolInputHelper.StartsWithCommand(value, "next track")
            || ToolInputHelper.StartsWithCommand(value, "previous track")
            || ToolInputHelper.StartsWithCommand(value, "prev track")
            || ToolInputHelper.StartsWithCommand(value, "skip track")
            || ToolInputHelper.StartsWithCommand(value, "volume up")
            || ToolInputHelper.StartsWithCommand(value, "volume down")
            || ToolInputHelper.StartsWithCommand(value, "mute")
            || ToolInputHelper.StartsWithCommand(value, "toggle mute"))
        {
            toolName = "media";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(value, "clipboard")
            || ToolInputHelper.StartsWithCommand(value, "show clipboard")
            || ToolInputHelper.StartsWithCommand(value, "get clipboard")
            || ToolInputHelper.StartsWithCommand(value, "read clipboard")
            || ToolInputHelper.StartsWithCommand(value, "set clipboard")
            || ToolInputHelper.StartsWithCommand(value, "copy to clipboard")
            || ToolInputHelper.StartsWithCommand(value, "clear clipboard")
            || ToolInputHelper.StartsWithCommand(value, "empty clipboard")
            || ToolInputHelper.StartsWithCommand(value, "paste clipboard"))
        {
            toolName = "clipboard";
            return true;
        }

        if (value.Contains("what app am i in", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what's open", StringComparison.OrdinalIgnoreCase)
            || value.Contains("whats open", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what am i doing", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what am i working on", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what's on my computer", StringComparison.OrdinalIgnoreCase)
            || value.Contains("whats on my computer", StringComparison.OrdinalIgnoreCase)
            || value.Contains("what can you see on my computer", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "computer";
            return true;
        }

        if (value.Contains("network", StringComparison.OrdinalIgnoreCase)
            || value.Contains("router", StringComparison.OrdinalIgnoreCase)
            || value.Contains("gateway", StringComparison.OrdinalIgnoreCase)
            || value.Contains("devices on my network", StringComparison.OrdinalIgnoreCase)
            || value.Contains("local lan", StringComparison.OrdinalIgnoreCase)
            || value.Contains("lan device", StringComparison.OrdinalIgnoreCase)
            || value.Contains("wifi device", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "network";
            return true;
        }

        if (value is "email"
            or "emails"
            or "email context"
            or "show email"
            or "show emails"
            or "list emails"
            or "inbox")
        {
            toolName = "email";
            return true;
        }

        if (value is "tasks"
            or "task"
            or "todo"
            or "to do"
            or "task context"
            or "show tasks"
            or "list tasks"
            or "show todo")
        {
            toolName = "tasks";
            return true;
        }

        if (value.Contains("screenshot", StringComparison.OrdinalIgnoreCase)
            || value.Contains("monitor", StringComparison.OrdinalIgnoreCase)
            || value.Contains("display", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ocr", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ui element", StringComparison.OrdinalIgnoreCase)
            || value.Contains("screen text", StringComparison.OrdinalIgnoreCase)
            || value.Contains("screen", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "screen";
            return true;
        }

        if (value.Contains("ambient", StringComparison.OrdinalIgnoreCase)
            || value.Contains("webcam", StringComparison.OrdinalIgnoreCase)
            || value.Contains("camera", StringComparison.OrdinalIgnoreCase)
            || value.Contains("presence", StringComparison.OrdinalIgnoreCase)
            || value.Contains("gesture", StringComparison.OrdinalIgnoreCase)
            || value.Contains("face recognition", StringComparison.OrdinalIgnoreCase)
            || value.Contains("room sensor", StringComparison.OrdinalIgnoreCase)
            || value.Contains("far field", StringComparison.OrdinalIgnoreCase)
            || value.Contains("spatial audio", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "ambient";
            return true;
        }

        if (value.StartsWith("analyze image", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("inspect image", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "analyze image";
            return true;
        }

        if (value.StartsWith("analyze document", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("inspect document", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "analyze document";
            return true;
        }

        if (value.Contains(".pdf", StringComparison.OrdinalIgnoreCase)
            || value.Contains(".tif", StringComparison.OrdinalIgnoreCase)
            || value.Contains(".tiff", StringComparison.OrdinalIgnoreCase)
            || value.Contains("receipt", StringComparison.OrdinalIgnoreCase)
            || value.Contains("invoice", StringComparison.OrdinalIgnoreCase)
            || value.Contains("document scan", StringComparison.OrdinalIgnoreCase)
            || value.Contains("scanned document", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ocr this document", StringComparison.OrdinalIgnoreCase))
        {
            toolName = "analyze document";
            return true;
        }

        toolName = string.Empty;
        return false;
    }

    private static bool LooksLikeSimpleReply(string input)
    {
        var trimmed = input.Trim();
        var value = trimmed.TrimEnd('.', '!').ToLowerInvariant();
        var simpleOpeners = new[]
        {
            "hi",
            "hello",
            "hey",
            "thanks",
            "thank you",
            "good morning",
            "good afternoon",
            "good evening",
            "good night",
            "how are you"
        };

        return simpleOpeners.Any(phrase => value.Equals(phrase, StringComparison.OrdinalIgnoreCase))
            || trimmed.Length <= 24 && trimmed.EndsWith("?", StringComparison.Ordinal);
    }

    private static bool LooksLikeResearchRequest(string input)
    {
        var value = input.Trim().ToLowerInvariant();
        var researchSignals = new[]
        {
            "who is",
            "what is",
            "when did",
            "why is",
            "explain",
            "compare",
            "research",
            "latest",
            "news",
            "summarize"
        };

        return researchSignals.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase))
            && !value.Contains("file", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("folder", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("app", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("computer", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("email", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("inbox", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("task", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("todo", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("screen", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("document", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("pdf", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("image", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("photo", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("window", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePlannerRequest(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = input.Trim().ToLowerInvariant();
        var startsWithSignals = new[]
        {
            "open ",
            "launch ",
            "start ",
            "close ",
            "quit ",
            "browse ",
            "visit ",
            "go to ",
            "read ",
            "find ",
            "run ",
            "execute ",
            "create ",
            "add ",
            "delete ",
            "remove ",
            "move ",
            "rename ",
            "copy ",
            "save ",
            "write ",
            "edit ",
            "update ",
            "change ",
            "set ",
            "install ",
            "download ",
            "send ",
            "reply ",
            "compose ",
            "inspect ",
            "analyze ",
            "monitor ",
            "watch ",
            "keep checking ",
            "keep watching ",
            "take ",
            "capture "
        };

        if (startsWithSignals.Any(signal => value.StartsWith(signal, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (value.Contains(" and then ", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" then ", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" after that ", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" before that ", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var embeddedActionSignals = new[]
        {
            " open ",
            " launch ",
            " start ",
            " close ",
            " browse ",
            " visit ",
            " read ",
            " find ",
            " run ",
            " create ",
            " add ",
            " delete ",
            " remove ",
            " move ",
            " rename ",
            " edit ",
            " update ",
            " send "
        };

        return embeddedActionSignals.Count(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase)) >= 2;
    }

    private static bool ShouldRunInBackground(string input)
    {
        var value = input.Trim().ToLowerInvariant();

        return value.Contains("background", StringComparison.OrdinalIgnoreCase)
            || value.Contains("keep watching", StringComparison.OrdinalIgnoreCase)
            || value.Contains("keep checking", StringComparison.OrdinalIgnoreCase)
            || value.Contains("monitor", StringComparison.OrdinalIgnoreCase)
            || value.Contains("deep research", StringComparison.OrdinalIgnoreCase)
            || value.Length > 180;
    }

    private static bool LooksLikeResumeRequest(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = input.Trim().ToLowerInvariant();

        if (value is "resume" or "continue" or "pick it back up" or "pick that back up")
        {
            return true;
        }

        if (value.StartsWith("resume ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("continue ", StringComparison.OrdinalIgnoreCase))
        {
            return value.Contains("last", StringComparison.OrdinalIgnoreCase)
                || value.Contains("workflow", StringComparison.OrdinalIgnoreCase)
                || value.Contains("session", StringComparison.OrdinalIgnoreCase)
                || value.Contains("task", StringComparison.OrdinalIgnoreCase)
                || value.Contains("background", StringComparison.OrdinalIgnoreCase)
                || value.Contains("plan", StringComparison.OrdinalIgnoreCase)
                || value.Contains("request", StringComparison.OrdinalIgnoreCase);
        }

        return value.Contains("resume the last", StringComparison.OrdinalIgnoreCase)
            || value.Contains("continue the last", StringComparison.OrdinalIgnoreCase)
            || value.Contains("resume my last", StringComparison.OrdinalIgnoreCase)
            || value.Contains("continue my last", StringComparison.OrdinalIgnoreCase)
            || value.Contains("resume that workflow", StringComparison.OrdinalIgnoreCase)
            || value.Contains("continue that workflow", StringComparison.OrdinalIgnoreCase)
            || value.Contains("pick back up", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeEmailContextQuestion(string input)
    {
        var value = input.Trim().ToLowerInvariant();
        if (value.StartsWith("email ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("send ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("reply ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("compose ", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("draft ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var mentionsEmailSurface = value.Contains("email", StringComparison.OrdinalIgnoreCase)
            || value.Contains("emails", StringComparison.OrdinalIgnoreCase)
            || value.Contains("inbox", StringComparison.OrdinalIgnoreCase)
            || value.Contains("mail", StringComparison.OrdinalIgnoreCase);
        var summarySignals = value.Contains("what", StringComparison.OrdinalIgnoreCase)
            || value.Contains("show", StringComparison.OrdinalIgnoreCase)
            || value.Contains("list", StringComparison.OrdinalIgnoreCase)
            || value.Contains("summarize", StringComparison.OrdinalIgnoreCase)
            || value.Contains("unread", StringComparison.OrdinalIgnoreCase)
            || value.Contains("do i have", StringComparison.OrdinalIgnoreCase)
            || value.Contains("need", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("?", StringComparison.Ordinal);

        return mentionsEmailSurface && summarySignals;
    }

    private static bool LooksLikeTaskContextQuestion(string input)
    {
        var value = input.Trim().ToLowerInvariant();
        if (value.StartsWith("add task", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("create task", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("set task", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var mentionsTaskSurface = value.Contains("task", StringComparison.OrdinalIgnoreCase)
            || value.Contains("tasks", StringComparison.OrdinalIgnoreCase)
            || value.Contains("todo", StringComparison.OrdinalIgnoreCase)
            || value.Contains("to-do", StringComparison.OrdinalIgnoreCase)
            || value.Contains("to do", StringComparison.OrdinalIgnoreCase);
        var summarySignals = value.Contains("what", StringComparison.OrdinalIgnoreCase)
            || value.Contains("show", StringComparison.OrdinalIgnoreCase)
            || value.Contains("list", StringComparison.OrdinalIgnoreCase)
            || value.Contains("summarize", StringComparison.OrdinalIgnoreCase)
            || value.Contains("due", StringComparison.OrdinalIgnoreCase)
            || value.Contains("need", StringComparison.OrdinalIgnoreCase)
            || value.Contains("my ", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("?", StringComparison.Ordinal);

        return mentionsTaskSurface && summarySignals;
    }

    private static bool LooksLikeImagePath(string input)
    {
        return input.Contains(".png", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".jpg", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".jpeg", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".webp", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".bmp", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDocumentPath(string input)
    {
        return input.Contains(".txt", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".md", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".json", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".cs", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".xml", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".yml", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".yaml", StringComparison.OrdinalIgnoreCase)
            || input.Contains(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractNaturalToolInput(string input, string toolHint)
    {
        var normalized = input.Trim();

        return toolHint switch
        {
            // Only "... in/for <place>" names a place; anything else uses the default location.
            "weather" => Regex.Match(normalized, @"\b(?:in|for)\s+(?<place>[\p{L}][\p{L} ,.'-]*?)\s*(?:\b(?:today|tomorrow|tonight|right now|now)\b)?[?.!]*$", RegexOptions.IgnoreCase) is { Success: true } place
                ? place.Groups["place"].Value.Trim(' ', ',', '.')
                : string.Empty,
            "email" or "tasks" => string.Empty,
            "open app" => ExtractDirectToolInput(normalized, "open app", "launch app", "launch", "start app"),
            "focus app" => ExtractDirectToolInput(normalized, "focus app", "focus window", "switch to", "activate app", "bring to front"),
            "spotify" => ExtractDirectToolInput(normalized, "spotify"),
            "code" => ExtractDirectToolInput(normalized, "code", "vscode", "vs code"),
            "chrome" => ExtractDirectToolInput(normalized, "chrome"),
            "office" => ExtractDirectToolInput(normalized, "office", "word", "excel", "powerpoint", "power point", "ppt", "outlook"),
            "explorer" => ExtractDirectToolInput(normalized, "explorer", "file explorer"),
            "discord" => ExtractDirectToolInput(normalized, "discord"),
            "slack" => ExtractDirectToolInput(normalized, "slack"),
            "windows" => ExtractDirectToolInput(normalized, "windows", "list windows", "show windows", "visible windows"),
            "click element" => ExtractDirectToolInput(normalized, "click element", "click", "tap"),
            "get element text" => ExtractDirectToolInput(normalized, "get element text", "read element", "element text", "what text", "get text from", "read text from"),
            "type into" => ExtractDirectToolInput(normalized, "type into", "types into", "enter into", "input into", "write into"),
            "close app" => ExtractDirectToolInput(normalized, "close app", "close window", "quit app"),
            "window state" => ExtractDirectToolInput(normalized, "window state", "minimize app", "maximize app", "restore app", "minimize window", "maximize window", "restore window"),
            "move window" => ExtractDirectToolInput(normalized, "move window", "move app", "position window"),
            "resize window" => ExtractDirectToolInput(normalized, "resize window", "resize app"),
            "snap window" => ExtractDirectToolInput(normalized, "snap window", "snap app", "dock window"),
            "send hotkey" => ExtractDirectToolInput(normalized, "send hotkey", "press key", "send shortcut", "shortcut"),
            "open path" => ExtractDirectToolInput(normalized, "open path", "open file", "open folder"),
            "read file" => ExtractDirectToolInput(normalized, "read file", "show file", "preview file"),
            "browse" => ExtractDirectToolInput(normalized, "browse", "open website", "go to", "visit"),
            "search web" => ExtractDirectToolInput(normalized, "search web", "web search"),
            "media" => ExtractMediaToolInput(normalized),
            "clipboard" => ExtractClipboardToolInput(normalized),
            "screen" => ExtractVisualToolInput(normalized),
            "ambient" => ExtractAmbientToolInput(normalized),
            "analyze image" => ExtractDocumentToolInput(normalized, "analyze image", "inspect image"),
            "analyze document" => ExtractDocumentToolInput(normalized, "analyze document", "inspect document"),
            _ => normalized
        };
    }

    private static string ExtractDirectToolInput(string normalized, params string[] prefixes)
    {
        return ToolInputHelper.GetCommandTail(normalized, prefixes);
    }

    private static string ExtractDocumentToolInput(string normalized, params string[] prefixes)
    {
        var tail = ToolInputHelper.GetCommandTail(normalized, prefixes);
        return string.IsNullOrWhiteSpace(tail) ? normalized : tail;
    }

    private static string ExtractVisualToolInput(string normalized)
    {
        var tail = ToolInputHelper.GetCommandTail(
            normalized,
            "screen",
            "analyze screen",
            "inspect screen",
            "screen capture",
            "capture screen",
            "take screenshot",
            "screenshot",
            "screen ocr",
            "screen text",
            "ocr",
            "extract text from screen",
            "detect ui",
            "screen ui",
            "screen elements");

        return string.IsNullOrWhiteSpace(tail) ? normalized : tail;
    }

    private static string ExtractAmbientToolInput(string normalized)
    {
        var tail = ToolInputHelper.GetCommandTail(
            normalized,
            "ambient",
            "webcam",
            "camera",
            "presence",
            "gesture",
            "face recognition",
            "face",
            "room sensors",
            "far field",
            "spatial audio");

        return string.IsNullOrWhiteSpace(tail) ? normalized : tail;
    }

    private static string ExtractMediaToolInput(string normalized)
    {
        if (ToolInputHelper.StartsWithCommand(normalized, "media"))
        {
            return ToolInputHelper.GetCommandTail(normalized, "media");
        }

        foreach (var (prefix, action) in new[]
                 {
                     ("play media", "play"),
                     ("pause media", "pause"),
                     ("resume media", "resume"),
                     ("toggle media", "toggle"),
                     ("stop media", "stop"),
                     ("next track", "next"),
                     ("previous track", "previous"),
                     ("prev track", "previous"),
                     ("skip track", "next"),
                     ("mute", "mute"),
                     ("toggle mute", "mute")
                 })
        {
            if (ToolInputHelper.StartsWithCommand(normalized, prefix))
            {
                return action;
            }
        }

        foreach (var prefix in new[] { "volume up", "volume down" })
        {
            if (!ToolInputHelper.StartsWithCommand(normalized, prefix))
            {
                continue;
            }

            var tail = ToolInputHelper.GetCommandTail(normalized, prefix);
            return string.IsNullOrWhiteSpace(tail) ? prefix : $"{prefix} {tail}";
        }

        return normalized;
    }

    private static string ExtractClipboardToolInput(string normalized)
    {
        if (ToolInputHelper.MatchesExact(normalized, "clipboard", "show clipboard", "get clipboard", "read clipboard", "clipboard show"))
        {
            return string.Empty;
        }

        if (ToolInputHelper.MatchesExact(normalized, "clear clipboard", "clipboard clear", "empty clipboard"))
        {
            return "clear";
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "set clipboard")
            || ToolInputHelper.StartsWithCommand(normalized, "copy to clipboard")
            || ToolInputHelper.StartsWithCommand(normalized, "clipboard set"))
        {
            var tail = ToolInputHelper.GetCommandTail(normalized, "set clipboard", "copy to clipboard", "clipboard set");
            return string.IsNullOrWhiteSpace(tail) ? "set" : $"set {tail}";
        }

        if (ToolInputHelper.StartsWithCommand(normalized, "paste clipboard into")
            || ToolInputHelper.StartsWithCommand(normalized, "paste clipboard")
            || ToolInputHelper.StartsWithCommand(normalized, "clipboard paste"))
        {
            var tail = ToolInputHelper.GetCommandTail(normalized, "paste clipboard into", "paste clipboard", "clipboard paste");
            tail = tail.StartsWith("into ", StringComparison.OrdinalIgnoreCase)
                ? tail["into ".Length..].Trim()
                : tail;
            return string.IsNullOrWhiteSpace(tail) ? "paste" : $"paste {tail}";
        }

        return normalized;
    }

    private static string ExtractToolInput(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("input", out var input))
            {
                return input.ValueKind == JsonValueKind.String
                    ? input.GetString()?.Trim() ?? string.Empty
                    : input.ToString().Trim();
            }

            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString()?.Trim() ?? string.Empty;
            }

            return root.ToString().Trim();
        }
        catch (JsonException)
        {
            return argumentsJson.Trim();
        }
    }

    private static readonly JsonSerializerOptions ParseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static T? ParseJson<T>(string content) where T : class
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var candidate = ExtractJson(content);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(candidate, ParseJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractJson(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = trimmed.IndexOf('\n');

            if (firstBreak >= 0)
            {
                trimmed = trimmed[(firstBreak + 1)..];
                var fenceIndex = trimmed.LastIndexOf("```", StringComparison.Ordinal);

                if (fenceIndex >= 0)
                {
                    trimmed = trimmed[..fenceIndex];
                }
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');

        return firstBrace >= 0 && lastBrace > firstBrace
            ? trimmed[firstBrace..(lastBrace + 1)]
            : trimmed;
    }

    private static AssistantCitation[] ParseCitationsFromOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var citations = new List<AssistantCitation>();
        var lines = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(" | ", StringComparison.Ordinal);

            if (separatorIndex <= 2)
            {
                continue;
            }

            var title = line[2..separatorIndex].Trim();
            var remainder = line[(separatorIndex + 3)..].Trim();
            var urlSeparator = remainder.IndexOf(" | ", StringComparison.Ordinal);

            if (urlSeparator <= 0)
            {
                continue;
            }

            citations.Add(new AssistantCitation(
                title,
                remainder[..urlSeparator].Trim(),
                remainder[(urlSeparator + 3)..].Trim()));
        }

        return citations.ToArray();
    }

    private static string BuildOfflineSimpleReply(string input)
    {
        return input.Trim().ToLowerInvariant() switch
        {
            "hi" or "hello" or "hey" => "Hello.",
            "thanks" or "thank you" => "You're welcome.",
            "how are you" => "Operational.",
            _ => "I can help. If you need an action on this computer, phrase it directly and I’ll route it through the local tools."
        };
    }

    private static string BuildOfflineResearchSummary(WebResearchResult research)
    {
        if (research.Citations.Length == 0)
        {
            return research.SummaryText;
        }

        var lines = new List<string>
        {
            research.SummaryText,
            "Sources:"
        };

        lines.AddRange(
            research.Citations.Select((citation, index) =>
                $"{index + 1}. {citation.Title} - {citation.Url}"));

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOfflinePlanSummary(IReadOnlyList<PlannerToolResult> toolResults)
    {
        var successful = toolResults.Where(result => !result.IsError).ToArray();
        var failed = toolResults.Where(result => result.IsError).ToArray();
        var unconfirmed = successful.Where(result =>
            string.Equals(result.VerificationStatus, "unconfirmed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result.VerificationStatus, "awaiting_approval", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (successful.Length == 0)
        {
            return failed.Length == 0
                ? "I could not complete that request."
                : $"I attempted {failed.Length} step{(failed.Length == 1 ? string.Empty : "s")}, but the last result was: {TrimForFeed(failed[^1].Output)}";
        }

        if (unconfirmed.Length > 0)
        {
            return $"I completed {successful.Length} step{(successful.Length == 1 ? string.Empty : "s")}, but {unconfirmed.Length} result{(unconfirmed.Length == 1 ? " remains" : "s remain")} unconfirmed. Last verification: {TrimForFeed(unconfirmed[^1].VerificationSummary)}";
        }

        var summary = successful[^1].Summary;
        return string.IsNullOrWhiteSpace(summary)
            ? TrimForFeed(successful[^1].Output)
            : summary;
    }

    private static string AppendCitations(string content, IReadOnlyList<AssistantCitation> citations)
    {
        if (citations.Count == 0)
        {
            return content.Trim();
        }

        var builder = new StringBuilder(content.Trim());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("Sources:");

        foreach (var citation in citations)
        {
            builder.Append("- ");
            builder.Append(citation.Title);
            builder.Append(" - ");
            builder.AppendLine(citation.Url);
        }

        return builder.ToString().TrimEnd();
    }

    private static string ResolveVerificationStatus(AssistantToolExecution result)
    {
        if (!string.IsNullOrWhiteSpace(result.VerificationStatus))
        {
            return result.VerificationStatus.Trim();
        }

        return result.Succeeded ? "observed" : "failed";
    }

    private static string ResolveVerificationSummary(AssistantToolExecution result)
    {
        if (!string.IsNullOrWhiteSpace(result.VerificationSummary))
        {
            return result.VerificationSummary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(result.VerificationText))
        {
            return result.VerificationText.Trim();
        }

        return result.Summary.Trim();
    }

    private static string TrimError(string text)
    {
        const int maxLength = 280;
        var value = text.Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static string TrimForFeed(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        const int maxLength = 220;
        var flattened = text.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..maxLength] + "...";
    }

    private static string TruncateForContext(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var flattened = text.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..maxLength] + "...";
    }
}
