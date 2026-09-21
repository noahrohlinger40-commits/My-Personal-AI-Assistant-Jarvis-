using Jarvis.Core;

namespace Jarvis.App;

internal readonly record struct VoiceEchoControlResult(
    bool IsPlaybackActive,
    bool IsLikelyEcho,
    bool ShouldSuppress,
    bool ShouldRequestBargeIn);

internal sealed class VoiceAcousticEchoSuppressor
{
    private readonly object _sync = new();
    private readonly JarvisOptions _options;
    private readonly ResolvedMicrophoneProcessingProfile _profile;
    private DateTimeOffset _lastBargeInUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _playbackStartedUtc = DateTimeOffset.MinValue;
    private double _ambientNoiseFloor;
    private double _playbackEchoFloor;
    private bool _isPlaybackActive;

    public VoiceAcousticEchoSuppressor(JarvisOptions options, ResolvedMicrophoneProcessingProfile profile)
    {
        _options = options;
        _profile = profile;
        _ambientNoiseFloor = Math.Max(
            profile.AmbientNoiseFloor * profile.AmbientFloorScale,
            profile.VoiceActivityThreshold * (profile.HardwareNoiseSuppressionEnabled ? 0.42 : 0.50) * profile.AmbientFloorScale);
        _playbackEchoFloor = Math.Max(
            (profile.HardwareEchoCancellationEnabled ? 0.008 : 0.012) * profile.PlaybackEchoFloorScale,
            profile.VoiceActivityThreshold * 1.50 * profile.EchoGateScale * profile.PlaybackEchoFloorScale);
    }

    public void SetPlaybackState(bool isSpeaking)
    {
        lock (_sync)
        {
            if (_isPlaybackActive == isSpeaking)
            {
                return;
            }

            _isPlaybackActive = isSpeaking;
            _playbackStartedUtc = isSpeaking ? DateTimeOffset.UtcNow : DateTimeOffset.MinValue;

            if (!isSpeaking)
            {
                _playbackEchoFloor = Math.Max(_playbackEchoFloor * 0.75, _ambientNoiseFloor * 1.50);
            }
        }
    }

    public VoiceEchoControlResult Process(
        double rmsLevel,
        double peakLevel,
        bool isCapturingSpeech,
        DateTimeOffset timestampUtc)
    {
        var effectiveLevel = Math.Max(rmsLevel, peakLevel * 0.42);

        lock (_sync)
        {
            if (!_isPlaybackActive)
            {
                UpdateAmbientFloor(effectiveLevel);
                return new VoiceEchoControlResult(false, false, false, false);
            }

            var playbackAge = timestampUtc - _playbackStartedUtc;

            if (playbackAge.TotalMilliseconds < 350)
            {
                UpdatePlaybackEchoFloor(effectiveLevel);
                return new VoiceEchoControlResult(
                    true,
                    true,
                    _options.SpeechRecognitionEchoCancellationEnabled,
                    false);
            }

            var floor = Math.Max(_playbackEchoFloor, _ambientNoiseFloor * 2.20);
            var bargeInThreshold = Math.Clamp(
                Math.Max(
                    _profile.VoiceActivityThreshold * (_profile.HardwareEchoCancellationEnabled ? 6.1 : 7.0) * _profile.EchoGateScale,
                    floor * (_profile.HardwareEchoCancellationEnabled ? 2.50 : 2.85)) * _profile.BargeInThresholdScale,
                0.045,
                0.18);
            var continuingSpeechThreshold = Math.Clamp(
                Math.Max(
                    _profile.VoiceActivityThreshold * (_profile.HardwareEchoCancellationEnabled ? 3.4 : 4.0) * _profile.EchoGateScale,
                    floor * (_profile.HardwareEchoCancellationEnabled ? 1.62 : 1.90)) * _profile.ContinuingSpeechThresholdScale,
                0.030,
                0.14);
            var speechThreshold = isCapturingSpeech ? continuingSpeechThreshold : bargeInThreshold;
            var likelyUserSpeech = effectiveLevel >= speechThreshold && peakLevel >= _profile.MinimumSpeechPeakLevel;

            if (!likelyUserSpeech)
            {
                UpdatePlaybackEchoFloor(effectiveLevel);
            }

            var shouldRequestBargeIn = _options.SpeechRecognitionBargeInEnabled
                && likelyUserSpeech
                && timestampUtc - _lastBargeInUtc > TimeSpan.FromMilliseconds(1200);

            if (shouldRequestBargeIn)
            {
                _lastBargeInUtc = timestampUtc;
            }

            var shouldSuppress = _options.SpeechRecognitionEchoCancellationEnabled
                && !likelyUserSpeech
                && effectiveLevel > 0.001;

            return new VoiceEchoControlResult(
                IsPlaybackActive: true,
                IsLikelyEcho: !likelyUserSpeech,
                ShouldSuppress: shouldSuppress,
                ShouldRequestBargeIn: shouldRequestBargeIn);
        }
    }

    private void UpdateAmbientFloor(double effectiveLevel)
    {
        if (effectiveLevel <= 0 || effectiveLevel > 0.08)
        {
            return;
        }

        var scaledLevel = effectiveLevel * _profile.AmbientFloorScale;
        _ambientNoiseFloor = (_ambientNoiseFloor * 0.94) + (scaledLevel * 0.06);
        _ambientNoiseFloor = Math.Clamp(_ambientNoiseFloor, 0.0015, 0.08);
    }

    private void UpdatePlaybackEchoFloor(double effectiveLevel)
    {
        if (effectiveLevel <= 0)
        {
            return;
        }

        var sample = Math.Clamp(effectiveLevel * _profile.PlaybackEchoFloorScale, 0.002, 0.20);
        _playbackEchoFloor = (_playbackEchoFloor * 0.82) + (sample * 0.18);
        _playbackEchoFloor = Math.Clamp(_playbackEchoFloor, 0.006, 0.20);
    }
}
