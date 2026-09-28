using System.Globalization;
using System.Text.RegularExpressions;

namespace Jarvis.Core;

internal sealed record TurnPersistencePlan(
    string EffectiveInput,
    string PrivacyMode,
    bool PersistTranscript,
    bool PersistAutomaticMemory,
    string Reason);

internal static class MemoryAutomation
{
    private static readonly string[] WholeTurnPrivacyPrefixes =
    [
        "off the record",
        "dont log this",
        "don't log this",
        "do not log this",
        "dont save this",
        "don't save this",
        "do not save this",
        "private request"
    ];

    private static readonly string[] SensitiveSignals =
    [
        "password",
        "passcode",
        "pin code",
        "api key",
        "token",
        "secret",
        "social security",
        "ssn",
        "one-time code",
        "one time code",
        "otp"
    ];

    private static readonly Regex CardLikeNumberRegex = new(@"\b(?:\d[ -]?){13,19}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LocationRegex = new(@"\b(?:i am in|i'm in|my location is|home is|default location is)\s+(?<location>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AppPreferenceRegex = new(@"\b(?:use|prefer)\s+(?<app>[a-z0-9][a-z0-9 .&+\-]{1,40})\s+(?:for|when)\s+(?<task>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AutomationLeadRegex = new(@"\b(?:when|if)\s+i\s+(?<trigger>[^,]+?)[, ]+(?:open|launch|start)\s+(?<app>[a-z0-9][a-z0-9 .&+\-]{1,40})$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AutomationTailRegex = new(@"\b(?:open|launch|start)\s+(?<app>[a-z0-9][a-z0-9 .&+\-]{1,40})\s+when\s+i\s+(?<trigger>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RoutineRegex = new(@"\b(?:every|each)\s+(?<when>morning|afternoon|evening|night|weekday|weekend|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b[, ]+(?<action>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TimedRoutineRegex = new(@"\b(?:every|each)\s+(?<when>morning|afternoon|evening|night|weekday|weekend|monday|tuesday|wednesday|thursday|friday|saturday|sunday)\s+at\s+(?<time>[^,]+?)\b[, ]+(?<action>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AnchoredRoutineRegex = new(@"\b(?:before|after)\s+(?<anchor>work|school|class|lunch|dinner|bed|sleep|the gym|gym)\b[, ]+(?:i\s+)?(?<action>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NamedRoutineRegex = new(@"\b(?:my|the)\s+(?<when>morning|afternoon|evening|night|weekday|weekend)\s+routine\s+(?:is|looks like|usually means)\s+(?<action>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex HabitRegex = new(@"\b(?:i usually|i normally|i tend to)\s+(?<habit>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FavoriteAppRegex = new(@"\b(?:my favorite app is|i always use|i mostly use)\s+(?<app>[a-z0-9][a-z0-9 .&+\-]{1,40})(?:\s+for\s+(?<task>.+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MyNamedThingRegex = new(@"\bmy\s+(?<label>wife|husband|spouse|boss|manager|partner|girlfriend|boyfriend|mom|mother|dad|father|brother|sister|roommate|project|side project|laptop|desktop|phone|headset|microphone|keyboard|office|home office|gym|gym playlist|coding playlist)\s+(?:is|is named|named)\s+(?<value>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WorkingOnProjectRegex = new(@"\b(?:i am working on|i'm working on|i am building|i'm building|my project is)\s+(?<project>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WorkFromPlaceRegex = new(@"\b(?:i work from|i study at|i train at)\s+(?<place>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AddressPreferenceRegex = new(@"\b(?:call me|you can call me|my nickname is|my preferred name is|refer to me as|address me as|my name is)\s+(?<name>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ReminderPrefixRegex = new(@"^(?:remind\s+me|set\s+(?:a\s+)?reminder)\s+(?:(?:to|about|that)\s+)?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RelativeReminderRegex = new(@"\bin\s+(?:(?<half>half\s+an)|(?<n>an?|one|two|three|four|five|six|seven|eight|nine|ten|fifteen|twenty|thirty|\d+(?:\.\d+)?))\s*(?<u>seconds?|secs?|minutes?|mins?|hours?|hrs?|days?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] ToneTerms =
    [
        "concise",
        "brief",
        "short",
        "direct",
        "formal",
        "casual",
        "friendly",
        "technical",
        "cinematic",
        "blunt",
        "warm",
        "playful",
        "witty",
        "serious",
        "energetic"
    ];

    public static TurnPersistencePlan PrepareTurn(string input)
    {
        var trimmed = input.Trim();

        foreach (var prefix in WholeTurnPrivacyPrefixes)
        {
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = trimmed[prefix.Length..].TrimStart(' ', ',', ':', ';', '-');
            return new TurnPersistencePlan(
                string.IsNullOrWhiteSpace(remainder) ? trimmed : remainder,
                "off-the-record",
                PersistTranscript: false,
                PersistAutomaticMemory: false,
                "The user explicitly marked this turn as off the record.");
        }

        if (LooksSensitive(trimmed))
        {
            return new TurnPersistencePlan(
                trimmed,
                "sensitive",
                PersistTranscript: false,
                PersistAutomaticMemory: false,
                "Sensitive content was detected, so Jarvis will not persist this turn.");
        }

        return new TurnPersistencePlan(trimmed, "persistent", PersistTranscript: true, PersistAutomaticMemory: true, string.Empty);
    }

    public static InteractionContextSnapshot BuildInteractionContext(
        DesktopContextSnapshot desktopContext,
        string defaultLocation,
        IEnumerable<string> recentActivity)
    {
        var location = string.IsNullOrWhiteSpace(defaultLocation) ? string.Empty : defaultLocation.Trim();
        var activities = recentActivity
            .Where(activity => !string.IsNullOrWhiteSpace(activity))
            .Select(activity => activity.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        return new InteractionContextSnapshot(
            desktopContext.LocalTime.ToUniversalTime(),
            GetTimeOfDay(desktopContext.LocalTime),
            location,
            desktopContext.ActiveApplication,
            desktopContext.ActiveWindow,
            desktopContext.VisibleApplications,
            activities);
    }

    public static IReadOnlyList<MemoryWriteRequest> ExtractLearnedPreferences(
        string userInput,
        InteractionContextSnapshot context)
    {
        var trimmed = userInput.Trim();
        var preferences = new List<MemoryWriteRequest>();

        if (TryExtractTonePreference(trimmed, context, out var tonePreference))
        {
            preferences.Add(tonePreference);
        }

        if (TryExtractAddressPreference(trimmed, context, out var addressPreference))
        {
            preferences.Add(addressPreference);
        }

        if (TryExtractResponseStylePreference(trimmed, context, out var responseStylePreference))
        {
            preferences.Add(responseStylePreference);
        }

        if (TryExtractConversationPreference(trimmed, context, out var conversationPreference))
        {
            preferences.Add(conversationPreference);
        }

        if (TryExtractPhrasingPreference(trimmed, context, out var phrasingPreference))
        {
            preferences.Add(phrasingPreference);
        }

        if (TryExtractAppPreference(trimmed, context, out var appPreference))
        {
            preferences.Add(appPreference);
        }

        if (TryExtractLocationPreference(trimmed, context, out var locationPreference))
        {
            preferences.Add(locationPreference);
        }

        if (TryExtractAutomationPreference(trimmed, context, out var automationPreference))
        {
            preferences.Add(automationPreference);
        }

        if (TryExtractRoutinePreference(trimmed, context, out var routinePreference))
        {
            preferences.Add(routinePreference);
        }

        if (TryExtractHabitPreference(trimmed, context, out var habitPreference))
        {
            preferences.Add(habitPreference);
        }

        if (TryExtractFavoritePreference(trimmed, context, out var favoritePreference))
        {
            preferences.Add(favoritePreference);
        }

        if (TryExtractRelationshipMemory(trimmed, context, out var relationshipMemory))
        {
            preferences.Add(relationshipMemory);
        }

        if (TryExtractProjectMemory(trimmed, context, out var projectMemory))
        {
            preferences.Add(projectMemory);
        }

        if (TryExtractDeviceMemory(trimmed, context, out var deviceMemory))
        {
            preferences.Add(deviceMemory);
        }

        if (TryExtractPlaceMemory(trimmed, context, out var placeMemory))
        {
            preferences.Add(placeMemory);
        }

        return preferences
            .GroupBy(preference => $"{preference.Kind}|{preference.Category}|{preference.Content}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    public static MemoryWriteRequest? BuildEpisodicMemory(
        string userInput,
        IReadOnlyList<AssistantToolExecution> toolResults,
        InteractionContextSnapshot context)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return null;
        }

        var successful = toolResults
            .Where(result => result.Succeeded && ShouldCaptureToolOutcome(result.ToolName))
            .Take(2)
            .ToArray();

        if (successful.Length == 0)
        {
            return null;
        }

        var outcomeText = string.Join("; ", successful.Select(result => $"{result.ToolName}: {TrimSentence(result.Summary)}"));
        var content = $"Task outcome: {TrimSentence(userInput)} -> {outcomeText}";

        return new MemoryWriteRequest(
            content,
            Kind: "episodic",
            Category: "activity",
            Source: "turn",
            Tags: ["episodic", "activity"],
            Context: context,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ImportanceScore: 0.64d,
            RetentionPolicy: "episodic");
    }

    public static MemoryWriteRequest? BuildReminderMemory(
        string input,
        InteractionContextSnapshot context,
        DateTimeOffset nowLocal)
    {
        if (!TryParseReminder(input, nowLocal, out var reminderText, out var reminderAtLocal))
        {
            return null;
        }

        return new MemoryWriteRequest(
            $"Reminder: {TrimSentence(reminderText)}",
            Kind: "reminder",
            Category: "reminders",
            Source: "manual",
            Tags: ["reminder", "time-based"],
            Context: context,
            CreatedAtUtc: nowLocal.ToUniversalTime(),
            ImportanceScore: 0.82d,
            RetentionPolicy: "until-complete",
            ReminderAtUtc: reminderAtLocal.ToUniversalTime(),
            ReminderText: TrimSentence(reminderText),
            ReminderStatus: "pending");
    }

    public static MemoryWriteRequest? BuildConversationSummary(
        IReadOnlyList<AssistantTurn> turns,
        IReadOnlyList<MemoryNote> notes)
    {
        if (turns.Count == 0)
        {
            return null;
        }

        var summarizedThroughUtc = notes
            .Where(note => string.Equals(note.Kind, "summary", StringComparison.OrdinalIgnoreCase))
            .Max(note => note.SummaryWindowEndUtc) ?? DateTimeOffset.MinValue;

        var eligibleTurns = turns
            .Where(turn => turn.TimestampUtc > summarizedThroughUtc
                && !string.Equals(turn.Privacy, "off-the-record", StringComparison.OrdinalIgnoreCase))
            .OrderBy(turn => turn.TimestampUtc)
            .ToArray();

        const int reserveRecentTurnCount = 6;
        const int minimumSummaryTurnCount = 6;
        const int summaryWindowTurnCount = 8;

        if (eligibleTurns.Length <= reserveRecentTurnCount)
        {
            return null;
        }

        var olderTurns = eligibleTurns
            .Take(eligibleTurns.Length - reserveRecentTurnCount)
            .Take(summaryWindowTurnCount)
            .ToArray();

        if (olderTurns.Length < minimumSummaryTurnCount)
        {
            return null;
        }

        var requests = olderTurns
            .Select(turn => TrimSentence(turn.UserInput))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        var outcomes = olderTurns
            .SelectMany(turn => turn.ToolResults ?? Array.Empty<AssistantToolExecution>())
            .Where(result => result.Succeeded && !string.IsNullOrWhiteSpace(result.Summary))
            .Select(result => TrimSentence(result.Summary))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        if (requests.Length == 0 && outcomes.Length == 0)
        {
            return null;
        }

        var apps = olderTurns
            .Select(turn => turn.Context?.ActiveApp)
            .Where(app => !string.IsNullOrWhiteSpace(app))
            .Select(app => app!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        var location = olderTurns
            .Select(turn => turn.Context?.Location)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        var dominantTimeOfDay = olderTurns
            .Select(turn => turn.Context?.TimeOfDay)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? string.Empty;
        var recentActivity = outcomes.Length > 0 ? outcomes : requests;
        var summaryContext = new InteractionContextSnapshot(
            olderTurns[^1].TimestampUtc,
            dominantTimeOfDay,
            location,
            apps.FirstOrDefault() ?? string.Empty,
            string.Empty,
            apps,
            recentActivity);
        var content = BuildSummaryText(olderTurns, requests, outcomes, apps);

        return new MemoryWriteRequest(
            content,
            Kind: "summary",
            Category: "conversation",
            Source: "summary",
            Tags: ["summary", "conversation"],
            Context: summaryContext,
            CreatedAtUtc: olderTurns[^1].TimestampUtc,
            SummaryWindowStartUtc: olderTurns[0].TimestampUtc,
            SummaryWindowEndUtc: olderTurns[^1].TimestampUtc);
    }

    public static bool LooksSensitive(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (SensitiveSignals.Any(signal => input.Contains(signal, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return CardLikeNumberRegex.IsMatch(input);
    }

    private static bool TryExtractTonePreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var lowered = input.ToLowerInvariant();

        if (!(lowered.Contains("reply", StringComparison.Ordinal)
                || lowered.Contains("replies", StringComparison.Ordinal)
                || lowered.Contains("response", StringComparison.Ordinal)
                || lowered.Contains("responses", StringComparison.Ordinal)
                || lowered.Contains("tone", StringComparison.Ordinal)
                || lowered.Contains("sound", StringComparison.Ordinal)
                || lowered.Contains("be more", StringComparison.Ordinal)
                || lowered.Contains("be less", StringComparison.Ordinal)
                || lowered.Contains("keep it", StringComparison.Ordinal)
                || lowered.Contains("prefer", StringComparison.Ordinal)))
        {
            preference = default!;
            return false;
        }

        var tone = ToneTerms.FirstOrDefault(term => lowered.Contains(term, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(tone))
        {
            preference = default!;
            return false;
        }

        preference = BuildPreference(
            $"Tone preference: keep replies {tone}.",
            "tone",
            context,
            ["preference", "tone", tone]);
        return true;
    }

    private static bool TryExtractAddressPreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var trimmed = input.Trim();

        if (trimmed.StartsWith("don't call me ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("dont call me ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("do not call me ", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = trimmed.StartsWith("do not call me ", StringComparison.OrdinalIgnoreCase)
                ? "do not call me "
                : trimmed.StartsWith("don't call me ", StringComparison.OrdinalIgnoreCase)
                    ? "don't call me "
                    : "dont call me ";
            var nameToAvoid = TrimSentence(trimmed[prefix.Length..]);

            if (!string.IsNullOrWhiteSpace(nameToAvoid))
            {
                preference = BuildPreference(
                    $"Address preference: do not call the user {nameToAvoid}.",
                    "address",
                    context,
                    ["preference", "address", "avoid"]);
                return true;
            }
        }

        var match = AddressPreferenceRegex.Match(trimmed);

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var name = TrimSentence(match.Groups["name"].Value);

        if (string.IsNullOrWhiteSpace(name))
        {
            preference = default!;
            return false;
        }

        preference = BuildPreference(
            $"Address preference: call the user {name}.",
            "address",
            context,
            ["preference", "address"]);
        return true;
    }

    private static bool TryExtractResponseStylePreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var lowered = input.Trim().ToLowerInvariant();

        if (!LooksLikeReplyShapingRequest(lowered))
        {
            preference = default!;
            return false;
        }

        string content;
        string tag;

        if (lowered.Contains("bullet point", StringComparison.Ordinal)
            || lowered.Contains("bullet-point", StringComparison.Ordinal)
            || lowered.Contains("bullet list", StringComparison.Ordinal)
            || lowered.Contains("use bullets", StringComparison.Ordinal))
        {
            content = "Response style preference: prefer bullet summaries.";
            tag = "bullets";
        }
        else if (lowered.Contains("step by step", StringComparison.Ordinal)
                 || lowered.Contains("step-by-step", StringComparison.Ordinal))
        {
            content = "Response style preference: prefer step-by-step answers.";
            tag = "step-by-step";
        }
        else if (lowered.Contains("paragraph", StringComparison.Ordinal)
                 || lowered.Contains("prose", StringComparison.Ordinal))
        {
            content = "Response style preference: prefer paragraph responses.";
            tag = "paragraphs";
        }
        else if (lowered.Contains("detailed", StringComparison.Ordinal)
                 || lowered.Contains("more detail", StringComparison.Ordinal)
                 || lowered.Contains("longer", StringComparison.Ordinal))
        {
            content = "Response style preference: prefer detailed replies.";
            tag = "detailed";
        }
        else if (lowered.Contains("concise", StringComparison.Ordinal)
                 || lowered.Contains("brief", StringComparison.Ordinal)
                 || lowered.Contains("short", StringComparison.Ordinal))
        {
            content = "Response style preference: prefer concise replies.";
            tag = "concise";
        }
        else if (lowered.Contains("cinematic", StringComparison.Ordinal))
        {
            content = "Response style preference: prefer cinematic phrasing when appropriate.";
            tag = "cinematic";
        }
        else
        {
            preference = default!;
            return false;
        }

        preference = BuildPreference(
            content,
            "response style",
            context,
            ["preference", "response-style", tag]);
        return true;
    }

    private static bool TryExtractConversationPreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var lowered = input.Trim().ToLowerInvariant();
        string content;
        string tag;

        if (lowered.Contains("don't use emojis", StringComparison.Ordinal)
            || lowered.Contains("dont use emojis", StringComparison.Ordinal)
            || lowered.Contains("do not use emojis", StringComparison.Ordinal)
            || lowered.Contains("no emojis", StringComparison.Ordinal))
        {
            content = "Conversation preference: avoid emojis.";
            tag = "no-emoji";
        }
        else if (lowered.Contains("use emojis", StringComparison.Ordinal)
                 || lowered.Contains("emojis are fine", StringComparison.Ordinal)
                 || lowered.Contains("emoji is fine", StringComparison.Ordinal))
        {
            content = "Conversation preference: emojis are allowed sparingly.";
            tag = "emoji";
        }
        else if (lowered.Contains("skip the small talk", StringComparison.Ordinal)
                 || lowered.Contains("no small talk", StringComparison.Ordinal)
                 || lowered.Contains("less small talk", StringComparison.Ordinal)
                 || lowered.Contains("keep small talk minimal", StringComparison.Ordinal))
        {
            content = "Conversation preference: keep small talk minimal.";
            tag = "small-talk-low";
        }
        else if (lowered.Contains("small talk is fine", StringComparison.Ordinal)
                 || lowered.Contains("a little small talk is fine", StringComparison.Ordinal)
                 || lowered.Contains("be more conversational", StringComparison.Ordinal)
                 || lowered.Contains("be a little warmer", StringComparison.Ordinal)
                 || lowered.Contains("be warmer", StringComparison.Ordinal))
        {
            content = "Conversation preference: brief friendly small talk is okay.";
            tag = "small-talk-okay";
        }
        else if (lowered.Contains("light humor is fine", StringComparison.Ordinal)
                 || lowered.Contains("be more playful", StringComparison.Ordinal)
                 || lowered.Contains("joke a little", StringComparison.Ordinal))
        {
            content = "Conversation preference: light humor is okay when it fits.";
            tag = "humor";
        }
        else
        {
            preference = default!;
            return false;
        }

        preference = BuildPreference(
            content,
            "conversation",
            context,
            ["preference", "conversation", tag]);
        return true;
    }

    private static bool TryExtractPhrasingPreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var trimmed = input.Trim();

        if (trimmed.Contains("instead of", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("don't say", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("dont say", StringComparison.OrdinalIgnoreCase))
        {
            preference = BuildPreference(
                $"Phrasing preference: {TrimSentence(trimmed)}",
                "phrasing",
                context,
                ["preference", "phrasing"]);
            return true;
        }

        preference = default!;
        return false;
    }

    private static bool TryExtractAppPreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var match = AppPreferenceRegex.Match(input.Trim());

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var app = match.Groups["app"].Value.Trim();
        var task = TrimSentence(match.Groups["task"].Value);

        if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(task))
        {
            preference = default!;
            return false;
        }

        preference = BuildPreference(
            $"App preference: use {app} for {task}.",
            "apps",
            context,
            ["preference", "apps", app]);
        return true;
    }

    private static bool TryExtractLocationPreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var match = LocationRegex.Match(input.Trim());

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var location = TrimSentence(match.Groups["location"].Value);

        if (string.IsNullOrWhiteSpace(location))
        {
            preference = default!;
            return false;
        }

        preference = BuildPreference(
            $"Location preference: use {location} as the user's location context.",
            "locations",
            context with { Location = location },
            ["preference", "locations", location]);
        return true;
    }

    private static bool TryExtractAutomationPreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var match = AutomationLeadRegex.Match(input.Trim());

        if (!match.Success)
        {
            match = AutomationTailRegex.Match(input.Trim());
        }

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var trigger = TrimSentence(match.Groups["trigger"].Value);
        var app = match.Groups["app"].Value.Trim();

        if (string.IsNullOrWhiteSpace(trigger) || string.IsNullOrWhiteSpace(app))
        {
            preference = default!;
            return false;
        }

        preference = BuildPreference(
            $"Automation preference: when the user {trigger}, prefer opening {app}.",
            "automations",
            context,
            ["preference", "automations", app]);
        return true;
    }

    private static bool TryExtractRoutinePreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var timedMatch = TimedRoutineRegex.Match(input.Trim());

        if (timedMatch.Success)
        {
            var timedCadence = timedMatch.Groups["when"].Value.Trim();
            var timedTime = TrimSentence(timedMatch.Groups["time"].Value);
            var timedAction = TrimSentence(timedMatch.Groups["action"].Value);

            if (!string.IsNullOrWhiteSpace(timedCadence)
                && !string.IsNullOrWhiteSpace(timedTime)
                && !string.IsNullOrWhiteSpace(timedAction))
            {
                preference = BuildStructuredMemory(
                    $"Routine preference: every {timedCadence} at {timedTime} the user {timedAction}.",
                    "routines",
                    "routine",
                    $"{timedCadence} {timedTime} routine",
                    context,
                    ["preference", "routine", timedCadence],
                    [new MemoryAttribute("cadence", timedCadence), new MemoryAttribute("time", timedTime), new MemoryAttribute("action", timedAction)]);
                return true;
            }
        }

        var anchoredMatch = AnchoredRoutineRegex.Match(input.Trim());

        if (anchoredMatch.Success)
        {
            var anchor = TrimSentence(anchoredMatch.Groups["anchor"].Value);
            var anchoredAction = TrimSentence(anchoredMatch.Groups["action"].Value);

            if (!string.IsNullOrWhiteSpace(anchor) && !string.IsNullOrWhiteSpace(anchoredAction))
            {
                preference = BuildStructuredMemory(
                    $"Routine preference: {anchor} usually means the user {anchoredAction}.",
                    "routines",
                    "routine",
                    $"{anchor} routine",
                    context,
                    ["preference", "routine", anchor],
                    [new MemoryAttribute("anchor", anchor), new MemoryAttribute("action", anchoredAction)]);
                return true;
            }
        }

        var namedMatch = NamedRoutineRegex.Match(input.Trim());

        if (namedMatch.Success)
        {
            var namedCadence = namedMatch.Groups["when"].Value.Trim();
            var namedAction = TrimSentence(namedMatch.Groups["action"].Value);

            if (!string.IsNullOrWhiteSpace(namedCadence) && !string.IsNullOrWhiteSpace(namedAction))
            {
                preference = BuildStructuredMemory(
                    $"Routine preference: the user's {namedCadence} routine is {namedAction}.",
                    "routines",
                    "routine",
                    $"{namedCadence} routine",
                    context,
                    ["preference", "routine", namedCadence],
                    [new MemoryAttribute("cadence", namedCadence), new MemoryAttribute("action", namedAction)]);
                return true;
            }
        }

        var match = RoutineRegex.Match(input.Trim());

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var cadence = match.Groups["when"].Value.Trim();
        var action = TrimSentence(match.Groups["action"].Value);

        if (string.IsNullOrWhiteSpace(cadence) || string.IsNullOrWhiteSpace(action))
        {
            preference = default!;
            return false;
        }

        preference = BuildStructuredMemory(
            $"Routine preference: every {cadence} the user {action}.",
            "routines",
            "routine",
            $"{cadence} routine",
            context,
            ["preference", "routine", cadence],
            [new MemoryAttribute("cadence", cadence), new MemoryAttribute("action", action)]);
        return true;
    }

    private static bool TryExtractHabitPreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var match = HabitRegex.Match(input.Trim());

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var habit = TrimSentence(match.Groups["habit"].Value);

        if (string.IsNullOrWhiteSpace(habit))
        {
            preference = default!;
            return false;
        }

        preference = BuildStructuredMemory(
            $"Habit: the user usually {habit}.",
            "habits",
            "habit",
            habit,
            context,
            ["preference", "habit"],
            [new MemoryAttribute("behavior", habit)]);
        return true;
    }

    private static bool TryExtractFavoritePreference(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var match = FavoriteAppRegex.Match(input.Trim());

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var app = match.Groups["app"].Value.Trim();
        var task = TrimSentence(match.Groups["task"].Value);

        if (string.IsNullOrWhiteSpace(app))
        {
            preference = default!;
            return false;
        }

        var content = string.IsNullOrWhiteSpace(task)
            ? $"Favorite app: {app}."
            : $"Favorite app: {app} for {task}.";
        preference = BuildStructuredMemory(
            content,
            "favorites",
            "other",
            app,
            context,
            ["preference", "favorite", app],
            string.IsNullOrWhiteSpace(task) ? null : [new MemoryAttribute("task", task)]);
        return true;
    }

    private static bool TryExtractRelationshipMemory(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var match = MyNamedThingRegex.Match(input.Trim());

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var label = match.Groups["label"].Value.Trim();
        var value = TrimSentence(match.Groups["value"].Value);

        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
        {
            preference = default!;
            return false;
        }

        var normalizedLabel = label.ToLowerInvariant();
        var mappedKind = normalizedLabel switch
        {
            "project" or "side project" => "project",
            "laptop" or "desktop" or "phone" or "headset" or "microphone" or "keyboard" => "device",
            "office" or "home office" or "gym" => "place",
            "gym playlist" or "coding playlist" => "playlist",
            _ => "relationship"
        };

        var category = mappedKind switch
        {
            "project" => "projects",
            "device" => "devices",
            "place" => "places",
            _ => "relationships"
        };

        preference = BuildStructuredMemory(
            $"{TitleCase(label)}: {value}.",
            category,
            mappedKind,
            value,
            context,
            ["memory", mappedKind, normalizedLabel],
            [new MemoryAttribute("label", label)],
            [label, $"my {label}"]);
        return true;
    }

    private static bool TryExtractProjectMemory(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var match = WorkingOnProjectRegex.Match(input.Trim());

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var project = TrimSentence(match.Groups["project"].Value);

        if (string.IsNullOrWhiteSpace(project))
        {
            preference = default!;
            return false;
        }

        preference = BuildStructuredMemory(
            $"Project: {project}.",
            "projects",
            "project",
            project,
            context,
            ["memory", "project"],
            [new MemoryAttribute("status", "active")]);
        return true;
    }

    private static bool TryExtractDeviceMemory(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var match = MyNamedThingRegex.Match(input.Trim());

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var label = match.Groups["label"].Value.Trim().ToLowerInvariant();

        if (label is not ("laptop" or "desktop" or "phone" or "headset" or "microphone" or "keyboard"))
        {
            preference = default!;
            return false;
        }

        var value = TrimSentence(match.Groups["value"].Value);

        if (string.IsNullOrWhiteSpace(value))
        {
            preference = default!;
            return false;
        }

        preference = BuildStructuredMemory(
            $"{TitleCase(label)}: {value}.",
            "devices",
            "device",
            value,
            context,
            ["memory", "device", label],
            [new MemoryAttribute("deviceType", label)],
            [label, $"my {label}"]);
        return true;
    }

    private static bool TryExtractPlaceMemory(
        string input,
        InteractionContextSnapshot context,
        out MemoryWriteRequest preference)
    {
        var namedThingMatch = MyNamedThingRegex.Match(input.Trim());

        if (namedThingMatch.Success)
        {
            var label = namedThingMatch.Groups["label"].Value.Trim().ToLowerInvariant();

            if (label is "office" or "home office" or "gym")
            {
                var value = TrimSentence(namedThingMatch.Groups["value"].Value);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    preference = BuildStructuredMemory(
                        $"{TitleCase(label)}: {value}.",
                        "places",
                        "place",
                        value,
                        context with { Location = value },
                        ["memory", "place", label],
                        [new MemoryAttribute("label", label)],
                        [label, $"my {label}"]);
                    return true;
                }
            }
        }

        var match = WorkFromPlaceRegex.Match(input.Trim());

        if (!match.Success)
        {
            preference = default!;
            return false;
        }

        var place = TrimSentence(match.Groups["place"].Value);

        if (string.IsNullOrWhiteSpace(place))
        {
            preference = default!;
            return false;
        }

        preference = BuildStructuredMemory(
            $"Place: {place}.",
            "places",
            "place",
            place,
            context with { Location = place },
            ["memory", "place"]);
        return true;
    }

    private static MemoryWriteRequest BuildPreference(
        string content,
        string category,
        InteractionContextSnapshot context,
        string[] tags)
    {
        return new MemoryWriteRequest(
            content,
            Kind: "preference",
            Category: category,
            Source: "learned",
            Tags: tags,
            Context: context,
            CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static MemoryWriteRequest BuildStructuredMemory(
        string content,
        string category,
        string entityKind,
        string entityName,
        InteractionContextSnapshot context,
        string[] tags,
        MemoryAttribute[]? attributes = null,
        string[]? aliases = null)
    {
        var kind = entityKind == "relationship" ? "relationship" : "entity";

        return new MemoryWriteRequest(
            content,
            Kind: kind,
            Category: category,
            Source: "learned",
            Tags: tags,
            Context: context,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ImportanceScore: entityKind == "relationship" ? 0.80d : 0.70d,
            RetentionPolicy: "long",
            Entities:
            [
                new StructuredMemoryReference(
                    entityKind,
                    entityName,
                    content,
                    aliases,
                    attributes)
            ]);
    }

    private static bool LooksLikeReplyShapingRequest(string lowered)
    {
        return lowered.Contains("reply", StringComparison.Ordinal)
               || lowered.Contains("replies", StringComparison.Ordinal)
               || lowered.Contains("response", StringComparison.Ordinal)
               || lowered.Contains("responses", StringComparison.Ordinal)
               || lowered.Contains("answer", StringComparison.Ordinal)
               || lowered.Contains("answers", StringComparison.Ordinal)
               || lowered.Contains("explain", StringComparison.Ordinal)
               || lowered.Contains("explanations", StringComparison.Ordinal)
               || lowered.Contains("be more", StringComparison.Ordinal)
               || lowered.Contains("be less", StringComparison.Ordinal)
               || lowered.Contains("keep it", StringComparison.Ordinal)
               || lowered.Contains("use bullet", StringComparison.Ordinal)
               || lowered.Contains("small talk", StringComparison.Ordinal)
               || lowered.Contains("emoji", StringComparison.Ordinal);
    }

    private static bool TryParseReminder(
        string input,
        DateTimeOffset nowLocal,
        out string reminderText,
        out DateTimeOffset reminderAtLocal)
    {
        reminderText = string.Empty;
        reminderAtLocal = default;

        var prefix = ReminderPrefixRegex.Match(input.Trim());

        if (!prefix.Success)
        {
            return false;
        }

        var payload = input.Trim()[prefix.Length..];
        var relative = RelativeReminderRegex.Match(payload);

        if (relative.Success)
        {
            reminderAtLocal = nowLocal.Add(ParseRelativeReminder(relative));
            payload = payload.Remove(relative.Index, relative.Length);
        }
        else
        {
            // The calendar's reader, so reminders understand the same times: "3.30", "2:29 a. m.", "noon".
            var (date, time) = CalendarText.ExtractWhen(ref payload, nowLocal.DateTime);
            var today = DateOnly.FromDateTime(nowLocal.DateTime);

            // A time with no date is the next one; a date with no time ("tomorrow") is 9 AM.
            var at = time is TimeOnly clock
                ? (date ?? (clock > TimeOnly.FromDateTime(nowLocal.DateTime) ? today : today.AddDays(1))).ToDateTime(clock)
                : date?.ToDateTime(new TimeOnly(9, 0));

            // An unspecified-kind DateTime takes the local offset for its own date, so DST is right.
            if (at is null || new DateTimeOffset(at.Value) < nowLocal)
            {
                return false;
            }

            reminderAtLocal = new DateTimeOffset(at.Value);
        }

        // Words stranded by the removed time: "to stretch" from "in 10 minutes to stretch", "call mom at".
        reminderText = Regex.Replace(payload, @"\s+", " ").Trim(' ', ',', '.', ';', ':', '-');
        reminderText = Regex.Replace(reminderText, @"^(?:(?:to|about|that)\s+)+|(?:\s+(?:at|on|in|for|by|and|to|,))+$", string.Empty, RegexOptions.IgnoreCase).Trim(' ', ',', '.');
        return reminderText.Length > 0;
    }

    private static TimeSpan ParseRelativeReminder(Match match)
    {
        var amount = match.Groups["half"].Success ? 0.5 : match.Groups["n"].Value.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            "five" => 5,
            "six" => 6,
            "seven" => 7,
            "eight" => 8,
            "nine" => 9,
            "ten" => 10,
            "fifteen" => 15,
            "twenty" => 20,
            "thirty" => 30,
            var number => double.Parse(number, CultureInfo.InvariantCulture)
        };

        return char.ToLowerInvariant(match.Groups["u"].Value[0]) switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            _ => TimeSpan.FromDays(amount)
        };
    }

    private static string BuildSummaryText(
        IReadOnlyList<AssistantTurn> turns,
        IReadOnlyList<string> requests,
        IReadOnlyList<string> outcomes,
        IReadOnlyList<string> apps)
    {
        var start = turns[0].TimestampUtc;
        var end = turns[^1].TimestampUtc;
        var parts = new List<string>
        {
            $"Conversation summary from {start:yyyy-MM-dd HH:mm}Z to {end:yyyy-MM-dd HH:mm}Z."
        };

        if (requests.Count > 0)
        {
            parts.Add("Key requests: " + string.Join(" | ", requests));
        }

        if (outcomes.Count > 0)
        {
            parts.Add("Outcomes: " + string.Join(" | ", outcomes));
        }

        if (apps.Count > 0)
        {
            parts.Add("Active apps: " + string.Join(" | ", apps));
        }

        return string.Join(" ", parts);
    }

    private static string GetTimeOfDay(DateTimeOffset localTime)
    {
        return localTime.Hour switch
        {
            >= 5 and < 12 => "morning",
            >= 12 and < 17 => "afternoon",
            >= 17 and < 22 => "evening",
            _ => "night"
        };
    }

    private static bool ShouldCaptureToolOutcome(string toolName)
    {
        var normalized = toolName.Trim().ToLowerInvariant();

        return normalized is "open app"
            or "open path"
            or "read file"
            or "find files"
            or "browse"
            or "search web"
            or "research"
            or "run"
            or "screen"
            or "ambient"
            or "analyze image"
            or "analyze document";
    }

    private static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    private static string TrimSentence(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var flattened = value.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= 180 ? flattened : flattened[..180] + "...";
    }
}
