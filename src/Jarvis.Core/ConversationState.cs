using System.Text;

namespace Jarvis.Core;

public sealed record ConversationStateEntry(
    DateTimeOffset TimestampUtc,
    string UserInput,
    string ResponseText,
    string Intent,
    string Privacy,
    AssistantToolExecution[] ToolResults);

public sealed record ConversationStateSnapshot(
    ConversationStateEntry[] Entries,
    string CurrentFocus,
    bool ContainsOffRecordTurns)
{
    public static ConversationStateSnapshot Empty { get; } = new(Array.Empty<ConversationStateEntry>(), string.Empty, false);

    public string ToPromptText()
    {
        if (Entries.Length == 0)
        {
            return "No short-term session state yet.";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Current focus: {OrNone(CurrentFocus)}");
        builder.AppendLine($"Off-the-record turns in session: {(ContainsOffRecordTurns ? "yes" : "no")}");
        builder.AppendLine("Recent session turns:");

        foreach (var entry in Entries.TakeLast(6))
        {
            builder.Append("- ");
            builder.Append(entry.TimestampUtc.ToLocalTime().ToString("MM-dd h:mm tt"));
            builder.Append(" | ");
            builder.Append(Trim(entry.UserInput, 110));
            builder.Append(" | intent ");
            builder.Append(string.IsNullOrWhiteSpace(entry.Intent) ? "unknown" : entry.Intent);

            if (entry.ToolResults.Length > 0)
            {
                var latestTool = entry.ToolResults.Last();
                builder.Append(" | tool ");
                builder.Append(latestTool.ToolName);
                builder.Append(": ");
                builder.Append(Trim(latestTool.Summary, 80));
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string OrNone(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var flattened = value.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..maxLength] + "...";
    }
}

public sealed class ShortTermConversationState
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly List<ConversationStateEntry> _entries = new();

    public ShortTermConversationState(int capacity = 18)
    {
        _capacity = Math.Max(6, capacity);
    }

    public void AddTurn(AssistantTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        lock (_gate)
        {
            _entries.Add(new ConversationStateEntry(
                turn.TimestampUtc,
                turn.UserInput,
                turn.ResponseText,
                turn.Intent,
                turn.Privacy,
                turn.ToolResults ?? Array.Empty<AssistantToolExecution>()));

            if (_entries.Count > _capacity)
            {
                _entries.RemoveRange(0, _entries.Count - _capacity);
            }
        }
    }

    public ConversationStateSnapshot GetSnapshot(int maxResults)
    {
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                return ConversationStateSnapshot.Empty;
            }

            var entries = _entries
                .TakeLast(Math.Max(1, maxResults))
                .ToArray();
            var latest = entries.Last();
            var currentFocus = latest.ToolResults.LastOrDefault()?.Summary;

            if (string.IsNullOrWhiteSpace(currentFocus))
            {
                currentFocus = latest.UserInput;
            }

            var containsOffRecordTurns = entries.Any(entry =>
                string.Equals(entry.Privacy, "off-the-record", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Privacy, "sensitive", StringComparison.OrdinalIgnoreCase));

            return new ConversationStateSnapshot(entries, currentFocus ?? string.Empty, containsOffRecordTurns);
        }
    }
}
