using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jarvis.Core;
using NAudio.Wave;

namespace Jarvis.App;

internal sealed class OpenAiCompatibleVoiceRuntime : IVoiceRuntime
{
    // A single spoken command is far shorter than this; the cap only matters for steady background sound.
    private const int MaximumUtteranceMilliseconds = 15_000;

    // While Jarvis talks through a speaker its own voice never leaves the 850 ms of quiet that ends an
    // utterance, so "Jarvis, stop" would wait inside a 15 s piece of echo. Short pieces get it heard quickly;
    // the pre-roll overlap keeps a wake word cut at a boundary whole.
    private const int PlaybackUtteranceMilliseconds = 3_000;

    private readonly object _sync = new();
    private readonly JarvisOptions _options;
    private readonly OpenAiCompatibleTranscriptionClient _client;
    private readonly SemaphoreSlim _transcriptionGate = new(1, 1);
    private readonly ClapDetector? _clapDetector;
    private readonly WakeWordDetector? _wakeWordDetector;
    private readonly ResolvedMicrophoneProcessingProfile _audioProcessing;
    private readonly VendorDspControlSession? _vendorDspControl;
    private readonly VoiceAcousticEchoSuppressor _echoSuppressor;
    private readonly Queue<byte[]> _preRollChunks = new();
    private readonly int _preRollByteLimit;
    private readonly int _minimumUtteranceBytes;
    private readonly int _maximumUtteranceBytes;
    private readonly int _playbackUtteranceBytes;
    private WaveInEvent? _capture;
    private CancellationTokenSource? _lifetimeCts;
    private MemoryStream? _activeSpeechBuffer;
    private System.Threading.Timer? _wakeWindowTimer;
    private DateTimeOffset _lastVoiceDetectedUtc;
    private int _preRollBytes;
    private int _pendingUtterances;
    private bool _isListening;
    private bool _isArmed;
    private double _noiseFloor;
    private double _smoothedInputGain = 1.0;
    private string _vendorDspWarning = string.Empty;

    public OpenAiCompatibleVoiceRuntime(JarvisOptions options, OpenAiCompatibleTranscriptionClient client)
    {
        _options = options;
        _client = client;
        _clapDetector = options.ClapShortcutEnabled ? new ClapDetector(options) : null;
        _audioProcessing = MicrophoneProcessingProfileResolver.Resolve(options);
        _wakeWordDetector = WakeWordDetector.Create(options, _audioProcessing);
        _vendorDspControl = VendorDspControlSession.Create(options, _audioProcessing);
        _echoSuppressor = new VoiceAcousticEchoSuppressor(options, _audioProcessing);
        _preRollByteLimit = BytesForMilliseconds(Math.Max(0, options.SpeechRecognitionPreRollMilliseconds), options.SpeechRecognitionSampleRateHz);
        _minimumUtteranceBytes = BytesForMilliseconds(Math.Max(0, options.SpeechRecognitionMinimumUtteranceMilliseconds), options.SpeechRecognitionSampleRateHz);
        _maximumUtteranceBytes = BytesForMilliseconds(MaximumUtteranceMilliseconds, options.SpeechRecognitionSampleRateHz);
        _playbackUtteranceBytes = BytesForMilliseconds(PlaybackUtteranceMilliseconds, options.SpeechRecognitionSampleRateHz);
        _noiseFloor = _audioProcessing.AmbientNoiseFloor;

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
            status = "Rolling transcription fallback is enabled, but no transcription model is configured.";
            return false;
        }

        if (!Uri.TryCreate(options.SpeechRecognitionBaseUrl, UriKind.Absolute, out var baseUri))
        {
            status = "Rolling transcription fallback base URL is invalid.";
            return false;
        }

        var apiKey = ResolveApiKey(options);

        if (string.IsNullOrWhiteSpace(apiKey)
            && !string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(baseUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            status =
                $"Rolling transcription fallback requires an API key in `speechRecognitionApiKey`, `speechRecognitionApiKeyCredentialTarget`, or `{options.SpeechRecognitionApiKeyEnvironmentVariable}`.";
            return false;
        }

        runtime = new OpenAiCompatibleVoiceRuntime(
            options,
            new OpenAiCompatibleTranscriptionClient(
                options.SpeechRecognitionBaseUrl,
                options.SpeechRecognitionModel,
                apiKey,
                options.SpeechRecognitionLanguage,
                VoiceRecognitionText.BuildTranscriptionPrompt(options)));

        status = $"Rolling transcription fallback ready with model {options.SpeechRecognitionModel}.";
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

        lock (_sync)
        {
            if (_isListening)
            {
                PublishStatus(_isArmed, CurrentStatus.StatusText);
                return;
            }

            try
            {
                _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _capture = BuildWaveIn();
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();

                _isListening = true;
                PublishSignal(0, 0, false, false);
                PublishStatus(
                    isArmed: false,
                    statusText: BuildIdleStatusText());
            }
            catch (Exception exception)
            {
                DisposeCapture();
                _isListening = false;
                PublishStatus(
                    isArmed: false,
                    statusText: $"Rolling microphone startup failed: {TrimStatus(exception.Message)}",
                    isAvailable: false);
            }
        }

        if (!CurrentStatus.IsAvailable)
        {
            await ReleaseVendorDspAsync().ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_isListening)
            {
                return;
            }

            _isListening = false;
            _isArmed = false;
            ClearWakeWindow();
            ClearSpeechBuffers();
            DisposeCapture();
            _lifetimeCts?.Cancel();
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
            PublishSignal(0, 0, false, false);
            PublishStatus(isArmed: false, statusText: "Microphone listening paused.");
        }

        _vendorDspWarning = string.Empty;
        await ReleaseVendorDspAsync().ConfigureAwait(false);
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

        _transcriptionGate.Dispose();
        _wakeWordDetector?.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (eventArgs.BytesRecorded <= 0)
        {
            return;
        }

        var chunk = new byte[eventArgs.BytesRecorded];
        Buffer.BlockCopy(eventArgs.Buffer, 0, chunk, 0, eventArgs.BytesRecorded);

        byte[]? utterance = null;
        var originalLevel = CalculateRootMeanSquareLevel(chunk);
        var originalPeak = CalculatePeakLevel(chunk);
        double level = 0;
        double peakLevel = 0;
        bool detectedSpeech = false;
        bool clapDetected = false;
        bool wakeWindowOpen = false;
        bool isCapturingSpeech;

        lock (_sync)
        {
            if (!_isListening)
            {
                return;
            }

            wakeWindowOpen = _isArmed;
            isCapturingSpeech = _activeSpeechBuffer is not null;
        }

        var echoControl = _echoSuppressor.Process(originalLevel, originalPeak, isCapturingSpeech, DateTimeOffset.UtcNow);

        if (echoControl.ShouldRequestBargeIn)
        {
            BargeInRequested?.Invoke(this, new VoiceBargeInEventArgs("speech-over-playback"));
        }

        if (echoControl.ShouldSuppress)
        {
            if (isCapturingSpeech)
            {
                lock (_sync)
                {
                    if (_activeSpeechBuffer is not null
                        && DateTimeOffset.UtcNow - _lastVoiceDetectedUtc >= TimeSpan.FromMilliseconds(_options.SpeechRecognitionSilenceDurationMilliseconds))
                    {
                        utterance = FinalizeSpeechBuffer();
                    }
                }
            }

            PublishSignal(0, 0, false, false, wakeWindowOpen);

            if (utterance is not null)
            {
                _ = Task.Run(() => ProcessUtteranceAsync(utterance));
            }

            return;
        }

        lock (_sync)
        {
            if (!_isListening)
            {
                return;
            }

            ApplyInputGain(chunk, originalLevel, originalPeak);

            AppendToPreRoll(chunk);

            level = CalculateRootMeanSquareLevel(chunk);
            peakLevel = CalculatePeakLevel(chunk);
            detectedSpeech = IsSpeechChunk(level, peakLevel);
            wakeWindowOpen = _isArmed;

            if (_clapDetector is not null
                && _clapDetector.ProcessSample(peakLevel, DateTimeOffset.UtcNow))
            {
                clapDetected = true;
                ActivationRequested?.Invoke(this, new VoiceActivationRequestedEventArgs("double-clap"));
            }

            if (detectedSpeech)
            {
                _lastVoiceDetectedUtc = DateTimeOffset.UtcNow;

                if (_activeSpeechBuffer is null)
                {
                    _activeSpeechBuffer = new MemoryStream();
                    foreach (var bufferedChunk in _preRollChunks)
                    {
                        _activeSpeechBuffer.Write(bufferedChunk, 0, bufferedChunk.Length);
                    }
                }

                _activeSpeechBuffer.Write(chunk, 0, chunk.Length);

                // Steady background sound never produces the silence that normally ends an utterance,
                // so send what we have and start a fresh one instead of recording without limit.
                if (_activeSpeechBuffer.Length >= (echoControl.IsPlaybackActive ? _playbackUtteranceBytes : _maximumUtteranceBytes))
                {
                    utterance = FinalizeSpeechBuffer();
                }
            }
            else if (_activeSpeechBuffer is not null)
            {
                _activeSpeechBuffer.Write(chunk, 0, chunk.Length);

                var silenceDuration = DateTimeOffset.UtcNow - _lastVoiceDetectedUtc;

                if (silenceDuration.TotalMilliseconds >= _options.SpeechRecognitionSilenceDurationMilliseconds)
                {
                    utterance = FinalizeSpeechBuffer();
                }
            }
        }

        PublishSignal(level, peakLevel, detectedSpeech, clapDetected, wakeWindowOpen);

        if (utterance is not null)
        {
            _ = Task.Run(() => ProcessUtteranceAsync(utterance));
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        lock (_sync)
        {
            if (!_isListening)
            {
                return;
            }

            _isListening = false;
            _isArmed = false;
            ClearWakeWindow();
            ClearSpeechBuffers();
            DisposeCapture();
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
        }

        _vendorDspWarning = string.Empty;
        _ = ReleaseVendorDspAsync();

        var statusText = eventArgs.Exception is null
            ? "Rolling speech recognition stopped."
            : $"Rolling speech recognition stopped: {TrimStatus(eventArgs.Exception.Message)}";

        PublishSignal(0, 0, false, false);
        PublishStatus(isArmed: false, statusText: statusText, isAvailable: eventArgs.Exception is null);
    }

    private async Task ProcessUtteranceAsync(byte[] utteranceBytes)
    {
        try
        {
            await TranscribeUtteranceAsync(utteranceBytes);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingUtterances);
        }
    }

    private async Task TranscribeUtteranceAsync(byte[] utteranceBytes)
    {
        var lifetimeToken = _lifetimeCts?.Token ?? CancellationToken.None;
        // The built-in wake check takes up to a few seconds, so run it alongside transcription, not before it.
        // Skipped while Jarvis is talking: every piece of its echo would wait on it and the queue would fall
        // behind, and interrupting needs the wake word in the transcript anyway.
        var wakeHintTask = _echoSuppressor.IsPlaybackActive
            ? Task.FromResult<WakeWordDetectionResult?>(null)
            : Task.Run(() => TryDetectWakeWord(utteranceBytes));

        try
        {
            await _transcriptionGate.WaitAsync(lifetimeToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var transcript = await _client.TranscribeAsync(utteranceBytes, _options.SpeechRecognitionSampleRateHz, lifetimeToken);
            await HandleRecognizedTextAsync(transcript.Text.Trim(), transcript.Confidence, wakeHintTask);
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpRequestException) when (IsLocalSpeechEndpoint())
        {
            // A refused connection to localhost means the bundled speech server is not running.
            PublishStatus(
                isArmed: _isArmed,
                statusText: "The local speech server is not running, so Jarvis cannot turn your voice into text. "
                    + "See data\\whisper-server.log, or run scripts\\Setup-LocalSpeech.ps1.");
        }
        catch (Exception exception)
        {
            PublishStatus(
                isArmed: _isArmed,
                statusText: $"Rolling transcription error: {TrimStatus(exception.Message)}");
        }
        finally
        {
            _transcriptionGate.Release();
        }
    }

    private bool IsLocalSpeechEndpoint() =>
        Uri.TryCreate(_options.SpeechRecognitionBaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;

    private async Task HandleRecognizedTextAsync(
        string recognizedText,
        double? recognitionConfidence,
        Task<WakeWordDetectionResult?> wakeHintTask)
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

        // Awaited only here: when the transcript already carried the wake word, the slower built-in
        // check (up to 2.5 s) has nothing to add, so the command goes out without waiting for it.
        if (await wakeHintTask is { IsDetected: true })
        {
            // The built-in recognizer only vouches that the wake word was said. When the transcription
            // service also produced text, that text is far more accurate than the built-in recognizer's
            // own guess at the command, so prefer it and fall back to the guess only if there is none.
            if (!string.IsNullOrWhiteSpace(recognizedText))
            {
                // A leading "Harvest," is a mishearing of the wake word the hint heard. With no such word,
                // the hint is a false positive on ordinary speech, and passing it on would send the whole
                // conversation to the model as a command.
                var command = VoiceRecognitionText.StripLeadingVocative(recognizedText);

                if (command == recognizedText)
                {
                    return;
                }

                ResetToIdle();
                PublishRecognizedCommand(
                    JarvisCommandCatalog.NormalizeVoiceDirective(command),
                    rawRecognizedText,
                    recognitionConfidence,
                    minimumConfidence);
                return;
            }

            // The transcription service heard no speech, so any command the built-in recognizer guessed
            // is most likely noise ("open app if"). Treat the hint as a wake word only and let the
            // user say the command, instead of running a command nobody spoke.
            ArmWakeWindow();
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

            // The command may already be spoken and waiting on transcription, which takes seconds on a
            // CPU. Closing the window now would drop it, so wait until that speech has been handled.
            if (_activeSpeechBuffer is not null || Volatile.Read(ref _pendingUtterances) > 0)
            {
                _wakeWindowTimer?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
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
        var providerLabel = $"rolling transcription fallback ({_options.SpeechRecognitionModel})";
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

    private bool IsSpeechChunk(double level, double peakLevel)
    {
        var configuredThreshold = Math.Max(0.0035, _audioProcessing.VoiceActivityThreshold);
        var effectiveLevel = Math.Max(level, peakLevel * 0.42);
        var isContinuingSpeech = _activeSpeechBuffer is not null;
        var adaptiveThreshold = isContinuingSpeech
            ? Math.Clamp(Math.Max(configuredThreshold * 0.34, _noiseFloor * 1.95), 0.0026, configuredThreshold * 1.8)
            : Math.Clamp(Math.Max(configuredThreshold * 0.46, _noiseFloor * 2.65), 0.0030, configuredThreshold * 2.2);

        if (effectiveLevel < adaptiveThreshold)
        {
            UpdateNoiseFloor(level);
            return false;
        }

        if (!isContinuingSpeech)
        {
            _noiseFloor = Math.Max(0.0022, _noiseFloor * 0.94);
        }

        return true;
    }

    private void UpdateNoiseFloor(double level)
    {
        var sample = Math.Clamp(level, 0.0005, 0.10);
        _noiseFloor = (_noiseFloor * 0.92) + (sample * 0.08);
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

    private byte[]? FinalizeSpeechBuffer()
    {
        if (_activeSpeechBuffer is null)
        {
            return null;
        }

        var utterance = _activeSpeechBuffer.ToArray();
        _activeSpeechBuffer.Dispose();
        _activeSpeechBuffer = null;

        if (utterance.Length < _minimumUtteranceBytes)
        {
            return null;
        }

        // Every returned utterance is handed to ProcessUtteranceAsync, which decrements this.
        Interlocked.Increment(ref _pendingUtterances);
        return utterance;
    }

    private void ClearWakeWindow()
    {
        _wakeWindowTimer?.Dispose();
        _wakeWindowTimer = null;
    }

    private void ClearSpeechBuffers()
    {
        _activeSpeechBuffer?.Dispose();
        _activeSpeechBuffer = null;
        _preRollChunks.Clear();
        _preRollBytes = 0;
    }

    private WaveInEvent BuildWaveIn()
    {
        var waveIn = new WaveInEvent
        {
            BufferMilliseconds = _audioProcessing.RollingCaptureBufferMilliseconds,
            WaveFormat = new WaveFormat(_options.SpeechRecognitionSampleRateHz, 16, 1)
        };

        var deviceNumber = AudioDeviceResolver.GetPreferredInputDeviceIndex(_options.SpeechRecognitionDeviceName);

        if (deviceNumber >= 0)
        {
            waveIn.DeviceNumber = deviceNumber;
        }

        return waveIn;
    }

    private void ApplyInputGain(byte[] pcm16MonoChunk, double rmsLevel, double peakLevel)
    {
        if (pcm16MonoChunk.Length < 2)
        {
            return;
        }

        double targetGain;

        if (peakLevel < 0.004 && rmsLevel < 0.0008)
        {
            targetGain = 1.0;
        }
        else
        {
            targetGain = Math.Clamp(_audioProcessing.TargetPeakLevel / Math.Max(peakLevel, 0.025), 1.0, _audioProcessing.InputGainCap);
        }

        _smoothedInputGain = (_smoothedInputGain * 0.76) + (targetGain * 0.24);

        if (_smoothedInputGain <= 1.02)
        {
            return;
        }

        for (var index = 0; index < pcm16MonoChunk.Length - 1; index += 2)
        {
            var sample = BitConverter.ToInt16(pcm16MonoChunk, index);
            var amplified = (int)Math.Round(sample * _smoothedInputGain);
            amplified = Math.Clamp(amplified, short.MinValue, short.MaxValue);

            var bytes = BitConverter.GetBytes((short)amplified);
            pcm16MonoChunk[index] = bytes[0];
            pcm16MonoChunk[index + 1] = bytes[1];
        }
    }

    private void DisposeCapture()
    {
        if (_capture is null)
        {
            return;
        }

        try
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.StopRecording();
        }
        catch
        {
        }
        finally
        {
            _capture.Dispose();
            _capture = null;
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

    private WakeWordDetectionResult? TryDetectWakeWord(byte[] utteranceBytes)
    {
        if (_wakeWordDetector is null)
        {
            return null;
        }

        var detection = _wakeWordDetector.Detect(utteranceBytes, _options.SpeechRecognitionSampleRateHz);
        return detection.IsDetected ? detection : null;
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

    internal static string ResolveApiKey(JarvisOptions options) =>
        ApiKeyResolver.Resolve(
            options.SpeechRecognitionApiKey,
            options.SpeechRecognitionApiKeyCredentialTarget,
            options.SpeechRecognitionApiKeyEnvironmentVariable);

    private static int BytesForMilliseconds(int milliseconds, int sampleRateHz)
    {
        var samples = (int)Math.Ceiling(sampleRateHz * (milliseconds / 1000d));
        return Math.Max(0, samples * 2);
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

internal sealed class OpenAiCompatibleTranscriptionClient
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly string _language;
    private readonly string _prompt;

    public OpenAiCompatibleTranscriptionClient(
        string baseUrl,
        string model,
        string apiKey,
        string language,
        string prompt)
    {
        _endpoint = BuildEndpoint(baseUrl);
        _model = model;
        _apiKey = apiKey;
        _language = language;
        _prompt = prompt;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
    }

    public async Task<VoiceTranscriptResult> TranscribeAsync(byte[] pcm16MonoAudio, int sampleRateHz, CancellationToken cancellationToken)
    {
        var body = await SendAsync(pcm16MonoAudio, sampleRateHz, "text", cancellationToken);
        return new VoiceTranscriptResult(body.Trim());
    }

    /// <summary>The transcript as timed lines, each at its offset into the audio.</summary>
    public async Task<IReadOnlyList<TranscriptLine>> TranscribeLinesAsync(byte[] pcm16MonoAudio, int sampleRateHz, CancellationToken cancellationToken)
    {
        using var json = JsonDocument.Parse(await SendAsync(pcm16MonoAudio, sampleRateHz, "verbose_json", cancellationToken));

        return json.RootElement.GetProperty("segments").EnumerateArray()
            .Select(segment => new TranscriptLine(
                TimeSpan.FromSeconds(segment.GetProperty("start").GetDouble()),
                segment.GetProperty("text").GetString()?.Trim() ?? string.Empty))
            .Where(line => line.Text.Length > 0)
            .ToList();
    }

    private async Task<string> SendAsync(byte[] pcm16MonoAudio, int sampleRateHz, string responseFormat, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();

        var wavBytes = Pcm16WavWriter.WrapPcm16Mono(pcm16MonoAudio, sampleRateHz);
        var audioContent = new ByteArrayContent(wavBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        form.Add(audioContent, "file", "speech.wav");
        form.Add(new StringContent(_model), "model");
        form.Add(new StringContent(responseFormat), "response_format");

        if (!string.IsNullOrWhiteSpace(_language))
        {
            form.Add(new StringContent(_language), "language");
        }

        if (!string.IsNullOrWhiteSpace(_prompt))
        {
            form.Add(new StringContent(_prompt), "prompt");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = form
        };

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"speech API HTTP {(int)response.StatusCode}: {TrimForError(body)}");
        }

        return body;
    }

    private static string BuildEndpoint(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');

        if (trimmed.EndsWith("/audio/transcriptions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}/audio/transcriptions";
    }

    private static string TrimForError(string text)
    {
        const int maxLength = 500;
        var value = text.Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}

internal static class Pcm16WavWriter
{
    public static byte[] WrapPcm16Mono(byte[] pcm16MonoAudio, int sampleRateHz)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        const short channels = 1;
        const short bitsPerSample = 16;
        var byteRate = sampleRateHz * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcm16MonoAudio.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRateHz);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcm16MonoAudio.Length);
        writer.Write(pcm16MonoAudio);
        writer.Flush();

        return stream.ToArray();
    }
}
