using System.Linq;

namespace Jarvis.Core;

internal static class UiSafetyExtensions
{
    private static readonly string[] SensitiveIndicators =
    [
        "pass",
        "token",
        "secret",
        "ssn",
        "otp",
        "credit card",
        "password"
    ];

    public static bool LooksSensitive(this string command)
    {
        var normalized = command.ToLowerInvariant();
        return SensitiveIndicators.Any(indicator => normalized.Contains(indicator));
    }

    private static SafetyDecision RequireApproval(string reason, string prompt, string summary)
    {
        var response = $"UI action paused: {reason}{Environment.NewLine}Reply `approve` or `deny`.";
        return new SafetyDecision(
            SafetyDecisionKind.RequireApproval,
            response,
            prompt,
            summary);
    }
}
