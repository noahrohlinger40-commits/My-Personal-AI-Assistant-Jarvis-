using System.Text.Json;

namespace Jarvis.Core;

public interface ITranscriptStore
{
    Task AppendAsync(AssistantTurn turn, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AssistantTurn>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AssistantTurn>> GetRecentAsync(int maxResults, CancellationToken cancellationToken);

    Task<TranscriptImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken);

    Task<string> ExportAsync(string destinationPath, CancellationToken cancellationToken);
}

public sealed class JsonLineTranscriptStore : ITranscriptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonLineTranscriptStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task AppendAsync(AssistantTurn turn, CancellationToken cancellationToken)
    {
        EnsureParentDirectoryExists();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var line = JsonSerializer.Serialize(turn, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(_filePath, line, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        var turns = await GetAllAsync(cancellationToken);
        return turns.Count;
    }

    public async Task<IReadOnlyList<AssistantTurn>> GetAllAsync(CancellationToken cancellationToken)
    {
        EnsureParentDirectoryExists();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            return await LoadTurnsUnsafeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AssistantTurn>> GetRecentAsync(int maxResults, CancellationToken cancellationToken)
    {
        if (maxResults <= 0)
        {
            return Array.Empty<AssistantTurn>();
        }

        EnsureParentDirectoryExists();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var turns = await LoadTurnsUnsafeAsync(cancellationToken);
            return turns
                .TakeLast(maxResults)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TranscriptImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        EnsureParentDirectoryExists();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Transcript import file not found.", sourcePath);
            }

            var existingTurns = await LoadTurnsUnsafeAsync(cancellationToken);
            var importedTurns = await ReadImportTurnsAsync(sourcePath, cancellationToken);
            var importedCount = 0;
            var skippedCount = 0;
            var builder = new List<string>(capacity: existingTurns.Count + importedTurns.Count);
            builder.AddRange(existingTurns.Select(turn => JsonSerializer.Serialize(turn, JsonOptions)));

            foreach (var turn in importedTurns)
            {
                if (ContainsEquivalent(existingTurns, turn))
                {
                    skippedCount++;
                    continue;
                }

                existingTurns.Add(turn);
                builder.Add(JsonSerializer.Serialize(turn, JsonOptions));
                importedCount++;
            }

            if (importedCount > 0)
            {
                await File.WriteAllTextAsync(
                    _filePath,
                    string.Join(Environment.NewLine, builder) + Environment.NewLine,
                    cancellationToken);
            }

            return new TranscriptImportResult(
                importedCount,
                skippedCount,
                $"Imported {importedCount} transcript turn(s); skipped {skippedCount} duplicate or invalid turn(s).");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> ExportAsync(string destinationPath, CancellationToken cancellationToken)
    {
        EnsureParentDirectoryExists();

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var turns = await LoadTurnsUnsafeAsync(cancellationToken);
            var exportPath = NormalizeExportPath(destinationPath);
            EnsureDirectory(Path.GetDirectoryName(exportPath));

            if (string.Equals(Path.GetExtension(exportPath), ".json", StringComparison.OrdinalIgnoreCase))
            {
                var document = new TranscriptExportDocument(1, DateTimeOffset.UtcNow, turns.ToArray());
                await using var stream = File.Create(exportPath);
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
            }
            else
            {
                var content = string.Join(Environment.NewLine, turns.Select(turn => JsonSerializer.Serialize(turn, JsonOptions)));
                await File.WriteAllTextAsync(
                    exportPath,
                    string.IsNullOrWhiteSpace(content) ? string.Empty : content + Environment.NewLine,
                    cancellationToken);
            }

            return exportPath;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<AssistantTurn>> LoadTurnsUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new List<AssistantTurn>();
        }

        var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
        var turns = new List<AssistantTurn>(capacity: lines.Length);

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var turn = JsonSerializer.Deserialize<AssistantTurn>(line, JsonOptions);

            if (turn is not null)
            {
                turns.Add(turn);
            }
        }

        return turns
            .OrderBy(turn => turn.TimestampUtc)
            .ToList();
    }

    private static async Task<List<AssistantTurn>> ReadImportTurnsAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(sourcePath);

        if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            var text = await File.ReadAllTextAsync(sourcePath, cancellationToken);

            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<AssistantTurn>();
            }

            using var document = JsonDocument.Parse(text);

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return DeserializeTurns(document.RootElement);
            }

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (TryGetProperty(document.RootElement, "items", out var items)
                    && items.ValueKind == JsonValueKind.Array)
                {
                    return DeserializeTurns(items);
                }

                var single = document.RootElement.Deserialize<AssistantTurn>(JsonOptions);
                return single is null ? new List<AssistantTurn>() : new List<AssistantTurn> { single };
            }

            return new List<AssistantTurn>();
        }

        var lines = await File.ReadAllLinesAsync(sourcePath, cancellationToken);
        var turns = new List<AssistantTurn>();

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var turn = JsonSerializer.Deserialize<AssistantTurn>(line, JsonOptions);

            if (turn is not null)
            {
                turns.Add(turn);
            }
        }

        return turns;
    }

    private static List<AssistantTurn> DeserializeTurns(JsonElement arrayElement)
    {
        var turns = new List<AssistantTurn>();

        foreach (var item in arrayElement.EnumerateArray())
        {
            var turn = item.Deserialize<AssistantTurn>(JsonOptions);

            if (turn is not null)
            {
                turns.Add(turn);
            }
        }

        return turns;
    }

    private static bool ContainsEquivalent(IReadOnlyList<AssistantTurn> existingTurns, AssistantTurn candidate)
    {
        return existingTurns.Any(existing =>
            existing.TimestampUtc == candidate.TimestampUtc
            && string.Equals(existing.UserInput, candidate.UserInput, StringComparison.Ordinal)
            && string.Equals(existing.ResponseText, candidate.ResponseText, StringComparison.Ordinal));
    }

    private static string NormalizeExportPath(string destinationPath)
    {
        var trimmed = destinationPath.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("An export path is required.");
        }

        return string.IsNullOrWhiteSpace(Path.GetExtension(trimmed))
            ? trimmed + ".jsonl"
            : trimmed;
    }

    private void EnsureParentDirectoryExists()
    {
        EnsureDirectory(Path.GetDirectoryName(_filePath));
    }

    private static void EnsureDirectory(string? directory)
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

    private sealed record TranscriptExportDocument(
        int Version,
        DateTimeOffset ExportedAtUtc,
        AssistantTurn[] Items);
}
