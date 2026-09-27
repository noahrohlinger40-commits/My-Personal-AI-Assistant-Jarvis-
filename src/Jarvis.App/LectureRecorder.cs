using Jarvis.Core;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Jarvis.App;

/// <summary>
/// Records the microphone and the computer's own audio (a Zoom call, a lecture video) as one 16 kHz mono
/// track. The audio goes to a WAV file as it comes in, and every ~30 seconds the new part is transcribed by
/// the local Whisper server (about 9x faster than real time on this laptop), so the transcript is nearly
/// done when recording stops.
/// </summary>
internal sealed class LectureRecorder(JarvisOptions options) : ILectureRecorder
{
    private const int SampleRate = MicrophonePcmStream.SampleRateHz;
    private const int BytesPerSecond = SampleRate * 2;
    private const int PieceBytes = 30 * BytesPerSecond;

    private readonly OpenAiCompatibleTranscriptionClient _whisper = new(
        options.SpeechRecognitionBaseUrl,
        options.SpeechRecognitionModel,
        OpenAiCompatibleVoiceRuntime.ResolveApiKey(options),
        options.SpeechRecognitionLanguage,
        prompt: string.Empty);

    private readonly List<TranscriptLine> _lines = [];
    private MicrophonePcmStream? _microphone;
    private WasapiLoopbackCapture? _computerAudio;
    private WaveFileWriter? _wav;
    private Task _pump = Task.CompletedTask;
    private Task _transcription = Task.CompletedTask;
    private string _wavPath = string.Empty;
    private long _bytesRecorded;

    public bool IsRecording => _microphone is not null;

    public void Start(string wavPath)
    {
        if (!AudioDeviceResolver.TryResolveMicrophoneDevice(options.SpeechRecognitionDeviceName, out var device))
        {
            throw new InvalidOperationException("no microphone was found");
        }

        _wav = new WaveFileWriter(wavPath, new WaveFormat(SampleRate, 16, 1));
        _wavPath = wavPath;
        _bytesRecorded = 0;
        _lines.Clear();
        _transcription = Task.CompletedTask;

        // The same gain the voice commands use, which lifts a lecturer across the room.
        var profile = MicrophoneProcessingProfileResolver.Resolve(options);
        _microphone = new MicrophonePcmStream(device, 100, profile.InputGainCap, profile.TargetPeakLevel);
        var computerAudio = StartComputerAudio();
        _microphone.StartCapture();
        _pump = Task.Factory.StartNew(() => Pump(computerAudio), TaskCreationOptions.LongRunning);
    }

    public async Task<LectureRecording> StopAsync(CancellationToken cancellationToken)
    {
        _microphone!.StopCapture();

        try
        {
            await _pump; // queues the last piece for transcription
        }
        catch (Exception)
        {
            // Writing failed partway (a full disk): what was written is still a playable recording.
        }

        _computerAudio?.StopRecording();
        _computerAudio?.Dispose();
        _computerAudio = null;
        _microphone.Dispose();
        _microphone = null;
        _wav!.Dispose();
        _wav = null;

        var saving = Task.Run(() => SaveAsMp3(_wavPath));
        await _transcription;
        var audioPath = await saving;

        lock (_lines)
        {
            return new LectureRecording(
                _lines.OrderBy(line => line.At).ToList(),
                TimeSpan.FromSeconds((double)_bytesRecorded / BytesPerSecond),
                audioPath);
        }
    }

    /// <summary>
    /// The computer's own sound, converted to match the microphone, or null when there is no output device.
    /// WASAPI loopback only delivers data while something plays; the buffer reads as silence otherwise.
    /// </summary>
    private ISampleProvider? StartComputerAudio()
    {
        try
        {
            _computerAudio = new WasapiLoopbackCapture();
            // ponytail: the two devices' clocks drift apart slowly; the 5 s buffer absorbs it, with a skip if it
            // ever overflows. Resample against the microphone's clock if long recordings show glitches.
            var buffer = new BufferedWaveProvider(_computerAudio.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(5),
                DiscardOnBufferOverflow = true
            };
            _computerAudio.DataAvailable += (_, eventArgs) => buffer.AddSamples(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
            _computerAudio.StartRecording();

            var samples = buffer.ToSampleProvider();
            var mono = samples.WaveFormat.Channels switch
            {
                1 => samples,
                2 => new StereoToMonoSampleProvider(samples),
                _ => new MultiplexingSampleProvider([samples], 1) // surround: the front-left channel
            };
            return new WdlResamplingSampleProvider(mono, SampleRate);
        }
        catch (Exception)
        {
            _computerAudio?.Dispose();
            _computerAudio = null;
            return null;
        }
    }

    private void Pump(ISampleProvider? computerAudio)
    {
        var microphone = new byte[BytesPerSecond / 10]; // 100 ms
        var computer = new float[microphone.Length / 2];
        var piece = new MemoryStream();
        var pieceStart = 0L;

        while (true)
        {
            // Blocks until the microphone has 100 ms; returns 0 once recording stops.
            var read = _microphone!.Read(microphone, 0, microphone.Length) & ~1;

            if (read == 0)
            {
                break;
            }

            computerAudio?.Read(computer, 0, read / 2);

            for (var index = 0; index < read / 2; index++)
            {
                var mixed = BitConverter.ToInt16(microphone, index * 2) + (computer[index] * short.MaxValue);
                var sample = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
                microphone[index * 2] = (byte)sample;
                microphone[(index * 2) + 1] = (byte)(sample >> 8);
            }

            _wav!.Write(microphone, 0, read);
            piece.Write(microphone, 0, read);
            _bytesRecorded += read;

            if (piece.Length >= PieceBytes)
            {
                var audio = piece.ToArray();
                var cut = QuietestPoint(audio);
                Transcribe(audio[..cut], pieceStart);
                pieceStart += cut;
                piece.SetLength(0);
                piece.Write(audio, cut, audio.Length - cut);
            }
        }

        if (piece.Length > BytesPerSecond / 2)
        {
            Transcribe(piece.ToArray(), pieceStart);
        }
    }

    /// <summary>Where the last 5 seconds are quietest, so no word is split between two transcriptions.</summary>
    private static int QuietestPoint(byte[] audio)
    {
        const int frame = BytesPerSecond / 20; // 50 ms
        var best = audio.Length;
        var bestEnergy = double.MaxValue;

        for (var start = Math.Max(0, audio.Length - (5 * BytesPerSecond)); start + frame <= audio.Length; start += frame)
        {
            var energy = 0.0;

            for (var index = start; index < start + frame; index += 2)
            {
                double sample = BitConverter.ToInt16(audio, index);
                energy += sample * sample;
            }

            if (energy < bestEnergy)
            {
                bestEnergy = energy;
                best = start + (frame / 2);
            }
        }

        return best & ~1;
    }

    /// <summary>Queued so pieces are transcribed one at a time, in order.</summary>
    private void Transcribe(byte[] audio, long startByte)
    {
        var offset = TimeSpan.FromSeconds((double)startByte / BytesPerSecond);
        _transcription = _transcription.ContinueWith(_ => TranscribeAsync(audio, offset), TaskScheduler.Default).Unwrap();
    }

    private async Task TranscribeAsync(byte[] audio, TimeSpan offset)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var lines = await _whisper.TranscribeLinesAsync(audio, SampleRate, CancellationToken.None);

                lock (_lines)
                {
                    _lines.AddRange(lines.Select(line => line with { At = offset + line.At }));
                }

                return;
            }
            catch (Exception) when (attempt < 3)
            {
                // The speech server may be busy with a voice command or restarting.
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
            catch (Exception)
            {
                lock (_lines)
                {
                    _lines.Add(new TranscriptLine(offset, "(This part couldn't be transcribed. It's still in the recording.)"));
                }

                return;
            }
        }
    }

    /// <summary>
    /// 48 kbps is the MP3 encoder's highest rate for 16 kHz mono: about 21 MB an hour, against 115 MB as WAV.
    /// If conversion fails, the WAV is kept, so the recording is never lost.
    /// </summary>
    private static string SaveAsMp3(string wavPath)
    {
        var mp3Path = Path.ChangeExtension(wavPath, ".mp3");

        try
        {
            MediaFoundationApi.Startup();

            using (var reader = new WaveFileReader(wavPath))
            {
                MediaFoundationEncoder.EncodeToMp3(reader, mp3Path, 48_000);
            }

            File.Delete(wavPath);
            return mp3Path;
        }
        catch (Exception)
        {
            return wavPath;
        }
    }
}
