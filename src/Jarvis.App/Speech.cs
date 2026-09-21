using System.Speech.Synthesis;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed record SpeakerStatusSnapshot(
    bool IsSpeaking,
    string Message,
    DateTimeOffset TimestampUtc);

internal sealed class SpeakerStatusChangedEventArgs : EventArgs
{
    public SpeakerStatusChangedEventArgs(SpeakerStatusSnapshot status)
    {
        Status = status;
    }

    public SpeakerStatusSnapshot Status { get; }
}

internal interface ISpeaker
{
    event EventHandler<SpeakerStatusChangedEventArgs>? StatusChanged;

    SpeakerStatusSnapshot CurrentStatus { get; }

    void Interrupt();

    Task SpeakAsync(string message, CancellationToken cancellationToken);
}

internal static class SpeakerFactory
{
    public static ISpeaker Create(JarvisOptions options)
    {
        if (!options.VoiceEnabled)
        {
            return new NullSpeaker();
        }

        var provider = NormalizeProvider(options.VoiceProvider);

        if (provider is "auto" or "openai-compatible" or "openai" or "streaming")
        {
            if (OpenAiCompatibleStreamingSpeaker.TryCreate(options, out var speaker))
            {
                return speaker!;
            }

            if (provider is "openai-compatible" or "openai" or "streaming")
            {
                return new WindowsSpeechSpeaker(options);
            }
        }

        return new WindowsSpeechSpeaker(options);
    }

    private static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return "auto";
        }

        return provider.Trim().ToLowerInvariant();
    }
}

internal sealed class NullSpeaker : ISpeaker
{
    public event EventHandler<SpeakerStatusChangedEventArgs>? StatusChanged
    {
        add { }
        remove { }
    }

    public SpeakerStatusSnapshot CurrentStatus { get; } = new(false, string.Empty, DateTimeOffset.UtcNow);

    public void Interrupt()
    {
    }

    public Task SpeakAsync(string message, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class WindowsSpeechSpeaker : ISpeaker
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _activeSpeechCts;
    private SpeechSynthesizer? _activeSynthesizer;
    private readonly JarvisOptions _options;

    public WindowsSpeechSpeaker(JarvisOptions options)
    {
        _options = options;
    }

    public event EventHandler<SpeakerStatusChangedEventArgs>? StatusChanged;

    public SpeakerStatusSnapshot CurrentStatus { get; private set; } = new(false, string.Empty, DateTimeOffset.UtcNow);

    public async Task SpeakAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);

        using var interruptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            var normalized = message.Trim();
            PublishStatus(true, normalized);

            await SpeakWithSynthesizerAsync(normalized, interruptCts, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeSpeechCts, interruptCts))
                {
                    _activeSpeechCts = null;
                    _activeSynthesizer = null;
                }
            }

            PublishStatus(false, string.Empty);
            _gate.Release();
        }
    }

    public void Interrupt()
    {
        CancellationTokenSource? cts;
        SpeechSynthesizer? synthesizer;

        lock (_sync)
        {
            cts = _activeSpeechCts;
            synthesizer = _activeSynthesizer;
        }

        try
        {
            cts?.Cancel();
            synthesizer?.SpeakAsyncCancelAll();
        }
        catch
        {
        }
    }

    private async Task SpeakWithSynthesizerAsync(
        string message,
        CancellationTokenSource interruptCts,
        CancellationToken callerToken)
    {
        using var speaker = new SpeechSynthesizer();
        var completed = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs eventArgs)
        {
            if (eventArgs.Error is not null)
            {
                completed.TrySetException(eventArgs.Error);
                return;
            }

            completed.TrySetResult(null);
        }

        speaker.SetOutputToDefaultAudioDevice();
        
        var volume = Math.Clamp(_options.SpeakerVolumeMultiplier * 100, 0, 100);
        if (_options.WhisperModeEnabled)
        {
            volume *= 0.6;
        }
        speaker.Rate = AssistantPersonaConfiguration.ResolveWindowsSpeechRate(_options);
        speaker.Volume = (int)volume;
        speaker.SpeakCompleted += OnSpeakCompleted;

        lock (_sync)
        {
            _activeSpeechCts = interruptCts;
            _activeSynthesizer = speaker;
        }

        using var registration = interruptCts.Token.Register(() =>
        {
            try
            {
                speaker.SpeakAsyncCancelAll();
            }
            catch
            {
            }
        });

        try
        {
            speaker.SpeakAsync(message);
            await completed.Task.WaitAsync(interruptCts.Token);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
        }
        finally
        {
            speaker.SpeakCompleted -= OnSpeakCompleted;

            lock (_sync)
            {
                if (ReferenceEquals(_activeSynthesizer, speaker))
                {
                    _activeSynthesizer = null;
                    _activeSpeechCts = null;
                }
            }
        }
    }

    private void PublishStatus(bool isSpeaking, string message)
    {
        CurrentStatus = new SpeakerStatusSnapshot(isSpeaking, message, DateTimeOffset.UtcNow);
        StatusChanged?.Invoke(this, new SpeakerStatusChangedEventArgs(CurrentStatus));
    }
}
