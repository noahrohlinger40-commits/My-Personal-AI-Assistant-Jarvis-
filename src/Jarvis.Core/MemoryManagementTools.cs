namespace Jarvis.Core;

public sealed class MemoryTool : IAssistantTool
{
    public string Name => "memory";

    public string Description => "Memory diagnostics and maintenance. Use `memory review`, `memory set retention`, `memory set importance`, `memory routines`, `memory diagnostics`, `memory maintain`, or `memory merge`.";

    public bool CanHandle(string input) => MemoryCommandParsing.TryParse(input, out _);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!MemoryCommandParsing.TryParse(input, out var command))
        {
            return BuildUsageResult();
        }

        switch (command.Action)
        {
            case "help":
                return BuildUsageResult();
            case "review":
                return await ReviewMemoryAsync(command.Target, context, cancellationToken);
            case "set-retention":
                return await SetRetentionAsync(command.Target, command.Payload, context, cancellationToken);
            case "set-importance":
                return await SetImportanceAsync(command.Target, command.Payload, context, cancellationToken);
            case "routines":
                return await ShowRoutinesAsync(context, cancellationToken);
            case "diagnostics":
                return await BuildDiagnosticsAsync(context, cancellationToken);
            case "maintain":
                return await MaintainAsync(applyChanges: false, context, cancellationToken);
            case "maintain-apply":
                return await MaintainAsync(applyChanges: true, context, cancellationToken);
            case "merge":
                return await MergeMemoriesAsync(command.Target, command.Payload, context, cancellationToken);
            default:
                return BuildUsageResult();
        }
    }

    private static ToolResult BuildUsageResult()
    {
        return new ToolResult(
            string.Join(
                Environment.NewLine,
                "Memory commands:",
                "- memory review <id-or-phrase>",
                "- memory set retention <id-or-phrase> :: <short|standard|episodic|rolling|long|forever|until-complete>",
                "- memory set importance <id-or-phrase> :: <5-100>",
                "- memory routines",
                "- memory diagnostics",
                "- memory maintain",
                "- memory maintain apply",
                "- memory merge <primary-id-or-phrase> :: <secondary-id-or-phrase>"),
            VerificationText: "Displayed memory command usage.",
            SummaryText: "Showed memory command usage.");
    }

    private static async Task<ToolResult> ReviewMemoryAsync(
        string target,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveSingleMemoryAsync(target, context, cancellationToken);

        if (resolved.ErrorResult is not null)
        {
            return resolved.ErrorResult;
        }

        var note = resolved.Note!;
        var lines = new List<string>
        {
            $"Memory {note.Id}:",
            $"- Kind: {note.Kind}",
            $"- Category: {OrNone(note.Category)}",
            $"- Source: {OrNone(note.Source)}",
            $"- Privacy: {OrNone(note.Privacy)}",
            $"- Created: {note.CreatedAtUtc:yyyy-MM-dd HH:mm}Z",
            $"- Last observed: {(note.LastObservedAtUtc ?? note.CreatedAtUtc):yyyy-MM-dd HH:mm}Z",
            $"- Observations: {note.ObservationCount}",
            $"- Importance: {Math.Round(note.ImportanceScore * 100d)}",
            $"- Retention: {OrNone(note.RetentionPolicy)}",
            $"- Expires: {(note.ExpiresAtUtc is null ? "none" : note.ExpiresAtUtc.Value.ToString("yyyy-MM-dd HH:mm") + "Z")}",
            $"- Reminder: {(note.ReminderAtUtc is null ? "none" : $"{note.ReminderAtUtc.Value.ToLocalTime():yyyy-MM-dd h:mm tt} | {MemoryPolicies.NormalizeReminderStatus(note.ReminderStatus)}")}",
            $"- Tags: {(note.Tags is { Length: > 0 } ? string.Join(", ", note.Tags) : "none")}",
            $"- Context: {(note.Context is null ? "none" : note.Context.ToInlineText())}",
            "- Content:",
            note.Content
        };

        if (note.Entities is { Length: > 0 })
        {
            lines.Add("- Entities:");
            lines.AddRange(note.Entities.Select(entity => $"  {entity.ToInlineText()}"));
        }

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: $"Reviewed memory `{ShortId(note.Id)}`.",
            SummaryText: "Reviewed one memory.");
    }

    private static async Task<ToolResult> SetRetentionAsync(
        string target,
        string payload,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveSingleMemoryAsync(target, context, cancellationToken);

        if (resolved.ErrorResult is not null)
        {
            return resolved.ErrorResult;
        }

        var normalizedRetention = MemoryPolicies.NormalizeRetentionPolicy(payload);

        if (string.IsNullOrWhiteSpace(normalizedRetention))
        {
            return new ToolResult(
                "Usage: memory set retention <id-or-phrase> :: <short|standard|episodic|rolling|long|forever|until-complete>",
                Succeeded: false,
                SummaryText: "Missing retention value.");
        }

        var note = resolved.Note!;
        var updated = await context.MemoryStore.UpdateAsync(
            BuildUpdateRequest(
                note,
                retentionPolicy: normalizedRetention,
                expiresAtUtc: null,
                userApprovedRetention: true),
            cancellationToken);

        return updated is null
            ? new ToolResult(
                $"Unable to update retention for {ShortId(note.Id)}.",
                Succeeded: false,
                VerificationText: "The memory could not be updated.",
                SummaryText: "Memory retention update failed.")
            : new ToolResult(
                $"Updated retention for {ShortId(updated.Id)} to {normalizedRetention}.",
                VerificationText: $"Retention for `{ShortId(updated.Id)}` is now `{normalizedRetention}`.",
                SummaryText: "Updated memory retention.");
    }

    private static async Task<ToolResult> SetImportanceAsync(
        string target,
        string payload,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveSingleMemoryAsync(target, context, cancellationToken);

        if (resolved.ErrorResult is not null)
        {
            return resolved.ErrorResult;
        }

        if (!TryParseImportance(payload, out var importance))
        {
            return new ToolResult(
                "Usage: memory set importance <id-or-phrase> :: <5-100>",
                Succeeded: false,
                SummaryText: "Missing importance value.");
        }

        var note = resolved.Note!;
        var updated = await context.MemoryStore.UpdateAsync(
            BuildUpdateRequest(note, importanceScore: importance),
            cancellationToken);

        return updated is null
            ? new ToolResult(
                $"Unable to update importance for {ShortId(note.Id)}.",
                Succeeded: false,
                VerificationText: "The memory could not be updated.",
                SummaryText: "Memory importance update failed.")
            : new ToolResult(
                $"Updated importance for {ShortId(updated.Id)} to {Math.Round(updated.ImportanceScore * 100d)}.",
                VerificationText: $"Importance for `{ShortId(updated.Id)}` is now `{Math.Round(updated.ImportanceScore * 100d)}`.",
                SummaryText: "Updated memory importance.");
    }

    private static async Task<ToolResult> ShowRoutinesAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var notes = await context.MemoryStore.GetAllAsync(cancellationToken);
        var routines = notes
            .Where(note =>
                string.Equals(note.Category, "routines", StringComparison.OrdinalIgnoreCase)
                || string.Equals(note.Category, "habits", StringComparison.OrdinalIgnoreCase)
                || string.Equals(note.Category, "favorites", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(note => note.ObservationCount)
            .ThenByDescending(note => note.LastObservedAtUtc ?? note.CreatedAtUtc)
            .Take(16)
            .ToArray();

        if (routines.Length == 0)
        {
            return new ToolResult(
                "No routines, habits, or favorites have been learned yet.",
                VerificationText: "The memory store has no routine-like entries.",
                SummaryText: "No routines learned.");
        }

        var lines = new List<string> { "Learned routines and habits:" };
        lines.AddRange(routines.Select(ToolInputHelper.RenderMemoryLine));
        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: $"Listed {routines.Length} routine-like memories.",
            SummaryText: "Listed routines and habits.");
    }

    private static async Task<ToolResult> BuildDiagnosticsAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var notes = await context.MemoryStore.GetAllAsync(cancellationToken);
        var report = MemoryMaintenance.Analyze(notes);
        return new ToolResult(
            report.ToDisplayText(),
            VerificationText: "Built a heuristic memory-quality and maintenance report from the active memory store.",
            SummaryText: "Built memory diagnostics.");
    }

    private static async Task<ToolResult> MaintainAsync(
        bool applyChanges,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var notes = await context.MemoryStore.GetAllAsync(cancellationToken);
        var plan = MemoryMaintenance.Analyze(notes);

        if (!applyChanges)
        {
            return new ToolResult(
                plan.ToMaintenancePreviewText(),
                VerificationText: "Built a memory maintenance preview without changing the store.",
                SummaryText: "Previewed memory maintenance.");
        }

        var rescored = 0;
        var retuned = 0;
        var deduped = 0;

        foreach (var candidate in plan.RescoreCandidates)
        {
            var note = notes.FirstOrDefault(item => string.Equals(item.Id, candidate.Id, StringComparison.OrdinalIgnoreCase));

            if (note is null)
            {
                continue;
            }

            var updated = await context.MemoryStore.UpdateAsync(
                BuildUpdateRequest(note, importanceScore: candidate.SuggestedImportanceScore),
                cancellationToken);

            if (updated is not null)
            {
                rescored++;
            }
        }

        foreach (var candidate in plan.RetentionCandidates)
        {
            var note = notes.FirstOrDefault(item => string.Equals(item.Id, candidate.Id, StringComparison.OrdinalIgnoreCase));

            if (note is null)
            {
                continue;
            }

            var updated = await context.MemoryStore.UpdateAsync(
                BuildUpdateRequest(
                    note,
                    retentionPolicy: candidate.SuggestedRetentionPolicy,
                    expiresAtUtc: null,
                    userApprovedRetention: note.UserApprovedRetention),
                cancellationToken);

            if (updated is not null)
            {
                retuned++;
            }
        }

        foreach (var group in plan.ExactDuplicateGroups)
        {
            var keep = group.Ids[0];

            foreach (var duplicateId in group.Ids.Skip(1))
            {
                if (string.Equals(duplicateId, keep, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var forgot = await context.MemoryStore.ForgetAsync(duplicateId, "memory maintenance duplicate cleanup", cancellationToken);

                if (forgot)
                {
                    deduped++;
                }
            }
        }

        var lines = new List<string>
        {
            "Applied memory maintenance.",
            $"- Rescored memories: {rescored}",
            $"- Retention updates: {retuned}",
            $"- Forgotten duplicates: {deduped}"
        };

        if (rescored == 0 && retuned == 0 && deduped == 0)
        {
            lines.Add("- No changes were needed.");
        }

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: "Applied heuristic memory maintenance updates to the active memory store.",
            SummaryText: "Applied memory maintenance.");
    }

    private static async Task<ToolResult> MergeMemoriesAsync(
        string primaryTarget,
        string secondaryTarget,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var primaryResolution = await ResolveSingleMemoryAsync(primaryTarget, context, cancellationToken);

        if (primaryResolution.ErrorResult is not null)
        {
            return primaryResolution.ErrorResult;
        }

        var secondaryResolution = await ResolveSingleMemoryAsync(secondaryTarget, context, cancellationToken);

        if (secondaryResolution.ErrorResult is not null)
        {
            return secondaryResolution.ErrorResult;
        }

        var primary = primaryResolution.Note!;
        var secondary = secondaryResolution.Note!;

        if (string.Equals(primary.Id, secondary.Id, StringComparison.OrdinalIgnoreCase))
        {
            return new ToolResult(
                "Select two different memories to merge.",
                Succeeded: false,
                SummaryText: "Memory merge target was duplicated.");
        }

        var mergedEntities = MergeEntities(primary.Entities, secondary.Entities);
        var mergedTags = MergeTags(primary.Tags, secondary.Tags);
        var mergedImportance = MemoryPolicies.ClampImportance(Math.Max(primary.ImportanceScore, secondary.ImportanceScore) + 0.04d);
        var mergedRetention = ChooseRetentionPolicy(primary, secondary);
        var mergedReminderAt = primary.ReminderAtUtc ?? secondary.ReminderAtUtc;
        var mergedReminderText = string.IsNullOrWhiteSpace(primary.ReminderText) ? secondary.ReminderText : primary.ReminderText;
        var mergedReminderStatus = string.IsNullOrWhiteSpace(primary.ReminderStatus) ? secondary.ReminderStatus : primary.ReminderStatus;
        var mergedContent = primary.Content.Length >= secondary.Content.Length ? primary.Content : secondary.Content;
        var updatedPrimary = await context.MemoryStore.UpdateAsync(
            new MemoryUpdateRequest(
                primary.Id,
                mergedContent,
                primary.Kind,
                primary.Category,
                primary.Privacy,
                mergedTags,
                mergedImportance,
                mergedRetention,
                null,
                primary.UserApprovedRetention || secondary.UserApprovedRetention,
                mergedReminderAt,
                mergedReminderText,
                mergedReminderStatus,
                mergedEntities),
            cancellationToken);

        if (updatedPrimary is null)
        {
            return new ToolResult(
                $"Unable to update {ShortId(primary.Id)} during merge.",
                Succeeded: false,
                VerificationText: "The primary memory could not be updated.",
                SummaryText: "Memory merge failed.");
        }

        var forgot = await context.MemoryStore.ForgetAsync(secondary.Id, $"merged into {primary.Id}", cancellationToken);

        return forgot
            ? new ToolResult(
                $"Merged {ShortId(secondary.Id)} into {ShortId(primary.Id)}.",
                VerificationText: $"Merged memory `{ShortId(secondary.Id)}` into `{ShortId(primary.Id)}` and forgot the secondary note.",
                SummaryText: "Merged two memories.")
            : new ToolResult(
                $"Updated {ShortId(primary.Id)} but could not forget {ShortId(secondary.Id)}.",
                Succeeded: false,
                VerificationText: "The primary memory updated, but the secondary memory could not be forgotten.",
                SummaryText: "Memory merge was only partially applied.");
    }

    private static async Task<ResolvedMemory> ResolveSingleMemoryAsync(
        string target,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return new ResolvedMemory(null, new ToolResult("A memory id or search phrase is required.", Succeeded: false, SummaryText: "Missing memory target."));
        }

        var notes = await context.MemoryStore.GetAllAsync(cancellationToken);
        var directMatch = notes.FirstOrDefault(note =>
            string.Equals(note.Id, target, StringComparison.OrdinalIgnoreCase)
            || note.Id.StartsWith(target, StringComparison.OrdinalIgnoreCase));

        if (directMatch is not null)
        {
            return new ResolvedMemory(directMatch, null);
        }

        var searchContext = await ToolInputHelper.BuildMemoryQueryContextAsync(context, target, cancellationToken);
        var searched = await context.MemoryStore.SearchAsync(target, searchContext, 5, cancellationToken);

        if (searched.Count == 0)
        {
            return new ResolvedMemory(
                null,
                new ToolResult(
                    $"No stored memory matched \"{target}\".",
                    Succeeded: false,
                    VerificationText: "The requested memory target did not match any active memory.",
                    SummaryText: "No matching memory."));
        }

        if (searched.Count > 1 && !string.Equals(searched[0].Content, target, StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedMemory(
                null,
                new ToolResult(
                    "More than one memory matched. Use a longer phrase or an id prefix:" + Environment.NewLine +
                    string.Join(Environment.NewLine, searched.Select(ToolInputHelper.RenderMemoryLine)),
                    Succeeded: false,
                    VerificationText: "The memory target was ambiguous.",
                    SummaryText: "Ambiguous memory target."));
        }

        return new ResolvedMemory(searched[0], null);
    }

    private static MemoryUpdateRequest BuildUpdateRequest(
        MemoryNote note,
        double? importanceScore = null,
        string? retentionPolicy = null,
        DateTimeOffset? expiresAtUtc = null,
        bool? userApprovedRetention = null,
        StructuredMemoryReference[]? entities = null)
    {
        return new MemoryUpdateRequest(
            note.Id,
            note.Content,
            note.Kind,
            note.Category,
            note.Privacy,
            note.Tags ?? Array.Empty<string>(),
            importanceScore ?? note.ImportanceScore,
            retentionPolicy ?? note.RetentionPolicy,
            expiresAtUtc,
            userApprovedRetention ?? note.UserApprovedRetention,
            note.ReminderAtUtc,
            note.ReminderText,
            note.ReminderStatus,
            entities ?? note.Entities);
    }

    private static bool TryParseImportance(string payload, out double importance)
    {
        importance = 0;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var trimmed = payload.Trim().TrimEnd('%');

        if (!double.TryParse(trimmed, out var raw))
        {
            return false;
        }

        importance = raw > 1d ? raw / 100d : raw;
        importance = MemoryPolicies.ClampImportance(importance);
        return true;
    }

    private static string[] MergeTags(IEnumerable<string>? left, IEnumerable<string>? right)
    {
        return (left ?? Array.Empty<string>())
            .Concat(right ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static StructuredMemoryReference[] MergeEntities(
        IEnumerable<StructuredMemoryReference>? left,
        IEnumerable<StructuredMemoryReference>? right)
    {
        return (left ?? Array.Empty<StructuredMemoryReference>())
            .Concat(right ?? Array.Empty<StructuredMemoryReference>())
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .GroupBy(
                entity => $"{entity.Kind.Trim().ToLowerInvariant()}|{NormalizeEntityName(entity.Name)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var sample = group.OrderByDescending(entity => entity.SafeAliases.Length).First();
                var aliases = group
                    .SelectMany(entity => entity.SafeAliases.Append(entity.Name))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(value => !string.Equals(value, sample.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var attributes = group
                    .SelectMany(entity => entity.SafeAttributes)
                    .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key) && !string.IsNullOrWhiteSpace(attribute.Value))
                    .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(attributeGroup => attributeGroup.Last())
                    .OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var summary = group
                    .Select(entity => entity.Summary)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .OrderByDescending(value => value.Length)
                    .FirstOrDefault() ?? string.Empty;
                return sample with
                {
                    Summary = summary,
                    Aliases = aliases,
                    Attributes = attributes
                };
            })
            .OrderBy(entity => entity.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeEntityName(string value)
    {
        var characters = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ChooseRetentionPolicy(MemoryNote primary, MemoryNote secondary)
    {
        var left = MemoryPolicies.NormalizeRetentionPolicy(primary.RetentionPolicy);
        var right = MemoryPolicies.NormalizeRetentionPolicy(secondary.RetentionPolicy);

        return new[] { left, right }
            .OrderByDescending(GetRetentionRank)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? left;
    }

    private static int GetRetentionRank(string policy)
    {
        return policy switch
        {
            "forever" => 6,
            "long" => 5,
            "until-complete" => 4,
            "standard" => 3,
            "episodic" => 2,
            "rolling" => 1,
            "short" => 0,
            _ => -1
        };
    }

    private static string OrNone(string value) => string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();

    private static string ShortId(string id) => string.IsNullOrWhiteSpace(id) ? "n/a" : id.Length <= 8 ? id : id[..8];

    private sealed record ResolvedMemory(MemoryNote? Note, ToolResult? ErrorResult);
}

internal sealed record MemoryCommand(
    string Action,
    string Target = "",
    string Payload = "");

internal static class MemoryCommandParsing
{
    public static bool TryParse(string input, out MemoryCommand command)
    {
        if (ToolInputHelper.MatchesExact(input, "memory", "memory help", "memory commands"))
        {
            command = new MemoryCommand("help");
            return true;
        }

        if (ToolInputHelper.MatchesExact(input, "routines", "show routines", "list routines", "habits", "show habits"))
        {
            command = new MemoryCommand("routines");
            return true;
        }

        if (!ToolInputHelper.StartsWithCommand(input, "memory"))
        {
            command = new MemoryCommand(string.Empty);
            return false;
        }

        var tail = ToolInputHelper.GetCommandTail(input, "memory");

        if (string.IsNullOrWhiteSpace(tail))
        {
            command = new MemoryCommand("help");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(tail, "review"))
        {
            command = new MemoryCommand("review", ToolInputHelper.GetCommandTail(tail, "review"));
            return true;
        }

        if (TryParseTargetAndPayload(tail, "set retention", out command, "set-retention"))
        {
            return true;
        }

        if (TryParseTargetAndPayload(tail, "set importance", out command, "set-importance"))
        {
            return true;
        }

        if (TryParseTargetAndPayload(tail, "merge", out command, "merge"))
        {
            return true;
        }

        if (tail.Equals("routines", StringComparison.OrdinalIgnoreCase)
            || tail.Equals("habits", StringComparison.OrdinalIgnoreCase)
            || tail.Equals("favorites", StringComparison.OrdinalIgnoreCase))
        {
            command = new MemoryCommand("routines");
            return true;
        }

        if (tail.Equals("diagnostics", StringComparison.OrdinalIgnoreCase)
            || tail.Equals("quality", StringComparison.OrdinalIgnoreCase)
            || tail.Equals("audit", StringComparison.OrdinalIgnoreCase))
        {
            command = new MemoryCommand("diagnostics");
            return true;
        }

        if (tail.Equals("maintain", StringComparison.OrdinalIgnoreCase)
            || tail.Equals("maintenance", StringComparison.OrdinalIgnoreCase)
            || tail.Equals("cleanup", StringComparison.OrdinalIgnoreCase))
        {
            command = new MemoryCommand("maintain");
            return true;
        }

        if (tail.Equals("maintain apply", StringComparison.OrdinalIgnoreCase)
            || tail.Equals("maintenance apply", StringComparison.OrdinalIgnoreCase)
            || tail.Equals("cleanup apply", StringComparison.OrdinalIgnoreCase))
        {
            command = new MemoryCommand("maintain-apply");
            return true;
        }

        command = new MemoryCommand("help");
        return true;
    }

    private static bool TryParseTargetAndPayload(
        string tail,
        string prefix,
        out MemoryCommand command,
        string action)
    {
        if (!ToolInputHelper.StartsWithCommand(tail, prefix))
        {
            command = new MemoryCommand(string.Empty);
            return false;
        }

        var remainder = ToolInputHelper.GetCommandTail(tail, prefix);
        var parts = remainder.Split(["::"], 2, StringSplitOptions.None | StringSplitOptions.TrimEntries);
        command = parts.Length < 2
            ? new MemoryCommand(action, remainder.Trim())
            : new MemoryCommand(action, parts[0].Trim(), parts[1].Trim());
        return true;
    }
}

internal sealed record MemoryExactDuplicateGroup(string Key, string[] Ids);

internal sealed record MemoryRescoreCandidate(string Id, double SuggestedImportanceScore);

internal sealed record MemoryRetentionCandidate(string Id, string SuggestedRetentionPolicy);

internal sealed record MemoryMaintenanceReport(
    int TotalMemories,
    int ExactDuplicateGroupCount,
    int DuplicateMemoryCount,
    int LowSignalCandidateCount,
    int HighImportanceSingleObservationCount,
    int StaleEpisodicCount,
    int RetentionMismatchCount,
    int OlderThan90DaysCount,
    int OlderThan180DaysCount,
    int SummaryCount,
    int OldestActiveMemoryAgeDays,
    MemoryExactDuplicateGroup[] ExactDuplicateGroups,
    MemoryRescoreCandidate[] RescoreCandidates,
    MemoryRetentionCandidate[] RetentionCandidates,
    string[] LowSignalIds,
    string[] HighImportanceSingleObservationIds)
{
    public string ToDisplayText()
    {
        var lines = new List<string>
        {
            "Memory diagnostics:",
            $"- Active memories: {TotalMemories}",
            $"- Exact duplicate groups: {ExactDuplicateGroupCount} ({DuplicateMemoryCount} duplicate note(s))",
            $"- Low-signal candidates: {LowSignalCandidateCount}",
            $"- High-importance single-observation learned notes: {HighImportanceSingleObservationCount}",
            $"- Stale episodic notes: {StaleEpisodicCount}",
            $"- Retention mismatches: {RetentionMismatchCount}",
            $"- Memories older than 90 days: {OlderThan90DaysCount}",
            $"- Memories older than 180 days: {OlderThan180DaysCount}",
            $"- Conversation summaries: {SummaryCount}",
            $"- Oldest active memory age: {OldestActiveMemoryAgeDays} day(s)"
        };

        if (LowSignalIds.Length > 0)
        {
            lines.Add("- Low-signal candidates:");
            lines.AddRange(LowSignalIds.Select(id => $"  {id}"));
        }

        if (HighImportanceSingleObservationIds.Length > 0)
        {
            lines.Add("- Possible false-positive learned memories:");
            lines.AddRange(HighImportanceSingleObservationIds.Select(id => $"  {id}"));
        }

        if (ExactDuplicateGroups.Length > 0)
        {
            lines.Add("- Duplicate groups:");
            lines.AddRange(ExactDuplicateGroups.Take(6).Select(group => $"  {group.Key} => {string.Join(", ", group.Ids.Select(id => id[..Math.Min(8, id.Length)]))}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public string ToMaintenancePreviewText()
    {
        var lines = new List<string>
        {
            "Memory maintenance preview:",
            $"- Memories to rescore: {RescoreCandidates.Length}",
            $"- Memories with retention mismatches: {RetentionCandidates.Length}",
            $"- Exact duplicate memories that can be forgotten: {DuplicateMemoryCount}"
        };

        if (RescoreCandidates.Length == 0 && RetentionCandidates.Length == 0 && DuplicateMemoryCount == 0)
        {
            lines.Add("- No maintenance actions are currently recommended.");
        }
        else
        {
            lines.Add("Run `memory maintain apply` to apply these changes.");
        }

        return string.Join(Environment.NewLine, lines);
    }
}

internal static class MemoryMaintenance
{
    public static MemoryMaintenanceReport Analyze(IReadOnlyList<MemoryNote> notes)
    {
        var active = notes
            .Where(note => !note.IsForgotten)
            .OrderByDescending(note => note.LastObservedAtUtc ?? note.CreatedAtUtc)
            .ToArray();
        var now = DateTimeOffset.UtcNow;
        var duplicateGroups = active
            .GroupBy(BuildDuplicateKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new MemoryExactDuplicateGroup(
                group.Key,
                group.OrderByDescending(note => note.UserApprovedRetention)
                    .ThenByDescending(note => note.ObservationCount)
                    .ThenByDescending(note => note.ImportanceScore)
                    .ThenByDescending(note => note.LastObservedAtUtc ?? note.CreatedAtUtc)
                    .Select(note => note.Id)
                    .ToArray()))
            .OrderByDescending(group => group.Ids.Length)
            .ToArray();
        var lowSignal = active
            .Where(IsLowSignalCandidate)
            .ToArray();
        var highImportanceSingles = active
            .Where(note =>
                string.Equals(note.Source, "learned", StringComparison.OrdinalIgnoreCase)
                && note.ObservationCount == 1
                && note.ImportanceScore >= 0.75d)
            .ToArray();
        var staleEpisodic = active
            .Where(note =>
                string.Equals(note.Kind, "episodic", StringComparison.OrdinalIgnoreCase)
                && (now - (note.LastObservedAtUtc ?? note.CreatedAtUtc)).TotalDays >= 120)
            .ToArray();
        var rescoreCandidates = active
            .Select(note => new { Note = note, Suggested = MemoryPolicies.ResolveImportanceScore(note) })
            .Where(item => Math.Abs(item.Suggested - item.Note.ImportanceScore) >= 0.08d)
            .OrderByDescending(item => Math.Abs(item.Suggested - item.Note.ImportanceScore))
            .Select(item => new MemoryRescoreCandidate(item.Note.Id, item.Suggested))
            .ToArray();
        var retentionCandidates = active
            .Select(note => new { Note = note, Suggested = SuggestRetentionPolicy(note) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Suggested)
                && !string.Equals(MemoryPolicies.NormalizeRetentionPolicy(item.Note.RetentionPolicy), item.Suggested, StringComparison.OrdinalIgnoreCase)
                && !item.Note.UserApprovedRetention)
            .Select(item => new MemoryRetentionCandidate(item.Note.Id, item.Suggested))
            .ToArray();
        var oldestAge = active.Length == 0
            ? 0
            : (int)Math.Floor((now - active.Min(note => note.CreatedAtUtc)).TotalDays);

        return new MemoryMaintenanceReport(
            active.Length,
            duplicateGroups.Length,
            duplicateGroups.Sum(group => Math.Max(0, group.Ids.Length - 1)),
            lowSignal.Length,
            highImportanceSingles.Length,
            staleEpisodic.Length,
            retentionCandidates.Length,
            active.Count(note => (now - note.CreatedAtUtc).TotalDays >= 90),
            active.Count(note => (now - note.CreatedAtUtc).TotalDays >= 180),
            active.Count(note => string.Equals(note.Kind, "summary", StringComparison.OrdinalIgnoreCase)),
            oldestAge,
            duplicateGroups,
            rescoreCandidates,
            retentionCandidates,
            lowSignal.Take(8).Select(note => note.Id[..Math.Min(8, note.Id.Length)] + " | " + TrimForPreview(note.Content, 72)).ToArray(),
            highImportanceSingles.Take(8).Select(note => note.Id[..Math.Min(8, note.Id.Length)] + " | " + TrimForPreview(note.Content, 72)).ToArray());
    }

    private static string BuildDuplicateKey(MemoryNote note)
    {
        return string.Join(
            "|",
            note.Kind.Trim().ToLowerInvariant(),
            note.Category.Trim().ToLowerInvariant(),
            NormalizeForDuplicate(note.Content),
            note.ReminderAtUtc?.ToUniversalTime().ToString("O") ?? string.Empty);
    }

    private static bool IsLowSignalCandidate(MemoryNote note)
    {
        if (note.ObservationCount > 1
            || note.UserApprovedRetention
            || string.Equals(note.Kind, "summary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(note.Kind, "reminder", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var content = note.Content.Trim();
        return content.Length <= 24
            || (string.Equals(note.Source, "learned", StringComparison.OrdinalIgnoreCase)
                && content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4);
    }

    private static string SuggestRetentionPolicy(MemoryNote note)
    {
        return MemoryPolicies.ResolveRetentionPolicy(new MemoryWriteRequest(
            note.Content,
            Kind: note.Kind,
            Category: note.Category,
            Source: note.Source,
            Privacy: note.Privacy,
            Tags: note.Tags,
            Context: note.Context,
            CreatedAtUtc: note.CreatedAtUtc,
            SummaryWindowStartUtc: note.SummaryWindowStartUtc,
            SummaryWindowEndUtc: note.SummaryWindowEndUtc,
            ImportanceScore: null,
            RetentionPolicy: string.Empty,
            ExpiresAtUtc: null,
            UserApprovedRetention: note.UserApprovedRetention,
            Entities: note.Entities,
            ReminderAtUtc: note.ReminderAtUtc,
            ReminderText: note.ReminderText,
            ReminderStatus: note.ReminderStatus));
    }

    private static string NormalizeForDuplicate(string value)
    {
        var characters = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray();
        return string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string TrimForPreview(string value, int maxLength)
    {
        var flattened = value.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..maxLength] + "...";
    }
}
