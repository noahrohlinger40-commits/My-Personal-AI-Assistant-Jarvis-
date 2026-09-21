using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.Core;

/// <summary>
/// The application index: every app Jarvis can open by name, the spoken names it answers to, and a
/// saved copy on disk (data\app-index.json) that also feeds the speech vocabulary.
/// </summary>
public sealed partial class DesktopAutomationService
{
    private const string ShellAppsFolderPrefix = "shell:AppsFolder\\";

    // Installed apps change rarely; a short cache avoids rescanning the disk for every spoken command.
    private static readonly TimeSpan StaticCatalogLifetime = TimeSpan.FromMinutes(10);

    // Windows' own app list needs a PowerShell call (about two seconds), so it is cached much longer.
    private static readonly TimeSpan StartAppsLifetime = TimeSpan.FromHours(6);

    // After an unknown app name, rescan at most this often so gibberish cannot trigger endless rescans.
    private static readonly TimeSpan MissRefreshInterval = TimeSpan.FromMinutes(2);

    private static readonly JsonSerializerOptions IndexJsonOptions = new() { WriteIndented = true };

    private readonly object _catalogSync = new();
    private IReadOnlyList<ApplicationCandidate>? _staticCatalog;
    private HashSet<string> _knownNames = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _staticCatalogBuiltUtc = DateTime.MinValue;
    private IReadOnlyList<StartAppEntry>? _startApps;
    private IReadOnlyList<StartAppEntry>? _lastPersistedStartApps;
    private DateTime _startAppsBuiltUtc = DateTime.MinValue;
    private DateTime _lastMissRefreshUtc = DateTime.MinValue;

    private string ApplicationIndexPath => Path.Combine(_workspaceRoot, "data", "app-index.json");

    /// <summary>
    /// Builds (or refreshes) the application index without blocking the caller, so the first spoken
    /// "open" command does not pay for the scan.
    /// </summary>
    public void WarmApplicationIndexInBackground()
    {
        _ = Task.Run(() =>
        {
            try
            {
                GetStaticCatalog(forceRefresh: false);
            }
            catch
            {
                // The index is an optimisation; commands still work through the on-demand scan.
            }
        });
    }

    /// <summary>
    /// True when the text is exactly the name of an installed app (or a spoken shortcut for one), so
    /// "open Movies and TV" can be recognised as an app even though the name contains an "and".
    /// </summary>
    public bool IsKnownApplicationName(string spokenName)
    {
        GetStaticCatalog(forceRefresh: false);
        var normalized = StripQueryNoise(NormalizeForMatching(spokenName));

        lock (_catalogSync)
        {
            return normalized.Length > 0 && _knownNames.Contains(normalized);
        }
    }

    /// <summary>The names people can say for every indexed app, for the speech vocabulary.</summary>
    public IReadOnlyList<string> GetSpokenApplicationNames()
    {
        return GetStaticCatalog(forceRefresh: false)
            .Where(candidate => !string.Equals(candidate.Source, "alias", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.DisplayName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<ApplicationCandidate> GetStaticCatalog(bool forceRefresh)
    {
        lock (_catalogSync)
        {
            if (!forceRefresh
                && _staticCatalog is not null
                && DateTime.UtcNow - _staticCatalogBuiltUtc < StaticCatalogLifetime)
            {
                return _staticCatalog;
            }

            var startApps = GetStartApps(forceRefresh);
            var candidates = BuildStaticApplicationCandidates(startApps);

            _staticCatalog = candidates;
            _staticCatalogBuiltUtc = DateTime.UtcNow;
            _knownNames = candidates
                .SelectMany(candidate => GetSpokenNames(candidate.DisplayName).Append(NormalizeForMatching(candidate.DisplayName)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // The saved copy is informational, so write it only after a real rescan of Windows' app list
            // (or the first build), not on every ten-minute cache rebuild.
            if (!ReferenceEquals(startApps, _lastPersistedStartApps) || !File.Exists(ApplicationIndexPath))
            {
                PersistApplicationIndex(candidates, startApps);
                _lastPersistedStartApps = startApps;
            }

            return candidates;
        }
    }

    private IReadOnlyList<ApplicationCandidate> BuildStaticApplicationCandidates(IReadOnlyList<StartAppEntry> startApps)
    {
        var candidates = new List<ApplicationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in _appAliases)
        {
            AddCandidate(
                candidates,
                seen,
                new ApplicationCandidate(alias.Key, alias.Value, "alias", false, IntPtr.Zero, 0, alias.Key, string.Empty));
        }

        foreach (var source in new Func<IEnumerable<ApplicationCandidate>>[]
                 {
                     GetStartMenuApplications,
                     GetDesktopApplications,
                     GetAppPathApplications,
                     GetInstalledApplications,
                     GetLocalUserApplications,
                     GetEpicLibraryApplications,
                     GetRiotLibraryApplications,
                     GetXboxLibraryApplications,
                     GetBattleNetLibraryApplications,
                     GetSteamLibraryApplications
                 })
        {
            foreach (var candidate in source())
            {
                AddCandidate(candidates, seen, candidate);
            }
        }

        // Anything Windows lists as an app that the scans above did not find, mainly Microsoft Store apps
        // such as Clock, Photos, Claude or ChatGPT, which have no Start Menu shortcut file.
        var known = candidates
            .Where(candidate => !string.Equals(candidate.Source, "alias", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => NormalizeForMatching(candidate.DisplayName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var startApp in startApps)
        {
            if (known.Contains(NormalizeForMatching(startApp.Name)))
            {
                continue;
            }

            AddCandidate(
                candidates,
                seen,
                new ApplicationCandidate(
                    startApp.Name,
                    ShellAppsFolderPrefix + startApp.AppId,
                    startApp.AppId.Contains('!') ? "store app" : "start app",
                    false,
                    IntPtr.Zero,
                    0,
                    startApp.Name,
                    string.Empty));
        }

        return candidates;
    }

    private IReadOnlyList<StartAppEntry> GetStartApps(bool forceRefresh)
    {
        if (!forceRefresh && _startApps is not null && DateTime.UtcNow - _startAppsBuiltUtc < StartAppsLifetime)
        {
            return _startApps;
        }

        IReadOnlyList<StartAppEntry> persisted = [];
        var persistedUtc = DateTime.MinValue;

        if (!forceRefresh && TryLoadPersistedStartApps(out persisted, out persistedUtc))
        {
            _startApps = persisted;
            _startAppsBuiltUtc = persistedUtc;
            return persisted;
        }

        var scanned = QueryStartApps();

        if (scanned.Count > 0)
        {
            _startApps = scanned;
            _startAppsBuiltUtc = DateTime.UtcNow;
            return scanned;
        }

        // The scan failed (for example PowerShell was blocked); keep whatever we had rather than lose apps.
        return _startApps ?? persisted ?? [];
    }

    private static IReadOnlyList<StartAppEntry> QueryStartApps()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(
                "[Console]::OutputEncoding = [Text.Encoding]::UTF8; "
                + "Get-StartApps | ForEach-Object { $_.Name + [char]9 + $_.AppID }");

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return [];
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(20_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return [];
            }

            var results = new List<StartAppEntry>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in outputTask.GetAwaiter().GetResult().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.TrimEnd('\r').Split('\t', 2);

                if (parts.Length != 2
                    || string.IsNullOrWhiteSpace(parts[0])
                    || string.IsNullOrWhiteSpace(parts[1])
                    || !seenNames.Add(parts[0].Trim()))
                {
                    continue;
                }

                results.Add(new StartAppEntry(parts[0].Trim(), parts[1].Trim()));
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    private bool TryLoadPersistedStartApps(out IReadOnlyList<StartAppEntry> startApps, out DateTime generatedUtc)
    {
        startApps = [];
        generatedUtc = DateTime.MinValue;

        try
        {
            if (!File.Exists(ApplicationIndexPath))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(ApplicationIndexPath));

            if (!document.RootElement.TryGetProperty("generatedUtc", out var generatedElement)
                || !generatedElement.TryGetDateTime(out var generated)
                || !document.RootElement.TryGetProperty("startApps", out var startAppsElement)
                || startAppsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var loaded = new List<StartAppEntry>();

            foreach (var item in startAppsElement.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                var appId = item.TryGetProperty("appId", out var idElement) ? idElement.GetString() : null;

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(appId))
                {
                    loaded.Add(new StartAppEntry(name, appId));
                }
            }

            if (loaded.Count == 0)
            {
                return false;
            }

            startApps = loaded;
            generatedUtc = generated.ToUniversalTime();

            // A stale file is still handed back as a fallback, but only a fresh one skips the rescan.
            return DateTime.UtcNow - generatedUtc < StartAppsLifetime;
        }
        catch
        {
            return false;
        }
    }

    private void PersistApplicationIndex(IReadOnlyList<ApplicationCandidate> candidates, IReadOnlyList<StartAppEntry> startApps)
    {
        try
        {
            var payload = new
            {
                generatedUtc = _startAppsBuiltUtc == DateTime.MinValue ? DateTime.UtcNow : _startAppsBuiltUtc,
                startApps = startApps.Select(app => new { name = app.Name, appId = app.AppId }),
                apps = candidates
                    .Where(candidate => !string.Equals(candidate.Source, "alias", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(candidate => new
                    {
                        name = candidate.DisplayName,
                        source = candidate.Source,
                        launch = candidate.LaunchTarget,
                        arguments = candidate.LaunchArguments,
                        spoken = BuildSpokenNames(candidate.DisplayName)
                    })
            };

            Directory.CreateDirectory(Path.GetDirectoryName(ApplicationIndexPath)!);
            File.WriteAllText(ApplicationIndexPath, JsonSerializer.Serialize(payload, IndexJsonOptions));
        }
        catch
        {
            // Saving the index is best effort; it never blocks opening an app.
        }
    }

    // ---- Spoken names ---------------------------------------------------------------------------

    private static readonly HashSet<string> LeadingNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft", "windows", "google", "adobe", "apple", "intel", "nvidia", "dell", "hp", "amazon", "my", "the"
    };

    private static readonly HashSet<string> TrailingNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "application", "desktop", "launcher", "client", "program", "shortcut", "for", "windows", "edition"
    };

    /// <summary>
    /// The ways a person would naturally say an app's name: the full name, the name without a subtitle
    /// or version, and without words such as "Microsoft" or "app". "Microsoft Edge" answers to "edge".
    /// </summary>
    internal static IReadOnlyList<string> BuildSpokenNames(string displayName)
    {
        var names = new List<string>();

        void Add(string value)
        {
            var normalized = NormalizeForMatching(value);

            if (normalized.Length >= 3 && !names.Contains(normalized))
            {
                names.Add(normalized);
            }
        }

        Add(displayName);

        // "Movies & TV" is spoken "Movies and TV".
        if (displayName.Contains('&'))
        {
            Add(displayName.Replace("&", " and ", StringComparison.Ordinal));
        }

        // "Audibly — Audiobook Player" -> "Audibly"
        var cleaned = Regex.Split(displayName, @"\s[—–\-:|]\s")[0];
        Add(cleaned);

        cleaned = Regex.Replace(cleaned, @"[\(\[].*?[\)\]]", " ");
        cleaned = Regex.Replace(cleaned, @"[®™©]", " ");
        cleaned = Regex.Replace(cleaned, @"\b(v?\d+(\.\d+)*|x64|x86|64-bit|32-bit)\b", " ", RegexOptions.IgnoreCase);
        Add(cleaned);

        var words = NormalizeForMatching(cleaned).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        while (words.Count > 1 && LeadingNoiseWords.Contains(words[0]))
        {
            words.RemoveAt(0);
            Add(string.Join(' ', words));
        }

        while (words.Count > 1 && TrailingNoiseWords.Contains(words[^1]))
        {
            words.RemoveAt(words.Count - 1);
            Add(string.Join(' ', words));
        }

        return names;
    }

    // ---- Matching -------------------------------------------------------------------------------

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> SpokenNameCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cached <see cref="BuildSpokenNames"/>: scoring asks for every app on every command.</summary>
    private static IReadOnlyList<string> GetSpokenNames(string displayName) =>
        SpokenNameCache.GetOrAdd(displayName, name => BuildSpokenNames(name).ToArray());

    private static readonly HashSet<string> QueryLeadingNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "my", "microsoft", "windows", "app", "application", "program"
    };

    private static readonly HashSet<string> QueryTrailingNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "application", "program"
    };

    /// <summary>
    /// Drops filler from a spoken app name so "the Spotify app" is "spotify" and "Microsoft Photos" is
    /// "photos". A name that is only filler ("windows") is left alone.
    /// </summary>
    private static string StripQueryNoise(string normalizedQuery)
    {
        var words = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        while (words.Count > 1 && QueryLeadingNoiseWords.Contains(words[0]))
        {
            words.RemoveAt(0);
        }

        while (words.Count > 1 && QueryTrailingNoiseWords.Contains(words[^1]))
        {
            words.RemoveAt(words.Count - 1);
        }

        return string.Join(' ', words);
    }

    /// <summary>
    /// Scores how close a possibly misheard name is to an app, from 60 (barely close enough) to about 80.
    /// Returns 0 when nothing is close, so unrelated speech never opens an app.
    /// </summary>
    private static int ScoreFuzzyMatch(string normalizedQuery, string displayName)
    {
        var query = normalizedQuery.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (query.Length < 4)
        {
            return 0;
        }

        var best = 0.0;
        var bestSoundAlike = 0.0;
        var queryKey = query.Length >= 5 ? PhoneticKey(query) : string.Empty;

        foreach (var spoken in GetSpokenNames(displayName))
        {
            var candidate = spoken.Replace(" ", string.Empty, StringComparison.Ordinal);

            if (candidate.Length < 4 || Math.Abs(candidate.Length - query.Length) > 3)
            {
                continue;
            }

            var distance = EditDistance(query, candidate);
            var similarity = 1.0 - ((double)distance / Math.Max(query.Length, candidate.Length));
            best = Math.Max(best, similarity);

            // Sounds alike but is spelled differently ("clawed" for "Claude", "vidburner" for "Bitburner").
            if (queryKey.Length >= 3
                && queryKey == PhoneticKey(candidate)
                && (query[0] == candidate[0] || IsSameSoundGroup(query[0], candidate[0])))
            {
                bestSoundAlike = Math.Max(bestSoundAlike, similarity);
            }
        }

        if (best >= 0.8)
        {
            return 60 + (int)((best - 0.8) * 100);
        }

        // Kept below every spelling-based match, and only when the names are also reasonably alike on paper.
        return bestSoundAlike >= 0.5 ? 60 + (int)(bestSoundAlike * 5) : 0;
    }

    private static bool IsSameSoundGroup(char left, char right) => SoundGroup(left) != '0' && SoundGroup(left) == SoundGroup(right);

    private static char SoundGroup(char letter) => letter switch
    {
        'b' or 'p' or 'f' or 'v' => '1',
        'c' or 'g' or 'j' or 'k' or 'q' or 's' or 'x' or 'z' => '2',
        'd' or 't' => '3',
        'l' => '4',
        'm' or 'n' => '5',
        'r' => '6',
        _ => '0'
    };

    /// <summary>
    /// A rough "how it sounds" code: similar consonants share a digit, vowels are dropped. Two names with
    /// the same code sound alike ("claude" and "clawed" are both 243).
    /// </summary>
    private static string PhoneticKey(string value)
    {
        var key = new StringBuilder();
        var previous = '\0';

        foreach (var letter in value)
        {
            if (letter is 'a' or 'e' or 'i' or 'o' or 'u' or 'y')
            {
                previous = '\0';
                continue;
            }

            var group = SoundGroup(letter);

            if (group == '0')
            {
                continue;
            }

            if (group != previous)
            {
                key.Append(group);
            }

            previous = group;
        }

        return key.ToString();
    }

    private static int EditDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private sealed record StartAppEntry(string Name, string AppId);
}
