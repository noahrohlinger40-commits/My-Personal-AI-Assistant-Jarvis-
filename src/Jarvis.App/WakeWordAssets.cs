using System.Text.Json;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed record WakeWordAssetDescriptor(
    string Path,
    string Name,
    string DisplayName,
    string Description,
    string[] WakePhrases,
    string[] Aliases,
    double? MinimumConfidence,
    double SensitivityBias,
    string PreferredBeamformingProfile,
    string PreferredHardwareDspProfile,
    string PreferredWakeEnginePath,
    string PreferredVendorDspProfilePath,
    string[] DeviceKeywords,
    string[] ExcludedDeviceKeywords)
{
    public bool IsRecognizerDefault => string.IsNullOrWhiteSpace(Path);

    public bool IsAutomaticSelection =>
        string.Equals(
            WakeWordAssetCatalog.NormalizeStoredPath(Path),
            WakeWordAssetCatalog.AutoSelectionPath,
            StringComparison.OrdinalIgnoreCase);

    public string BuildShortLabel()
    {
        if (!string.IsNullOrWhiteSpace(DisplayName))
        {
            return DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(Name))
        {
            return Name;
        }

        return "Recognizer Default";
    }

    public IReadOnlyList<string> BuildWakeAliases()
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var phrase in WakePhrases)
        {
            AddAlias(phrase);
        }

        foreach (var alias in Aliases)
        {
            AddAlias(alias);
        }

        return aliases.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

        void AddAlias(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            aliases.Add(value.Trim());
        }
    }

    public int ScoreDeviceMatch(string? deviceName)
    {
        if (IsRecognizerDefault || IsAutomaticSelection)
        {
            return int.MinValue;
        }

        var normalizedDeviceName = deviceName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedDeviceName))
        {
            return 0;
        }

        if (ExcludedDeviceKeywords.Any(keyword =>
                normalizedDeviceName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return int.MinValue;
        }

        var score = 0;

        foreach (var keyword in DeviceKeywords)
        {
            if (normalizedDeviceName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 18 + Math.Min(keyword.Length, 18);
            }
        }

        return score;
    }
}

internal static class WakeWordAssetCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object Sync = new();
    private static readonly Dictionary<string, CachedWakeWordAsset> Cache = new(StringComparer.OrdinalIgnoreCase);
    private const string DefaultRelativeDirectory = "data\\wake-word-assets";
    public const string AutoSelectionPath = "auto";

    public static WakeWordAssetDescriptor AutoSelection { get; } =
        new(
            Path: AutoSelectionPath,
            Name: "auto",
            DisplayName: "Auto (Per Device)",
            Description: "Automatically choose the best dedicated wake asset for the active microphone when one matches.",
            WakePhrases: Array.Empty<string>(),
            Aliases: Array.Empty<string>(),
            MinimumConfidence: null,
            SensitivityBias: 0,
            PreferredBeamformingProfile: string.Empty,
            PreferredHardwareDspProfile: string.Empty,
            PreferredWakeEnginePath: string.Empty,
            PreferredVendorDspProfilePath: string.Empty,
            DeviceKeywords: Array.Empty<string>(),
            ExcludedDeviceKeywords: Array.Empty<string>());

    public static WakeWordAssetDescriptor RecognizerDefault { get; } =
        new(
            Path: string.Empty,
            Name: "recognizer-default",
            DisplayName: "Recognizer Default",
            Description: "Use the built-in recognizer wake grammars without a dedicated asset manifest.",
            WakePhrases: Array.Empty<string>(),
            Aliases: Array.Empty<string>(),
            MinimumConfidence: null,
            SensitivityBias: 0,
            PreferredBeamformingProfile: string.Empty,
            PreferredHardwareDspProfile: string.Empty,
            PreferredWakeEnginePath: string.Empty,
            PreferredVendorDspProfilePath: string.Empty,
            DeviceKeywords: Array.Empty<string>(),
            ExcludedDeviceKeywords: Array.Empty<string>());

    public static IReadOnlyList<WakeWordAssetDescriptor> Discover(string workspaceRoot, JarvisOptions options)
    {
        var discovered = new List<WakeWordAssetDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeStoredPath(string.Empty),
            AutoSelectionPath
        };
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configuredPath = NormalizeStoredPath(options.SpeechRecognitionWakeWordAssetPath);

        if (!string.IsNullOrWhiteSpace(configuredPath) && !IsAutoSelectionPath(configuredPath))
        {
            var configuredAbsolutePath = ResolveAbsolutePath(configuredPath, workspaceRoot);
            TryAdd(configuredPath, configuredAbsolutePath, discovered, seen);

            var configuredDirectory = Path.GetDirectoryName(configuredAbsolutePath);

            if (!string.IsNullOrWhiteSpace(configuredDirectory))
            {
                directories.Add(configuredDirectory);
            }
        }

        directories.Add(ResolveAbsolutePath(DefaultRelativeDirectory, workspaceRoot));

        foreach (var directory in directories.Where(Directory.Exists))
        {
            foreach (var filePath in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var storedPath = ToStoredPath(workspaceRoot, filePath);
                TryAdd(storedPath, filePath, discovered, seen);
            }
        }

        return [AutoSelection, RecognizerDefault, .. discovered
            .Where(asset => !asset.IsRecognizerDefault)
            .OrderBy(asset => asset.BuildShortLabel(), StringComparer.OrdinalIgnoreCase)];
    }

    public static WakeWordAssetDescriptor? ResolveConfiguredAsset(JarvisOptions options)
    {
        var storedPath = NormalizeStoredPath(options.SpeechRecognitionWakeWordAssetPath);

        if (IsAutoSelectionPath(storedPath))
        {
            return ResolveAutoAsset(options);
        }

        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return null;
        }

        var absolutePath = ResolveAbsolutePath(storedPath, Directory.GetCurrentDirectory());
        return TryLoadDescriptor(storedPath, absolutePath);
    }

    public static WakeWordAssetDescriptor ResolveSelectionDescriptor(JarvisOptions options)
    {
        var storedPath = NormalizeStoredPath(options.SpeechRecognitionWakeWordAssetPath);

        if (IsAutoSelectionPath(storedPath))
        {
            return AutoSelection;
        }

        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return RecognizerDefault;
        }

        var absolutePath = ResolveAbsolutePath(storedPath, Directory.GetCurrentDirectory());
        return TryLoadDescriptor(storedPath, absolutePath) ?? RecognizerDefault;
    }

    public static bool IsAutoSelectionPath(string? path) =>
        string.Equals(NormalizeStoredPath(path), AutoSelectionPath, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeStoredPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('/', '\\');
    }

    private static WakeWordAssetDescriptor? ResolveAutoAsset(JarvisOptions options)
    {
        var discovered = Discover(Directory.GetCurrentDirectory(), options)
            .Where(asset => !asset.IsRecognizerDefault && !asset.IsAutomaticSelection)
            .ToArray();

        if (discovered.Length == 0)
        {
            return null;
        }

        var beamformingProfile = BeamformingProfileCatalog.ResolveAuto(options.SpeechRecognitionDeviceName);
        var hardwareDspProfile = HardwareDspProfileCatalog.ResolveAuto(options.SpeechRecognitionDeviceName, beamformingProfile);

        var bestMatch = discovered
            .Select(asset => new
            {
                Asset = asset,
                Score = ScoreAutoSelection(asset, options.SpeechRecognitionDeviceName, beamformingProfile.Name, hardwareDspProfile.Name)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Asset.BuildShortLabel(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (bestMatch is null || bestMatch.Score <= 0)
        {
            return discovered
                .OrderByDescending(asset =>
                    string.Equals(asset.PreferredBeamformingProfile, beamformingProfile.Name, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(asset =>
                    string.Equals(asset.PreferredHardwareDspProfile, hardwareDspProfile.Name, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(asset => asset.BuildShortLabel(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        return bestMatch.Asset;
    }

    private static void TryAdd(
        string storedPath,
        string absolutePath,
        ICollection<WakeWordAssetDescriptor> discovered,
        ISet<string> seen)
    {
        var normalizedPath = NormalizeStoredPath(storedPath);

        if (!seen.Add(normalizedPath))
        {
            return;
        }

        var asset = TryLoadDescriptor(normalizedPath, absolutePath);

        if (asset is null)
        {
            return;
        }

        discovered.Add(asset);
    }

    private static WakeWordAssetDescriptor? TryLoadDescriptor(string storedPath, string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            return null;
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(absolutePath);

        lock (Sync)
        {
            if (Cache.TryGetValue(absolutePath, out var cached)
                && cached.LastWriteUtc == lastWriteUtc)
            {
                return cached.Asset;
            }
        }

        try
        {
            var json = File.ReadAllText(absolutePath);
            var manifest = JsonSerializer.Deserialize<WakeWordAssetManifest>(json, JsonOptions);

            if (manifest is null)
            {
                return null;
            }

            var descriptor = new WakeWordAssetDescriptor(
                Path: storedPath,
                Name: string.IsNullOrWhiteSpace(manifest.Name)
                    ? System.IO.Path.GetFileNameWithoutExtension(absolutePath)
                    : manifest.Name.Trim(),
                DisplayName: string.IsNullOrWhiteSpace(manifest.DisplayName)
                    ? System.IO.Path.GetFileNameWithoutExtension(absolutePath)
                    : manifest.DisplayName.Trim(),
                Description: manifest.Description?.Trim() ?? string.Empty,
                WakePhrases: NormalizeValues(manifest.WakePhrases),
                Aliases: NormalizeValues(manifest.Aliases),
                MinimumConfidence: manifest.MinimumConfidence,
                SensitivityBias: Math.Clamp(manifest.SensitivityBias ?? 0, -0.20, 0.20),
                PreferredBeamformingProfile: manifest.PreferredBeamformingProfile?.Trim() ?? string.Empty,
                PreferredHardwareDspProfile: manifest.PreferredHardwareDspProfile?.Trim() ?? string.Empty,
                PreferredWakeEnginePath: manifest.PreferredWakeEnginePath?.Trim() ?? string.Empty,
                PreferredVendorDspProfilePath: manifest.PreferredVendorDspProfilePath?.Trim() ?? string.Empty,
                DeviceKeywords: NormalizeValues(manifest.DeviceKeywords),
                ExcludedDeviceKeywords: NormalizeValues(manifest.ExcludedDeviceKeywords));

            lock (Sync)
            {
                Cache[absolutePath] = new CachedWakeWordAsset(lastWriteUtc, descriptor);
            }

            return descriptor;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveAbsolutePath(string path, string baseDirectory)
    {
        return System.IO.Path.IsPathRooted(path)
            ? System.IO.Path.GetFullPath(path)
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, path));
    }

    private static string ToStoredPath(string workspaceRoot, string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return absolutePath;
        }

        try
        {
            return NormalizeStoredPath(System.IO.Path.GetRelativePath(workspaceRoot, absolutePath));
        }
        catch
        {
            return absolutePath;
        }
    }

    private static string[] NormalizeValues(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record CachedWakeWordAsset(DateTime LastWriteUtc, WakeWordAssetDescriptor? Asset);

    private static int ScoreAutoSelection(
        WakeWordAssetDescriptor asset,
        string? deviceName,
        string beamformingProfileName,
        string hardwareDspProfileName)
    {
        var score = asset.ScoreDeviceMatch(deviceName);

        if (score == int.MinValue)
        {
            return score;
        }

        if (!string.IsNullOrWhiteSpace(asset.PreferredBeamformingProfile)
            && string.Equals(asset.PreferredBeamformingProfile, beamformingProfileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 22;
        }

        if (!string.IsNullOrWhiteSpace(asset.PreferredHardwareDspProfile)
            && string.Equals(asset.PreferredHardwareDspProfile, hardwareDspProfileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 18;
        }

        if (score <= 0 && !string.IsNullOrWhiteSpace(deviceName))
        {
            if (asset.BuildShortLabel().Contains("desktop", StringComparison.OrdinalIgnoreCase)
                && beamformingProfileName == "desktop")
            {
                score += 8;
            }
            else if (asset.BuildShortLabel().Contains("headset", StringComparison.OrdinalIgnoreCase)
                     && beamformingProfileName == "headset")
            {
                score += 8;
            }
            else if ((asset.BuildShortLabel().Contains("conference", StringComparison.OrdinalIgnoreCase)
                      || asset.BuildShortLabel().Contains("far field", StringComparison.OrdinalIgnoreCase))
                     && (beamformingProfileName == "conference" || beamformingProfileName == "far-field-array"))
            {
                score += 8;
            }
        }

        return score;
    }

    private sealed record WakeWordAssetManifest
    {
        public string Name { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string[] WakePhrases { get; init; } = Array.Empty<string>();

        public string[] Aliases { get; init; } = Array.Empty<string>();

        public double? MinimumConfidence { get; init; }

        public double? SensitivityBias { get; init; }

        public string PreferredBeamformingProfile { get; init; } = string.Empty;

        public string PreferredHardwareDspProfile { get; init; } = string.Empty;

        public string PreferredWakeEnginePath { get; init; } = string.Empty;

        public string PreferredVendorDspProfilePath { get; init; } = string.Empty;

        public string[] DeviceKeywords { get; init; } = Array.Empty<string>();

        public string[] ExcludedDeviceKeywords { get; init; } = Array.Empty<string>();
    }
}
