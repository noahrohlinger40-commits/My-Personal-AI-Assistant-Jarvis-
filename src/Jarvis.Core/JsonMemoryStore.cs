using System.Text.Json;

namespace Jarvis.Core;

public interface IMemoryStore
{
    Task AddAsync(string content, CancellationToken cancellationToken);

    Task AddAsync(MemoryWriteRequest request, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryNote>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryNote>> GetRecentAsync(int maxResults, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryNote>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryNote>> SearchAsync(string query, MemoryQueryContext queryContext, int maxResults, CancellationToken cancellationToken);

    Task<MemoryProfileSnapshot> GetProfileAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryNote>> GetPendingRemindersAsync(int maxResults, CancellationToken cancellationToken);

    Task<IReadOnlyList<MemoryNote>> GetDueRemindersAsync(DateTimeOffset nowUtc, int maxResults, CancellationToken cancellationToken);

    Task<MemoryNote?> GetByIdAsync(string id, CancellationToken cancellationToken);

    Task<MemoryNote?> UpdateAsync(MemoryUpdateRequest request, CancellationToken cancellationToken);

    Task<bool> ForgetAsync(string id, string reason, CancellationToken cancellationToken);

    Task<MemoryImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken);

    Task<string> ExportAsync(string destinationPath, CancellationToken cancellationToken);
}

public sealed class JsonMemoryStore : IMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonMemoryStore(string filePath)
    {
        _filePath = filePath;
    }

    public Task AddAsync(string content, CancellationToken cancellationToken)
    {
        return AddAsync(new MemoryWriteRequest(content), cancellationToken);
    }

    public async Task AddAsync(MemoryWriteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            UpsertNote(notes, request);
            await SaveAsync(notes, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            return GetActiveNotes(notes, DateTimeOffset.UtcNow).Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryNote>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            return GetActiveNotes(notes, DateTimeOffset.UtcNow)
                .OrderBy(note => note.CreatedAtUtc)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryNote>> GetRecentAsync(int maxResults, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            return GetActiveNotes(notes, DateTimeOffset.UtcNow)
                .OrderByDescending(note => note.LastObservedAtUtc ?? note.CreatedAtUtc)
                .ThenByDescending(note => note.ImportanceScore)
                .ThenByDescending(note => note.CreatedAtUtc)
                .Take(Math.Max(0, maxResults))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<MemoryNote>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        return SearchAsync(query, new MemoryQueryContext(query), maxResults, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryNote>> SearchAsync(
        string query,
        MemoryQueryContext queryContext,
        int maxResults,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            return MemorySemanticSearch.Search(
                GetActiveNotes(notes, DateTimeOffset.UtcNow),
                query,
                maxResults,
                queryContext);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MemoryProfileSnapshot> GetProfileAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var notes = await LoadAsync(cancellationToken);
            var activeNotes = GetActiveNotes(notes, nowUtc);
            var preferenceGroups = activeNotes
                .Where(note => string.Equals(note.Kind, "preference", StringComparison.OrdinalIgnoreCase))
                .GroupBy(
                    note => $"{NormalizeLabel(note.Category, "other")}|{note.Content.Trim()}",
                    note => note,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var sample = group
                        .OrderByDescending(note => note.LastObservedAtUtc ?? note.CreatedAtUtc)
                        .ThenByDescending(note => note.ImportanceScore)
                        .First();
                    var observationCount = group.Sum(note => Math.Max(1, note.ObservationCount));
                    var lastObservedAtUtc = group.Max(note => note.LastObservedAtUtc ?? note.CreatedAtUtc);
                    return new LearnedPreference(
                        NormalizeLabel(sample.Category, "other"),
                        sample.Content.Trim(),
                        observationCount,
                        lastObservedAtUtc);
                })
                .OrderByDescending(preference => preference.ObservationCount)
                .ThenByDescending(preference => preference.LastObservedAtUtc)
                .ThenBy(preference => preference.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var structuredMemories = activeNotes
                .SelectMany(BuildStructuredEntries)
                .GroupBy(
                    item => BuildStructuredIdentityKey(item.Kind, item.Name, item.Aliases, item.Attributes),
                    item => item,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var sample = group
                        .OrderByDescending(item => item.Note.LastObservedAtUtc ?? item.Note.CreatedAtUtc)
                        .ThenByDescending(item => item.Note.ImportanceScore)
                        .First();
                    var observationCount = group.Sum(item => Math.Max(1, item.Note.ObservationCount));
                    var lastObservedAtUtc = group.Max(item => item.Note.LastObservedAtUtc ?? item.Note.CreatedAtUtc);
                    return new StructuredMemorySummary(
                        NormalizeLabel(sample.Kind, "other"),
                        sample.Name.Trim(),
                        sample.Summary.Trim(),
                        observationCount,
                        lastObservedAtUtc);
                })
                .OrderByDescending(item => item.ObservationCount)
                .ThenByDescending(item => item.LastObservedAtUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var pendingReminders = activeNotes
                .Where(MemoryPolicies.IsPendingReminder)
                .OrderBy(note => note.ReminderAtUtc)
                .ThenByDescending(note => note.ImportanceScore)
                .Select(ToReminderSnapshot)
                .Take(6)
                .ToArray();

            var summaryCount = activeNotes.Count(note => string.Equals(note.Kind, "summary", StringComparison.OrdinalIgnoreCase));
            return new MemoryProfileSnapshot(
                activeNotes.Count,
                preferenceGroups.Length,
                summaryCount,
                structuredMemories.Length,
                activeNotes.Count(MemoryPolicies.IsPendingReminder),
                preferenceGroups,
                structuredMemories,
                pendingReminders);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryNote>> GetPendingRemindersAsync(int maxResults, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            return GetActiveNotes(notes, DateTimeOffset.UtcNow)
                .Where(MemoryPolicies.IsPendingReminder)
                .OrderBy(note => note.ReminderAtUtc)
                .ThenByDescending(note => note.ImportanceScore)
                .Take(Math.Max(0, maxResults))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryNote>> GetDueRemindersAsync(DateTimeOffset nowUtc, int maxResults, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            return GetActiveNotes(notes, nowUtc)
                .Where(note => MemoryPolicies.IsDueReminder(note, nowUtc, TimeSpan.FromHours(18)))
                .OrderBy(note => note.ReminderAtUtc)
                .ThenByDescending(note => note.ImportanceScore)
                .Take(Math.Max(0, maxResults))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MemoryNote?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            return notes.FirstOrDefault(note => string.Equals(note.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MemoryNote?> UpdateAsync(MemoryUpdateRequest request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            var index = notes.FindIndex(note => string.Equals(note.Id, request.Id.Trim(), StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                return null;
            }

            var existing = notes[index];
            var createdAtUtc = existing.CreatedAtUtc == default ? DateTimeOffset.UtcNow : existing.CreatedAtUtc;
            var reminderStatus = MemoryPolicies.NormalizeReminderStatus(request.ReminderStatus);
            var retentionPolicy = MemoryPolicies.NormalizeRetentionPolicy(request.RetentionPolicy);
            var expiresAtUtc = request.ExpiresAtUtc?.ToUniversalTime()
                ?? MemoryPolicies.ResolveExpiryUtc(
                    retentionPolicy,
                    createdAtUtc,
                    request.ReminderAtUtc?.ToUniversalTime(),
                    reminderStatus);
            var normalizedKind = NormalizeLabel(
                request.ReminderAtUtc is not null && string.Equals(request.Kind, "note", StringComparison.OrdinalIgnoreCase)
                    ? "reminder"
                    : request.Kind,
                existing.Kind);

            var updated = NormalizeNote(existing with
            {
                Content = request.Content.Trim(),
                Kind = normalizedKind,
                Category = NormalizeLabel(request.Category, string.Empty),
                Privacy = NormalizeLabel(request.Privacy, "persistent"),
                Tags = NormalizeTags(request.Tags),
                ImportanceScore = MemoryPolicies.ClampImportance(request.ImportanceScore),
                RetentionPolicy = retentionPolicy,
                ExpiresAtUtc = expiresAtUtc,
                UserApprovedRetention = request.UserApprovedRetention,
                Entities = request.Entities is null ? existing.Entities : NormalizeEntities(request.Entities),
                ReminderAtUtc = request.ReminderAtUtc?.ToUniversalTime(),
                ReminderText = string.IsNullOrWhiteSpace(request.ReminderText) ? string.Empty : request.ReminderText.Trim(),
                ReminderStatus = reminderStatus
            });

            notes[index] = updated;
            await SaveAsync(notes, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ForgetAsync(string id, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            var index = notes.FindIndex(note => string.Equals(note.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                return false;
            }

            notes[index] = notes[index] with
            {
                IsForgotten = true,
                ForgottenAtUtc = DateTimeOffset.UtcNow,
                ForgottenReason = string.IsNullOrWhiteSpace(reason) ? "forgotten" : reason.Trim()
            };

            await SaveAsync(notes, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MemoryImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var existingNotes = await LoadAsync(cancellationToken);
            var importedNotes = await ReadImportNotesAsync(sourcePath, cancellationToken);
            var importedCount = 0;
            var skippedCount = 0;

            foreach (var imported in importedNotes)
            {
                var normalized = NormalizeNote(imported);

                if (string.IsNullOrWhiteSpace(normalized.Content))
                {
                    skippedCount++;
                    continue;
                }

                if (ContainsEquivalent(existingNotes, normalized))
                {
                    skippedCount++;
                    continue;
                }

                var candidate = normalized;

                if (existingNotes.Any(note => string.Equals(note.Id, candidate.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    candidate = candidate with { Id = Guid.NewGuid().ToString("n") };
                }

                existingNotes.Add(candidate);
                importedCount++;
            }

            if (importedCount > 0)
            {
                await SaveAsync(existingNotes, cancellationToken);
            }

            return new MemoryImportResult(
                importedCount,
                skippedCount,
                $"Imported {importedCount} memory item(s); skipped {skippedCount} duplicate or invalid item(s).");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> ExportAsync(string destinationPath, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var notes = await LoadAsync(cancellationToken);
            var activeNotes = GetActiveNotes(notes, DateTimeOffset.UtcNow)
                .OrderBy(note => note.CreatedAtUtc)
                .ToArray();
            var exportPath = NormalizeExportPath(destinationPath, ".json");
            EnsureDirectoryExists(Path.GetDirectoryName(exportPath));

            var document = new MemoryExportDocument(
                2,
                DateTimeOffset.UtcNow,
                activeNotes);

            await using var stream = File.Create(exportPath);
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
            return exportPath;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<MemoryNote>> LoadAsync(CancellationToken cancellationToken)
    {
        EnsureParentDirectoryExists();

        if (!File.Exists(_filePath))
        {
            return new List<MemoryNote>();
        }

        return await ReadNotesFromJsonAsync(_filePath, cancellationToken);
    }

    private async Task SaveAsync(List<MemoryNote> notes, CancellationToken cancellationToken)
    {
        EnsureParentDirectoryExists();

        var normalized = notes
            .Select(NormalizeNote)
            .OrderBy(note => note.CreatedAtUtc)
            .ToArray();

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken);
    }

    private void UpsertNote(List<MemoryNote> notes, MemoryWriteRequest request)
    {
        var prepared = PrepareWriteRequest(request);
        var duplicateIndex = FindDuplicateIndex(
            notes,
            prepared.Content.Trim(),
            prepared.Kind,
            prepared.Category,
            prepared.SummaryWindowStartUtc,
            prepared.SummaryWindowEndUtc,
            prepared.ReminderAtUtc);

        if (duplicateIndex >= 0)
        {
            var existing = notes[duplicateIndex];
            var retentionPolicy = ResolveDuplicateRetentionPolicy(existing, prepared);
            var expiresAtUtc = prepared.ExpiresAtUtc
                ?? (existing.UserApprovedRetention && !prepared.UserApprovedRetention
                    ? existing.ExpiresAtUtc
                    : MemoryPolicies.ResolveExpiryUtc(
                        retentionPolicy,
                        existing.CreatedAtUtc,
                        prepared.ReminderAtUtc ?? existing.ReminderAtUtc,
                        string.IsNullOrWhiteSpace(prepared.ReminderStatus) ? existing.ReminderStatus : prepared.ReminderStatus));

            notes[duplicateIndex] = NormalizeNote(existing with
            {
                Source = prepared.Source,
                Privacy = prepared.Privacy,
                Tags = MergeTags(existing.Tags, prepared.Tags),
                Context = prepared.Context ?? existing.Context,
                ObservationCount = existing.ObservationCount + 1,
                LastObservedAtUtc = prepared.CreatedAtUtc,
                SummaryWindowStartUtc = prepared.SummaryWindowStartUtc ?? existing.SummaryWindowStartUtc,
                SummaryWindowEndUtc = prepared.SummaryWindowEndUtc ?? existing.SummaryWindowEndUtc,
                ImportanceScore = MemoryPolicies.BlendImportance(existing, prepared),
                RetentionPolicy = retentionPolicy,
                ExpiresAtUtc = expiresAtUtc,
                UserApprovedRetention = existing.UserApprovedRetention || prepared.UserApprovedRetention,
                Entities = MergeEntities(existing.Entities, prepared.Entities),
                ReminderAtUtc = prepared.ReminderAtUtc ?? existing.ReminderAtUtc,
                ReminderText = string.IsNullOrWhiteSpace(prepared.ReminderText) ? existing.ReminderText : prepared.ReminderText.Trim(),
                ReminderStatus = string.IsNullOrWhiteSpace(prepared.ReminderStatus)
                    ? existing.ReminderStatus
                    : MemoryPolicies.NormalizeReminderStatus(prepared.ReminderStatus),
                IsForgotten = false,
                ForgottenAtUtc = null,
                ForgottenReason = string.Empty
            });

            return;
        }

        var createdAtUtc = prepared.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var retention = MemoryPolicies.ResolveRetentionPolicy(prepared);
        var reminderStatus = MemoryPolicies.NormalizeReminderStatus(prepared.ReminderStatus);

        notes.Add(new MemoryNote(
            Guid.NewGuid().ToString("n"),
            prepared.Content.Trim(),
            createdAtUtc,
            prepared.Kind,
            prepared.Category,
            prepared.Source,
            prepared.Privacy,
            prepared.Tags,
            prepared.Context ?? InteractionContextSnapshot.Empty,
            1,
            createdAtUtc,
            prepared.SummaryWindowStartUtc,
            prepared.SummaryWindowEndUtc,
            prepared.ImportanceScore ?? MemoryPolicies.ResolveImportanceScore(prepared),
            retention,
            prepared.ExpiresAtUtc ?? MemoryPolicies.ResolveExpiryUtc(prepared, createdAtUtc),
            prepared.UserApprovedRetention,
            NormalizeEntities(prepared.Entities),
            prepared.ReminderAtUtc?.ToUniversalTime(),
            string.IsNullOrWhiteSpace(prepared.ReminderText) ? string.Empty : prepared.ReminderText.Trim(),
            reminderStatus));
    }

    private static MemoryWriteRequest PrepareWriteRequest(MemoryWriteRequest request)
    {
        var content = request.Content?.Trim() ?? string.Empty;
        var kind = NormalizeLabel(request.ReminderAtUtc is not null && string.Equals(request.Kind, "note", StringComparison.OrdinalIgnoreCase)
            ? "reminder"
            : request.Kind, "note");

        return request with
        {
            Content = content,
            Kind = kind,
            Category = NormalizeLabel(request.Category, kind == "preference" ? "other" : string.Empty),
            Source = NormalizeLabel(request.Source, "manual"),
            Privacy = NormalizeLabel(request.Privacy, "persistent"),
            Tags = NormalizeTags(request.Tags),
            Context = request.Context ?? InteractionContextSnapshot.Empty,
            ImportanceScore = request.ImportanceScore is double score ? MemoryPolicies.ClampImportance(score) : MemoryPolicies.ResolveImportanceScore(request),
            RetentionPolicy = MemoryPolicies.ResolveRetentionPolicy(request),
            ExpiresAtUtc = request.ExpiresAtUtc?.ToUniversalTime(),
            Entities = NormalizeEntities(request.Entities),
            ReminderAtUtc = request.ReminderAtUtc?.ToUniversalTime(),
            ReminderText = string.IsNullOrWhiteSpace(request.ReminderText) ? string.Empty : request.ReminderText.Trim(),
            ReminderStatus = MemoryPolicies.NormalizeReminderStatus(request.ReminderStatus)
        };
    }

    private static IReadOnlyList<MemoryNote> GetActiveNotes(IReadOnlyList<MemoryNote> notes, DateTimeOffset nowUtc)
    {
        return notes
            .Where(note => MemoryPolicies.IsActive(note, nowUtc))
            .ToArray();
    }

    private static IEnumerable<(MemoryNote Note, string Kind, string Name, string Summary, string[] Aliases, MemoryAttribute[] Attributes)> BuildStructuredEntries(MemoryNote note)
    {
        if (note.Entities is { Length: > 0 })
        {
            foreach (var entity in note.Entities)
            {
                if (string.IsNullOrWhiteSpace(entity.Name))
                {
                    continue;
                }

                yield return (
                    note,
                    NormalizeLabel(entity.Kind, "other"),
                    entity.Name.Trim(),
                    string.IsNullOrWhiteSpace(entity.Summary) ? note.Content.Trim() : entity.Summary.Trim(),
                    entity.SafeAliases,
                    entity.SafeAttributes);
            }
        }

        if (note.Entities is not { Length: > 0 }
            && note.Category is "routines" or "habits" or "favorites")
        {
            yield return (
                note,
                note.Category,
                note.Content.Trim(),
                note.Content.Trim(),
                Array.Empty<string>(),
                Array.Empty<MemoryAttribute>());
        }
    }

    private static ReminderSnapshot ToReminderSnapshot(MemoryNote note)
    {
        return new ReminderSnapshot(
            note.Id,
            string.IsNullOrWhiteSpace(note.ReminderText) ? note.Content.Trim() : note.ReminderText.Trim(),
            note.ReminderAtUtc ?? note.CreatedAtUtc,
            MemoryPolicies.NormalizeReminderStatus(note.ReminderStatus),
            note.Id);
    }

    private static string ResolveDuplicateRetentionPolicy(MemoryNote existing, MemoryWriteRequest incoming)
    {
        if (existing.UserApprovedRetention && !incoming.UserApprovedRetention && string.IsNullOrWhiteSpace(incoming.RetentionPolicy))
        {
            return NormalizeLabel(existing.RetentionPolicy, "standard");
        }

        return MemoryPolicies.ResolveRetentionPolicy(incoming);
    }

    private static int FindDuplicateIndex(
        IReadOnlyList<MemoryNote> notes,
        string content,
        string kind,
        string category,
        DateTimeOffset? summaryWindowStartUtc,
        DateTimeOffset? summaryWindowEndUtc,
        DateTimeOffset? reminderAtUtc)
    {
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];

            if (note.IsForgotten
                || !string.Equals(note.Kind, kind, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(note.Category, category, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(note.Content.Trim(), content, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (kind == "summary")
            {
                if (note.SummaryWindowStartUtc != summaryWindowStartUtc
                    || note.SummaryWindowEndUtc != summaryWindowEndUtc)
                {
                    continue;
                }
            }

            if ((kind == "reminder" || reminderAtUtc is not null)
                && note.ReminderAtUtc != reminderAtUtc)
            {
                continue;
            }

            return index;
        }

        return -1;
    }

    private static bool ContainsEquivalent(IReadOnlyList<MemoryNote> existingNotes, MemoryNote candidate)
    {
        return existingNotes.Any(existing =>
            string.Equals(existing.Content.Trim(), candidate.Content.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.Kind, candidate.Kind, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.Category, candidate.Category, StringComparison.OrdinalIgnoreCase)
            && existing.SummaryWindowStartUtc == candidate.SummaryWindowStartUtc
            && existing.SummaryWindowEndUtc == candidate.SummaryWindowEndUtc
            && existing.ReminderAtUtc == candidate.ReminderAtUtc
            && existing.CreatedAtUtc == candidate.CreatedAtUtc);
    }

    private static string NormalizeExportPath(string destinationPath, string defaultExtension)
    {
        var trimmed = destinationPath.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("An export path is required.");
        }

        var extension = Path.GetExtension(trimmed);
        return string.IsNullOrWhiteSpace(extension)
            ? trimmed + defaultExtension
            : trimmed;
    }

    private static async Task<List<MemoryNote>> ReadNotesFromJsonAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<MemoryNote>();
        }

        using var document = JsonDocument.Parse(text);

        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => DeserializeArray(document.RootElement),
            JsonValueKind.Object when TryGetProperty(document.RootElement, "items", out var items) && items.ValueKind == JsonValueKind.Array => DeserializeArray(items),
            JsonValueKind.Object => DeserializeSingle(document.RootElement),
            _ => new List<MemoryNote>()
        };
    }

    private static async Task<List<MemoryNote>> ReadImportNotesAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Memory import file not found.", path);
        }

        var extension = Path.GetExtension(path);

        if (string.Equals(extension, ".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            var notes = new List<MemoryNote>();
            var lines = await File.ReadAllLinesAsync(path, cancellationToken);

            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var note = JsonSerializer.Deserialize<MemoryNote>(line, JsonOptions);

                if (note is not null)
                {
                    notes.Add(NormalizeNote(note));
                }
            }

            return notes;
        }

        return await ReadNotesFromJsonAsync(path, cancellationToken);
    }

    private static List<MemoryNote> DeserializeArray(JsonElement element)
    {
        var notes = new List<MemoryNote>();

        foreach (var item in element.EnumerateArray())
        {
            var note = item.Deserialize<MemoryNote>(JsonOptions);

            if (note is not null)
            {
                notes.Add(NormalizeNote(note));
            }
        }

        return notes;
    }

    private static List<MemoryNote> DeserializeSingle(JsonElement element)
    {
        var note = element.Deserialize<MemoryNote>(JsonOptions);
        return note is null
            ? new List<MemoryNote>()
            : new List<MemoryNote> { NormalizeNote(note) };
    }

    private static MemoryNote NormalizeNote(MemoryNote note)
    {
        var content = note.Content?.Trim() ?? string.Empty;
        var createdAtUtc = note.CreatedAtUtc == default ? DateTimeOffset.UtcNow : note.CreatedAtUtc.ToUniversalTime();
        var lastObservedAtUtc = note.LastObservedAtUtc?.ToUniversalTime() ?? createdAtUtc;
        var retentionPolicy = string.IsNullOrWhiteSpace(note.RetentionPolicy)
            ? MemoryPolicies.ResolveRetentionPolicy(new MemoryWriteRequest(
                content,
                Kind: note.Kind,
                Category: note.Category,
                Source: note.Source,
                Privacy: note.Privacy,
                Tags: note.Tags,
                Context: note.Context,
                CreatedAtUtc: createdAtUtc,
                SummaryWindowStartUtc: note.SummaryWindowStartUtc,
                SummaryWindowEndUtc: note.SummaryWindowEndUtc,
                ImportanceScore: note.ImportanceScore,
                Entities: note.Entities,
                ReminderAtUtc: note.ReminderAtUtc,
                ReminderText: note.ReminderText,
                ReminderStatus: note.ReminderStatus))
            : MemoryPolicies.NormalizeRetentionPolicy(note.RetentionPolicy);
        var reminderAtUtc = note.ReminderAtUtc?.ToUniversalTime();
        var reminderStatus = MemoryPolicies.NormalizeReminderStatus(note.ReminderStatus);
        var expiresAtUtc = note.ExpiresAtUtc?.ToUniversalTime()
            ?? MemoryPolicies.ResolveExpiryUtc(retentionPolicy, createdAtUtc, reminderAtUtc, reminderStatus);

        return note with
        {
            Id = string.IsNullOrWhiteSpace(note.Id) ? Guid.NewGuid().ToString("n") : note.Id.Trim(),
            Content = content,
            CreatedAtUtc = createdAtUtc,
            Kind = NormalizeLabel(note.Kind, "note"),
            Category = NormalizeLabel(note.Category, string.Empty),
            Source = NormalizeLabel(note.Source, "manual"),
            Privacy = NormalizeLabel(note.Privacy, "persistent"),
            Tags = NormalizeTags(note.Tags),
            Context = note.Context ?? InteractionContextSnapshot.Empty,
            ObservationCount = Math.Max(1, note.ObservationCount),
            LastObservedAtUtc = lastObservedAtUtc,
            SummaryWindowStartUtc = note.SummaryWindowStartUtc?.ToUniversalTime(),
            SummaryWindowEndUtc = note.SummaryWindowEndUtc?.ToUniversalTime(),
            ImportanceScore = note.ImportanceScore <= 0 ? 0.35d : MemoryPolicies.ClampImportance(note.ImportanceScore),
            RetentionPolicy = retentionPolicy,
            ExpiresAtUtc = expiresAtUtc,
            Entities = NormalizeEntities(note.Entities),
            ReminderAtUtc = reminderAtUtc,
            ReminderText = string.IsNullOrWhiteSpace(note.ReminderText) ? string.Empty : note.ReminderText.Trim(),
            ReminderStatus = reminderStatus,
            ForgottenAtUtc = note.ForgottenAtUtc?.ToUniversalTime(),
            ForgottenReason = string.IsNullOrWhiteSpace(note.ForgottenReason) ? string.Empty : note.ForgottenReason.Trim()
        };
    }

    private static StructuredMemoryReference[] NormalizeEntities(StructuredMemoryReference[]? entities)
    {
        var normalized = (entities ?? Array.Empty<StructuredMemoryReference>())
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => new StructuredMemoryReference(
                NormalizeLabel(entity.Kind, "other"),
                entity.Name.Trim(),
                string.IsNullOrWhiteSpace(entity.Summary) ? string.Empty : entity.Summary.Trim(),
                NormalizeAliases(entity.Aliases),
                NormalizeAttributes(entity.Attributes)))
            .ToList();
        var merged = new List<StructuredMemoryReference>();

        foreach (var candidate in normalized)
        {
            var existingIndex = merged.FindIndex(existing => ShouldMergeEntity(existing, candidate));

            if (existingIndex < 0)
            {
                merged.Add(candidate);
                continue;
            }

            var existing = merged[existingIndex];
            var summary = SelectEntitySummary(existing, candidate);
            var aliases = NormalizeAliases(
                existing.SafeAliases
                    .Append(existing.Name)
                    .Concat(candidate.SafeAliases)
                    .Append(candidate.Name)
                    .ToArray());
            var attributes = NormalizeAttributes(existing.SafeAttributes.Concat(candidate.SafeAttributes));
            var canonicalName = SelectCanonicalEntityName(existing.Name, candidate.Name, aliases, attributes);
            var canonicalAliases = NormalizeAliases(aliases.Where(alias => !string.Equals(alias, canonicalName, StringComparison.OrdinalIgnoreCase)).ToArray());

            merged[existingIndex] = existing with
            {
                Name = canonicalName,
                Summary = summary,
                Aliases = canonicalAliases,
                Attributes = attributes
            };
        }

        return merged
            .OrderBy(entity => entity.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] NormalizeAliases(string[]? aliases)
    {
        return (aliases ?? Array.Empty<string>())
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MemoryAttribute[] NormalizeAttributes(IEnumerable<MemoryAttribute>? attributes)
    {
        return (attributes ?? Array.Empty<MemoryAttribute>())
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key) && !string.IsNullOrWhiteSpace(attribute.Value))
            .Select(attribute => new MemoryAttribute(attribute.Key.Trim(), attribute.Value.Trim()))
            .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static StructuredMemoryReference[] MergeEntities(StructuredMemoryReference[]? existing, StructuredMemoryReference[]? incoming)
    {
        return NormalizeEntities((existing ?? Array.Empty<StructuredMemoryReference>())
            .Concat(incoming ?? Array.Empty<StructuredMemoryReference>())
            .ToArray());
    }

    private static string[] NormalizeTags(IEnumerable<string>? tags)
    {
        return (tags ?? Array.Empty<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] MergeTags(string[]? existing, string[]? incoming)
    {
        return NormalizeTags((existing ?? Array.Empty<string>()).Concat(incoming ?? Array.Empty<string>()));
    }

    private static string NormalizeLabel(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
    }

    private static bool ShouldMergeEntity(StructuredMemoryReference existing, StructuredMemoryReference candidate)
    {
        if (!string.Equals(existing.Kind, candidate.Kind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HasConflictingEntityDisambiguators(existing.SafeAttributes, candidate.SafeAttributes))
        {
            return false;
        }

        var existingNames = BuildEntityNameSet(existing);
        var candidateNames = BuildEntityNameSet(candidate);
        return existingNames.Overlaps(candidateNames);
    }

    private static HashSet<string> BuildEntityNameSet(StructuredMemoryReference entity)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddNormalizedEntityName(values, entity.Name);

        foreach (var alias in entity.SafeAliases)
        {
            AddNormalizedEntityName(values, alias);
        }

        return values;
    }

    private static void AddNormalizedEntityName(ISet<string> values, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var normalized = NormalizeEntityName(name);

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            values.Add(normalized);
        }
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

    private static bool HasConflictingEntityDisambiguators(
        IReadOnlyList<MemoryAttribute> existing,
        IReadOnlyList<MemoryAttribute> candidate)
    {
        foreach (var key in new[] { "label", "devicetype", "role", "relationship", "anchor", "cadence" })
        {
            var left = existing.FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
            var right = candidate.FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

            if (!string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && !string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string SelectEntitySummary(StructuredMemoryReference existing, StructuredMemoryReference candidate)
    {
        if (string.IsNullOrWhiteSpace(existing.Summary))
        {
            return candidate.Summary;
        }

        if (string.IsNullOrWhiteSpace(candidate.Summary))
        {
            return existing.Summary;
        }

        return candidate.Summary.Length > existing.Summary.Length
            ? candidate.Summary
            : existing.Summary;
    }

    private static string SelectCanonicalEntityName(
        string existingName,
        string candidateName,
        IReadOnlyList<string> aliases,
        IReadOnlyList<MemoryAttribute> attributes)
    {
        return new[] { existingName, candidateName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .OrderBy(value => value.Length)
            .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? aliases.OrderBy(value => value.Length).ThenBy(value => value, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            ?? existingName;
    }

    private static string BuildStructuredIdentityKey(
        string kind,
        string name,
        IReadOnlyList<string> aliases,
        IReadOnlyList<MemoryAttribute> attributes)
    {
        var normalizedNames = aliases
            .Append(name)
            .Select(NormalizeEntityName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var discriminator = string.Join(
            "|",
            attributes
                .Where(attribute => attribute.Key is not null)
                .Select(attribute => (Key: NormalizeLabel(attribute.Key, string.Empty), Value: NormalizeEntityName(attribute.Value)))
                .Where(attribute => attribute.Key is "label" or "devicetype" or "role" or "relationship" or "anchor" or "cadence")
                .OrderBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(attribute => attribute.Value, StringComparer.OrdinalIgnoreCase)
                .Select(attribute => $"{attribute.Key}:{attribute.Value}"));

        return $"{NormalizeLabel(kind, "other")}|{string.Join("/", normalizedNames)}|{discriminator}";
    }

    private void EnsureParentDirectoryExists()
    {
        EnsureDirectoryExists(Path.GetDirectoryName(_filePath));
    }

    private static void EnsureDirectoryExists(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private sealed record MemoryExportDocument(
        int Version,
        DateTimeOffset ExportedAtUtc,
        MemoryNote[] Items);
}
