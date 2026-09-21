using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Jarvis.App;

internal static class AudioDeviceResolver
{
    public static int GetPreferredInputDeviceIndex(string? preferredDeviceName)
    {
        if (TryResolveMicrophoneDevice(preferredDeviceName, out var deviceNumber))
        {
            return deviceNumber;
        }

        return WaveInEvent.DeviceCount > 0 ? 0 : -1;
    }

    public static bool TryResolveMicrophoneDevice(string? preferredDeviceName, out int deviceNumber)
    {
        deviceNumber = -1;

        if (WaveInEvent.DeviceCount <= 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(preferredDeviceName)
            && TryMatchWaveInDevice(preferredDeviceName, out deviceNumber))
        {
            return true;
        }

        foreach (var candidateName in GetDefaultCaptureEndpointNames())
        {
            if (TryMatchWaveInDevice(candidateName, out deviceNumber))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryMatchWaveInDevice(string candidateName, out int deviceNumber)
    {
        deviceNumber = -1;
        var bestScore = int.MinValue;

        for (var index = 0; index < WaveInEvent.DeviceCount; index++)
        {
            var capabilities = WaveInEvent.GetCapabilities(index);
            var score = ComputeMatchScore(candidateName, capabilities.ProductName);

            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            deviceNumber = index;
        }

        return bestScore > 0 && deviceNumber >= 0;
    }

    private static int ComputeMatchScore(string? candidateName, string? productName)
    {
        var normalizedCandidate = Normalize(candidateName);
        var normalizedProduct = Normalize(productName);

        if (string.IsNullOrWhiteSpace(normalizedCandidate)
            || string.IsNullOrWhiteSpace(normalizedProduct))
        {
            return 0;
        }

        if (string.Equals(normalizedCandidate, normalizedProduct, StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        if (string.Equals(RemoveWhitespace(normalizedCandidate), RemoveWhitespace(normalizedProduct), StringComparison.OrdinalIgnoreCase))
        {
            return 900;
        }

        if (normalizedProduct.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return 700 + Math.Min(normalizedCandidate.Length, 120);
        }

        if (normalizedCandidate.Contains(normalizedProduct, StringComparison.OrdinalIgnoreCase))
        {
            return 600 + Math.Min(normalizedProduct.Length, 120);
        }

        var candidateTokens = Tokenize(normalizedCandidate);
        var productTokens = Tokenize(normalizedProduct);
        var sharedTokens = candidateTokens.Intersect(productTokens, StringComparer.OrdinalIgnoreCase).ToArray();

        if (sharedTokens.Length == 0)
        {
            return 0;
        }

        return (sharedTokens.Length * 100) + sharedTokens.Sum(token => Math.Min(token.Length, 18));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Trim()
            .Replace('\u0000', ' ')
            .Replace("  ", " ", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string RemoveWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string[] Tokenize(string value)
    {
        return value
            .Split([' ', '-', '(', ')', '[', ']', '/', '\\', ',', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> GetDefaultCaptureEndpointNames()
    {
        using var enumerator = new MMDeviceEnumerator();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in new[] { Role.Communications, Role.Console, Role.Multimedia })
        {
            try
            {
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);

                if (!string.IsNullOrWhiteSpace(device.FriendlyName))
                {
                    names.Add(device.FriendlyName.Trim());
                }
            }
            catch
            {
            }
        }

        return names;
    }
}
