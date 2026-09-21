using System.Text;

namespace Jarvis.Core;

public sealed record InteractionContextSnapshot(
    DateTimeOffset ObservedAtUtc,
    string TimeOfDay = "",
    string Location = "",
    string ActiveApp = "",
    string ActiveWindow = "",
    string[]? VisibleApps = null,
    string[]? RecentActivity = null)
{
    public static InteractionContextSnapshot Empty { get; } = new(DateTimeOffset.UtcNow);

    public string[] SafeVisibleApps => VisibleApps ?? Array.Empty<string>();

    public string[] SafeRecentActivity => RecentActivity ?? Array.Empty<string>();

    public string ToPromptText()
    {
        var lines = new List<string>
        {
            $"Observed at: {ObservedAtUtc:yyyy-MM-dd HH:mm}Z",
            $"Time of day: {OrNone(TimeOfDay)}",
            $"Location: {OrNone(Location)}",
            $"Active app: {OrNone(ActiveApp)}",
            $"Active window: {OrNone(ActiveWindow)}",
            $"Visible apps: {JoinOrNone(SafeVisibleApps)}",
            $"Recent activity: {JoinOrNone(SafeRecentActivity)}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    public string ToInlineText()
    {
        var parts = new List<string>();

        AddIfPresent(parts, TimeOfDay, "time");
        AddIfPresent(parts, Location, "location");
        AddIfPresent(parts, ActiveApp, "app");
        AddIfPresent(parts, ActiveWindow, "window");

        if (SafeRecentActivity.Length > 0)
        {
            parts.Add("recent activity " + string.Join(", ", SafeRecentActivity.Take(3)));
        }

        return parts.Count == 0 ? "No contextual cues." : string.Join(" | ", parts);
    }

    private static void AddIfPresent(ICollection<string> parts, string value, string label)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label} {value.Trim()}");
        }
    }

    private static string JoinOrNone(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "none" : string.Join(" | ", values);
    }

    private static string OrNone(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }
}

public sealed record MemoryAttribute(
    string Key,
    string Value);

public sealed record StructuredMemoryReference(
    string Kind,
    string Name,
    string Summary = "",
    string[]? Aliases = null,
    MemoryAttribute[]? Attributes = null)
{
    public string[] SafeAliases => Aliases ?? Array.Empty<string>();

    public MemoryAttribute[] SafeAttributes => Attributes ?? Array.Empty<MemoryAttribute>();

    public string ToInlineText()
    {
        var parts = new List<string>
        {
            $"[{NormalizeKind(Kind)}] {Name.Trim()}"
        };

        if (!string.IsNullOrWhiteSpace(Summary))
        {
            parts.Add(Summary.Trim());
        }

        if (SafeAliases.Length > 0)
        {
            parts.Add("aliases " + string.Join(", ", SafeAliases));
        }

        if (SafeAttributes.Length > 0)
        {
            parts.Add(string.Join(", ", SafeAttributes.Select(attribute => $"{attribute.Key}: {attribute.Value}")));
        }

        return string.Join(" | ", parts);
    }

    private static string NormalizeKind(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "entity";
        }

        var trimmed = value.Trim().ToLowerInvariant();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }
}

public sealed record ReminderSnapshot(
    string Id,
    string Text,
    DateTimeOffset ReminderAtUtc,
    string Status,
    string SourceMemoryId);

public sealed record StructuredMemorySummary(
    string Kind,
    string Name,
    string Summary,
    int ObservationCount,
    DateTimeOffset LastObservedAtUtc);

public sealed record MemoryWriteRequest(
    string Content,
    string Kind = "note",
    string Category = "",
    string Source = "manual",
    string Privacy = "persistent",
    string[]? Tags = null,
    InteractionContextSnapshot? Context = null,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? SummaryWindowStartUtc = null,
    DateTimeOffset? SummaryWindowEndUtc = null,
    double? ImportanceScore = null,
    string RetentionPolicy = "",
    DateTimeOffset? ExpiresAtUtc = null,
    bool UserApprovedRetention = false,
    StructuredMemoryReference[]? Entities = null,
    DateTimeOffset? ReminderAtUtc = null,
    string ReminderText = "",
    string ReminderStatus = "");

public sealed record MemoryUpdateRequest(
    string Id,
    string Content,
    string Kind,
    string Category,
    string Privacy,
    string[] Tags,
    double ImportanceScore,
    string RetentionPolicy,
    DateTimeOffset? ExpiresAtUtc,
    bool UserApprovedRetention,
    DateTimeOffset? ReminderAtUtc,
    string ReminderText,
    string ReminderStatus,
    StructuredMemoryReference[]? Entities = null);

public sealed record MemoryQueryContext(
    string Query,
    string TimeOfDay = "",
    string Location = "",
    string ActiveApp = "",
    string ActiveWindow = "",
    string[]? RecentActivity = null)
{
    public string[] SafeRecentActivity => RecentActivity ?? Array.Empty<string>();
}

public sealed record LearnedPreference(
    string Category,
    string Value,
    int ObservationCount,
    DateTimeOffset LastObservedAtUtc);

public sealed record MemoryProfileSnapshot(
    int TotalMemories,
    int PreferenceCount,
    int SummaryCount,
    int StructuredMemoryCount,
    int PendingReminderCount,
    LearnedPreference[] Preferences,
    StructuredMemorySummary[] StructuredMemories,
    ReminderSnapshot[] PendingReminders)
{
    public static MemoryProfileSnapshot Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        Array.Empty<LearnedPreference>(),
        Array.Empty<StructuredMemorySummary>(),
        Array.Empty<ReminderSnapshot>());

    public string ToPromptText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Memories: {TotalMemories}");
        builder.AppendLine($"Preferences: {PreferenceCount}");
        builder.AppendLine($"Summaries: {SummaryCount}");
        builder.AppendLine($"Structured memories: {StructuredMemoryCount}");
        builder.AppendLine($"Pending reminders: {PendingReminderCount}");

        foreach (var category in OrderedPreferenceCategories)
        {
            var matches = Preferences
                .Where(preference => string.Equals(preference.Category, category, StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToArray();

            builder.AppendLine(matches.Length == 0
                ? $"{TitleCase(category)}: none"
                : $"{TitleCase(category)}:{Environment.NewLine}{string.Join(Environment.NewLine, matches.Select(RenderPreference))}");
        }

        foreach (var kind in OrderedStructuredKinds)
        {
            var matches = StructuredMemories
                .Where(memory => string.Equals(memory.Kind, kind, StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToArray();

            if (matches.Length == 0)
            {
                continue;
            }

            builder.AppendLine($"{TitleCase(kind)}:");
            builder.AppendLine(string.Join(Environment.NewLine, matches.Select(RenderStructuredMemory)));
        }

        if (PendingReminders.Length > 0)
        {
            builder.AppendLine("Upcoming reminders:");
            builder.AppendLine(string.Join(Environment.NewLine, PendingReminders.Take(4).Select(RenderReminder)));
        }

        return builder.ToString().TrimEnd();
    }

    public string ToDisplayText()
    {
        var lines = new List<string>
        {
            $"Stored memories: {TotalMemories}",
            $"Learned preferences: {PreferenceCount}",
            $"Conversation summaries: {SummaryCount}",
            $"Structured memories: {StructuredMemoryCount}",
            $"Pending reminders: {PendingReminderCount}"
        };

        if (PreferenceCount == 0 && StructuredMemoryCount == 0 && PendingReminderCount == 0)
        {
            lines.Add("No learned personalization signals yet.");
            return string.Join(Environment.NewLine, lines);
        }

        foreach (var category in OrderedPreferenceCategories)
        {
            var matches = Preferences
                .Where(preference => string.Equals(preference.Category, category, StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToArray();

            if (matches.Length == 0)
            {
                continue;
            }

            lines.Add($"{TitleCase(category)}:");
            lines.AddRange(matches.Select(RenderPreference));
        }

        foreach (var kind in OrderedStructuredKinds)
        {
            var matches = StructuredMemories
                .Where(memory => string.Equals(memory.Kind, kind, StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToArray();

            if (matches.Length == 0)
            {
                continue;
            }

            lines.Add($"{TitleCase(kind)}:");
            lines.AddRange(matches.Select(RenderStructuredMemory));
        }

        if (PendingReminders.Length > 0)
        {
            lines.Add("Upcoming reminders:");
            lines.AddRange(PendingReminders.Take(6).Select(RenderReminder));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderPreference(LearnedPreference preference)
    {
        return $"- {preference.Value} | seen {preference.ObservationCount} time(s) | last observed {preference.LastObservedAtUtc:yyyy-MM-dd HH:mm}Z";
    }

    private static string RenderStructuredMemory(StructuredMemorySummary summary)
    {
        var content = string.IsNullOrWhiteSpace(summary.Summary)
            ? summary.Name
            : $"{summary.Name} | {summary.Summary}";
        return $"- {content} | seen {summary.ObservationCount} time(s) | last observed {summary.LastObservedAtUtc:yyyy-MM-dd HH:mm}Z";
    }

    private static string RenderReminder(ReminderSnapshot reminder)
    {
        return $"- {reminder.Text} | due {reminder.ReminderAtUtc.ToLocalTime():yyyy-MM-dd h:mm tt} | status {NormalizeStatus(reminder.Status)}";
    }

    private static string NormalizeStatus(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "pending" : value.Trim().ToLowerInvariant();
    }

    private static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Other";
        }

        var trimmed = value.Trim().ToLowerInvariant();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    private static readonly string[] OrderedPreferenceCategories =
    [
        "tone",
        "response style",
        "address",
        "conversation",
        "phrasing",
        "apps",
        "locations",
        "automations",
        "routines",
        "habits",
        "favorites",
        "other"
    ];

    private static readonly string[] OrderedStructuredKinds =
    [
        "relationship",
        "person",
        "project",
        "device",
        "place",
        "routine",
        "habit",
        "playlist",
        "other"
    ];
}

public sealed record MemoryImportResult(
    int ImportedCount,
    int SkippedCount,
    string SummaryText);

public sealed record TranscriptImportResult(
    int ImportedCount,
    int SkippedCount,
    string SummaryText);
