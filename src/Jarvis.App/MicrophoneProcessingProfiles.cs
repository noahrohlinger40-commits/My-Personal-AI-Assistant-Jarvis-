using Jarvis.Core;

namespace Jarvis.App;

internal sealed record BeamformingProfile(
    string Name,
    string DisplayName,
    string Description,
    bool UseFarFieldNoiseReduction,
    double VoiceActivityScale,
    double InputGainCapScale,
    double TargetPeakLevel,
    double EchoGateScale,
    double MinimumSpeechPeakLevel,
    string[] AutoKeywords);

internal sealed record HardwareDspProfile(
    string Name,
    string DisplayName,
    string Description,
    bool UseHardwareEchoCancellation,
    bool UseHardwareNoiseSuppression,
    bool UseHardwareBeamforming,
    string RealtimeNoiseReductionMode,
    int RealtimeCaptureBufferMilliseconds,
    int RollingCaptureBufferMilliseconds,
    double VoiceActivityScale,
    double EchoGateScale,
    double ServerVadThresholdScale,
    double PlaybackEchoFloorScale,
    double AmbientFloorScale,
    double BargeInThresholdScale,
    double ContinuingSpeechThresholdScale,
    string[] AutoKeywords);

internal sealed record ResolvedMicrophoneProcessingProfile(
    string DeviceName,
    string RequestedBeamformingProfileName,
    string EffectiveBeamformingProfileName,
    string EffectiveBeamformingDisplayName,
    string EffectiveBeamformingDescription,
    string RequestedHardwareDspProfileName,
    string EffectiveHardwareDspProfileName,
    string EffectiveHardwareDspDisplayName,
    string EffectiveHardwareDspDescription,
    string RequestedWakeEnginePath,
    string WakeEngineDisplayName,
    string EffectiveWakeEnginePath,
    string EffectiveWakeEngineDescription,
    bool WakeEngineAutomaticallyResolved,
    string WakeWordAssetDisplayName,
    string EffectiveWakeWordAssetPath,
    bool WakeWordAssetAutomaticallyResolved,
    string RequestedVendorDspProfilePath,
    string VendorDspProfileDisplayName,
    string EffectiveVendorDspProfilePath,
    string EffectiveVendorDspDescription,
    bool VendorDspProfileAutomaticallyResolved,
    bool HardwareEchoCancellationEnabled,
    bool HardwareNoiseSuppressionEnabled,
    bool HardwareBeamformingEnabled,
    bool UseFarFieldNoiseReduction,
    string? RealtimeNoiseReductionMode,
    int RealtimeCaptureBufferMilliseconds,
    int RollingCaptureBufferMilliseconds,
    double VoiceActivityThreshold,
    double AmbientNoiseFloor,
    double InputGainCap,
    double TargetPeakLevel,
    double EchoGateScale,
    double MinimumSpeechPeakLevel,
    double ServerVadThresholdScale,
    double PlaybackEchoFloorScale,
    double AmbientFloorScale,
    double BargeInThresholdScale,
    double ContinuingSpeechThresholdScale,
    SpeechRecognitionCalibrationProfile? Calibration,
    SpeechRecognitionValidationProfile? Validation)
{
    public string BuildStatusLabel()
    {
        var calibrationLabel = Calibration is null ? "uncalibrated" : "calibrated";
        var validationLabel = Validation is null
            ? "unvalidated"
            : Validation.RecommendedConversationMode;
        var parts = new List<string>
        {
            EffectiveBeamformingDisplayName,
            EffectiveHardwareDspDisplayName
        };

        if (!string.IsNullOrWhiteSpace(EffectiveVendorDspProfilePath))
        {
            parts.Add(VendorDspProfileDisplayName);
        }

        parts.Add(WakeEngineDisplayName);
        parts.Add(calibrationLabel);
        parts.Add(validationLabel);
        return string.Join(", ", parts);
    }

    public string BuildUiSummary()
    {
        var parts = new List<string>();

        if (Calibration is null)
        {
            parts.Add("No calibration");
        }
        else
        {
            parts.Add($"Ambient {Calibration.AmbientRmsLevel:P1}");
        }

        if (Validation is not null)
        {
            parts.Add($"wake {Validation.WakeWordReadinessScore:P0}");
            parts.Add($"duplex {Validation.FullDuplexReadinessScore:P0}");
        }

        parts.Add(EffectiveBeamformingDisplayName);
        parts.Add(EffectiveHardwareDspDisplayName);

        if (!string.IsNullOrWhiteSpace(EffectiveVendorDspProfilePath))
        {
            parts.Add(VendorDspProfileDisplayName);
        }

        parts.Add(WakeEngineDisplayName);

        if (!string.IsNullOrWhiteSpace(WakeWordAssetDisplayName))
        {
            parts.Add(WakeWordAssetDisplayName);
        }

        return string.Join(" | ", parts);
    }
}

internal static class BeamformingProfileCatalog
{
    public static IReadOnlyList<BeamformingProfile> All { get; } =
    [
        new(
            "auto",
            "Auto",
            "Choose a sensible profile from the microphone name, wake asset, and saved calibration.",
            UseFarFieldNoiseReduction: false,
            VoiceActivityScale: 1.0,
            InputGainCapScale: 1.0,
            TargetPeakLevel: 0.36,
            EchoGateScale: 1.0,
            MinimumSpeechPeakLevel: 0.055,
            AutoKeywords: Array.Empty<string>()),
        new(
            "off",
            "Off",
            "Disable beamforming hints and keep the speech path in a plain near-field mode.",
            UseFarFieldNoiseReduction: false,
            VoiceActivityScale: 1.0,
            InputGainCapScale: 1.0,
            TargetPeakLevel: 0.36,
            EchoGateScale: 1.0,
            MinimumSpeechPeakLevel: 0.055,
            AutoKeywords: Array.Empty<string>()),
        new(
            "desktop",
            "Desktop",
            "Tune for a nearby desk microphone or monitor-mounted mic.",
            UseFarFieldNoiseReduction: false,
            VoiceActivityScale: 0.92,
            InputGainCapScale: 1.0,
            TargetPeakLevel: 0.34,
            EchoGateScale: 0.95,
            MinimumSpeechPeakLevel: 0.050,
            AutoKeywords: ["usb", "desktop", "monitor", "webcam", "yeti", "seiren", "snowball"]),
        new(
            "headset",
            "Headset",
            "Tune for close-talk headsets and boom microphones.",
            UseFarFieldNoiseReduction: false,
            VoiceActivityScale: 0.78,
            InputGainCapScale: 0.85,
            TargetPeakLevel: 0.28,
            EchoGateScale: 0.82,
            MinimumSpeechPeakLevel: 0.040,
            AutoKeywords: ["headset", "headphone", "boom", "earbud", "airpods", "hands-free"]),
        new(
            "conference",
            "Conference",
            "Tune for room microphones and devices with built-in array processing.",
            UseFarFieldNoiseReduction: true,
            VoiceActivityScale: 1.15,
            InputGainCapScale: 1.10,
            TargetPeakLevel: 0.38,
            EchoGateScale: 1.18,
            MinimumSpeechPeakLevel: 0.065,
            AutoKeywords: ["conference", "speakerphone", "poly", "logi dock", "surface", "studio", "dock"]),
        new(
            "far-field-array",
            "Far-Field Array",
            "Tune for microphone arrays intended to hear speech across the room.",
            UseFarFieldNoiseReduction: true,
            VoiceActivityScale: 1.35,
            InputGainCapScale: 1.20,
            TargetPeakLevel: 0.42,
            EchoGateScale: 1.30,
            MinimumSpeechPeakLevel: 0.075,
            AutoKeywords: ["array", "beam", "far field", "far-field", "respeaker", "smart speaker", "studio mic"])
    ];

    public static BeamformingProfile Resolve(string? requestedName, string? deviceName)
    {
        var normalized = Normalize(requestedName);

        if (normalized == "auto")
        {
            return ResolveAuto(deviceName);
        }

        return All.FirstOrDefault(profile => Normalize(profile.Name) == normalized)
            ?? All.First(profile => profile.Name == "desktop");
    }

    public static BeamformingProfile ResolveAuto(string? deviceName)
    {
        var name = deviceName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return All.First(profile => profile.Name == "desktop");
        }

        foreach (var profile in All.Where(profile => profile.Name is not "auto" and not "off"))
        {
            if (profile.AutoKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return profile;
            }
        }

        return All.First(profile => profile.Name == "desktop");
    }

    private static string Normalize(string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? "auto"
            : name.Trim().ToLowerInvariant();
    }
}

internal static class HardwareDspProfileCatalog
{
    public static IReadOnlyList<HardwareDspProfile> All { get; } =
    [
        new(
            "auto",
            "Auto",
            "Choose a hardware DSP posture from the microphone type and beamforming profile.",
            UseHardwareEchoCancellation: false,
            UseHardwareNoiseSuppression: false,
            UseHardwareBeamforming: false,
            RealtimeNoiseReductionMode: string.Empty,
            RealtimeCaptureBufferMilliseconds: 20,
            RollingCaptureBufferMilliseconds: 60,
            VoiceActivityScale: 1.0,
            EchoGateScale: 1.0,
            ServerVadThresholdScale: 1.0,
            PlaybackEchoFloorScale: 1.0,
            AmbientFloorScale: 1.0,
            BargeInThresholdScale: 1.0,
            ContinuingSpeechThresholdScale: 1.0,
            AutoKeywords: Array.Empty<string>()),
        new(
            "off",
            "Software Only",
            "Disable hardware-DSP assumptions and keep the app in software-only mode.",
            UseHardwareEchoCancellation: false,
            UseHardwareNoiseSuppression: false,
            UseHardwareBeamforming: false,
            RealtimeNoiseReductionMode: string.Empty,
            RealtimeCaptureBufferMilliseconds: 20,
            RollingCaptureBufferMilliseconds: 60,
            VoiceActivityScale: 1.0,
            EchoGateScale: 1.0,
            ServerVadThresholdScale: 1.0,
            PlaybackEchoFloorScale: 1.0,
            AmbientFloorScale: 1.0,
            BargeInThresholdScale: 1.0,
            ContinuingSpeechThresholdScale: 1.0,
            AutoKeywords: Array.Empty<string>()),
        new(
            "system-aec",
            "System AEC",
            "Assume Windows or the device stack can provide acoustic echo cancellation and communication noise suppression.",
            UseHardwareEchoCancellation: true,
            UseHardwareNoiseSuppression: true,
            UseHardwareBeamforming: false,
            RealtimeNoiseReductionMode: "near_field",
            RealtimeCaptureBufferMilliseconds: 18,
            RollingCaptureBufferMilliseconds: 48,
            VoiceActivityScale: 0.94,
            EchoGateScale: 0.82,
            ServerVadThresholdScale: 0.94,
            PlaybackEchoFloorScale: 0.82,
            AmbientFloorScale: 0.92,
            BargeInThresholdScale: 0.88,
            ContinuingSpeechThresholdScale: 0.84,
            AutoKeywords: ["headset", "hands-free", "bluetooth", "communications", "earbud"]),
        new(
            "voice-processing",
            "Voice Processing",
            "Assume the microphone exposes a near-field voice-processing chain for speech capture.",
            UseHardwareEchoCancellation: true,
            UseHardwareNoiseSuppression: true,
            UseHardwareBeamforming: false,
            RealtimeNoiseReductionMode: "near_field",
            RealtimeCaptureBufferMilliseconds: 18,
            RollingCaptureBufferMilliseconds: 46,
            VoiceActivityScale: 0.92,
            EchoGateScale: 0.78,
            ServerVadThresholdScale: 0.92,
            PlaybackEchoFloorScale: 0.78,
            AmbientFloorScale: 0.90,
            BargeInThresholdScale: 0.82,
            ContinuingSpeechThresholdScale: 0.78,
            AutoKeywords: ["webcam", "usb", "microphone", "snowball", "yeti", "seiren"]),
        new(
            "conference-array",
            "Conference Array",
            "Assume the device stack exposes hardware beamforming with room-scale echo control.",
            UseHardwareEchoCancellation: true,
            UseHardwareNoiseSuppression: true,
            UseHardwareBeamforming: true,
            RealtimeNoiseReductionMode: "far_field",
            RealtimeCaptureBufferMilliseconds: 16,
            RollingCaptureBufferMilliseconds: 42,
            VoiceActivityScale: 1.08,
            EchoGateScale: 0.74,
            ServerVadThresholdScale: 0.98,
            PlaybackEchoFloorScale: 0.70,
            AmbientFloorScale: 0.88,
            BargeInThresholdScale: 0.74,
            ContinuingSpeechThresholdScale: 0.70,
            AutoKeywords: ["conference", "speakerphone", "surface", "dock", "poly", "studio"]),
        new(
            "vendor-beamforming",
            "Vendor Beamforming",
            "Assume the microphone stack exposes vendor-managed far-field beamforming and noise cleanup.",
            UseHardwareEchoCancellation: false,
            UseHardwareNoiseSuppression: true,
            UseHardwareBeamforming: true,
            RealtimeNoiseReductionMode: "far_field",
            RealtimeCaptureBufferMilliseconds: 16,
            RollingCaptureBufferMilliseconds: 44,
            VoiceActivityScale: 1.02,
            EchoGateScale: 0.88,
            ServerVadThresholdScale: 1.00,
            PlaybackEchoFloorScale: 0.80,
            AmbientFloorScale: 0.90,
            BargeInThresholdScale: 0.80,
            ContinuingSpeechThresholdScale: 0.76,
            AutoKeywords: ["array", "beam", "far field", "far-field", "respeaker", "smart speaker"])
    ];

    public static HardwareDspProfile Resolve(
        string? requestedName,
        string? deviceName,
        BeamformingProfile beamformingProfile)
    {
        var normalized = Normalize(requestedName);

        if (normalized == "auto")
        {
            return ResolveAuto(deviceName, beamformingProfile);
        }

        return All.FirstOrDefault(profile => Normalize(profile.Name) == normalized)
            ?? ResolveAuto(deviceName, beamformingProfile);
    }

    public static HardwareDspProfile ResolveAuto(string? deviceName, BeamformingProfile beamformingProfile)
    {
        var name = deviceName?.Trim() ?? string.Empty;

        if (beamformingProfile.Name == "far-field-array")
        {
            return All.First(profile => profile.Name == "vendor-beamforming");
        }

        if (beamformingProfile.Name == "conference")
        {
            return All.First(profile => profile.Name == "conference-array");
        }

        foreach (var profile in All.Where(profile => profile.Name is not "auto" and not "off"))
        {
            if (profile.AutoKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return profile;
            }
        }

        return All.First(profile => profile.Name == "off");
    }

    private static string Normalize(string? name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? "auto"
            : name.Trim().ToLowerInvariant();
    }
}

internal static class MicrophoneProcessingProfileResolver
{
    public static ResolvedMicrophoneProcessingProfile Resolve(JarvisOptions options)
    {
        var selectedWakeWordAsset = WakeWordAssetCatalog.ResolveSelectionDescriptor(options);
        var wakeWordAsset = WakeWordAssetCatalog.ResolveConfiguredAsset(options);
        var initialRequestedBeamformingProfileName = ResolveRequestedBeamformingProfile(options, calibration: null, wakeWordAsset);
        var initialBeamformingProfile = BeamformingProfileCatalog.Resolve(initialRequestedBeamformingProfileName, options.SpeechRecognitionDeviceName);
        var initialRequestedHardwareDspProfileName = ResolveRequestedHardwareDspProfile(options, validation: null, wakeWordAsset);
        var initialHardwareDspProfile = HardwareDspProfileCatalog.Resolve(
            initialRequestedHardwareDspProfileName,
            options.SpeechRecognitionDeviceName,
            initialBeamformingProfile);
        var initialRequestedWakeEnginePath = ResolveRequestedWakeEnginePath(options, validation: null, wakeWordAsset);
        var selectedWakeEngine = WakeWordEngineCatalog.ResolveSelectionDescriptor(options);
        var wakeEngine = ResolveWakeEngine(
            options,
            wakeWordAsset,
            initialRequestedWakeEnginePath,
            initialBeamformingProfile.Name,
            initialHardwareDspProfile.Name);
        var initialRequestedVendorDspProfilePath = ResolveRequestedVendorDspProfilePath(options, validation: null, wakeWordAsset);
        var selectedVendorDspProfile = VendorDspProfileCatalog.ResolveSelectionDescriptor(options);
        var vendorDspProfile = ResolveVendorDspProfile(
            options,
            wakeWordAsset,
            initialRequestedVendorDspProfilePath,
            initialBeamformingProfile.Name,
            initialHardwareDspProfile.Name);
        var calibration = ResolveCalibration(
            options,
            initialBeamformingProfile.Name,
            initialHardwareDspProfile.Name,
            vendorDspProfile);
        var requestedBeamformingProfileName = ResolveRequestedBeamformingProfile(options, calibration, wakeWordAsset);
        var beamformingProfile = BeamformingProfileCatalog.Resolve(requestedBeamformingProfileName, options.SpeechRecognitionDeviceName);
        var tentativeHardwareDspProfileName = ResolveRequestedHardwareDspProfile(options, validation: null, wakeWordAsset);
        var hardwareDspProfile = HardwareDspProfileCatalog.Resolve(
            tentativeHardwareDspProfileName,
            options.SpeechRecognitionDeviceName,
            beamformingProfile);
        var tentativeWakeEnginePath = ResolveRequestedWakeEnginePath(options, validation: null, wakeWordAsset);
        wakeEngine = ResolveWakeEngine(
            options,
            wakeWordAsset,
            tentativeWakeEnginePath,
            beamformingProfile.Name,
            hardwareDspProfile.Name);
        var tentativeVendorDspProfilePath = ResolveRequestedVendorDspProfilePath(options, validation: null, wakeWordAsset);
        vendorDspProfile = ResolveVendorDspProfile(
            options,
            wakeWordAsset,
            tentativeVendorDspProfilePath,
            beamformingProfile.Name,
            hardwareDspProfile.Name);
        var validation = ResolveValidation(
            options,
            wakeWordAsset,
            wakeEngine,
            beamformingProfile.Name,
            hardwareDspProfile.Name,
            vendorDspProfile);
        var requestedHardwareDspProfileName = ResolveRequestedHardwareDspProfile(options, validation, wakeWordAsset);
        hardwareDspProfile = HardwareDspProfileCatalog.Resolve(
            requestedHardwareDspProfileName,
            options.SpeechRecognitionDeviceName,
            beamformingProfile);
        var requestedWakeEnginePath = ResolveRequestedWakeEnginePath(options, validation, wakeWordAsset);
        wakeEngine = ResolveWakeEngine(
            options,
            wakeWordAsset,
            requestedWakeEnginePath,
            beamformingProfile.Name,
            hardwareDspProfile.Name);
        var requestedVendorDspProfilePath = ResolveRequestedVendorDspProfilePath(options, validation, wakeWordAsset);
        vendorDspProfile = ResolveVendorDspProfile(
            options,
            wakeWordAsset,
            requestedVendorDspProfilePath,
            beamformingProfile.Name,
            hardwareDspProfile.Name);
        validation = ResolveValidation(
            options,
            wakeWordAsset,
            wakeEngine,
            beamformingProfile.Name,
            hardwareDspProfile.Name,
            vendorDspProfile);

        var baseThreshold = calibration?.RecommendedVoiceActivityThreshold > 0
            ? calibration.RecommendedVoiceActivityThreshold
            : options.SpeechRecognitionVoiceActivityThreshold;
        var wakeSensitivityScale = validation is not null && validation.WakeWordReadinessScore < 0.55
            ? 0.94
            : 1.0;
        var voiceActivityThreshold = Math.Clamp(
            baseThreshold
            * beamformingProfile.VoiceActivityScale
            * hardwareDspProfile.VoiceActivityScale
            * vendorDspProfile.VoiceActivityScale
            * wakeSensitivityScale,
            0.0025,
            0.040);
        var ambientNoiseFloor = Math.Clamp(
            Math.Max(calibration?.AmbientRmsLevel ?? (voiceActivityThreshold * 0.30), voiceActivityThreshold * 0.22),
            0.0015,
            0.10);
        var inputGainCap = Math.Clamp(
            (calibration?.RecommendedInputGainCap ?? 4.0)
            * beamformingProfile.InputGainCapScale
            * vendorDspProfile.InputGainCapScale,
            1.0,
            6.0);
        var targetPeak = Math.Clamp(
            (calibration is null
                ? beamformingProfile.TargetPeakLevel
                : (calibration.RecommendedTargetPeakLevel + beamformingProfile.TargetPeakLevel) / 2.0)
            * vendorDspProfile.TargetPeakLevelScale,
            0.24,
            0.50);
        var validationEchoScale = validation is null
            ? 1.0
            : Math.Clamp(1.18 - (validation.FullDuplexReadinessScore * 0.18), 0.96, 1.18);
        var validationSpeechPeakOffset = validation is null
            ? 0
            : Math.Clamp((0.70 - validation.FullDuplexReadinessScore) * 0.028, 0, 0.018);
        var useFarFieldNoiseReduction = beamformingProfile.UseFarFieldNoiseReduction
            || string.Equals(hardwareDspProfile.RealtimeNoiseReductionMode, "far_field", StringComparison.OrdinalIgnoreCase)
            || string.Equals(vendorDspProfile.RealtimeNoiseReductionMode, "far_field", StringComparison.OrdinalIgnoreCase);
        var hardwareEchoCancellationEnabled = hardwareDspProfile.UseHardwareEchoCancellation
            || vendorDspProfile.UseHardwareEchoCancellation;
        var hardwareNoiseSuppressionEnabled = hardwareDspProfile.UseHardwareNoiseSuppression
            || vendorDspProfile.UseHardwareNoiseSuppression;
        var hardwareBeamformingEnabled = options.SpeechRecognitionBeamformingEnabled
            || beamformingProfile.UseFarFieldNoiseReduction
            || hardwareDspProfile.UseHardwareBeamforming
            || vendorDspProfile.UseHardwareBeamforming;
        var realtimeNoiseReductionMode = ResolveRealtimeNoiseReductionMode(
            options,
            hardwareDspProfile,
            vendorDspProfile,
            useFarFieldNoiseReduction);

        return new ResolvedMicrophoneProcessingProfile(
            DeviceName: options.SpeechRecognitionDeviceName?.Trim() ?? string.Empty,
            RequestedBeamformingProfileName: requestedBeamformingProfileName,
            EffectiveBeamformingProfileName: beamformingProfile.Name,
            EffectiveBeamformingDisplayName: beamformingProfile.DisplayName,
            EffectiveBeamformingDescription: beamformingProfile.Description,
            RequestedHardwareDspProfileName: requestedHardwareDspProfileName,
            EffectiveHardwareDspProfileName: hardwareDspProfile.Name,
            EffectiveHardwareDspDisplayName: hardwareDspProfile.DisplayName,
            EffectiveHardwareDspDescription: hardwareDspProfile.Description,
            RequestedWakeEnginePath: requestedWakeEnginePath,
            WakeEngineDisplayName: BuildWakeEngineDisplayName(selectedWakeEngine, wakeEngine),
            EffectiveWakeEnginePath: wakeEngine.Path,
            EffectiveWakeEngineDescription: wakeEngine.Description,
            WakeEngineAutomaticallyResolved: selectedWakeEngine.IsAutomaticSelection,
            WakeWordAssetDisplayName: BuildWakeWordAssetDisplayName(selectedWakeWordAsset, wakeWordAsset),
            EffectiveWakeWordAssetPath: wakeWordAsset?.Path ?? string.Empty,
            WakeWordAssetAutomaticallyResolved: selectedWakeWordAsset.IsAutomaticSelection,
            RequestedVendorDspProfilePath: requestedVendorDspProfilePath,
            VendorDspProfileDisplayName: BuildVendorDspProfileDisplayName(selectedVendorDspProfile, vendorDspProfile),
            EffectiveVendorDspProfilePath: vendorDspProfile.Path,
            EffectiveVendorDspDescription: vendorDspProfile.Description,
            VendorDspProfileAutomaticallyResolved: selectedVendorDspProfile.IsAutomaticSelection,
            HardwareEchoCancellationEnabled: hardwareEchoCancellationEnabled,
            HardwareNoiseSuppressionEnabled: hardwareNoiseSuppressionEnabled,
            HardwareBeamformingEnabled: hardwareBeamformingEnabled,
            UseFarFieldNoiseReduction: useFarFieldNoiseReduction,
            RealtimeNoiseReductionMode: realtimeNoiseReductionMode,
            RealtimeCaptureBufferMilliseconds: vendorDspProfile.RealtimeCaptureBufferMilliseconds > 0
                ? vendorDspProfile.RealtimeCaptureBufferMilliseconds
                : hardwareDspProfile.RealtimeCaptureBufferMilliseconds,
            RollingCaptureBufferMilliseconds: vendorDspProfile.RollingCaptureBufferMilliseconds > 0
                ? vendorDspProfile.RollingCaptureBufferMilliseconds
                : hardwareDspProfile.RollingCaptureBufferMilliseconds,
            VoiceActivityThreshold: voiceActivityThreshold,
            AmbientNoiseFloor: ambientNoiseFloor,
            InputGainCap: inputGainCap,
            TargetPeakLevel: targetPeak,
            EchoGateScale: Math.Clamp(
                beamformingProfile.EchoGateScale
                * hardwareDspProfile.EchoGateScale
                * vendorDspProfile.EchoGateScale
                * validationEchoScale,
                0.70,
                1.80),
            MinimumSpeechPeakLevel: Math.Clamp(
                beamformingProfile.MinimumSpeechPeakLevel
                + vendorDspProfile.MinimumSpeechPeakLevelOffset
                + validationSpeechPeakOffset,
                0.030,
                0.12),
            ServerVadThresholdScale: Math.Clamp(
                hardwareDspProfile.ServerVadThresholdScale * vendorDspProfile.ServerVadThresholdScale,
                0.60,
                1.80),
            PlaybackEchoFloorScale: Math.Clamp(
                hardwareDspProfile.PlaybackEchoFloorScale * vendorDspProfile.PlaybackEchoFloorScale,
                0.55,
                1.80),
            AmbientFloorScale: Math.Clamp(
                hardwareDspProfile.AmbientFloorScale * vendorDspProfile.AmbientFloorScale,
                0.55,
                1.80),
            BargeInThresholdScale: Math.Clamp(
                hardwareDspProfile.BargeInThresholdScale * vendorDspProfile.BargeInThresholdScale,
                0.55,
                1.90),
            ContinuingSpeechThresholdScale: Math.Clamp(
                hardwareDspProfile.ContinuingSpeechThresholdScale * vendorDspProfile.ContinuingSpeechThresholdScale,
                0.55,
                1.90),
            Calibration: calibration,
            Validation: validation);
    }

    private static string? ResolveRealtimeNoiseReductionMode(
        JarvisOptions options,
        HardwareDspProfile hardwareDspProfile,
        VendorDspProfileDescriptor vendorDspProfile,
        bool useFarFieldNoiseReduction)
    {
        if (!options.SpeechRecognitionNoiseSuppressionEnabled
            && !options.SpeechRecognitionEchoCancellationEnabled
            && !hardwareDspProfile.UseHardwareBeamforming
            && !hardwareDspProfile.UseHardwareEchoCancellation
            && !hardwareDspProfile.UseHardwareNoiseSuppression
            && !vendorDspProfile.UseHardwareBeamforming
            && !vendorDspProfile.UseHardwareEchoCancellation
            && !vendorDspProfile.UseHardwareNoiseSuppression
            && !useFarFieldNoiseReduction)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(vendorDspProfile.RealtimeNoiseReductionMode))
        {
            return vendorDspProfile.RealtimeNoiseReductionMode;
        }

        if (!string.IsNullOrWhiteSpace(hardwareDspProfile.RealtimeNoiseReductionMode))
        {
            return hardwareDspProfile.RealtimeNoiseReductionMode;
        }

        return useFarFieldNoiseReduction ? "far_field" : "near_field";
    }

    private static SpeechRecognitionCalibrationProfile? ResolveCalibration(
        JarvisOptions options,
        string beamformingProfileName,
        string hardwareDspProfileName,
        VendorDspProfileDescriptor vendorDspProfile)
    {
        if (options.SpeechRecognitionCalibrationProfiles.Length == 0
            || string.IsNullOrWhiteSpace(options.SpeechRecognitionDeviceName))
        {
            return null;
        }

        var normalizedBeamformingProfileName = NormalizeProfileName(beamformingProfileName);
        var normalizedHardwareDspProfileName = NormalizeProfileName(hardwareDspProfileName);
        var normalizedVendorDspProfilePath = VendorDspProfileCatalog.NormalizeStoredPath(vendorDspProfile.Path);
        var normalizedVendorDspProfileName = vendorDspProfile.IsNone
            ? "No Vendor SDK"
            : vendorDspProfile.BuildShortLabel();

        return options.SpeechRecognitionCalibrationProfiles
            .Where(profile =>
                string.Equals(
                    profile.DeviceName?.Trim(),
                    options.SpeechRecognitionDeviceName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            .Select(profile => new
            {
                Profile = profile,
                Score = ScoreCalibrationProfile(
                    profile,
                    normalizedBeamformingProfileName,
                    normalizedHardwareDspProfileName,
                    normalizedVendorDspProfilePath,
                    normalizedVendorDspProfileName)
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Profile.CapturedUtc)
            .Select(item => item.Profile)
            .FirstOrDefault();
    }

    private static SpeechRecognitionValidationProfile? ResolveValidation(
        JarvisOptions options,
        WakeWordAssetDescriptor? wakeWordAsset,
        WakeWordEngineDescriptor wakeEngine,
        string beamformingProfileName,
        string hardwareDspProfileName,
        VendorDspProfileDescriptor vendorDspProfile)
    {
        if (options.SpeechRecognitionValidationProfiles.Length == 0
            || string.IsNullOrWhiteSpace(options.SpeechRecognitionDeviceName))
        {
            return null;
        }

        var normalizedWakeWordAssetPath = WakeWordAssetCatalog.NormalizeStoredPath(wakeWordAsset?.Path);
        var normalizedWakeWordAssetName = wakeWordAsset?.BuildShortLabel() ?? "Recognizer Default";
        var normalizedWakeEnginePath = WakeWordEngineCatalog.NormalizeStoredPath(wakeEngine.Path);
        var normalizedWakeEngineName = wakeEngine.BuildShortLabel();
        var normalizedBeamformingProfileName = NormalizeProfileName(beamformingProfileName);
        var normalizedHardwareDspProfileName = NormalizeProfileName(hardwareDspProfileName);
        var normalizedVendorDspProfilePath = VendorDspProfileCatalog.NormalizeStoredPath(vendorDspProfile.Path);
        var normalizedVendorDspProfileName = vendorDspProfile.IsNone
            ? "No Vendor SDK"
            : vendorDspProfile.BuildShortLabel();

        return options.SpeechRecognitionValidationProfiles
            .Where(profile =>
                string.Equals(
                    profile.DeviceName?.Trim(),
                    options.SpeechRecognitionDeviceName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            .Select(profile => new
            {
                Profile = profile,
                Score = ScoreValidationProfile(
                    profile,
                    normalizedWakeWordAssetPath,
                    normalizedWakeWordAssetName,
                    normalizedWakeEnginePath,
                    normalizedWakeEngineName,
                    normalizedBeamformingProfileName,
                    normalizedHardwareDspProfileName,
                    normalizedVendorDspProfilePath,
                    normalizedVendorDspProfileName)
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Profile.ValidatedUtc)
            .Select(item => item.Profile)
            .FirstOrDefault();
    }

    private static string ResolveRequestedBeamformingProfile(
        JarvisOptions options,
        SpeechRecognitionCalibrationProfile? calibration,
        WakeWordAssetDescriptor? wakeWordAsset)
    {
        if (!string.IsNullOrWhiteSpace(options.SpeechRecognitionBeamformingProfile)
            && !string.Equals(options.SpeechRecognitionBeamformingProfile, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return options.SpeechRecognitionBeamformingProfile.Trim();
        }

        if (!string.IsNullOrWhiteSpace(calibration?.PreferredBeamformingProfile))
        {
            return calibration.PreferredBeamformingProfile.Trim();
        }

        if (!string.IsNullOrWhiteSpace(wakeWordAsset?.PreferredBeamformingProfile))
        {
            return wakeWordAsset.PreferredBeamformingProfile.Trim();
        }

        return string.IsNullOrWhiteSpace(options.SpeechRecognitionBeamformingProfile)
            ? "auto"
            : options.SpeechRecognitionBeamformingProfile.Trim();
    }

    private static string ResolveRequestedHardwareDspProfile(
        JarvisOptions options,
        SpeechRecognitionValidationProfile? validation,
        WakeWordAssetDescriptor? wakeWordAsset)
    {
        if (!string.IsNullOrWhiteSpace(options.SpeechRecognitionHardwareDspProfile)
            && !string.Equals(options.SpeechRecognitionHardwareDspProfile, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return options.SpeechRecognitionHardwareDspProfile.Trim();
        }

        if (!string.IsNullOrWhiteSpace(validation?.HardwareDspProfileName)
            && !string.Equals(validation.HardwareDspProfileName, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return validation.HardwareDspProfileName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(wakeWordAsset?.PreferredHardwareDspProfile))
        {
            return wakeWordAsset.PreferredHardwareDspProfile.Trim();
        }

        return string.IsNullOrWhiteSpace(options.SpeechRecognitionHardwareDspProfile)
            ? "auto"
            : options.SpeechRecognitionHardwareDspProfile.Trim();
    }

    private static string ResolveRequestedWakeEnginePath(
        JarvisOptions options,
        SpeechRecognitionValidationProfile? validation,
        WakeWordAssetDescriptor? wakeWordAsset)
    {
        if (!string.IsNullOrWhiteSpace(options.SpeechRecognitionWakeEnginePath)
            && !WakeWordEngineCatalog.IsAutoSelectionPath(options.SpeechRecognitionWakeEnginePath))
        {
            return WakeWordEngineCatalog.NormalizeStoredPath(options.SpeechRecognitionWakeEnginePath);
        }

        if (validation is not null
            && (!string.IsNullOrWhiteSpace(validation.WakeEnginePath)
                || !string.IsNullOrWhiteSpace(validation.WakeEngineName)))
        {
            return WakeWordEngineCatalog.NormalizeStoredPath(validation.WakeEnginePath);
        }

        if (!string.IsNullOrWhiteSpace(wakeWordAsset?.PreferredWakeEnginePath))
        {
            return WakeWordEngineCatalog.NormalizeStoredPath(wakeWordAsset.PreferredWakeEnginePath);
        }

        return string.IsNullOrWhiteSpace(options.SpeechRecognitionWakeEnginePath)
            ? WakeWordEngineCatalog.AutoSelectionPath
            : WakeWordEngineCatalog.NormalizeStoredPath(options.SpeechRecognitionWakeEnginePath);
    }

    private static string ResolveRequestedVendorDspProfilePath(
        JarvisOptions options,
        SpeechRecognitionValidationProfile? validation,
        WakeWordAssetDescriptor? wakeWordAsset)
    {
        if (!string.IsNullOrWhiteSpace(options.SpeechRecognitionVendorDspProfilePath)
            && !VendorDspProfileCatalog.IsAutoSelectionPath(options.SpeechRecognitionVendorDspProfilePath))
        {
            return VendorDspProfileCatalog.NormalizeStoredPath(options.SpeechRecognitionVendorDspProfilePath);
        }

        if (validation is not null
            && (!string.IsNullOrWhiteSpace(validation.VendorDspProfilePath)
                || !string.IsNullOrWhiteSpace(validation.VendorDspProfileName)))
        {
            return VendorDspProfileCatalog.NormalizeStoredPath(validation.VendorDspProfilePath);
        }

        if (!string.IsNullOrWhiteSpace(wakeWordAsset?.PreferredVendorDspProfilePath))
        {
            return VendorDspProfileCatalog.NormalizeStoredPath(wakeWordAsset.PreferredVendorDspProfilePath);
        }

        return string.IsNullOrWhiteSpace(options.SpeechRecognitionVendorDspProfilePath)
            ? VendorDspProfileCatalog.AutoSelectionPath
            : VendorDspProfileCatalog.NormalizeStoredPath(options.SpeechRecognitionVendorDspProfilePath);
    }

    private static WakeWordEngineDescriptor ResolveWakeEngine(
        JarvisOptions options,
        WakeWordAssetDescriptor? wakeWordAsset,
        string requestedWakeEnginePath,
        string beamformingProfileName,
        string hardwareDspProfileName)
    {
        if (WakeWordEngineCatalog.IsAutoSelectionPath(requestedWakeEnginePath))
        {
            return WakeWordEngineCatalog.ResolveConfiguredEngine(
                options with
                {
                    SpeechRecognitionWakeEnginePath = WakeWordEngineCatalog.AutoSelectionPath
                },
                wakeWordAsset,
                beamformingProfileName,
                hardwareDspProfileName);
        }

        return WakeWordEngineCatalog.ResolveDescriptorByPath(
            Directory.GetCurrentDirectory(),
            options,
            requestedWakeEnginePath);
    }

    private static VendorDspProfileDescriptor ResolveVendorDspProfile(
        JarvisOptions options,
        WakeWordAssetDescriptor? wakeWordAsset,
        string requestedVendorDspProfilePath,
        string beamformingProfileName,
        string hardwareDspProfileName)
    {
        if (VendorDspProfileCatalog.IsAutoSelectionPath(requestedVendorDspProfilePath))
        {
            return VendorDspProfileCatalog.ResolveConfiguredProfile(
                options with
                {
                    SpeechRecognitionVendorDspProfilePath = VendorDspProfileCatalog.AutoSelectionPath
                },
                wakeWordAsset,
                beamformingProfileName,
                hardwareDspProfileName);
        }

        return VendorDspProfileCatalog.ResolveDescriptorByPath(
            Directory.GetCurrentDirectory(),
            options,
            requestedVendorDspProfilePath);
    }

    private static string BuildWakeWordAssetDisplayName(
        WakeWordAssetDescriptor selectionDescriptor,
        WakeWordAssetDescriptor? effectiveDescriptor)
    {
        if (selectionDescriptor.IsAutomaticSelection)
        {
            return effectiveDescriptor is null
                ? "Auto -> Recognizer Default"
                : $"Auto -> {effectiveDescriptor.BuildShortLabel()}";
        }

        return effectiveDescriptor?.BuildShortLabel() ?? "Recognizer Default";
    }

    private static string BuildWakeEngineDisplayName(
        WakeWordEngineDescriptor selectionDescriptor,
        WakeWordEngineDescriptor effectiveDescriptor)
    {
        if (selectionDescriptor.IsAutomaticSelection)
        {
            return $"Auto -> {effectiveDescriptor.BuildShortLabel()}";
        }

        return effectiveDescriptor.BuildShortLabel();
    }

    private static string BuildVendorDspProfileDisplayName(
        VendorDspProfileDescriptor selectionDescriptor,
        VendorDspProfileDescriptor effectiveDescriptor)
    {
        if (selectionDescriptor.IsAutomaticSelection)
        {
            return $"Auto -> {effectiveDescriptor.BuildShortLabel()}";
        }

        return effectiveDescriptor.BuildShortLabel();
    }

    private static int ScoreCalibrationProfile(
        SpeechRecognitionCalibrationProfile profile,
        string normalizedBeamformingProfileName,
        string normalizedHardwareDspProfileName,
        string normalizedVendorDspProfilePath,
        string normalizedVendorDspProfileName)
    {
        var score = 0;
        var profileBeamformingProfileName = NormalizeProfileName(profile.PreferredBeamformingProfile);
        var profileHardwareDspProfileName = NormalizeProfileName(profile.HardwareDspProfileName);
        var profileVendorDspProfilePath = VendorDspProfileCatalog.NormalizeStoredPath(profile.VendorDspProfilePath);
        var profileVendorDspProfileName = string.IsNullOrWhiteSpace(profile.VendorDspProfileName)
            ? "No Vendor SDK"
            : profile.VendorDspProfileName.Trim();

        if (string.Equals(profileBeamformingProfileName, normalizedBeamformingProfileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 24;
        }
        else if (profileBeamformingProfileName == "auto" || normalizedBeamformingProfileName == "auto")
        {
            score += 10;
        }

        if (string.Equals(profileHardwareDspProfileName, normalizedHardwareDspProfileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 18;
        }
        else if (profileHardwareDspProfileName == "auto" || normalizedHardwareDspProfileName == "auto")
        {
            score += 8;
        }

        if (string.IsNullOrWhiteSpace(normalizedVendorDspProfilePath))
        {
            if (string.IsNullOrWhiteSpace(profileVendorDspProfilePath)
                && string.Equals(profileVendorDspProfileName, "No Vendor SDK", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
        }
        else if (VendorDspProfileCatalog.StoredPathsMatch(profileVendorDspProfilePath, normalizedVendorDspProfilePath))
        {
            score += 24;
        }
        else if (string.Equals(profileVendorDspProfileName, normalizedVendorDspProfileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 14;
        }

        return score;
    }

    private static int ScoreValidationProfile(
        SpeechRecognitionValidationProfile profile,
        string normalizedWakeWordAssetPath,
        string normalizedWakeWordAssetName,
        string normalizedWakeEnginePath,
        string normalizedWakeEngineName,
        string normalizedBeamformingProfileName,
        string normalizedHardwareDspProfileName,
        string normalizedVendorDspProfilePath,
        string normalizedVendorDspProfileName)
    {
        var score = 0;
        var profileWakeWordAssetPath = WakeWordAssetCatalog.NormalizeStoredPath(profile.WakeWordAssetPath);
        var profileWakeWordAssetName = string.IsNullOrWhiteSpace(profile.WakeWordAssetName)
            ? "Recognizer Default"
            : profile.WakeWordAssetName.Trim();
        var profileWakeEnginePath = WakeWordEngineCatalog.NormalizeStoredPath(profile.WakeEnginePath);
        var profileWakeEngineName = string.IsNullOrWhiteSpace(profile.WakeEngineName)
            ? "Recognizer Wake"
            : profile.WakeEngineName.Trim();
        var profileBeamformingProfileName = NormalizeProfileName(profile.BeamformingProfileName);
        var profileHardwareDspProfileName = NormalizeProfileName(profile.HardwareDspProfileName);
        var profileVendorDspProfilePath = VendorDspProfileCatalog.NormalizeStoredPath(profile.VendorDspProfilePath);
        var profileVendorDspProfileName = string.IsNullOrWhiteSpace(profile.VendorDspProfileName)
            ? "No Vendor SDK"
            : profile.VendorDspProfileName.Trim();

        if (string.IsNullOrWhiteSpace(normalizedWakeWordAssetPath))
        {
            if (string.IsNullOrWhiteSpace(profileWakeWordAssetPath)
                && string.Equals(profileWakeWordAssetName, "Recognizer Default", StringComparison.OrdinalIgnoreCase))
            {
                score += 28;
            }
        }
        else if (string.Equals(profileWakeWordAssetPath, normalizedWakeWordAssetPath, StringComparison.OrdinalIgnoreCase))
        {
            score += 34;
        }
        else if (string.Equals(profileWakeWordAssetName, normalizedWakeWordAssetName, StringComparison.OrdinalIgnoreCase))
        {
            score += 18;
        }

        if (string.IsNullOrWhiteSpace(normalizedWakeEnginePath))
        {
            if (string.IsNullOrWhiteSpace(profileWakeEnginePath)
                && string.Equals(profileWakeEngineName, "Recognizer Wake", StringComparison.OrdinalIgnoreCase))
            {
                score += 26;
            }
        }
        else if (WakeWordEngineCatalog.StoredPathsMatch(profileWakeEnginePath, normalizedWakeEnginePath))
        {
            score += 30;
        }
        else if (string.Equals(profileWakeEngineName, normalizedWakeEngineName, StringComparison.OrdinalIgnoreCase))
        {
            score += 16;
        }

        if (string.Equals(profileBeamformingProfileName, normalizedBeamformingProfileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 24;
        }
        else if (profileBeamformingProfileName == "auto" || normalizedBeamformingProfileName == "auto")
        {
            score += 10;
        }

        if (string.Equals(profileHardwareDspProfileName, normalizedHardwareDspProfileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 24;
        }
        else if (profileHardwareDspProfileName == "auto" || normalizedHardwareDspProfileName == "auto")
        {
            score += 10;
        }

        if (string.IsNullOrWhiteSpace(normalizedVendorDspProfilePath))
        {
            if (string.IsNullOrWhiteSpace(profileVendorDspProfilePath)
                && string.Equals(profileVendorDspProfileName, "No Vendor SDK", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
        }
        else if (VendorDspProfileCatalog.StoredPathsMatch(profileVendorDspProfilePath, normalizedVendorDspProfilePath))
        {
            score += 26;
        }
        else if (string.Equals(profileVendorDspProfileName, normalizedVendorDspProfileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 14;
        }

        return score;
    }

    private static string NormalizeProfileName(string? profileName)
    {
        return string.IsNullOrWhiteSpace(profileName)
            ? "auto"
            : profileName.Trim().ToLowerInvariant();
    }
}
