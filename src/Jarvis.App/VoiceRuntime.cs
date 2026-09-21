using System.Drawing;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed record VoiceStatusSnapshot(
    bool IsAvailable,
    bool IsListening,
    bool IsArmed,
    string StatusText);

internal sealed class VoiceStatusChangedEventArgs : EventArgs
{
    public VoiceStatusChangedEventArgs(VoiceStatusSnapshot status)
    {
        Status = status;
    }

    public VoiceStatusSnapshot Status { get; }
}

internal sealed record VoiceSignalSnapshot(
    double RmsLevel,
    double PeakLevel,
    bool IsSpeechDetected,
    bool IsWakeWindowOpen,
    bool IsClapDetected,
    DateTimeOffset TimestampUtc);

internal sealed class VoiceSignalChangedEventArgs : EventArgs
{
    public VoiceSignalChangedEventArgs(VoiceSignalSnapshot signal)
    {
        Signal = signal;
    }

    public VoiceSignalSnapshot Signal { get; }
}

internal sealed class VoiceCommandRecognizedEventArgs : EventArgs
{
    public VoiceCommandRecognizedEventArgs(
        string text,
        double? confidence = null,
        bool requiresClarification = false,
        string clarificationPrompt = "",
        string rawText = "")
    {
        Text = text;
        RawText = string.IsNullOrWhiteSpace(rawText) ? text : rawText;
        Confidence = confidence;
        RequiresClarification = requiresClarification;
        ClarificationPrompt = clarificationPrompt;
    }

    public string Text { get; }

    public string RawText { get; }

    public double? Confidence { get; }

    public bool RequiresClarification { get; }

    public string ClarificationPrompt { get; }
}

internal sealed record VoiceTranscriptResult(string Text, double? Confidence = null);

internal sealed class VoiceActivationRequestedEventArgs : EventArgs
{
    public VoiceActivationRequestedEventArgs(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}

internal sealed class VoiceBargeInEventArgs : EventArgs
{
    public VoiceBargeInEventArgs(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}

internal enum SurfaceMode
{
    OperatorConsole,
    VoiceOnly,
    OverlayHud,
    EdgeDock,
    Dashboard
}

internal enum SurfaceState
{
    Idle,
    Listening,
    Thinking,
    Acting,
    Speaking,
    AwaitingApproval,
    Error
}

internal interface IVoiceRuntime : IDisposable
{
    event EventHandler<VoiceStatusChangedEventArgs>? StatusChanged;

    event EventHandler<VoiceSignalChangedEventArgs>? SignalLevelChanged;

    event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized;

    event EventHandler<VoiceActivationRequestedEventArgs>? ActivationRequested;

    event EventHandler<VoiceBargeInEventArgs>? BargeInRequested;

    VoiceStatusSnapshot CurrentStatus { get; }

    VoiceSignalSnapshot CurrentSignal { get; }

    void SetPlaybackState(bool isSpeaking);

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

internal static class VoiceRecognitionConfidence
{
    public static double ClarificationThreshold(JarvisOptions options) =>
        Math.Clamp(options.SpeechRecognitionConfidenceThreshold + 0.12, 0.55, 0.88);

    public static bool ShouldIgnore(double? confidence, double minimumConfidence) =>
        confidence is double value && value < minimumConfidence;

    public static bool ShouldClarify(JarvisOptions options, double? confidence, double minimumConfidence)
    {
        if (confidence is not double value)
        {
            return false;
        }

        return value < Math.Max(minimumConfidence, ClarificationThreshold(options));
    }

    public static string BuildClarificationPrompt(string text, double? confidence)
    {
        var confidenceText = confidence is double value
            ? $"{value:P0}"
            : "low";

        return $"I heard \"{text}\", but recognition confidence was {confidenceText}. Say yes to continue, no to cancel, or tell me what you actually said.";
    }

    public static double? FromAverageLogProbability(double averageLogProbability)
    {
        if (double.IsNaN(averageLogProbability) || double.IsInfinity(averageLogProbability))
        {
            return null;
        }

        return Math.Clamp(Math.Exp(averageLogProbability), 0.0, 1.0);
    }

    public static double? FromLogProbabilities(IEnumerable<double> logProbabilities)
    {
        var values = logProbabilities
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        return FromAverageLogProbability(values.Average());
    }
}

internal static class VoiceRuntimeFactory
{
    public static IVoiceRuntime Create(JarvisOptions options)
    {
        if (!options.SpeechRecognitionEnabled)
        {
            return new NullVoiceRuntime(
                new VoiceStatusSnapshot(
                    IsAvailable: true,
                    IsListening: false,
                    IsArmed: false,
                    StatusText: "Speech recognition is disabled in jarvis.settings.json."));
        }

        var provider = NormalizeProvider(options.SpeechRecognitionProvider);
        var lastStatus = string.Empty;

        if (provider is "auto" or "openai-compatible" or "openai" or "streaming" or "realtime")
        {
            if (ShouldPreferRealtimeRuntime(options, provider))
            {
                if (RealtimeTranscriptionVoiceRuntime.TryCreate(options, out var realtimeRuntime, out lastStatus))
                {
                    return realtimeRuntime!;
                }

                if (provider == "realtime")
                {
                    return new NullVoiceRuntime(
                        new VoiceStatusSnapshot(
                            IsAvailable: false,
                            IsListening: false,
                            IsArmed: false,
                            StatusText: lastStatus));
                }
            }

            if (OpenAiCompatibleVoiceRuntime.TryCreate(options, out var runtime, out lastStatus))
            {
                return runtime!;
            }

            if (provider is "openai-compatible" or "openai" or "streaming" or "realtime")
            {
                return new NullVoiceRuntime(
                    new VoiceStatusSnapshot(
                        IsAvailable: false,
                        IsListening: false,
                        IsArmed: false,
                        StatusText: lastStatus));
            }
        }

        return CreateWindowsVoiceRuntime(options, lastStatus);
    }

    private static IVoiceRuntime CreateWindowsVoiceRuntime(JarvisOptions options, string fallbackReason)
    {
        try
        {
            var installedRecognizers = SpeechRecognitionEngine.InstalledRecognizers().ToArray();

            if (installedRecognizers.Length == 0)
            {
                return new NullVoiceRuntime(
                    new VoiceStatusSnapshot(
                        IsAvailable: false,
                        IsListening: false,
                        IsArmed: false,
                        StatusText: $"No Windows speech recognizer is installed for {options.RecognizerCulture}."));
            }

            var recognizer = installedRecognizers.FirstOrDefault(
                    candidate => string.Equals(
                        candidate.Culture.Name,
                        options.RecognizerCulture,
                        StringComparison.OrdinalIgnoreCase))
                ?? installedRecognizers[0];

            return new SpeechRecognitionVoiceRuntime(options, recognizer, fallbackReason);
        }
        catch (Exception exception)
        {
            return new NullVoiceRuntime(
                new VoiceStatusSnapshot(
                    IsAvailable: false,
                    IsListening: false,
                    IsArmed: false,
                    StatusText: $"Speech recognition unavailable: {exception.Message}"));
        }
    }

    private static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return "auto";
        }

        return provider.Trim().ToLowerInvariant();
    }

    private static bool ShouldPreferRealtimeRuntime(JarvisOptions options, string provider)
    {
        if (provider == "realtime")
        {
            return true;
        }

        if (!Uri.TryCreate(options.SpeechRecognitionBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        return baseUri.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(baseUri.Host, "openai.com", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class NullVoiceRuntime : IVoiceRuntime
{
    private EventHandler<VoiceStatusChangedEventArgs>? _statusChanged;
    private EventHandler<VoiceSignalChangedEventArgs>? _signalChanged;

    public NullVoiceRuntime(VoiceStatusSnapshot status)
    {
        CurrentStatus = status;
        CurrentSignal = new VoiceSignalSnapshot(0, 0, false, false, false, DateTimeOffset.UtcNow);
    }

    public event EventHandler<VoiceStatusChangedEventArgs>? StatusChanged
    {
        add => _statusChanged += value;
        remove => _statusChanged -= value;
    }

    public event EventHandler<VoiceSignalChangedEventArgs>? SignalLevelChanged
    {
        add => _signalChanged += value;
        remove => _signalChanged -= value;
    }

    public event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized
    {
        add { }
        remove { }
    }

    public event EventHandler<VoiceActivationRequestedEventArgs>? ActivationRequested
    {
        add { }
        remove { }
    }

    public event EventHandler<VoiceBargeInEventArgs>? BargeInRequested
    {
        add { }
        remove { }
    }

    public VoiceStatusSnapshot CurrentStatus { get; private set; }

    public VoiceSignalSnapshot CurrentSignal { get; private set; }

    public void SetPlaybackState(bool isSpeaking)
    {
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _statusChanged?.Invoke(this, new VoiceStatusChangedEventArgs(CurrentStatus));
        _signalChanged?.Invoke(this, new VoiceSignalChangedEventArgs(CurrentSignal));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _statusChanged = null;
        _signalChanged = null;
    }
}

internal sealed class SpeechRecognitionVoiceRuntime : IVoiceRuntime
{
    private const string WakePhraseGrammarName = "wake-phrase";
    private const string DictationGrammarName = "dictation";
    private const string CommandGrammarPrefix = "command:";
    private const string WakeCommandGrammarPrefix = "wake-command:";

    private readonly object _sync = new();
    private readonly JarvisOptions _options;
    private readonly RecognizerInfo _recognizer;
    private readonly SpeechRecognitionEngine _engine;
    private readonly string _fallbackReason;
    private MicrophonePcmStream? _microphoneStream;
    private EventHandler<VoiceActivationRequestedEventArgs>? _activationRequested;

    private System.Threading.Timer? _wakeWindowTimer;
    private DateTimeOffset _lastBargeInUtc = DateTimeOffset.MinValue;
    private bool _isListening;
    private bool _isArmed;
    private bool _isPlaybackActive;

    public SpeechRecognitionVoiceRuntime(JarvisOptions options, RecognizerInfo recognizer, string fallbackReason = "")
    {
        _options = options;
        _recognizer = recognizer;
        _fallbackReason = fallbackReason;
        _engine = new SpeechRecognitionEngine(recognizer);
        _engine.SpeechRecognized += OnSpeechRecognized;
        _engine.RecognizeCompleted += OnRecognizeCompleted;
        _engine.AudioLevelUpdated += OnAudioLevelUpdated;


        var wakePhraseGrammarBuilder = new GrammarBuilder
        {
            Culture = recognizer.Culture
        };
        wakePhraseGrammarBuilder.Append(new Choices(VoiceRecognitionText.BuildWakeGrammarChoices(options).ToArray()));

        _engine.LoadGrammar(new Grammar(wakePhraseGrammarBuilder) { Name = WakePhraseGrammarName });
        LoadCommandGrammars();
        _engine.LoadGrammar(new DictationGrammar { Name = DictationGrammarName });

        CurrentStatus = new VoiceStatusSnapshot(
            IsAvailable: true,
            IsListening: false,
            IsArmed: false,
            StatusText: BuildIdleStatusText());
        CurrentSignal = new VoiceSignalSnapshot(0, 0, false, false, false, DateTimeOffset.UtcNow);
    }

    public event EventHandler<VoiceStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<VoiceSignalChangedEventArgs>? SignalLevelChanged;

    public event EventHandler<VoiceCommandRecognizedEventArgs>? CommandRecognized;

    public event EventHandler<VoiceActivationRequestedEventArgs>? ActivationRequested
    {
        add => _activationRequested += value;
        remove => _activationRequested -= value;
    }

    public event EventHandler<VoiceBargeInEventArgs>? BargeInRequested;

    public VoiceStatusSnapshot CurrentStatus { get; private set; }

    public VoiceSignalSnapshot CurrentSignal { get; private set; }

    public void SetPlaybackState(bool isSpeaking)
    {
        lock (_sync)
        {
            _isPlaybackActive = isSpeaking;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_isListening)
            {
                PublishStatus(_isArmed, CurrentStatus.StatusText);
                return Task.CompletedTask;
            }

            try
            {
                var inputDescription = ConfigureAudioInput();
                _engine.RecognizeAsync(RecognizeMode.Multiple);
                _isListening = true;
                PublishSignal(0, 0, false, false);

                var listeningText = _options.WakeWordEnabled
                    ? $"Listening for \"{_options.WakePhrase}\" with offline {_recognizer.Culture.Name} speech recognition on {inputDescription}."
                    : $"Listening for voice directives with offline {_recognizer.Culture.Name} speech recognition on {inputDescription}.";

                PublishStatus(
                    isArmed: false,
                    statusText: string.IsNullOrWhiteSpace(_fallbackReason)
                        ? listeningText
                        : $"{listeningText} Cloud recognition unavailable: {_fallbackReason}");
            }
            catch (Exception exception)
            {
                DisposeMicrophoneStream();
                _isListening = false;
                PublishStatus(
                    isArmed: false,
                    statusText: $"Microphone startup failed: {exception.Message}",
                    isAvailable: false);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_isListening)
            {
                return Task.CompletedTask;
            }

            ClearWakeWindow();

            // End the audio stream first so the recognizer's blocked read returns and it can shut down.
            _microphoneStream?.StopCapture();

            try
            {
                _engine.RecognizeAsyncCancel();
                _engine.RecognizeAsyncStop();
            }
            catch
            {
            }

            DisposeMicrophoneStream();
            _isListening = false;
            _isArmed = false;
            PublishSignal(0, 0, false, false);
            PublishStatus(isArmed: false, statusText: "Microphone listening paused.");
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            ClearWakeWindow();
            _microphoneStream?.StopCapture();

            try
            {
                if (_isListening)
                {
                    _engine.RecognizeAsyncCancel();
                    _engine.RecognizeAsyncStop();
                }
            }
            catch
            {
            }

            _engine.SpeechRecognized -= OnSpeechRecognized;
            _engine.RecognizeCompleted -= OnRecognizeCompleted;
            _engine.AudioLevelUpdated -= OnAudioLevelUpdated;
            _engine.Dispose();
            DisposeMicrophoneStream();
            _isListening = false;
            _isArmed = false;
            PublishSignal(0, 0, false, false);
        }
    }

    /// <summary>
    /// Points the recognizer at the configured microphone with automatic gain. Falls back to the
    /// Windows default device (previous behavior) if the configured one cannot be opened.
    /// </summary>
    private string ConfigureAudioInput()
    {
        DisposeMicrophoneStream();

        try
        {
            if (AudioDeviceResolver.TryResolveMicrophoneDevice(_options.SpeechRecognitionDeviceName, out var deviceNumber))
            {
                var profile = MicrophoneProcessingProfileResolver.Resolve(_options);
                var stream = new MicrophonePcmStream(
                    deviceNumber,
                    profile.RollingCaptureBufferMilliseconds,
                    profile.InputGainCap,
                    profile.TargetPeakLevel);

                try
                {
                    _engine.SetInputToAudioStream(
                        stream,
                        new SpeechAudioFormatInfo(MicrophonePcmStream.SampleRateHz, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                    stream.LevelMeasured += HandleAudioLevel;
                    stream.StartCapture();
                    _microphoneStream = stream;
                    return $"\"{stream.DeviceName.Trim()}\"";
                }
                catch
                {
                    stream.Dispose();
                }
            }
        }
        catch
        {
            // Fall through to the Windows default device below.
        }

        _engine.SetInputToDefaultAudioDevice();
        return "the Windows default microphone";
    }

    private void DisposeMicrophoneStream()
    {
        var stream = _microphoneStream;
        _microphoneStream = null;
        stream?.Dispose();
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs eventArgs)
    {
        if (eventArgs.Result is null || string.IsNullOrWhiteSpace(eventArgs.Result.Text))
        {
            return;
        }

        var minimumConfidence = GetMinimumConfidence(eventArgs.Result);

        if (eventArgs.Result.Confidence < minimumConfidence)
        {
            return;
        }

        var rawRecognizedText = VoiceRecognitionText.NormalizeTranscript(eventArgs.Result.Text, _options, applyPersonalCorrections: false);
        var recognizedText = VoiceRecognitionText.ApplyPersonalCorrections(rawRecognizedText, _options);

        if (!_options.WakeWordEnabled)
        {
            PublishRecognizedCommand(
                JarvisCommandCatalog.NormalizeVoiceDirective(recognizedText),
                rawRecognizedText,
                eventArgs.Result.Confidence,
                minimumConfidence);
            return;
        }

        if (VoiceRecognitionText.TryExtractWakeCommand(recognizedText, _options, out var inlineCommand))
        {
            if (string.IsNullOrWhiteSpace(inlineCommand))
            {
                ArmWakeWindow();
                return;
            }

            ResetToIdle();
            PublishRecognizedCommand(
                JarvisCommandCatalog.NormalizeVoiceDirective(inlineCommand),
                rawRecognizedText,
                eventArgs.Result.Confidence,
                minimumConfidence);
            return;
        }

        lock (_sync)
        {
            if (!_isArmed)
            {
                return;
            }

            _isArmed = false;
            ClearWakeWindow();
        }

        PublishStatus(isArmed: false, statusText: BuildIdleStatusText());
        PublishRecognizedCommand(
            JarvisCommandCatalog.NormalizeVoiceDirective(recognizedText),
            rawRecognizedText,
            eventArgs.Result.Confidence,
            minimumConfidence);
    }

    private void OnAudioLevelUpdated(object? sender, AudioLevelUpdatedEventArgs eventArgs)
    {
        HandleAudioLevel(Math.Clamp(eventArgs.AudioLevel / 100d, 0, 1));
    }

    private void HandleAudioLevel(double level)
    {
        bool isListening;
        bool isWakeWindowOpen;

        lock (_sync)
        {
            isListening = _isListening;
            isWakeWindowOpen = _isArmed;
        }

        if (!isListening)
        {
            return;
        }

        var speechThreshold = Math.Max(0.08, _options.SpeechRecognitionVoiceActivityThreshold * 4.0);
        var clapDetected = false;

        if (ShouldRequestBargeIn(level))
        {
            BargeInRequested?.Invoke(this, new VoiceBargeInEventArgs("speech-over-playback"));
        }

        PublishSignal(level, level, level >= speechThreshold, clapDetected, isWakeWindowOpen);
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs eventArgs)
    {
        if (eventArgs.Error is null && !eventArgs.Cancelled)
        {
            return;
        }

        lock (_sync)
        {
            _isListening = false;
            _isArmed = false;
            ClearWakeWindow();
        }

        var statusText = eventArgs.Error is null
            ? "Speech recognition stopped."
            : $"Speech recognition stopped: {eventArgs.Error.Message}";

        PublishSignal(0, 0, false, false);
        PublishStatus(isArmed: false, statusText: statusText, isAvailable: eventArgs.Error is null);
    }

    private void ArmWakeWindow()
    {
        lock (_sync)
        {
            _isArmed = true;
            ClearWakeWindow();
            _wakeWindowTimer = new System.Threading.Timer(
                _ => OnWakeWindowExpired(),
                null,
                TimeSpan.FromSeconds(Math.Max(2, _options.VoiceCommandTimeoutSeconds)),
                Timeout.InfiniteTimeSpan);
        }

        PublishStatus(
            isArmed: true,
            statusText: $"Wake word detected. Listening for the next command for {_options.VoiceCommandTimeoutSeconds} seconds.");
    }

    private void OnWakeWindowExpired()
    {
        lock (_sync)
        {
            if (!_isArmed)
            {
                return;
            }

            _isArmed = false;
            ClearWakeWindow();
        }

        PublishStatus(isArmed: false, statusText: BuildIdleStatusText());
    }

    private void ResetToIdle()
    {
        lock (_sync)
        {
            _isArmed = false;
            ClearWakeWindow();
        }

        PublishStatus(isArmed: false, statusText: BuildIdleStatusText());
    }

    private void PublishStatus(bool isArmed, string statusText, bool? isAvailable = null)
    {
        var snapshot = new VoiceStatusSnapshot(
            IsAvailable: isAvailable ?? CurrentStatus.IsAvailable,
            IsListening: _isListening,
            IsArmed: isArmed,
            StatusText: statusText);

        CurrentStatus = snapshot;
        StatusChanged?.Invoke(this, new VoiceStatusChangedEventArgs(snapshot));
        PublishSignal(
            CurrentSignal.RmsLevel,
            CurrentSignal.PeakLevel,
            CurrentSignal.IsSpeechDetected,
            false,
            isArmed);
    }

    private string BuildIdleStatusText()
    {
        return _options.WakeWordEnabled
            ? $"Listening for \"{_options.WakePhrase}\" with {_recognizer.Culture.Name} speech recognition."
            : $"Listening for voice directives with {_recognizer.Culture.Name} speech recognition.";
    }

    private void ClearWakeWindow()
    {
        _wakeWindowTimer?.Dispose();
        _wakeWindowTimer = null;
    }

    private void PublishSignal(
        double rmsLevel,
        double peakLevel,
        bool isSpeechDetected,
        bool isClapDetected,
        bool? isWakeWindowOpen = null)
    {
        CurrentSignal = new VoiceSignalSnapshot(
            Math.Clamp(rmsLevel, 0, 1),
            Math.Clamp(peakLevel, 0, 1),
            isSpeechDetected,
            isWakeWindowOpen ?? _isArmed,
            isClapDetected,
            DateTimeOffset.UtcNow);
        SignalLevelChanged?.Invoke(this, new VoiceSignalChangedEventArgs(CurrentSignal));
    }

    private bool ShouldRequestBargeIn(double level)
    {
        if (!_options.SpeechRecognitionBargeInEnabled || level < 0.16)
        {
            return false;
        }

        lock (_sync)
        {
            if (!_isPlaybackActive
                || DateTimeOffset.UtcNow - _lastBargeInUtc <= TimeSpan.FromMilliseconds(1200))
            {
                return false;
            }

            _lastBargeInUtc = DateTimeOffset.UtcNow;
            return true;
        }
    }

    private void LoadCommandGrammars()
    {
        LoadPhraseGrammar("help", JarvisCommandCatalog.HelpPhrases);
        LoadPhraseGrammar("status", JarvisCommandCatalog.StatusPhrases);
        LoadPhraseGrammar("time", JarvisCommandCatalog.TimePhrases);
        LoadPhraseGrammar("notes", JarvisCommandCatalog.NotesPhrases);
        LoadPhraseGrammar("weather", JarvisCommandCatalog.WeatherPhrases);
        LoadPhraseGrammar("system", JarvisCommandCatalog.SystemPhrases);
        LoadPhraseGrammar("computer", JarvisCommandCatalog.ComputerPhrases);
        LoadPhraseGrammar("apps", JarvisCommandCatalog.AppsPhrases);
        LoadPhraseGrammar("exit", JarvisCommandCatalog.ExitPhrases);

        LoadPayloadGrammar("remember", JarvisCommandCatalog.RememberPrefixes);
        LoadPayloadGrammar("recall", JarvisCommandCatalog.RecallPrefixes);
        LoadPayloadGrammar("run", JarvisCommandCatalog.RunPrefixes);
        LoadPayloadGrammar("open-app", JarvisCommandCatalog.OpenAppPrefixes);
        LoadPayloadGrammar("open-path", JarvisCommandCatalog.OpenPathPrefixes);
        LoadPayloadGrammar("list-files", JarvisCommandCatalog.ListFilesPrefixes);
        LoadPayloadGrammar("read-file", JarvisCommandCatalog.ReadFilePrefixes);
        LoadPayloadGrammar("find-files", JarvisCommandCatalog.FindFilesPrefixes);
        LoadPayloadGrammar("weather", JarvisCommandCatalog.WeatherPrefixes);
        LoadPayloadGrammar("browse", JarvisCommandCatalog.BrowsePrefixes);
        LoadPayloadGrammar("search-web", JarvisCommandCatalog.SearchWebPrefixes);
        LoadPayloadGrammar("click-element", JarvisCommandCatalog.ClickPrefixes);
        LoadPayloadGrammar("type-into", JarvisCommandCatalog.TypePrefixes);
        LoadPayloadGrammar("get-element-text", JarvisCommandCatalog.GetTextPrefixes);
        LoadPayloadGrammar("scroll", JarvisCommandCatalog.ScrollPrefixes);
        LoadPayloadGrammar("wait", JarvisCommandCatalog.WaitPrefixes);
    }

    private void LoadPhraseGrammar(string suffix, IReadOnlyList<string> phrases)
    {
        _engine.LoadGrammar(BuildPhraseGrammar($"{CommandGrammarPrefix}{suffix}", phrases, includeWakePrefix: false));
        _engine.LoadGrammar(BuildPhraseGrammar($"{WakeCommandGrammarPrefix}{suffix}", phrases, includeWakePrefix: true));
    }

    private void LoadPayloadGrammar(string suffix, IReadOnlyList<string> prefixes)
    {
        _engine.LoadGrammar(BuildPayloadGrammar($"{CommandGrammarPrefix}{suffix}", prefixes, includeWakePrefix: false));
        _engine.LoadGrammar(BuildPayloadGrammar($"{WakeCommandGrammarPrefix}{suffix}", prefixes, includeWakePrefix: true));
    }

    private Grammar BuildPhraseGrammar(string grammarName, IReadOnlyList<string> phrases, bool includeWakePrefix)
    {
        var builder = CreateGrammarBuilder(includeWakePrefix);
        builder.Append(new Choices(phrases.ToArray()));
        return new Grammar(builder) { Name = grammarName };
    }

    private Grammar BuildPayloadGrammar(string grammarName, IReadOnlyList<string> prefixes, bool includeWakePrefix)
    {
        var builder = CreateGrammarBuilder(includeWakePrefix);
        builder.Append(new Choices(prefixes.ToArray()));
        builder.AppendDictation();
        return new Grammar(builder) { Name = grammarName };
    }

    private GrammarBuilder CreateGrammarBuilder(bool includeWakePrefix)
    {
        var builder = new GrammarBuilder
        {
            Culture = _recognizer.Culture
        };

        if (includeWakePrefix)
        {
            builder.Append(new Choices(VoiceRecognitionText.BuildWakeGrammarChoices(_options).ToArray()));
        }

        return builder;
    }

    private double GetMinimumConfidence(RecognitionResult result)
    {
        var grammarName = result.Grammar?.Name ?? string.Empty;

        if (string.Equals(grammarName, DictationGrammarName, StringComparison.Ordinal))
        {
            return _options.SpeechRecognitionConfidenceThreshold;
        }

        return Math.Max(0.35, _options.SpeechRecognitionConfidenceThreshold - 0.20);
    }

    private void PublishRecognizedCommand(string text, string rawText, double? confidence, double minimumConfidence)
    {
        if (string.IsNullOrWhiteSpace(text) || VoiceRecognitionConfidence.ShouldIgnore(confidence, minimumConfidence))
        {
            return;
        }

        var requiresClarification = VoiceRecognitionConfidence.ShouldClarify(_options, confidence, minimumConfidence);
        var prompt = requiresClarification
            ? VoiceRecognitionConfidence.BuildClarificationPrompt(rawText, confidence)
            : string.Empty;

        CommandRecognized?.Invoke(
            this,
            new VoiceCommandRecognizedEventArgs(text, confidence, requiresClarification, prompt, rawText));
    }
}
