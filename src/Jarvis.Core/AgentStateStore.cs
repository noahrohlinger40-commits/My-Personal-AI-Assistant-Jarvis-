using System.Text.Json;

namespace Jarvis.Core;

public interface IAgentStateStore
{
    Task<PersistedAgentSession?> GetPendingClarificationAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PersistedAgentSession>> GetBackgroundSessionsAsync(CancellationToken cancellationToken);

    Task<PersistedAgentSession?> GetLatestResumableSessionAsync(CancellationToken cancellationToken);

    Task<PersistedAgentSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken);

    Task UpsertSessionAsync(PersistedAgentSession session, CancellationToken cancellationToken);

    Task RemoveSessionAsync(string sessionId, CancellationToken cancellationToken);
}

public sealed class JsonAgentStateStore : IAgentStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonAgentStateStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<PersistedAgentSession?> GetPendingClarificationAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var sessions = await LoadAsync(cancellationToken);
            return sessions
                .Where(session => string.Equals(session.Status, "awaiting-clarification", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(session => session.UpdatedAtUtc)
                .FirstOrDefault();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PersistedAgentSession>> GetBackgroundSessionsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var sessions = await LoadAsync(cancellationToken);
            return sessions
                .Where(IsVisibleBackgroundSession)
                .OrderBy(session => session.CreatedAtUtc)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PersistedAgentSession?> GetLatestResumableSessionAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var sessions = await LoadAsync(cancellationToken);
            return sessions
                .Where(IsResumableSession)
                .OrderByDescending(session => session.UpdatedAtUtc)
                .FirstOrDefault();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PersistedAgentSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var sessions = await LoadAsync(cancellationToken);
            return sessions.FirstOrDefault(session => string.Equals(session.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertSessionAsync(PersistedAgentSession session, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var sessions = await LoadAsync(cancellationToken);
            var existingIndex = sessions.FindIndex(existing =>
                string.Equals(existing.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                sessions[existingIndex] = session;
            }
            else
            {
                sessions.Add(session);
            }

            await SaveAsync(sessions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var sessions = await LoadAsync(cancellationToken);
            sessions.RemoveAll(session => string.Equals(session.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
            await SaveAsync(sessions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<PersistedAgentSession>> LoadAsync(CancellationToken cancellationToken)
    {
        EnsureParentDirectoryExists();

        if (!File.Exists(_filePath))
        {
            return new List<PersistedAgentSession>();
        }

        await using var stream = File.OpenRead(_filePath);
        var sessions = await JsonSerializer.DeserializeAsync<List<PersistedAgentSession>>(stream, JsonOptions, cancellationToken);
        return sessions ?? new List<PersistedAgentSession>();
    }

    private async Task SaveAsync(List<PersistedAgentSession> sessions, CancellationToken cancellationToken)
    {
        EnsureParentDirectoryExists();

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, sessions, JsonOptions, cancellationToken);
    }

    private void EnsureParentDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static bool IsVisibleBackgroundSession(PersistedAgentSession session)
    {
        return session.RunInBackground
            && !MatchesStatus(session, "background-complete")
            && !MatchesStatus(session, "completed");
    }

    private static bool IsResumableSession(PersistedAgentSession session)
    {
        return MatchesStatus(session, "active")
            || MatchesStatus(session, "background-queued")
            || MatchesStatus(session, "background-running")
            || MatchesStatus(session, "background-failed");
    }

    private static bool MatchesStatus(PersistedAgentSession session, string expectedStatus)
    {
        return string.Equals(session.Status, expectedStatus, StringComparison.OrdinalIgnoreCase);
    }
}
