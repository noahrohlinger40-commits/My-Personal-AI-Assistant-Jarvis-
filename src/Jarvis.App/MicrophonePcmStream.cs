using System.Collections.Concurrent;
using NAudio.Wave;

namespace Jarvis.App;

/// <summary>
/// Captures 16 kHz mono PCM from a specific input device, applies a gentle automatic gain boost,
/// and exposes the audio as a blocking <see cref="Stream"/> so System.Speech can consume it.
/// System.Speech otherwise only listens to the Windows default microphone with no gain control.
/// </summary>
internal sealed class MicrophonePcmStream : Stream
{
    public const int SampleRateHz = 16_000;

    private const int MaxQueuedChunks = 256;

    // Conversational speech sits around 0.05-0.10 RMS; this maps it to roughly 0.25-0.5 for the meter.
    private const double LevelDisplayScale = 5.0;

    private readonly BlockingCollection<byte[]> _chunks = new(MaxQueuedChunks);
    private readonly WaveInEvent _waveIn;
    private readonly double _inputGainCap;
    private readonly double _targetPeakLevel;
    private double _smoothedGain = 1.0;
    private byte[] _current = [];
    private int _currentOffset;
    private long _totalBytesRead;
    private bool _captureStopped;
    private bool _disposed;

    public MicrophonePcmStream(int deviceNumber, int bufferMilliseconds, double inputGainCap, double targetPeakLevel)
    {
        _inputGainCap = Math.Max(1.0, inputGainCap);
        _targetPeakLevel = Math.Clamp(targetPeakLevel, 0.1, 0.9);
        DeviceName = WaveInEvent.GetCapabilities(deviceNumber).ProductName;

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            BufferMilliseconds = Math.Clamp(bufferMilliseconds, 20, 200),
            WaveFormat = new WaveFormat(SampleRateHz, 16, 1)
        };
        _waveIn.DataAvailable += OnDataAvailable;
    }

    public string DeviceName { get; }

    /// <summary>
    /// Raised for every captured buffer with a 0-1 level (scaled RMS after gain). System.Speech does not
    /// raise its own audio-level events for custom streams, so the voice meter and barge-in use this.
    /// </summary>
    public event Action<double>? LevelMeasured;

    public void StartCapture()
    {
        _waveIn.StartRecording();
    }

    /// <summary>
    /// Stops the microphone and signals end-of-stream so a blocked reader wakes up and returns 0.
    /// </summary>
    public void StopCapture()
    {
        if (_captureStopped)
        {
            return;
        }

        _captureStopped = true;

        try
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.StopRecording();
        }
        catch
        {
        }

        try
        {
            _chunks.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (eventArgs.BytesRecorded < 2)
        {
            return;
        }

        var chunk = new byte[eventArgs.BytesRecorded - (eventArgs.BytesRecorded % 2)];
        Buffer.BlockCopy(eventArgs.Buffer, 0, chunk, 0, chunk.Length);
        ApplyAutomaticGain(chunk);

        try
        {
            // Drop the chunk rather than block the capture thread if the recognizer falls far behind.
            _chunks.TryAdd(chunk);
        }
        catch (InvalidOperationException)
        {
            // Also covers ObjectDisposedException: capture was stopped while a buffer was in flight.
        }
    }

    private void ApplyAutomaticGain(byte[] pcm16Mono)
    {
        var peak = 0.0;
        var sumSquares = 0.0;
        var sampleCount = pcm16Mono.Length / 2;

        for (var index = 0; index < pcm16Mono.Length; index += 2)
        {
            var sample = BitConverter.ToInt16(pcm16Mono, index) / 32768.0;
            var magnitude = Math.Abs(sample);
            peak = Math.Max(peak, magnitude);
            sumSquares += sample * sample;
        }

        var rms = Math.Sqrt(sumSquares / Math.Max(1, sampleCount));
        double targetGain;

        if (peak < 0.004 && rms < 0.0008)
        {
            // True silence: do not amplify the noise floor.
            targetGain = 1.0;
        }
        else
        {
            targetGain = Math.Clamp(_targetPeakLevel / Math.Max(peak, 0.025), 1.0, _inputGainCap);
        }

        _smoothedGain = (_smoothedGain * 0.76) + (targetGain * 0.24);

        var appliedGain = _smoothedGain <= 1.02 ? 1.0 : _smoothedGain;
        LevelMeasured?.Invoke(Math.Clamp(rms * appliedGain * LevelDisplayScale, 0, 1));

        if (appliedGain == 1.0)
        {
            return;
        }

        for (var index = 0; index < pcm16Mono.Length; index += 2)
        {
            var amplified = (int)Math.Round(BitConverter.ToInt16(pcm16Mono, index) * _smoothedGain);
            amplified = Math.Clamp(amplified, short.MinValue, short.MaxValue);
            pcm16Mono[index] = (byte)(amplified & 0xFF);
            pcm16Mono[index + 1] = (byte)((amplified >> 8) & 0xFF);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        // System.Speech treats a short read as end-of-stream, so block until the whole request is
        // filled. Only a stopped capture (queue completed and drained) may return fewer bytes.
        var totalCopied = 0;

        while (totalCopied < count)
        {
            if (_currentOffset >= _current.Length)
            {
                byte[]? next;

                try
                {
                    if (!_chunks.TryTake(out next, Timeout.Infinite))
                    {
                        break;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _current = next;
                _currentOffset = 0;
            }

            var bytesToCopy = Math.Min(count - totalCopied, _current.Length - _currentOffset);
            Buffer.BlockCopy(_current, _currentOffset, buffer, offset + totalCopied, bytesToCopy);
            _currentOffset += bytesToCopy;
            totalCopied += bytesToCopy;
        }

        _totalBytesRead += totalCopied;
        return totalCopied;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            StopCapture();
            _waveIn.Dispose();
            _chunks.Dispose();
        }

        base.Dispose(disposing);
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    // System.Speech queries Length when it wraps the stream. A live microphone has no fixed length.
    public override long Length => long.MaxValue;

    public override long Position
    {
        get => _totalBytesRead;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        // System.Speech probes the current position with Seek(0, Current) and fails silently if that throws.
        if (origin == SeekOrigin.Current && offset == 0)
        {
            return _totalBytesRead;
        }

        throw new NotSupportedException();
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
