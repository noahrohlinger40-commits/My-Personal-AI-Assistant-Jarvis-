using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jarvis.Core;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Jarvis.App;

internal sealed class OpenAiCompatibleStreamingSpeaker : ISpeaker
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly JarvisOptions _options;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string _voice;
    private readonly string _instructions;
    private readonly string _responseFormat;
    private readonly string _apiKey;
    private CancellationTokenSource? _activeSpeechCts;

    private OpenAiCompatibleStreamingSpeaker(
        JarvisOptions options,
        string endpoint,
        string model,
        string voice,
        string instructions,
        string responseFormat,
        string apiKey)
    {
        _options = options;
        _endpoint = endpoint;
        _model = model;
        _voice = voice;
        _instructions = instructions;
        _responseFormat = responseFormat;
        _apiKey = apiKey;
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public event EventHandler<SpeakerStatusChangedEventArgs>? StatusChanged;

    public SpeakerStatusSnapshot CurrentStatus { get; private set; } = new(false, string.Empty, DateTimeOffset.UtcNow);

    public static bool TryCreate(JarvisOptions options, out ISpeaker? speaker)
    {
        speaker = null;

        var model = options.VoiceModel?.Trim();

        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var baseUrl = ResolveVoiceBaseUrl(options);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        var apiKey = ResolveVoiceApiKey(options);

        if (string.IsNullOrWhiteSpace(apiKey)
            && !string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(baseUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var voice = string.IsNullOrWhiteSpace(options.VoiceName)
            ? "coral"
            : options.VoiceName.Trim();
        var instructions = AssistantPersonaConfiguration.ResolveVoiceInstructions(options);
        var responseFormat = NormalizeResponseFormat(options.VoiceResponseFormat);

        speaker = new OpenAiCompatibleStreamingSpeaker(
            options,
            BuildEndpoint(baseUrl),
            model,
            voice,
            instructions,
            responseFormat,
            apiKey);
        return true;
    }

    public async Task SpeakAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var normalized = message.Trim();

        await _gate.WaitAsync(cancellationToken);

        using var interruptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            lock (_sync)
            {
                _activeSpeechCts = interruptCts;
            }

            PublishStatus(true, normalized);

            using var request = BuildRequest(normalized);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                interruptCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(interruptCts.Token);
                throw new InvalidOperationException(
                    $"speech API HTTP {(int)response.StatusCode}: {TrimForError(body)}");
            }

            await using var audioStream = await response.Content.ReadAsStreamAsync(interruptCts.Token);

            if (string.Equals(_responseFormat, "wav", StringComparison.OrdinalIgnoreCase))
            {
                await PlayBufferedWavAsync(audioStream, interruptCts.Token);
            }
            else
            {
                await PlayPcmStreamAsync(audioStream, interruptCts.Token);
            }
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
                }
            }

            PublishStatus(false, string.Empty);
            _gate.Release();
        }
    }

    public void Interrupt()
    {
        CancellationTokenSource? cts;

        lock (_sync)
        {
            cts = _activeSpeechCts;
        }

        try
        {
            cts?.Cancel();
        }
        catch
        {
        }
    }

    private HttpRequestMessage BuildRequest(string message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _model,
            ["voice"] = _voice,
            ["input"] = message,
            ["response_format"] = _responseFormat
        };

        if (!string.IsNullOrWhiteSpace(_instructions))
        {
            payload["instructions"] = _instructions;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        return request;
    }

    private async Task PlayPcmStreamAsync(Stream audioStream, CancellationToken cancellationToken)
    {
        var waveFormat = new WaveFormat(24000, 16, 1);
        var provider = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferLength = waveFormat.AverageBytesPerSecond * 8
        };

        using var output = new WaveOutEvent();
        output.Volume = 1.0f;
        output.Init(provider);
        output.Play();

        var buffer = new byte[8192];
        var outputGain = ResolveOutputGain();

        try
        {
            while (true)
            {
                var bytesRead = await audioStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);

                if (bytesRead <= 0)
                {
                    break;
                }

                if (Math.Abs(outputGain - 1.0) > 0.001)
                {
                    ScalePcm16Mono(buffer, bytesRead, outputGain);
                }

                provider.AddSamples(buffer, 0, bytesRead);
            }

            while (provider.BufferedBytes > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(30, cancellationToken);
            }
        }
        finally
        {
            provider.ClearBuffer();
            output.Stop();
        }
    }

    private async Task PlayBufferedWavAsync(Stream audioStream, CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await audioStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using var reader = new WaveFileReader(buffer);
        var sampleProvider = reader.ToSampleProvider();
        var volumeProvider = new VolumeSampleProvider(sampleProvider)
        {
            Volume = (float)ResolveOutputGain()
        };
        using var output = new WaveOutEvent();
        output.Volume = 1.0f;
        output.Init(volumeProvider);
        output.Play();

        try
        {
            while (output.PlaybackState == PlaybackState.Playing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(30, cancellationToken);
            }
        }
        finally
        {
            output.Stop();
        }
    }

    private double ResolveOutputGain()
    {
        var gain = Math.Clamp(_options.SpeakerVolumeMultiplier, 0.10, 2.0);

        if (_options.WhisperModeEnabled)
        {
            gain *= 0.60;
        }

        return Math.Clamp(gain, 0.05, 2.0);
    }

    private static void ScalePcm16Mono(byte[] pcmBuffer, int bytesRecorded, double gain)
    {
        if (bytesRecorded < 2)
        {
            return;
        }

        for (var index = 0; index < bytesRecorded - 1; index += 2)
        {
            var sample = BitConverter.ToInt16(pcmBuffer, index);
            var scaled = (int)Math.Round(sample * gain);
            scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
            var encoded = BitConverter.GetBytes((short)scaled);
            pcmBuffer[index] = encoded[0];
            pcmBuffer[index + 1] = encoded[1];
        }
    }

    private void PublishStatus(bool isSpeaking, string message)
    {
        CurrentStatus = new SpeakerStatusSnapshot(isSpeaking, message, DateTimeOffset.UtcNow);
        StatusChanged?.Invoke(this, new SpeakerStatusChangedEventArgs(CurrentStatus));
    }

    private static string BuildEndpoint(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');

        if (trimmed.EndsWith("/audio/speech", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}/audio/speech";
    }

    private static string ResolveVoiceBaseUrl(JarvisOptions options)
    {
        return string.IsNullOrWhiteSpace(options.VoiceBaseUrl)
            ? options.SpeechRecognitionBaseUrl
            : options.VoiceBaseUrl;
    }

    private static string ResolveVoiceApiKey(JarvisOptions options)
    {
        var voiceApiKey = ApiKeyResolver.Resolve(
            options.VoiceApiKey,
            options.VoiceApiKeyCredentialTarget,
            options.VoiceApiKeyEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(voiceApiKey))
        {
            return voiceApiKey;
        }

        return ApiKeyResolver.Resolve(
            options.SpeechRecognitionApiKey,
            options.SpeechRecognitionApiKeyCredentialTarget,
            options.SpeechRecognitionApiKeyEnvironmentVariable);
    }

    private static string NormalizeResponseFormat(string? responseFormat)
    {
        if (string.Equals(responseFormat?.Trim(), "wav", StringComparison.OrdinalIgnoreCase))
        {
            return "wav";
        }

        return "pcm";
    }

    private static string TrimForError(string text)
    {
        const int maxLength = 500;
        var value = text.Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
