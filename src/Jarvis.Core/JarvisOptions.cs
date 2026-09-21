namespace Jarvis.Core;

public sealed record JarvisOptions
{
    public string AssistantName { get; init; } = "Jarvis";

    public string AssistantPersona { get; init; } = string.Empty;

    public string AssistantPresencePreset { get; init; } = "baseline";

    public string AssistantResponseStyle { get; init; } = "adaptive";

    public string AssistantUserNickname { get; init; } = string.Empty;

    public bool AssistantSmallTalkEnabled { get; init; } = true;

    public string[] AssistantPronunciationHints { get; init; } = Array.Empty<string>();

    public string WakePhrase { get; init; } = "jarvis";

    public bool VoiceEnabled { get; init; }

    public string VoiceProvider { get; init; } = "auto";

    public string VoiceBaseUrl { get; init; } = string.Empty;

    public string VoiceModel { get; init; } = "gpt-4o-mini-tts";

    public string VoiceApiKey { get; init; } = string.Empty;

    public string VoiceApiKeyCredentialTarget { get; init; } = string.Empty;

    public string VoiceApiKeyEnvironmentVariable { get; init; } = "OPENAI_API_KEY";

    public string VoiceName { get; init; } = "coral";

    public string VoiceInstructions { get; init; } = string.Empty;

    public string VoiceResponseFormat { get; init; } = "pcm";

    public bool SpeechRecognitionEnabled { get; init; } = true;

    public string SpeechRecognitionProvider { get; init; } = "auto";

    public bool WakeWordEnabled { get; init; } = true;

    public string RecognizerCulture { get; init; } = "en-US";

    public string SpeechRecognitionBaseUrl { get; init; } = "https://api.openai.com/v1";

    public string SpeechRecognitionModel { get; init; } = "gpt-4o-mini-transcribe";

    public string SpeechRecognitionApiKey { get; init; } = string.Empty;

    public string SpeechRecognitionApiKeyCredentialTarget { get; init; } = string.Empty;

    public string SpeechRecognitionApiKeyEnvironmentVariable { get; init; } = "OPENAI_API_KEY";

    public string SpeechRecognitionLanguage { get; init; } = "en";

    public string SpeechRecognitionPrompt { get; init; } = string.Empty;

    public Dictionary<string, string> ApplicationAliases { get; init; } = new();

    public SpeechRecognitionCorrectionEntry[] SpeechRecognitionCorrections { get; init; } = Array.Empty<SpeechRecognitionCorrectionEntry>();

    public SpeechRecognitionRejectedPhrase[] SpeechRecognitionRejectedPhrases { get; init; } = Array.Empty<SpeechRecognitionRejectedPhrase>();

    public string[] SpeechRecognitionWakeAliases { get; init; } = Array.Empty<string>();

    public string SpeechRecognitionWakeWordAssetPath { get; init; } = "auto";

    public string SpeechRecognitionWakeEnginePath { get; init; } = "auto";

    public bool SpeechRecognitionWakeDetectionEnabled { get; init; } = true;

    public double SpeechRecognitionWakeDetectionSensitivity { get; init; } = 0.62;

    public int VoiceCommandTimeoutSeconds { get; init; } = 8;

    public double SpeechRecognitionConfidenceThreshold { get; init; } = 0.58;

    public int SpeechRecognitionSampleRateHz { get; init; } = 16000;

    public double SpeechRecognitionVoiceActivityThreshold { get; init; } = 0.008;

    public int SpeechRecognitionSilenceDurationMilliseconds { get; init; } = 850;

    public int SpeechRecognitionPreRollMilliseconds { get; init; } = 450;

    public int SpeechRecognitionMinimumUtteranceMilliseconds { get; init; } = 180;

    public string SpeechRecognitionDeviceName { get; init; } = string.Empty;

    public bool SpeechRecognitionPushToTalkEnabled { get; init; } = false;

    public bool SpeechRecognitionHotkeyEnabled { get; init; } = true;

    public string SpeechRecognitionHotkey { get; init; } = "Control+Space";

    public bool SpeechRecognitionNoiseSuppressionEnabled { get; init; } = false;

    public bool SpeechRecognitionEchoCancellationEnabled { get; init; } = false;

    public bool SpeechRecognitionBargeInEnabled { get; init; } = true;

    public bool SpeechRecognitionBeamformingEnabled { get; init; } = false;

    public string SpeechRecognitionBeamformingProfile { get; init; } = "auto";

    public string SpeechRecognitionHardwareDspProfile { get; init; } = "auto";

    public string SpeechRecognitionVendorDspProfilePath { get; init; } = "auto";

    public SpeechRecognitionCalibrationProfile[] SpeechRecognitionCalibrationProfiles { get; init; } = Array.Empty<SpeechRecognitionCalibrationProfile>();

    public SpeechRecognitionValidationProfile[] SpeechRecognitionValidationProfiles { get; init; } = Array.Empty<SpeechRecognitionValidationProfile>();

    public string SpeechRecognitionMode { get; init; } = "half-duplex";

    public bool SpeechRecognitionPunctuationRestorationEnabled { get; init; } = true;

    public bool ClapShortcutEnabled { get; init; } = false;

    public int ClapShortcutRequiredClapCount { get; init; } = 2;

    public int ClapShortcutMinimumIntervalMilliseconds { get; init; } = 120;

    public int ClapShortcutMaximumIntervalMilliseconds { get; init; } = 900;

    public int ClapShortcutCooldownMilliseconds { get; init; } = 2500;

    public double ClapShortcutThreshold { get; init; } = 0.40;

    public bool KeepRunningInTrayOnClose { get; init; } = true;

    public bool ShellExecutionEnabled { get; init; }

    public bool SafetyRequireApprovalForMajorActions { get; init; } = true;

    public bool SafetyBlockProtectedSystemPaths { get; init; } = true;

    public bool PlannerEnabled { get; init; } = true;

    public string PlannerProvider { get; init; } = "openai-compatible";

    public string PlannerBaseUrl { get; init; } = "https://api.openai.com/v1";

    public string PlannerModel { get; init; } = "gpt-4o-mini";

    public string PlannerFastModel { get; init; } = "gpt-4o-mini";

    public string PlannerReasoningModel { get; init; } = string.Empty;

    public string PlannerVisionModel { get; init; } = string.Empty;

    public string PlannerFastReasoningEffort { get; init; } = string.Empty;

    public string PlannerReasoningEffort { get; init; } = string.Empty;

    public string PlannerVisionReasoningEffort { get; init; } = string.Empty;

    public string[] PlannerFallbackModels { get; init; } = Array.Empty<string>();

    public PlannerProviderProfile[] PlannerFallbackProviders { get; init; } = Array.Empty<PlannerProviderProfile>();

    public string PlannerApiKey { get; init; } = string.Empty;

    public string PlannerApiKeyCredentialTarget { get; init; } = string.Empty;

    public string PlannerApiKeyEnvironmentVariable { get; init; } = "OPENAI_API_KEY";

    public int PlannerLocalContextLength { get; init; } = 16384;

    public string PlannerInstructions { get; init; } =
        "Ground replies in the live computer context. When the user refers to apps, windows, or what is happening on the computer, prefer the computer, apps, and open app tools.";

    public int PlannerRecentTurnCount { get; init; } = 6;

    public int PlannerRecentMemoryCount { get; init; } = 8;

    public int PlannerRecentToolResultCount { get; init; } = 6;

    public int PlannerMaxIterations { get; init; } = 5;

    public int PlannerRetryCount { get; init; } = 2;

    public bool PlannerReflectionEnabled { get; init; } = true;

    public bool PlannerBackgroundTasksEnabled { get; init; } = true;

    public bool PlannerWebResearchEnabled { get; init; } = true;

    public int PlannerWebResearchMaxResults { get; init; } = 5;

    public string VisualCaptureDirectory { get; init; } = Path.Combine("data", "captures");

    public int VisualCaptureRetentionCount { get; init; } = 24;

    public bool AmbientContextEnabled { get; init; }

    public string AmbientWebcamFramePath { get; init; } = string.Empty;

    public int AmbientWebcamFreshnessSeconds { get; init; } = 90;

    public string AmbientRoomSensorSnapshotPath { get; init; } = string.Empty;

    public bool AmbientPresenceDetectionEnabled { get; init; }

    public bool AmbientGestureDetectionEnabled { get; init; }

    public bool AmbientFaceRecognitionEnabled { get; init; }

    public bool AmbientFaceRecognitionConsentGranted { get; init; }

    public string AmbientFaceProfilesPath { get; init; } = string.Empty;

    public bool SpatialAudioCuesEnabled { get; init; }

    public bool FarFieldListeningEnabled { get; init; }

    public string DefaultWeatherLocation { get; init; } = string.Empty;

    public string MemoryFilePath { get; init; } = Path.Combine("data", "memory.json");

    public string TranscriptFilePath { get; init; } = Path.Combine("data", "transcript.jsonl");

    public string AgentStateFilePath { get; init; } = Path.Combine("data", "agent-state.json");

    public string PendingApprovalFilePath { get; init; } = Path.Combine("data", "pending-approval.json");

    public string UserStateFilePath { get; init; } = Path.Combine("data", "user-state.json");

    // New speaker volume features
    public bool WhisperModeEnabled { get; init; } = false;
    public double SpeakerVolumeMultiplier { get; init; } = 1.0;
    public bool RoomVolumeAwarenessEnabled { get; init; } = true;
    public double AmbientNoiseSampleIntervalSeconds { get; init; } = 10.0;
    public double RoomQuietThresholdRms { get; init; } = 0.006;
    public double RoomLoudThresholdRms { get; init; } = 0.025;
}

public sealed record PlannerProviderProfile
{
    public string Name { get; init; } = "fallback";

    public string Provider { get; init; } = "openai-compatible";

    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    public string Model { get; init; } = string.Empty;

    public string FastModel { get; init; } = string.Empty;

    public string ReasoningModel { get; init; } = string.Empty;

    public string VisionModel { get; init; } = string.Empty;

    public string FastReasoningEffort { get; init; } = string.Empty;

    public string ReasoningEffort { get; init; } = string.Empty;

    public string VisionReasoningEffort { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public string ApiKeyCredentialTarget { get; init; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; init; } = string.Empty;

    public string[] FallbackModels { get; init; } = Array.Empty<string>();
}

public sealed record SpeechRecognitionCalibrationProfile
{
    public string DeviceName { get; init; } = string.Empty;

    public DateTimeOffset CapturedUtc { get; init; } = DateTimeOffset.UtcNow;

    public double AmbientRmsLevel { get; init; } = 0.004;

    public double SpeechRmsLevel { get; init; } = 0.040;

    public double SpeechPeakLevel { get; init; } = 0.180;

    public double RecommendedVoiceActivityThreshold { get; init; } = 0.008;

    public double RecommendedInputGainCap { get; init; } = 4.0;

    public double RecommendedTargetPeakLevel { get; init; } = 0.36;

    public string PreferredBeamformingProfile { get; init; } = "auto";

    public string HardwareDspProfileName { get; init; } = "auto";

    public string VendorDspProfileName { get; init; } = string.Empty;

    public string VendorDspProfilePath { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;
}

public sealed record SpeechRecognitionValidationProfile
{
    public string DeviceName { get; init; } = string.Empty;

    public DateTimeOffset ValidatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string WakeWordAssetName { get; init; } = string.Empty;

    public string WakeWordAssetPath { get; init; } = string.Empty;

    public string WakeEngineName { get; init; } = string.Empty;

    public string WakeEnginePath { get; init; } = string.Empty;

    public string BeamformingProfileName { get; init; } = "auto";

    public string HardwareDspProfileName { get; init; } = "auto";

    public string VendorDspProfileName { get; init; } = string.Empty;

    public string VendorDspProfilePath { get; init; } = string.Empty;

    public double AmbientRmsLevel { get; init; } = 0.004;

    public double WakePhraseRmsLevel { get; init; } = 0.040;

    public double CommandSpeechRmsLevel { get; init; } = 0.050;

    public double PlaybackLeakageRmsLevel { get; init; } = 0.012;

    public double WakeWordReadinessScore { get; init; } = 0.50;

    public double FullDuplexReadinessScore { get; init; } = 0.50;

    public string RecommendedConversationMode { get; init; } = "full-duplex";

    public string Notes { get; init; } = string.Empty;
}

public sealed record SpeechRecognitionCorrectionEntry
{
    public string HeardText { get; init; } = string.Empty;

    public string CorrectedText { get; init; } = string.Empty;

    public int CorrectionCount { get; init; } = 1;

    public DateTimeOffset LastUpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public double? LastConfidence { get; init; }
}

public sealed record SpeechRecognitionRejectedPhrase
{
    public string HeardText { get; init; } = string.Empty;

    public int RejectionCount { get; init; } = 1;

    public DateTimeOffset LastRejectedUtc { get; init; } = DateTimeOffset.UtcNow;

    public double? LastConfidence { get; init; }
}
