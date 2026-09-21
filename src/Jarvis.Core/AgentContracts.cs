namespace Jarvis.Core;

public enum AgentIntentKind
{
    DirectTool,
    Clarification,
    SimpleReply,
    Planner,
    Research,
    Visual,
    Background
}

public enum AssistantActivityKind
{
    Status,
    Classification,
    Planning,
    ToolStart,
    ToolComplete,
    Reflection,
    Clarification,
    Background,
    Summary,
    Warning,
    Error
}

public sealed record AssistantCitation(string Title, string Url, string Snippet = "");

public sealed record AssistantToolExecution(
    string ToolName,
    string Input,
    string Summary,
    bool Succeeded,
    string VerificationText,
    string OutputText,
    string VerificationStatus = "",
    string VerificationSummary = "");

public sealed record PlanStepState(string Title, string Status, string Detail);

public sealed record UserStateItem(
    string Title,
    string Detail,
    string Category = "",
    string DueAt = "",
    bool IsUnread = false);

public sealed record UserStateSnapshot(
    string Summary,
    UserStateItem[] EmailItems,
    UserStateItem[] TaskItems)
{
    public static UserStateSnapshot Empty { get; } = new(
        "No email or task state is available.",
        Array.Empty<UserStateItem>(),
        Array.Empty<UserStateItem>());

    public string ToPromptText()
    {
        static string RenderSection(string title, IReadOnlyList<UserStateItem> items)
        {
            if (items.Count == 0)
            {
                return $"{title}: none";
            }

            var lines = items
                .Take(5)
                .Select(item =>
                {
                    var dueText = string.IsNullOrWhiteSpace(item.DueAt) ? string.Empty : $" | due {item.DueAt}";
                    var unreadText = item.IsUnread ? " | unread" : string.Empty;
                    return $"- {item.Title}: {item.Detail}{dueText}{unreadText}";
                });

            return title + ":" + Environment.NewLine + string.Join(Environment.NewLine, lines);
        }

        return string.Join(
            Environment.NewLine,
            $"Summary: {Summary}",
            RenderSection("Email", EmailItems),
            RenderSection("Tasks", TaskItems));
    }
}

public sealed record PersistedAgentSession(
    string SessionId,
    string Status,
    AgentIntentKind Intent,
    string OriginalRequest,
    string LatestInput,
    string FollowUpQuestion,
    string LatestResponse,
    bool RunInBackground,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    PlanStepState[] Steps,
    AssistantToolExecution[] ToolResults,
    AssistantCitation[] Citations);

public sealed record AssistantActivityEvent(
    AssistantActivityKind Kind,
    string Message,
    string SessionId = "",
    bool IsTransient = false,
    string Detail = "");

public sealed class AssistantActivityEventArgs : EventArgs
{
    public AssistantActivityEventArgs(AssistantActivityEvent activity)
    {
        Activity = activity;
    }

    public AssistantActivityEvent Activity { get; }
}
