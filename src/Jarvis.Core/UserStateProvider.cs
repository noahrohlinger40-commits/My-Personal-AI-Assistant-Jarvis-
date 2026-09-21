using System.Text.Json;

namespace Jarvis.Core;

public interface IUserStateProvider
{
    Task<UserStateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

public sealed class JsonUserStateProvider : IUserStateProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonUserStateProvider(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<UserStateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(_filePath))
            {
                return UserStateSnapshot.Empty;
            }

            await using var stream = File.OpenRead(_filePath);
            var document = await JsonSerializer.DeserializeAsync<UserStateDocument>(stream, JsonOptions, cancellationToken);

            if (document is null)
            {
                return UserStateSnapshot.Empty;
            }

            return new UserStateSnapshot(
                string.IsNullOrWhiteSpace(document.Summary)
                    ? "Email and task context is available from the local user-state file."
                    : document.Summary.Trim(),
                document.Email ?? Array.Empty<UserStateItem>(),
                document.Tasks ?? Array.Empty<UserStateItem>());
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record UserStateDocument(
        string Summary,
        UserStateItem[]? Email,
        UserStateItem[]? Tasks);
}
