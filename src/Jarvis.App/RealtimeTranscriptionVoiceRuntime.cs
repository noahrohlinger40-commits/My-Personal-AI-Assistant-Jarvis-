using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.Wave;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed class RealtimeTranscriptionVoiceRuntime : IVoiceRuntime
{
    private const int RealtimeSampleRateHz = 24000;

    private readonly object _sync = new();
    private readonly JarvisOptions _options;
    private readonly RealtimeTranscriptionWebSocketClient _client;
    private readonly ClapDetector? _clapDetector;
    private readonly WakeWordDetector? _wakeWordDetector;
    private readonly ResolvedMicrophoneProcessingProfile _audioProcessing;
    private readonly VendorDspControlSession? _vendorDspControl;
    private readonly VoiceAcousticEchoSuppressor _echoSuppressor;
    private readonly Queue<byte[]> _preRollChunks = new();
    private readonly int _preRollByteLimit;
    private WaveInEvent? _capture;
    private CancellationTokenSource? _lifetimeCts;
    private System.Threading.Timer? _wakeWindowTimer;
    private MemoryStream? _activeSpeechBuffer;
    private int _preRollBytes;
    private bool _isListening;
    private bool _isArmed;
    private bool _isSpeechDetected;
    private string _vendorDspWarning = string.Empty;

    public RealtimeTranscriptionVoiceRuntime(JarvisOptions options, RealtimeTranscriptionWebSocketClient client)
    {
        _options = options;
        _client = client;
        _clapDetector = options.ClapShortcutEnabled ? new ClapDetector(options) : null;
        _audioProcessing = MicrophoneProcessingProfileResolver.Resolve(options);
        _wakeWordDetector = WakeWordDetector.Create(options, _audioProcessing);
        _vendorDspControl = VendorDspControlSession.Create(options, _audioProcessing);
        _echoSuppressor = new VoiceAcousticEchoSuppressor(options, _audioProcessing);
        _preRollByteLimit = BytesForMilliseconds(Math.Max(0, options.SpeechRecognitionPreRollMilliseconds), RealtimeSampleRateHz);
        _client.SpeechActivityChanged += OnSpeechActivityChanged;
        _client.TranscriptCompleted += OnTranscriptCompleted;
        _client.ErrorReceived += OnClientError;

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

    public event EventHandler<VoiceActivationRequestedEventArgs>? ActivationRequested;

    public event EventHandler<VoiceBargeInEventArgs>? BargeInRequested;

    public VoiceStatusSnapshot CurrentStatus { get; private set; }

    public VoiceSignalSnapshot CurrentSignal { get; private set; }

    public void SetPlaybackState(bool isSpeaking)
    {
        _echoSuppressor.SetPlaybackState(isSpeaking);
    }

    public static bool TryCreate(JarvisOptions options, out IVoiceRuntime? runtime, out string status)
    {
        runtime = null;

        if (string.IsNullOrWhiteSpace(options.SpeechRecognitionModel))
        {
            status = "Realtime speech recognition is enabled, but no transcription model is configured.";
            return false;
        }

        if (!Uri.TryCreate(options.SpeechRecognitionBaseUrl, UriKind.Absolute, out var baseUri))
        {
            status = "Realtime speech recognition base URL is invalid.";
            return false;
        }

        var apiKey = ResolveApiKey(options);

        if (string.IsNullOrWhiteSpace(apiKey)
            && !string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(baseUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            status =
                $"Realtime speech recognition requires an API key in `speechRecognitionApiKey`, `speechRecognitionApiKeyCredentialTarget`, or `{options.SpeechRecognitionApiKeyEnvironmentVariable}`.";
            return false;
        }

        var audioProcessing = MicrophoneProcessingProfileResolver.Resolve(options);

        runtime = new RealtimeTranscriptionVoiceRuntime(
            options,
            new RealtimeTranscriptionWebSocketClient(
                BuildRealtimeEndpoint(baseUri),
                options.SpeechRecognitionModel,
                options.SpeechRecognitionLanguage,
                VoiceRecognitionText.BuildTranscriptionPrompt(options),
                apiKey,
                audioProcessing.VoiceActivityThreshold * audioProcessing.ServerVadThresholdScale,
                options.SpeechRecognitionPreRollMilliseconds,
                options.SpeechRecognitionSilenceDurationMilliseconds,
                options.SpeechRecognitionBargeInEnabled,
                audioProcessing.RealtimeNoiseReductionMode));

        status = $"Realtime speech recognition ready with model {options.SpeechRecognitionModel}.";
        return true;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_isListening)
            {
                PublishStatus(_isArmed, CurrentStatus.StatusText);
                return;
            }
        }

        _vendorDspWarning = await EnsureVendorDspAppliedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await _client.ConnectAsync(lifetimeCts.Token);
            var capture = BuildWaveIn();
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();

            lock (_sync)
            {
                _lifetimeCts = lifetimeCts;
                _capture = capture;
                _isListening = true;
                _isSpeechDetected = false;
            }

            PublishSignal(0, 0, false, false);
            PublishStatus(
                isArmed: false,
                statusText: BuildIdleStatusText());
        }
        catch (Exception exception)
        {
            await SafeDisconnectAsync().ConfigureAwait(false);
            await ReleaseVendorDspAsync().ConfigureAwait(false);

            lock (_sync)
            {
                _capture = null;
                _lifetimeCts = null;
                _isListening = false;
                _isSpeechDetected = false;
            }

            PublishStatus(
                isArmed: false,
                statusText: $"Realtime microphone startup failed: {TrimStatus(exception.Message)}",
                isAvailable: false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WaveInEvent? capture;
        CancellationTokenSource? lifetimeCts;

        lock (_sync)
        {
            if (!_isListening)
            {
                return;
            }

            _isListening = false;
            _isArmed = false;
            _isSpeechDetected = false;
            ClearWakeWindow();
            ClearSpeechBuffers();

            capture = _capture;
            _capture = null;

            lifetimeCts = _lifetimeCts;
            _lifetimeCts = null;
        }

        DisposeCapture(capture);
        lifetimeCts?.Cancel();
        lifetimeCts?.Dispose();
        await SafeDisconnectAsync().ConfigureAwait(false);
        _vendorDspWarning = string.Empty;
        await ReleaseVendorDspAsync().ConfigureAwait(false);
        PublishSignal(0, 0, false, false);
        PublishStatus(isArmed: false, statusText: "Realtime microphone listening paused.");
    }

    public void Dispose()
    {
        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
        }

        _client.SpeechActivityChanged -= OnSpeechActivityChanged;
        _client.TranscriptCompleted -= OnTranscriptCompleted;
        _client.ErrorReceived -= OnClientError;
        _wakeWordDetector?.Dispose();
        _client.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (eventArgs.BytesRecorded <= 0)
        {
            return;
        }

        var chunk = new byte[eventArgs.BytesRecorded];
        Buffer.BlockCopy(eventArgs.Buffer, 0, chunk, 0, eventArgs.BytesRecorded);

        double level;
        double peakLevel;
        bool clapDetected = false;
        bool wakeWindowOpen;
        bool isListening;
        bool isSpeechDetected;

        lock (_sync)
        {
            isListening = _isListening;
            isSpeechDetected = _isSpeechDetected;
            wakeWindowOpen = _isArmed;
        }

        if (!isListening)
        {
            return;
        }

        level = CalculateRootMeanSquareLevel(chunk);
        peakLevel = CalculatePeakLevel(chunk);
        var echoControl = _echoSuppressor.Process(level, peakLevel, isSpeechDetected, DateTimeOffset.UtcNow);

        if (echoControl.ShouldRequestBargeIn)
        {
            BargeInRequested?.Invoke(this, new VoiceBargeInEventArgs("speech-over-playback"));
        }

        if (echoControl.ShouldSuppress)
        {
            _client.EnqueueAudio(new byte[chunk.Length]);
            PublishSignal(0, 0, false, false, wakeWindowOpen);
            return;
        }

        lock (_sync)
        {
            if (!_isListening)
            {
                return;
            }

            AppendToPreRoll(chunk);

            if (_isSpeechDetected)
            {
                EnsureActiveSpeechBuffer();
                _activeSpeechBuffer?.Write(chunk, 0, chunk.Length);
            }
        }

        if (_clapDetector is not null
            && _clapDetector.ProcessSample(peakLevel, DateTimeOffset.UtcNow))
        {
            clapDetected = true;
            ActivationRequested?.Invoke(this, new VoiceActivationRequestedEventArgs("double-clap"));
        }

        _client.EnqueueAudio(chunk);
        PublishSignal(level, peakLevel, isSpeechDetected || echoControl.ShouldRequestBargeIn, clapDetected, wakeWindowOpen);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        lock (_sync)
        {
            _isListening = false;
            _isArmed = false;
            _isSpeechDetected = false;
            ClearWakeWindow();
            ClearSpeechBuffers();
            _capture = null;
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
        }

        _vendorDspWarning = string.Empty;
        _ = ReleaseVendorDspAsync();
        _ = SafeDisconnectAsync();

        var statusText = eventArgs.Exception is null
            ? "Realtime speech recognition stopped."
            : $"Realtime speech recognition stopped: {TrimStatus(eventArgs.Exception.Message)}";

        PublishSignal(0, 0, false, false);
        PublishStatus(isArmed: false, statusText: statusText, isAvailable: eventArgs.Exception is null);
    }

    private void OnSpeechActivityChanged(bool isSpeechDetected)
    {
        double rmsLevel;
        double peakLevel;
        bool wakeWindowOpen;
        byte[]? completedUtterance = null;

        lock (_sync)
        {
            var wasSpeechDetected = _isSpeechDetected;
            _isSpeechDetected = isSpeechDetected;
            rmsLevel = CurrentSignal.RmsLevel;
            peakLevel = CurrentSignal.PeakLevel;
            wakeWindowOpen = _isArmed;

            if (isSpeechDetected && !wasSpeechDetected)
            {
                EnsureActiveSpeechBuffer();
            }
            else if (!isSpeechDetected && wasSpeechDetected)
            {
                completedUtterance = FinalizeSpeechBuffer();
            }
        }

        if (completedUtterance is not null)
        {
            TryActivateFromAudio(completedUtterance);
        }

        PublishSignal(rmsLevel, peakLevel, isSpeechDetected, false, wakeWindowOpen);
    }

    private void OnTranscriptCompleted(VoiceTranscriptResult transcript)
    {
        HandleRecognizedText(transcript.Text, transcript.Confidence);
    }

    private void OnClientError(string message)
    {
        PublishStatus(
            isArmed: _isArmed,
            statusText: $"Realtime transcription error: {TrimStatus(message)}");
    }

    private void HandleRecognizedText(string recognizedText, double? recognitionConfidence = null)
    {
        var rawRecognizedText = VoiceRecognitionText.NormalizeTranscript(recognizedText, _options, applyPersonalCorrections: false);
        recognizedText = VoiceRecognitionText.ApplyPersonalCorrections(rawRecognizedText, _options);
        var minimumConfidence = GetMinimumRecognitionConfidence();

        if (!_options.WakeWordEnabled)
        {
            if (string.IsNullOrWhiteSpace(recognizedText))
            {
                return;
            }

            PublishRecognizedCommand(
                JarvisCommandCatalog.NormalizeVoiceDirective(recognizedText),
                rawRecognizedText,
                recognitionConfidence,
                minimumConfidence);
            return;
        }

        if (!string.IsNullOrWhiteSpace(recognizedText)
            && VoiceRecognitionText.TryExtractWakeCommand(recognizedText, _options, out var inlineCommand))
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
                recognitionConfidence,
                minimumConfidence);
            return;
        }

        if (string.IsNullOrWhiteSpace(recognizedText))
        {
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
            recognitionConfidence,
            minimumConfidence);
    }

    public void ListenForFollowUp()
    {
        if (_isListening)
        {
            ArmWakeWindow();
        }
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
        var providerLabel = $"realtime transcription ({_options.SpeechRecognitionModel})";
        var deviceLabel = string.IsNullOrWhiteSpace(_options.SpeechRecognitionDeviceName)
            ? "the default microphone"
            : $"microphone \"{_options.SpeechRecognitionDeviceName}\"";
        var modeLabel = _options.SpeechRecognitionMode?.Trim() ?? "half-duplex";
        var tuningLabel = _audioProcessing.BuildStatusLabel();
        var vendorWarning = string.IsNullOrWhiteSpace(_vendorDspWarning)
            ? string.Empty
            : $" Vendor DSP fallback: {TrimStatus(_vendorDspWarning)}";

        if (_options.SpeechRecognitionPushToTalkEnabled)
        {
            return $"Push-to-talk ready with {providerLabel} on {deviceLabel} ({modeLabel}).{vendorWarning}";
        }

        return _options.WakeWordEnabled
            ? _wakeWordDetector is null
                ? $"Listening for \"{_options.WakePhrase}\" with {providerLabel} on {deviceLabel} ({modeLabel}, {tuningLabel}).{vendorWarning}"
                : $"Listening for \"{_options.WakePhrase}\" with {_wakeWordDetector.DescriptionLabel} and {providerLabel} on {deviceLabel} ({modeLabel}, {tuningLabel}).{vendorWarning}"
            : $"Listening for voice directives with {providerLabel} on {deviceLabel} ({modeLabel}, {tuningLabel}).{vendorWarning}";
    }

    private void ClearWakeWindow()
    {
        _wakeWindowTimer?.Dispose();
        _wakeWindowTimer = null;
    }

    private void AppendToPreRoll(byte[] chunk)
    {
        if (_preRollByteLimit <= 0)
        {
            return;
        }

        _preRollChunks.Enqueue(chunk);
        _preRollBytes += chunk.Length;

        while (_preRollBytes > _preRollByteLimit && _preRollChunks.Count > 0)
        {
            _preRollBytes -= _preRollChunks.Dequeue().Length;
        }
    }

    private void EnsureActiveSpeechBuffer()
    {
        if (_activeSpeechBuffer is not null)
        {
            return;
        }

        _activeSpeechBuffer = new MemoryStream();

        foreach (var chunk in _preRollChunks)
        {
            _activeSpeechBuffer.Write(chunk, 0, chunk.Length);
        }
    }

    private byte[]? FinalizeSpeechBuffer()
    {
        if (_activeSpeechBuffer is null)
        {
            return null;
        }

        var utterance = _activeSpeechBuffer.ToArray();
        _activeSpeechBuffer.Dispose();
        _activeSpeechBuffer = null;
        return utterance.Length == 0 ? null : utterance;
    }

    private void ClearSpeechBuffers()
    {
        _activeSpeechBuffer?.Dispose();
        _activeSpeechBuffer = null;
        _preRollChunks.Clear();
        _preRollBytes = 0;
    }

    private void TryActivateFromAudio(byte[] utteranceBytes)
    {
        if (_wakeWordDetector is null)
        {
            return;
        }

        var detection = _wakeWordDetector.Detect(utteranceBytes, RealtimeSampleRateHz);

        if (!detection.IsDetected)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(detection.InlineCommand))
        {
            ResetToIdle();
            PublishRecognizedCommand(
                JarvisCommandCatalog.NormalizeVoiceDirective(detection.InlineCommand),
                detection.RecognizedText,
                detection.Confidence,
                GetMinimumRecognitionConfidence());
            return;
        }

        ArmWakeWindow();
    }

    private async Task<string> EnsureVendorDspAppliedAsync(CancellationToken cancellationToken)
    {
        if (_vendorDspControl is null)
        {
            return string.Empty;
        }

        return await _vendorDspControl.EnsureAppliedAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
    }

    private async Task ReleaseVendorDspAsync()
    {
        if (_vendorDspControl is null)
        {
            return;
        }

        try
        {
            await _vendorDspControl.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private WaveInEvent BuildWaveIn()
    {
        var waveIn = new WaveInEvent
        {
            BufferMilliseconds = _audioProcessing.RealtimeCaptureBufferMilliseconds,
            WaveFormat = new WaveFormat(RealtimeSampleRateHz, 16, 1)
        };

        var deviceNumber = AudioDeviceResolver.GetPreferredInputDeviceIndex(_options.SpeechRecognitionDeviceName);

        if (deviceNumber >= 0)
        {
            waveIn.DeviceNumber = deviceNumber;
        }

        return waveIn;
    }

    private void DisposeCapture(WaveInEvent? capture)
    {
        if (capture is null)
        {
            return;
        }

        try
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.StopRecording();
        }
        catch
        {
        }
        finally
        {
            capture.Dispose();
        }
    }

    private async Task SafeDisconnectAsync()
    {
        try
        {
            await _client.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
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

    private static string ResolveApiKey(JarvisOptions options) =>
        ApiKeyResolver.Resolve(
            options.SpeechRecognitionApiKey,
            options.SpeechRecognitionApiKeyCredentialTarget,
            options.SpeechRecognitionApiKeyEnvironmentVariable);

    private static int BytesForMilliseconds(int milliseconds, int sampleRateHz)
    {
        var samples = (int)Math.Ceiling(sampleRateHz * (milliseconds / 1000d));
        return Math.Max(0, samples * 2);
    }

    private static Uri BuildRealtimeEndpoint(Uri baseUri)
    {
        const string audioTranscriptionsSuffix = "/audio/transcriptions";
        const string audioSpeechSuffix = "/audio/speech";

        var builder = new UriBuilder(baseUri)
        {
            Scheme = string.Equals(baseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Port = baseUri.IsDefaultPort ? -1 : baseUri.Port
        };

        var path = baseUri.AbsolutePath.TrimEnd('/');

        if (path.EndsWith(audioTranscriptionsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^audioTranscriptionsSuffix.Length];
        }
        else if (path.EndsWith(audioSpeechSuffix, StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^audioSpeechSuffix.Length];
        }

        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            path = "/v1";
        }

        builder.Path = path.EndsWith("/realtime", StringComparison.OrdinalIgnoreCase)
            ? path
            : $"{path}/realtime";
        builder.Query = "intent=transcription";
        return builder.Uri;
    }

    private static double CalculateRootMeanSquareLevel(byte[] pcm16MonoChunk)
    {
        if (pcm16MonoChunk.Length < 2)
        {
            return 0;
        }

        double sumSquares = 0;
        var sampleCount = pcm16MonoChunk.Length / 2;

        for (var index = 0; index < pcm16MonoChunk.Length - 1; index += 2)
        {
            var sample = BitConverter.ToInt16(pcm16MonoChunk, index);
            var normalized = sample / 32768d;
            sumSquares += normalized * normalized;
        }

        return Math.Sqrt(sumSquares / sampleCount);
    }

    private static double CalculatePeakLevel(byte[] pcm16MonoChunk)
    {
        if (pcm16MonoChunk.Length < 2)
        {
            return 0;
        }

        double peak = 0;

        for (var index = 0; index < pcm16MonoChunk.Length - 1; index += 2)
        {
            var sample = BitConverter.ToInt16(pcm16MonoChunk, index);
            // Widen to int first: Math.Abs(short.MinValue) throws OverflowException on clipped audio.
            var normalized = Math.Abs((int)sample) / 32768d;

            if (normalized > peak)
            {
                peak = normalized;
            }
        }

        return peak;
    }

    private static string TrimStatus(string message)
    {
        const int maxLength = 220;
        var value = message.Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private double GetMinimumRecognitionConfidence() =>
        Math.Max(0.35, _options.SpeechRecognitionConfidenceThreshold - 0.20);

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

internal sealed class RealtimeTranscriptionWebSocketClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Uri _endpoint;
    private readonly string _model;
    private readonly string _language;
    private readonly string _prompt;
    private readonly string _apiKey;
    private readonly double _voiceActivityThreshold;
    private readonly int _preRollMilliseconds;
    private readonly int _silenceDurationMilliseconds;
    private readonly bool _bargeInEnabled;
    private readonly string _noiseReductionMode;
    private readonly object _sync = new();
    private readonly Queue<string> _committedItemOrder = new();
    private readonly Dictionary<string, VoiceTranscriptResult> _completedTranscripts = new(StringComparer.Ordinal);
    private ClientWebSocket? _socket;
    private Channel<string>? _outgoingMessages;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _sendTask;
    private Task? _receiveTask;
    private TaskCompletionSource<bool>? _sessionReadyTcs;

    public RealtimeTranscriptionWebSocketClient(
        Uri endpoint,
        string model,
        string language,
        string prompt,
        string apiKey,
        double voiceActivityThreshold,
        int preRollMilliseconds,
        int silenceDurationMilliseconds,
        bool bargeInEnabled,
        string? noiseReductionMode)
    {
        _endpoint = endpoint;
        _model = model;
        _language = language;
        _prompt = prompt;
        _apiKey = apiKey;
        _voiceActivityThreshold = voiceActivityThreshold;
        _preRollMilliseconds = preRollMilliseconds;
        _silenceDurationMilliseconds = silenceDurationMilliseconds;
        _bargeInEnabled = bargeInEnabled;
        _noiseReductionMode = noiseReductionMode?.Trim() ?? string.Empty;
    }

    public event Action<bool>? SpeechActivityChanged;

    public event Action<VoiceTranscriptResult>? TranscriptCompleted;

    public event Action<string>? ErrorReceived;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_socket is { State: WebSocketState.Open })
        {
            return;
        }

        var socket = new ClientWebSocket();

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
        }

        var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sessionReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await socket.ConnectAsync(_endpoint, cancellationToken);

        lock (_sync)
        {
            _socket = socket;
            _outgoingMessages = Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
            _lifetimeCts = lifetimeCts;
            _sessionReadyTcs = sessionReadyTcs;
            _committedItemOrder.Clear();
            _completedTranscripts.Clear();
        }

        _sendTask = Task.Run(() => SendLoopAsync(lifetimeCts.Token), lifetimeCts.Token);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(lifetimeCts.Token), lifetimeCts.Token);
        await SendAsync(BuildSessionUpdatePayload(), cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
        await sessionReadyTcs.Task.WaitAsync(timeoutCts.Token);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        ClientWebSocket? socket;
        Channel<string>? outgoingMessages;
        CancellationTokenSource? lifetimeCts;
        Task? sendTask;
        Task? receiveTask;

        lock (_sync)
        {
            socket = _socket;
            _socket = null;
            outgoingMessages = _outgoingMessages;
            _outgoingMessages = null;
            lifetimeCts = _lifetimeCts;
            _lifetimeCts = null;
            sendTask = _sendTask;
            receiveTask = _receiveTask;
            _sendTask = null;
            _receiveTask = null;
            _sessionReadyTcs = null;
            _committedItemOrder.Clear();
            _completedTranscripts.Clear();
        }

        lifetimeCts?.Cancel();
        outgoingMessages?.Writer.TryComplete();

        if (socket is not null)
        {
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client disconnect", cancellationToken);
                }
            }
            catch
            {
            }
            finally
            {
                socket.Dispose();
            }
        }

        if (sendTask is not null)
        {
            try
            {
                await sendTask;
            }
            catch
            {
            }
        }

        if (receiveTask is not null)
        {
            try
            {
                await receiveTask;
            }
            catch
            {
            }
        }

        lifetimeCts?.Dispose();
    }

    public void EnqueueAudio(byte[] pcm16MonoChunk)
    {
        var outgoingMessages = _outgoingMessages;

        if (outgoingMessages is null)
        {
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["type"] = "input_audio_buffer.append",
            ["audio"] = Convert.ToBase64String(pcm16MonoChunk)
        };

        outgoingMessages.Writer.TryWrite(JsonSerializer.Serialize(payload, JsonOptions));
    }

    public void Dispose()
    {
        try
        {
            DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private async Task SendAsync(string payload, CancellationToken cancellationToken)
    {
        var outgoingMessages = _outgoingMessages;

        if (outgoingMessages is null)
        {
            return;
        }

        outgoingMessages.Writer.TryWrite(payload);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        var outgoingMessages = _outgoingMessages;

        if (outgoingMessages is null)
        {
            return;
        }

        try
        {
            await foreach (var payload in outgoingMessages.Reader.ReadAllAsync(cancellationToken))
            {
                var socket = _socket;

                if (socket is null || socket.State != WebSocketState.Open)
                {
                    continue;
                }

                var bytes = Encoding.UTF8.GetBytes(payload);
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ErrorReceived?.Invoke(exception.Message);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var builder = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var socket = _socket;

                if (socket is null || socket.State != WebSocketState.Open)
                {
                    return;
                }

                builder.Clear();

                while (true)
                {
                    var result = await socket.ReceiveAsync(buffer, cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        break;
                    }
                }

                HandleIncomingMessage(builder.ToString());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _sessionReadyTcs?.TrySetException(exception);
            ErrorReceived?.Invoke(exception.Message);
        }
    }

    private void HandleIncomingMessage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var type = typeElement.GetString() ?? string.Empty;

        switch (type)
        {
            case "session.created":
            case "session.updated":
                _sessionReadyTcs?.TrySetResult(true);
                break;

            case "input_audio_buffer.speech_started":
                SpeechActivityChanged?.Invoke(true);
                break;

            case "input_audio_buffer.speech_stopped":
                SpeechActivityChanged?.Invoke(false);
                break;

            case "input_audio_buffer.committed":
                HandleCommittedEvent(root);
                break;

            case "conversation.item.input_audio_transcription.completed":
                HandleCompletedTranscript(root);
                break;

            case "error":
                HandleErrorEvent(root);
                break;
        }
    }

    private void HandleCommittedEvent(JsonElement root)
    {
        if (!TryGetString(root, "item_id", out var itemId))
        {
            return;
        }

        lock (_sync)
        {
            _committedItemOrder.Enqueue(itemId);
        }
    }

    private void HandleCompletedTranscript(JsonElement root)
    {
        if (!TryGetString(root, "item_id", out var itemId))
        {
            return;
        }

        var transcript = TryGetString(root, "transcript", out var value)
            ? value
            : string.Empty;

        List<VoiceTranscriptResult>? ready = null;
        var confidence = TryResolveTranscriptConfidence(root);

        lock (_sync)
        {
            _completedTranscripts[itemId] = new VoiceTranscriptResult(transcript, confidence);

            while (_committedItemOrder.Count > 0
                && _completedTranscripts.TryGetValue(_committedItemOrder.Peek(), out var nextTranscript))
            {
                ready ??= new List<VoiceTranscriptResult>();
                _completedTranscripts.Remove(_committedItemOrder.Dequeue());
                ready.Add(nextTranscript);
            }
        }

        if (ready is null)
        {
            return;
        }

        foreach (var completedTranscript in ready)
        {
            TranscriptCompleted?.Invoke(completedTranscript);
        }
    }

    private void HandleErrorEvent(JsonElement root)
    {
        var message = TryResolveErrorMessage(root);
        _sessionReadyTcs?.TrySetException(new InvalidOperationException(message));
        ErrorReceived?.Invoke(message);
    }

    private string BuildSessionUpdatePayload()
    {
        var turnDetection = new Dictionary<string, object?>
        {
            ["type"] = "server_vad",
            ["threshold"] = Math.Clamp(_voiceActivityThreshold * 18.0, 0.20, 0.80),
            ["prefix_padding_ms"] = Math.Clamp(_preRollMilliseconds, 120, 800),
            ["silence_duration_ms"] = Math.Clamp(_silenceDurationMilliseconds, 250, 1400),
            ["create_response"] = false,
            ["interrupt_response"] = _bargeInEnabled
        };

        var session = new Dictionary<string, object?>
        {
            ["type"] = "transcription",
            ["include"] = new[] { "item.input_audio_transcription.logprobs" },
            ["audio"] = new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["format"] = new Dictionary<string, object?>
                    {
                        ["type"] = "audio/pcm",
                        ["rate"] = 24000
                    },
                    ["transcription"] = new Dictionary<string, object?>
                    {
                        ["model"] = _model,
                        ["language"] = string.IsNullOrWhiteSpace(_language) ? null : _language,
                        ["prompt"] = string.IsNullOrWhiteSpace(_prompt) ? null : _prompt
                    },
                    ["turn_detection"] = turnDetection,
                    ["noise_reduction"] = BuildNoiseReduction()
                }
            }
        };

        return JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["type"] = "session.update",
                ["session"] = session
            },
            JsonOptions);
    }

    private Dictionary<string, object?>? BuildNoiseReduction()
    {
        if (string.IsNullOrWhiteSpace(_noiseReductionMode))
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["type"] = _noiseReductionMode
        };
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        if (root.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string TryResolveErrorMessage(JsonElement root)
    {
        if (root.TryGetProperty("error", out var errorElement))
        {
            if (errorElement.ValueKind == JsonValueKind.Object
                && TryGetString(errorElement, "message", out var nestedMessage)
                && !string.IsNullOrWhiteSpace(nestedMessage))
            {
                return nestedMessage;
            }

            if (errorElement.ValueKind == JsonValueKind.String)
            {
                return errorElement.GetString() ?? "Unknown realtime error.";
            }
        }

        return "Unknown realtime error.";
    }

    private static double? TryResolveTranscriptConfidence(JsonElement root)
    {
        if (!root.TryGetProperty("logprobs", out var logProbElement)
            || logProbElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<double>();

        foreach (var item in logProbElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var directValue))
            {
                values.Add(directValue);
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryGetDouble(item, "logprob", out var logProb))
            {
                values.Add(logProb);
            }
        }

        return VoiceRecognitionConfidence.FromLogProbabilities(values);
    }

    private static bool TryGetDouble(JsonElement root, string propertyName, out double value)
    {
        if (root.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }
}
