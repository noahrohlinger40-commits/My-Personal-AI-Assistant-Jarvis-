using System.Text.Json;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed record VendorDspProfileDescriptor(
    string Path,
    string DefinitionDirectory,
    string Name,
    string DisplayName,
    string Description,
    string ExecutablePath,
    string ApplyArguments,
    string ReleaseArguments,
    string WorkingDirectory,
    string ProfilePath,
    int ApplyTimeoutMilliseconds,
    int ReleaseTimeoutMilliseconds,
    bool UseHardwareEchoCancellation,
    bool UseHardwareNoiseSuppression,
    bool UseHardwareBeamforming,
    string RealtimeNoiseReductionMode,
    int RealtimeCaptureBufferMilliseconds,
    int RollingCaptureBufferMilliseconds,
    double VoiceActivityScale,
    double InputGainCapScale,
    double TargetPeakLevelScale,
    double EchoGateScale,
    double MinimumSpeechPeakLevelOffset,
    double ServerVadThresholdScale,
    double PlaybackEchoFloorScale,
    double AmbientFloorScale,
    double BargeInThresholdScale,
    double ContinuingSpeechThresholdScale,
    string PreferredBeamformingProfile,
    string PreferredHardwareDspProfile,
    string[] PreferredWakeAssetPaths,
    string[] DeviceKeywords,
    string[] ExcludedDeviceKeywords,
    IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    public bool IsNone => string.IsNullOrWhiteSpace(Path);

    public bool IsAutomaticSelection =>
        string.Equals(
            VendorDspProfileCatalog.NormalizeStoredPath(Path),
            VendorDspProfileCatalog.AutoSelectionPath,
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

        return "No Vendor SDK";
    }

    public int ScoreDeviceMatch(string? deviceName)
    {
        if (IsNone || IsAutomaticSelection)
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

internal static class VendorDspProfileCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly object Sync = new();
    private static readonly Dictionary<string, CachedVendorDspProfile> Cache = new(StringComparer.OrdinalIgnoreCase);
    private const string DefaultRelativeDirectory = "data\\vendor-dsp-profiles";
    public const string AutoSelectionPath = "auto";

    public static VendorDspProfileDescriptor AutoSelection { get; } =
        new(
            Path: AutoSelectionPath,
            DefinitionDirectory: string.Empty,
            Name: "auto",
            DisplayName: "Auto (Vendor DSP)",
            Description: "Automatically apply a vendor DSP profile when one matches the active microphone, otherwise leave vendor SDK controls disabled.",
            ExecutablePath: string.Empty,
            ApplyArguments: string.Empty,
            ReleaseArguments: string.Empty,
            WorkingDirectory: string.Empty,
            ProfilePath: string.Empty,
            ApplyTimeoutMilliseconds: 2500,
            ReleaseTimeoutMilliseconds: 1500,
            UseHardwareEchoCancellation: false,
            UseHardwareNoiseSuppression: false,
            UseHardwareBeamforming: false,
            RealtimeNoiseReductionMode: string.Empty,
            RealtimeCaptureBufferMilliseconds: 0,
            RollingCaptureBufferMilliseconds: 0,
            VoiceActivityScale: 1.0,
            InputGainCapScale: 1.0,
            TargetPeakLevelScale: 1.0,
            EchoGateScale: 1.0,
            MinimumSpeechPeakLevelOffset: 0,
            ServerVadThresholdScale: 1.0,
            PlaybackEchoFloorScale: 1.0,
            AmbientFloorScale: 1.0,
            BargeInThresholdScale: 1.0,
            ContinuingSpeechThresholdScale: 1.0,
            PreferredBeamformingProfile: string.Empty,
            PreferredHardwareDspProfile: string.Empty,
            PreferredWakeAssetPaths: Array.Empty<string>(),
            DeviceKeywords: Array.Empty<string>(),
            ExcludedDeviceKeywords: Array.Empty<string>(),
            EnvironmentVariables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static VendorDspProfileDescriptor None { get; } =
        new(
            Path: string.Empty,
            DefinitionDirectory: string.Empty,
            Name: "none",
            DisplayName: "No Vendor SDK",
            Description: "Leave vendor SDK-level DSP control disabled.",
            ExecutablePath: string.Empty,
            ApplyArguments: string.Empty,
            ReleaseArguments: string.Empty,
            WorkingDirectory: string.Empty,
            ProfilePath: string.Empty,
            ApplyTimeoutMilliseconds: 2500,
            ReleaseTimeoutMilliseconds: 1500,
            UseHardwareEchoCancellation: false,
            UseHardwareNoiseSuppression: false,
            UseHardwareBeamforming: false,
            RealtimeNoiseReductionMode: string.Empty,
            RealtimeCaptureBufferMilliseconds: 0,
            RollingCaptureBufferMilliseconds: 0,
            VoiceActivityScale: 1.0,
            InputGainCapScale: 1.0,
            TargetPeakLevelScale: 1.0,
            EchoGateScale: 1.0,
            MinimumSpeechPeakLevelOffset: 0,
            ServerVadThresholdScale: 1.0,
            PlaybackEchoFloorScale: 1.0,
            AmbientFloorScale: 1.0,
            BargeInThresholdScale: 1.0,
            ContinuingSpeechThresholdScale: 1.0,
            PreferredBeamformingProfile: string.Empty,
            PreferredHardwareDspProfile: string.Empty,
            PreferredWakeAssetPaths: Array.Empty<string>(),
            DeviceKeywords: Array.Empty<string>(),
            ExcludedDeviceKeywords: Array.Empty<string>(),
            EnvironmentVariables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<VendorDspProfileDescriptor> Discover(string workspaceRoot, JarvisOptions options)
    {
        var discovered = new List<VendorDspProfileDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizeStoredPath(string.Empty),
            AutoSelectionPath
        };
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configuredPath = NormalizeStoredPath(options.SpeechRecognitionVendorDspProfilePath);

        if (!string.IsNullOrWhiteSpace(configuredPath) && !IsAutoSelectionPath(configuredPath))
        {
            var configuredAbsolutePath = ResolveAbsolutePath(configuredPath, workspaceRoot);
            TryAdd(configuredPath, configuredAbsolutePath, workspaceRoot, discovered, seen);

            var configuredDirectory = System.IO.Path.GetDirectoryName(configuredAbsolutePath);

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
                TryAdd(storedPath, filePath, workspaceRoot, discovered, seen);
            }
        }

        return [AutoSelection, None, .. discovered
            .Where(profile => !profile.IsNone)
            .OrderBy(profile => profile.BuildShortLabel(), StringComparer.OrdinalIgnoreCase)];
    }

    public static VendorDspProfileDescriptor ResolveConfiguredProfile(
        JarvisOptions options,
        WakeWordAssetDescriptor? wakeWordAsset,
        string beamformingProfileName,
        string hardwareDspProfileName)
    {
        var storedPath = NormalizeStoredPath(options.SpeechRecognitionVendorDspProfilePath);

        if (IsAutoSelectionPath(storedPath))
        {
            return ResolveAutoProfile(options, wakeWordAsset, beamformingProfileName, hardwareDspProfileName);
        }

        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return None;
        }

        return ResolveDescriptorByPath(Directory.GetCurrentDirectory(), options, storedPath);
    }

    public static VendorDspProfileDescriptor ResolveSelectionDescriptor(JarvisOptions options)
    {
        var storedPath = NormalizeStoredPath(options.SpeechRecognitionVendorDspProfilePath);

        if (IsAutoSelectionPath(storedPath))
        {
            return AutoSelection;
        }

        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return None;
        }

        return ResolveDescriptorByPath(Directory.GetCurrentDirectory(), options, storedPath);
    }

    public static VendorDspProfileDescriptor ResolveDescriptorByPath(string workspaceRoot, JarvisOptions options, string? storedPath)
    {
        var normalizedStoredPath = NormalizeStoredPath(storedPath);

        if (IsAutoSelectionPath(normalizedStoredPath))
        {
            return AutoSelection;
        }

        if (string.IsNullOrWhiteSpace(normalizedStoredPath))
        {
            return None;
        }

        var absolutePath = ResolveAbsolutePath(normalizedStoredPath, workspaceRoot);
        var direct = TryLoadDescriptor(normalizedStoredPath, absolutePath, workspaceRoot);

        if (direct is not null)
        {
            return direct;
        }

        return Discover(workspaceRoot, options)
            .FirstOrDefault(profile => StoredPathsMatch(profile.Path, normalizedStoredPath))
            ?? None;
    }

    public static bool IsAutoSelectionPath(string? path) =>
        string.Equals(NormalizeStoredPath(path), AutoSelectionPath, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeStoredPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('/', '\\');
    }

    public static bool StoredPathsMatch(string? left, string? right)
    {
        var normalizedLeft = NormalizeStoredPath(left);
        var normalizedRight = NormalizeStoredPath(right);

        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
        {
            return false;
        }

        var leftFileName = System.IO.Path.GetFileName(normalizedLeft);
        var rightFileName = System.IO.Path.GetFileName(normalizedRight);
        return string.Equals(leftFileName, rightFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static VendorDspProfileDescriptor ResolveAutoProfile(
        JarvisOptions options,
        WakeWordAssetDescriptor? wakeWordAsset,
        string beamformingProfileName,
        string hardwareDspProfileName)
    {
        var discovered = Discover(Directory.GetCurrentDirectory(), options)
            .Where(profile => !profile.IsNone && !profile.IsAutomaticSelection)
            .ToArray();

        if (discovered.Length == 0)
        {
            return None;
        }

        if (!string.IsNullOrWhiteSpace(wakeWordAsset?.PreferredVendorDspProfilePath))
        {
            var preferred = discovered.FirstOrDefault(profile =>
                StoredPathsMatch(profile.Path, wakeWordAsset.PreferredVendorDspProfilePath));

            if (preferred is not null)
            {
                return preferred;
            }
        }

        var bestMatch = discovered
            .Select(profile => new
            {
                Profile = profile,
                Score = ScoreAutoSelection(
                    profile,
                    options.SpeechRecognitionDeviceName,
                    wakeWordAsset?.Path,
                    beamformingProfileName,
                    hardwareDspProfileName)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Profile.BuildShortLabel(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (bestMatch is null || bestMatch.Score <= 0)
        {
            return None;
        }

        return bestMatch.Profile;
    }

    private static void TryAdd(
        string storedPath,
        string absolutePath,
        string workspaceRoot,
        ICollection<VendorDspProfileDescriptor> discovered,
        ISet<string> seen)
    {
        var normalizedPath = NormalizeStoredPath(storedPath);

        if (!seen.Add(normalizedPath))
        {
            return;
        }

        var descriptor = TryLoadDescriptor(normalizedPath, absolutePath, workspaceRoot);

        if (descriptor is null)
        {
            return;
        }

        discovered.Add(descriptor);
    }

    private static VendorDspProfileDescriptor? TryLoadDescriptor(string storedPath, string absolutePath, string workspaceRoot)
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
                return cached.Descriptor;
            }
        }

        try
        {
            var json = File.ReadAllText(absolutePath);
            var manifest = JsonSerializer.Deserialize<VendorDspProfileManifest>(json, JsonOptions);

            if (manifest is null)
            {
                return null;
            }

            var descriptor = new VendorDspProfileDescriptor(
                Path: storedPath,
                DefinitionDirectory: System.IO.Path.GetDirectoryName(absolutePath) ?? workspaceRoot,
                Name: string.IsNullOrWhiteSpace(manifest.Name)
                    ? System.IO.Path.GetFileNameWithoutExtension(absolutePath)
                    : manifest.Name.Trim(),
                DisplayName: string.IsNullOrWhiteSpace(manifest.DisplayName)
                    ? System.IO.Path.GetFileNameWithoutExtension(absolutePath)
                    : manifest.DisplayName.Trim(),
                Description: manifest.Description?.Trim() ?? string.Empty,
                ExecutablePath: manifest.ExecutablePath?.Trim() ?? string.Empty,
                ApplyArguments: manifest.ApplyArguments?.Trim() ?? string.Empty,
                ReleaseArguments: manifest.ReleaseArguments?.Trim() ?? string.Empty,
                WorkingDirectory: manifest.WorkingDirectory?.Trim() ?? string.Empty,
                ProfilePath: manifest.ProfilePath?.Trim() ?? string.Empty,
                ApplyTimeoutMilliseconds: Math.Clamp(manifest.ApplyTimeoutMilliseconds ?? 2500, 250, 20000),
                ReleaseTimeoutMilliseconds: Math.Clamp(manifest.ReleaseTimeoutMilliseconds ?? 1500, 250, 20000),
                UseHardwareEchoCancellation: manifest.UseHardwareEchoCancellation ?? false,
                UseHardwareNoiseSuppression: manifest.UseHardwareNoiseSuppression ?? false,
                UseHardwareBeamforming: manifest.UseHardwareBeamforming ?? false,
                RealtimeNoiseReductionMode: manifest.RealtimeNoiseReductionMode?.Trim() ?? string.Empty,
                RealtimeCaptureBufferMilliseconds: Math.Clamp(manifest.RealtimeCaptureBufferMilliseconds ?? 0, 0, 250),
                RollingCaptureBufferMilliseconds: Math.Clamp(manifest.RollingCaptureBufferMilliseconds ?? 0, 0, 500),
                VoiceActivityScale: Math.Clamp(manifest.VoiceActivityScale ?? 1.0, 0.50, 1.75),
                InputGainCapScale: Math.Clamp(manifest.InputGainCapScale ?? 1.0, 0.60, 1.60),
                TargetPeakLevelScale: Math.Clamp(manifest.TargetPeakLevelScale ?? 1.0, 0.70, 1.40),
                EchoGateScale: Math.Clamp(manifest.EchoGateScale ?? 1.0, 0.60, 1.60),
                MinimumSpeechPeakLevelOffset: Math.Clamp(manifest.MinimumSpeechPeakLevelOffset ?? 0, -0.030, 0.030),
                ServerVadThresholdScale: Math.Clamp(manifest.ServerVadThresholdScale ?? 1.0, 0.60, 1.50),
                PlaybackEchoFloorScale: Math.Clamp(manifest.PlaybackEchoFloorScale ?? 1.0, 0.60, 1.50),
                AmbientFloorScale: Math.Clamp(manifest.AmbientFloorScale ?? 1.0, 0.60, 1.50),
                BargeInThresholdScale: Math.Clamp(manifest.BargeInThresholdScale ?? 1.0, 0.60, 1.60),
                ContinuingSpeechThresholdScale: Math.Clamp(manifest.ContinuingSpeechThresholdScale ?? 1.0, 0.60, 1.60),
                PreferredBeamformingProfile: manifest.PreferredBeamformingProfile?.Trim() ?? string.Empty,
                PreferredHardwareDspProfile: manifest.PreferredHardwareDspProfile?.Trim() ?? string.Empty,
                PreferredWakeAssetPaths: NormalizeStoredPaths(
                    manifest.PreferredWakeAssetPaths,
                    absolutePath,
                    workspaceRoot),
                DeviceKeywords: NormalizeValues(manifest.DeviceKeywords),
                ExcludedDeviceKeywords: NormalizeValues(manifest.ExcludedDeviceKeywords),
                EnvironmentVariables: NormalizeEnvironmentVariables(manifest.EnvironmentVariables));

            lock (Sync)
            {
                Cache[absolutePath] = new CachedVendorDspProfile(lastWriteUtc, descriptor);
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

    private static IReadOnlyDictionary<string, string> NormalizeEnvironmentVariables(
        IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[pair.Key.Trim()] = pair.Value?.Trim() ?? string.Empty;
        }

        return normalized;
    }

    private static string[] NormalizeStoredPaths(
        IEnumerable<string>? values,
        string manifestAbsolutePath,
        string workspaceRoot)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        var manifestDirectory = System.IO.Path.GetDirectoryName(manifestAbsolutePath) ?? workspaceRoot;
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = NormalizeStoredPath(value);

            if (System.IO.Path.IsPathRooted(trimmed))
            {
                normalized.Add(ToStoredPath(workspaceRoot, System.IO.Path.GetFullPath(trimmed)));
                continue;
            }

            var manifestRelativePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(manifestDirectory, trimmed));

            if (File.Exists(manifestRelativePath))
            {
                normalized.Add(ToStoredPath(workspaceRoot, manifestRelativePath));
                continue;
            }

            normalized.Add(trimmed);
        }

        return normalized.ToArray();
    }

    private static int ScoreAutoSelection(
        VendorDspProfileDescriptor descriptor,
        string? deviceName,
        string? wakeWordAssetPath,
        string beamformingProfileName,
        string hardwareDspProfileName)
    {
        var score = descriptor.ScoreDeviceMatch(deviceName);

        if (score == int.MinValue)
        {
            return score;
        }

        if (!string.IsNullOrWhiteSpace(wakeWordAssetPath)
            && descriptor.PreferredWakeAssetPaths.Any(path => StoredPathsMatch(path, wakeWordAssetPath)))
        {
            score += 26;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.PreferredBeamformingProfile)
            && string.Equals(descriptor.PreferredBeamformingProfile, beamformingProfileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.PreferredHardwareDspProfile)
            && string.Equals(descriptor.PreferredHardwareDspProfile, hardwareDspProfileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 22;
        }

        return score;
    }

    private sealed record CachedVendorDspProfile(DateTime LastWriteUtc, VendorDspProfileDescriptor? Descriptor);

    private sealed record VendorDspProfileManifest
    {
        public string Name { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string ExecutablePath { get; init; } = string.Empty;

        public string ApplyArguments { get; init; } = string.Empty;

        public string ReleaseArguments { get; init; } = string.Empty;

        public string WorkingDirectory { get; init; } = string.Empty;

        public string ProfilePath { get; init; } = string.Empty;

        public int? ApplyTimeoutMilliseconds { get; init; }

        public int? ReleaseTimeoutMilliseconds { get; init; }

        public bool? UseHardwareEchoCancellation { get; init; }

        public bool? UseHardwareNoiseSuppression { get; init; }

        public bool? UseHardwareBeamforming { get; init; }

        public string RealtimeNoiseReductionMode { get; init; } = string.Empty;

        public int? RealtimeCaptureBufferMilliseconds { get; init; }

        public int? RollingCaptureBufferMilliseconds { get; init; }

        public double? VoiceActivityScale { get; init; }

        public double? InputGainCapScale { get; init; }

        public double? TargetPeakLevelScale { get; init; }

        public double? EchoGateScale { get; init; }

        public double? MinimumSpeechPeakLevelOffset { get; init; }

        public double? ServerVadThresholdScale { get; init; }

        public double? PlaybackEchoFloorScale { get; init; }

        public double? AmbientFloorScale { get; init; }

        public double? BargeInThresholdScale { get; init; }

        public double? ContinuingSpeechThresholdScale { get; init; }

        public string PreferredBeamformingProfile { get; init; } = string.Empty;

        public string PreferredHardwareDspProfile { get; init; } = string.Empty;

        public string[] PreferredWakeAssetPaths { get; init; } = Array.Empty<string>();

        public string[] DeviceKeywords { get; init; } = Array.Empty<string>();

        public string[] ExcludedDeviceKeywords { get; init; } = Array.Empty<string>();

        public Dictionary<string, string> EnvironmentVariables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
