using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.Core;

internal sealed partial class StructuredAssistantPlanner
{
    private static readonly HashSet<string> VerificationStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "how", "i", "if", "in", "into",
        "is", "it", "of", "on", "or", "so", "that", "the", "this", "to", "up", "was", "what", "when",
        "which", "with", "would", "your"
    };

    private static PlannerToolCall ParsePlannerToolCall(ChatToolCall toolCall)
    {
        if (string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            return new PlannerToolCall(toolCall.Name, string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(toolCall.ArgumentsJson);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                return new PlannerToolCall(
                    toolCall.Name,
                    ReadToolArgument(root, "input"),
                    ReadToolArgument(root, "purpose"),
                    ReadToolArgument(root, "expectedEvidence"));
            }

            if (root.ValueKind == JsonValueKind.String)
            {
                return new PlannerToolCall(toolCall.Name, root.GetString()?.Trim() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
        }

        return new PlannerToolCall(toolCall.Name, toolCall.ArgumentsJson.Trim());
    }

    private static string ReadToolArgument(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => property.ToString().Trim()
        };
    }

    private static string BuildToolStepDetail(PlannerToolCall toolCall)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(toolCall.Purpose))
        {
            parts.Add($"purpose: {toolCall.Purpose.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(toolCall.Input))
        {
            parts.Add($"input: {TrimForFeed(toolCall.Input)}");
        }

        if (!string.IsNullOrWhiteSpace(toolCall.ExpectedEvidence))
        {
            parts.Add($"expect: {TrimForFeed(toolCall.ExpectedEvidence)}");
        }

        if (parts.Count == 0)
        {
            parts.Add("tool selected without additional planner metadata.");
        }

        return string.Join(" | ", parts);
    }

    private static ToolVerificationReport BuildToolVerificationReport(PlannerToolCall toolCall, PlannerToolResult result)
    {
        if (result.IsError)
        {
            return new ToolVerificationReport(
                "failed",
                "failed",
                "The tool reported a failure, so the expected evidence was not confirmed.",
                BuildVerificationDetail(toolCall, result, "The tool reported a failure before the expected evidence could be confirmed."));
        }

        if (LooksLikeApprovalGate(result))
        {
            return new ToolVerificationReport(
                "awaiting_approval",
                "needs_review",
                "The tool reached an approval gate, so the expected evidence is still pending.",
                BuildVerificationDetail(toolCall, result, "The tool paused behind an approval gate before the requested evidence could be observed."));
        }

        var expectedEvidence = toolCall.ExpectedEvidence.Trim();
        var verificationText = result.Verification.Trim();
        var verificationIsGeneric = IsGenericVerificationText(verificationText);
        var observedEvidence = BuildObservedEvidence(result);
        var evidenceScore = ComputeEvidenceMatchScore(expectedEvidence, observedEvidence);

        if (string.IsNullOrWhiteSpace(expectedEvidence))
        {
            var summary = verificationIsGeneric
                ? "The tool returned a success signal, but no explicit expected evidence was supplied for this step."
                : "The tool returned concrete evidence for this step.";
            var status = verificationIsGeneric ? "observed" : "verified";
            return new ToolVerificationReport(
                status,
                "completed",
                summary,
                BuildVerificationDetail(toolCall, result, summary));
        }

        if (evidenceScore >= 0.55 || (!verificationIsGeneric && evidenceScore >= 0.25))
        {
            var summary = $"Verified expected evidence: {TrimForFeed(expectedEvidence)}";
            return new ToolVerificationReport(
                "verified",
                "completed",
                summary,
                BuildVerificationDetail(toolCall, result, summary));
        }

        if (!verificationIsGeneric && !string.IsNullOrWhiteSpace(verificationText))
        {
            var summary = $"The tool returned concrete evidence, but it only partially confirmed the expected signal: {TrimForFeed(expectedEvidence)}";
            return new ToolVerificationReport(
                "observed",
                "completed",
                summary,
                BuildVerificationDetail(toolCall, result, summary));
        }

        var unconfirmedSummary = $"The tool returned output, but the expected evidence was not clearly confirmed: {TrimForFeed(expectedEvidence)}";
        return new ToolVerificationReport(
            "unconfirmed",
            "needs_review",
            unconfirmedSummary,
            BuildVerificationDetail(toolCall, result, unconfirmedSummary));
    }

    private static string BuildObservedEvidence(PlannerToolResult result)
    {
        return string.Join(
            " ",
            new[]
            {
                result.Verification,
                result.Summary,
                TrimForFeed(result.Output)
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static bool LooksLikeApprovalGate(PlannerToolResult result)
    {
        return result.Output.StartsWith("Approval required", StringComparison.OrdinalIgnoreCase)
            || result.Summary.Contains("approval required", StringComparison.OrdinalIgnoreCase)
            || result.Verification.Contains("approval", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGenericVerificationText(string verificationText)
    {
        if (string.IsNullOrWhiteSpace(verificationText))
        {
            return true;
        }

        var genericPhrases = new[]
        {
            "returned a success signal",
            "reported a success signal",
            "success-shaped response",
            "reported a failure signal"
        };

        return genericPhrases.Any(phrase => verificationText.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private static double ComputeEvidenceMatchScore(string expectedEvidence, string observedEvidence)
    {
        var expectedTokens = TokenizeForVerification(expectedEvidence);

        if (expectedTokens.Count == 0)
        {
            return 0;
        }

        var observedTokens = TokenizeForVerification(observedEvidence);

        if (observedTokens.Count == 0)
        {
            return 0;
        }

        var matches = expectedTokens.Count(token => observedTokens.Contains(token));
        return matches / (double)expectedTokens.Count;
    }

    private static HashSet<string> TokenizeForVerification(string value)
    {
        var tokens = Regex
            .Split(value ?? string.Empty, "[^A-Za-z0-9]+")
            .Select(token => token.Trim().ToLowerInvariant())
            .Where(token => token.Length >= 3 && !VerificationStopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return tokens;
    }

    private static string BuildVerificationDetail(PlannerToolCall toolCall, PlannerToolResult result, string headline)
    {
        var parts = new List<string> { headline };

        if (!string.IsNullOrWhiteSpace(toolCall.Purpose))
        {
            parts.Add($"Purpose: {toolCall.Purpose.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(toolCall.ExpectedEvidence))
        {
            parts.Add($"Expected evidence: {toolCall.ExpectedEvidence.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(result.Verification))
        {
            parts.Add($"Tool verification: {result.Verification.Trim()}");
        }
        else if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            parts.Add($"Observed summary: {result.Summary.Trim()}");
        }
        else if (!string.IsNullOrWhiteSpace(result.Output))
        {
            parts.Add($"Observed output: {TrimForFeed(result.Output)}");
        }

        return string.Join(" ", parts);
    }

    private sealed record ToolVerificationReport(
        string Status,
        string PlanStepStatus,
        string Summary,
        string Detail);
}
