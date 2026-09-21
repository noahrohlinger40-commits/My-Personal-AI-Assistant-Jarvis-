using Jarvis.Core;
using NAudio.Wave;

namespace Jarvis.App;

internal sealed record AudioLevelSampleSnapshot(
    double AverageRms,
    double Rms90,
    double Peak95,
    double MaxPeak,
    int ChunkCount);

internal sealed class MicrophoneCalibrationService
{
    public Task<AudioLevelSampleSnapshot> CaptureSampleAsync(
        JarvisOptions options,
        int durationMilliseconds,
        CancellationToken cancellationToken)
    {
        return CaptureSampleCoreAsync(options, durationMilliseconds, stimulus: null, cancellationToken);
    }

    public Task<AudioLevelSampleSnapshot> CapturePlaybackLeakageAsync(
        JarvisOptions options,
        int durationMilliseconds,
        CancellationToken cancellationToken)
    {
        return CaptureSampleCoreAsync(
            options,
            durationMilliseconds,
            token => PlayValidationToneAsync(durationMilliseconds, token),
            cancellationToken);
    }

    public async Task<double> GetCurrentAmbientRmsAsync(JarvisOptions options, CancellationToken cancellationToken = default)
    {
        var snapshot = await CaptureSampleAsync(options, 800, cancellationToken);
        return Math.Max(snapshot.AverageRms, snapshot.Rms90);
    }

    public SpeechRecognitionCalibrationProfile CreateCalibrationProfile(
        JarvisOptions options,
        AudioLevelSampleSnapshot ambient,
        AudioLevelSampleSnapshot speech,
        string beamformingProfileName,
        string hardwareDspProfileName,
        string vendorDspProfileName,
        string vendorDspProfilePath)
    {
        var ambientFloor = Math.Max(ambient.AverageRms, ambient.Rms90);
        var speechRms = Math.Max(speech.AverageRms, speech.Rms90 * 0.90);
        var speechPeak = Math.Max(speech.Peak95, speech.MaxPeak * 0.92);
        var speechSeparation = Math.Max(0.010, speechRms - ambientFloor);
        var threshold = Math.Clamp(
            Math.Max(ambientFloor * 2.60, Math.Min(speechRms * 0.30, ambientFloor + (speechSeparation * 0.32))),
            0.0030,
            0.040);
        var targetPeak = Math.Clamp(
            speechPeak switch
            {
                < 0.12 => 0.42,
                < 0.18 => 0.38,
                > 0.34 => 0.30,
                _ => 0.35
            },
            0.28,
            0.44);
        var gainCap = Math.Clamp(targetPeak / Math.Max(speechPeak, 0.08), 1.25, 4.80);

        return new SpeechRecognitionCalibrationProfile
        {
            DeviceName = options.SpeechRecognitionDeviceName?.Trim() ?? string.Empty,
            CapturedUtc = DateTimeOffset.UtcNow,
            AmbientRmsLevel = Math.Clamp(ambientFloor, 0.0015, 0.10),
            SpeechRmsLevel = Math.Clamp(speechRms, 0.008, 0.40),
            SpeechPeakLevel = Math.Clamp(speechPeak, 0.030, 1.0),
            RecommendedVoiceActivityThreshold = threshold,
            RecommendedInputGainCap = gainCap,
            RecommendedTargetPeakLevel = targetPeak,
            PreferredBeamformingProfile = string.IsNullOrWhiteSpace(beamformingProfileName) ? "auto" : beamformingProfileName.Trim(),
            HardwareDspProfileName = string.IsNullOrWhiteSpace(hardwareDspProfileName) ? "auto" : hardwareDspProfileName.Trim(),
            VendorDspProfileName = string.IsNullOrWhiteSpace(vendorDspProfileName) ? string.Empty : vendorDspProfileName.Trim(),
            VendorDspProfilePath = VendorDspProfileCatalog.NormalizeStoredPath(vendorDspProfilePath),
            Notes = $"ambient {ambientFloor:P1}, speech {speechRms:P1}, peak {speechPeak:P1}"
        };
    }

    public SpeechRecognitionValidationProfile CreateValidationProfile(
        JarvisOptions options,
        AudioLevelSampleSnapshot ambient,
        AudioLevelSampleSnapshot wakePhrase,
        AudioLevelSampleSnapshot speech,
        AudioLevelSampleSnapshot playbackLeakage,
        string wakeWordAssetName,
        string wakeWordAssetPath,
        string wakeEngineName,
        string wakeEnginePath,
        string beamformingProfileName,
        string hardwareDspProfileName,
        string vendorDspProfileName,
        string vendorDspProfilePath)
    {
        var ambientFloor = Math.Max(ambient.AverageRms, ambient.Rms90);
        var wakeRms = Math.Max(wakePhrase.AverageRms, wakePhrase.Rms90 * 0.95);
        var speechRms = Math.Max(speech.AverageRms, speech.Rms90 * 0.90);
        var leakageRms = Math.Max(playbackLeakage.AverageRms, playbackLeakage.Rms90);
        var wakeMargin = wakeRms / Math.Max(ambientFloor, 0.0015);
        var speechMargin = speechRms / Math.Max(ambientFloor, 0.0015);
        var duplexMargin = speechRms / Math.Max(leakageRms, 0.0015);
        var wakeScore = Math.Clamp((wakeMargin - 1.50) / 4.50, 0.0, 1.0);
        var fullDuplexScore = Math.Clamp((duplexMargin - 1.10) / 3.20, 0.0, 1.0);
        var recommendedMode = fullDuplexScore >= 0.58 && speechMargin >= 3.0
            ? "full-duplex"
            : "half-duplex";

        return new SpeechRecognitionValidationProfile
        {
            DeviceName = options.SpeechRecognitionDeviceName?.Trim() ?? string.Empty,
            ValidatedUtc = DateTimeOffset.UtcNow,
            WakeWordAssetName = string.IsNullOrWhiteSpace(wakeWordAssetName) ? "Recognizer Default" : wakeWordAssetName.Trim(),
            WakeWordAssetPath = WakeWordAssetCatalog.NormalizeStoredPath(wakeWordAssetPath),
            WakeEngineName = string.IsNullOrWhiteSpace(wakeEngineName) ? "Recognizer Wake" : wakeEngineName.Trim(),
            WakeEnginePath = WakeWordEngineCatalog.NormalizeStoredPath(wakeEnginePath),
            BeamformingProfileName = string.IsNullOrWhiteSpace(beamformingProfileName) ? "auto" : beamformingProfileName.Trim(),
            HardwareDspProfileName = string.IsNullOrWhiteSpace(hardwareDspProfileName) ? "auto" : hardwareDspProfileName.Trim(),
            VendorDspProfileName = string.IsNullOrWhiteSpace(vendorDspProfileName) ? string.Empty : vendorDspProfileName.Trim(),
            VendorDspProfilePath = VendorDspProfileCatalog.NormalizeStoredPath(vendorDspProfilePath),
            AmbientRmsLevel = Math.Clamp(ambientFloor, 0.0015, 0.10),
            WakePhraseRmsLevel = Math.Clamp(wakeRms, 0.0030, 0.50),
            CommandSpeechRmsLevel = Math.Clamp(speechRms, 0.0060, 0.50),
            PlaybackLeakageRmsLevel = Math.Clamp(leakageRms, 0.0010, 0.30),
            WakeWordReadinessScore = wakeScore,
            FullDuplexReadinessScore = fullDuplexScore,
            RecommendedConversationMode = recommendedMode,
            Notes = $"wake {wakeMargin:F1}x ambient, speech {speechMargin:F1}x ambient, leakage {leakageRms:P1}"
        };
    }

    private async Task<AudioLevelSampleSnapshot> CaptureSampleCoreAsync(
        JarvisOptions options,
        int durationMilliseconds,
        Func<CancellationToken, Task>? stimulus,
        CancellationToken cancellationToken)
    {
        if (durationMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        }

        var deviceIndex = AudioDeviceResolver.GetPreferredInputDeviceIndex(options.SpeechRecognitionDeviceName);

        if (deviceIndex < 0)
        {
            throw new InvalidOperationException("No microphone is available for calibration.");
        }

        var rmsLevels = new List<double>();
        var peakLevels = new List<double>();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var stimulusCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var capture = new WaveInEvent
        {
            DeviceNumber = deviceIndex,
            BufferMilliseconds = 40,
            WaveFormat = new WaveFormat(options.SpeechRecognitionSampleRateHz, 16, 1)
        };

        void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
        {
            if (eventArgs.BytesRecorded <= 0)
            {
                return;
            }

            var chunk = new byte[eventArgs.BytesRecorded];
            Buffer.BlockCopy(eventArgs.Buffer, 0, chunk, 0, eventArgs.BytesRecorded);
            rmsLevels.Add(CalculateRootMeanSquareLevel(chunk));
            peakLevels.Add(CalculatePeakLevel(chunk));
        }

        void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
        {
            if (eventArgs.Exception is not null)
            {
                completion.TrySetException(eventArgs.Exception);
                return;
            }

            completion.TrySetResult(null);
        }

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;

        try
        {
            capture.StartRecording();

            var stimulusTask = stimulus is null
                ? Task.CompletedTask
                : stimulus(stimulusCts.Token);

            await Task.Delay(durationMilliseconds, cancellationToken);
            capture.StopRecording();
            await completion.Task.WaitAsync(cancellationToken);
            stimulusCts.Cancel();

            try
            {
                await stimulusTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
        finally
        {
            stimulusCts.Cancel();
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;

            try
            {
                capture.StopRecording();
            }
            catch
            {
            }
        }

        return new AudioLevelSampleSnapshot(
            AverageRms: rmsLevels.Count == 0 ? 0 : rmsLevels.Average(),
            Rms90: Percentile(rmsLevels, 0.90),
            Peak95: Percentile(peakLevels, 0.95),
            MaxPeak: peakLevels.Count == 0 ? 0 : peakLevels.Max(),
            ChunkCount: rmsLevels.Count);
    }

    private static async Task PlayValidationToneAsync(int captureDurationMilliseconds, CancellationToken cancellationToken)
    {
        const int sampleRate = 48000;
        var toneDurationMilliseconds = Math.Clamp(captureDurationMilliseconds - 280, 520, 920);
        var toneBytes = BuildValidationTonePcm(sampleRate, toneDurationMilliseconds);

        await Task.Delay(160, cancellationToken);

        using var output = new WaveOutEvent
        {
            DesiredLatency = 60,
            NumberOfBuffers = 2
        };
        using var stream = new RawSourceWaveStream(new MemoryStream(toneBytes, writable: false), new WaveFormat(sampleRate, 16, 1));

        output.Init(stream);
        output.Play();

        try
        {
            while (output.PlaybackState == PlaybackState.Playing)
            {
                await Task.Delay(30, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                output.Stop();
            }
            catch
            {
            }

            throw;
        }
    }

    private static byte[] BuildValidationTonePcm(int sampleRate, int durationMilliseconds)
    {
        var sampleCount = (int)Math.Ceiling(sampleRate * (durationMilliseconds / 1000d));
        var bytes = new byte[sampleCount * 2];
        var durationSeconds = durationMilliseconds / 1000d;

        for (var index = 0; index < sampleCount; index++)
        {
            var timeSeconds = index / (double)sampleRate;
            var sweepProgress = durationSeconds <= 0 ? 0 : Math.Clamp(timeSeconds / durationSeconds, 0, 1);
            var frequency = 420 + (sweepProgress * 420);
            var fadeIn = Math.Min(1.0, timeSeconds / 0.05);
            var fadeOut = Math.Min(1.0, Math.Max(0, durationSeconds - timeSeconds) / 0.08);
            var envelope = Math.Min(fadeIn, fadeOut);
            var sample = Math.Sin(2 * Math.PI * frequency * timeSeconds) * 0.24 * envelope;
            var pcmSample = (short)Math.Round(sample * short.MaxValue);
            var offset = index * 2;
            var encoded = BitConverter.GetBytes(pcmSample);
            bytes[offset] = encoded[0];
            bytes[offset + 1] = encoded[1];
        }

        return bytes;
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var ordered = values.OrderBy(value => value).ToArray();
        var index = Math.Clamp((int)Math.Round((ordered.Length - 1) * percentile), 0, ordered.Length - 1);
        return ordered[index];
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
}
