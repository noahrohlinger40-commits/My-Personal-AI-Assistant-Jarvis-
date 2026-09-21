using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed record ExternalProcessExecutionResult(
    bool Succeeded,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    string ExecutablePath);

internal static class ConfiguredExternalProcessRunner
{
    public static async Task<ExternalProcessExecutionResult> RunAsync(
        string executablePath,
        string arguments,
        string workingDirectory,
        string definitionDirectory,
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string> placeholders,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var expandedExecutablePath = ExpandTemplate(executablePath, placeholders);
        var expandedArguments = ExpandTemplate(arguments, placeholders);
        var expandedWorkingDirectory = ExpandTemplate(workingDirectory, placeholders);
        var resolvedExecutablePath = ResolveExecutablePath(expandedExecutablePath, definitionDirectory);

        if (string.IsNullOrWhiteSpace(resolvedExecutablePath))
        {
            return new ExternalProcessExecutionResult(
                Succeeded: false,
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: "No executable was configured.",
                TimedOut: false,
                ExecutablePath: string.Empty);
        }

        var resolvedWorkingDirectory = ResolveWorkingDirectory(expandedWorkingDirectory, definitionDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedExecutablePath,
            Arguments = expandedArguments,
            WorkingDirectory = resolvedWorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var pair in BuildEnvironmentVariables(environmentVariables, placeholders))
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        try
        {
            if (!process.Start())
            {
                return new ExternalProcessExecutionResult(
                    Succeeded: false,
                    ExitCode: -1,
                    StandardOutput: string.Empty,
                    StandardError: "The configured process could not be started.",
                    TimedOut: false,
                    ExecutablePath: resolvedExecutablePath);
            }
        }
        catch (Exception exception)
        {
            return new ExternalProcessExecutionResult(
                Succeeded: false,
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: exception.Message,
                TimedOut: false,
                ExecutablePath: resolvedExecutablePath);
        }

        var timeout = Math.Clamp(timeoutMilliseconds, 250, 60000);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var waitForExitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completedTask = await Task.WhenAny(waitForExitTask, timeoutTask).ConfigureAwait(false);

        if (completedTask == timeoutTask)
        {
            TryTerminate(process);
            var timeoutOutput = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
            var timeoutError = await SafeReadAsync(stderrTask).ConfigureAwait(false);
            return new ExternalProcessExecutionResult(
                Succeeded: false,
                ExitCode: process.HasExited ? process.ExitCode : -1,
                StandardOutput: timeoutOutput,
                StandardError: string.IsNullOrWhiteSpace(timeoutError)
                    ? $"The process did not finish within {timeout} ms."
                    : timeoutError,
                TimedOut: true,
                ExecutablePath: resolvedExecutablePath);
        }

        await waitForExitTask.ConfigureAwait(false);
        var standardOutput = await SafeReadAsync(stdoutTask).ConfigureAwait(false);
        var standardError = await SafeReadAsync(stderrTask).ConfigureAwait(false);

        return new ExternalProcessExecutionResult(
            Succeeded: process.ExitCode == 0,
            ExitCode: process.ExitCode,
            StandardOutput: standardOutput,
            StandardError: standardError,
            TimedOut: false,
            ExecutablePath: resolvedExecutablePath);
    }

    public static string ExpandTemplate(string? template, IReadOnlyDictionary<string, string> placeholders)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return string.Empty;
        }

        var expanded = template;

        foreach (var pair in placeholders)
        {
            expanded = expanded.Replace("{" + pair.Key + "}", pair.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return expanded;
    }

    public static string ResolveOptionalPath(string? path, string definitionDirectory, IReadOnlyDictionary<string, string>? placeholders = null)
    {
        var expanded = placeholders is null
            ? path?.Trim() ?? string.Empty
            : ExpandTemplate(path, placeholders);

        if (string.IsNullOrWhiteSpace(expanded))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        return Path.GetFullPath(Path.Combine(definitionDirectory, expanded));
    }

    public static Dictionary<string, string> BuildCommonPlaceholders(
        JarvisOptions options,
        ResolvedMicrophoneProcessingProfile profile)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workspaceRoot"] = Directory.GetCurrentDirectory(),
            ["assistantName"] = options.AssistantName ?? string.Empty,
            ["deviceName"] = options.SpeechRecognitionDeviceName?.Trim() ?? string.Empty,
            ["wakePhrase"] = options.WakePhrase?.Trim() ?? string.Empty,
            ["beamformingProfile"] = profile.EffectiveBeamformingProfileName,
            ["hardwareDspProfile"] = profile.EffectiveHardwareDspProfileName,
            ["wakeWordAssetPath"] = profile.EffectiveWakeWordAssetPath,
            ["wakeEnginePath"] = profile.EffectiveWakeEnginePath,
            ["vendorDspProfilePath"] = profile.EffectiveVendorDspProfilePath,
            ["speechRecognitionModel"] = options.SpeechRecognitionModel?.Trim() ?? string.Empty
        };
    }

    private static string ResolveExecutablePath(string path, string definitionDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        if (path.Contains(Path.DirectorySeparatorChar)
            || path.Contains(Path.AltDirectorySeparatorChar)
            || path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(definitionDirectory, path));
        }

        return path;
    }

    private static string ResolveWorkingDirectory(string path, string definitionDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return definitionDirectory;
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(definitionDirectory, path));
    }

    private static IReadOnlyDictionary<string, string> BuildEnvironmentVariables(
        IReadOnlyDictionary<string, string> manifestVariables,
        IReadOnlyDictionary<string, string> placeholders)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["JARVIS_WORKSPACE_ROOT"] = Directory.GetCurrentDirectory()
        };

        foreach (var pair in placeholders)
        {
            var envName = BuildEnvironmentVariableName(pair.Key);

            if (!string.IsNullOrWhiteSpace(envName))
            {
                environment[envName] = pair.Value ?? string.Empty;
            }
        }

        foreach (var pair in manifestVariables)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            environment[pair.Key.Trim()] = ExpandTemplate(pair.Value, placeholders);
        }

        return environment;
    }

    private static string BuildEnvironmentVariableName(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var builder = new StringBuilder("JARVIS_");

        foreach (var character in key)
        {
            builder.Append(char.IsLetterOrDigit(character)
                ? char.ToUpperInvariant(character)
                : '_');
        }

        return builder.ToString();
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}

internal sealed class VendorDspControlSession
{
    private readonly JarvisOptions _options;
    private readonly ResolvedMicrophoneProcessingProfile _profile;
    private readonly VendorDspProfileDescriptor _descriptor;
    private readonly IReadOnlyDictionary<string, string> _basePlaceholders;
    private bool _isApplied;

    public VendorDspControlSession(
        JarvisOptions options,
        ResolvedMicrophoneProcessingProfile profile,
        VendorDspProfileDescriptor descriptor)
    {
        _options = options;
        _profile = profile;
        _descriptor = descriptor;
        _basePlaceholders = BuildPlaceholders();
    }

    public static VendorDspControlSession? Create(JarvisOptions options, ResolvedMicrophoneProcessingProfile profile)
    {
        var descriptor = VendorDspProfileCatalog.ResolveDescriptorByPath(
            Directory.GetCurrentDirectory(),
            options,
            profile.EffectiveVendorDspProfilePath);

        return descriptor.IsNone ? null : new VendorDspControlSession(options, profile, descriptor);
    }

    public async Task<string?> EnsureAppliedAsync(CancellationToken cancellationToken)
    {
        if (_isApplied)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_descriptor.ExecutablePath)
            || string.IsNullOrWhiteSpace(_descriptor.ApplyArguments))
        {
            return null;
        }

        var result = await ConfiguredExternalProcessRunner.RunAsync(
            _descriptor.ExecutablePath,
            _descriptor.ApplyArguments,
            _descriptor.WorkingDirectory,
            _descriptor.DefinitionDirectory,
            _descriptor.EnvironmentVariables,
            _basePlaceholders,
            _descriptor.ApplyTimeoutMilliseconds,
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return BuildErrorMessage("apply", result);
        }

        _isApplied = true;
        return null;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        if (!_isApplied)
        {
            return;
        }

        _isApplied = false;

        if (string.IsNullOrWhiteSpace(_descriptor.ExecutablePath)
            || string.IsNullOrWhiteSpace(_descriptor.ReleaseArguments))
        {
            return;
        }

        await ConfiguredExternalProcessRunner.RunAsync(
            _descriptor.ExecutablePath,
            _descriptor.ReleaseArguments,
            _descriptor.WorkingDirectory,
            _descriptor.DefinitionDirectory,
            _descriptor.EnvironmentVariables,
            _basePlaceholders,
            _descriptor.ReleaseTimeoutMilliseconds,
            cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyDictionary<string, string> BuildPlaceholders()
    {
        var placeholders = ConfiguredExternalProcessRunner.BuildCommonPlaceholders(_options, _profile);
        placeholders["vendorDspProfileName"] = _descriptor.BuildShortLabel();
        placeholders["vendorDspProfileFile"] = ConfiguredExternalProcessRunner.ResolveOptionalPath(
            _descriptor.ProfilePath,
            _descriptor.DefinitionDirectory);
        placeholders["vendorDspDefinitionDirectory"] = _descriptor.DefinitionDirectory;
        return placeholders;
    }

    private string BuildErrorMessage(string operation, ExternalProcessExecutionResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = result.TimedOut
                ? "The vendor DSP command timed out."
                : $"The vendor DSP command exited with code {result.ExitCode}.";
        }

        return $"{_descriptor.BuildShortLabel()} {operation} failed: {detail.Trim()}";
    }
}

internal static class ExternalWakeEngineResponseParser
{
    public static WakeWordDetectionResult Parse(
        string output,
        JarvisOptions options,
        IReadOnlyList<string> wakeChoices,
        Func<string, (bool Matched, string Alias, string InlineCommand)> tryExtractWakeCommand,
        double minimumConfidence)
    {
        var trimmedOutput = output?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedOutput))
        {
            return WakeWordDetectionResult.None;
        }

        if (string.Equals(trimmedOutput, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmedOutput, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmedOutput, "detected", StringComparison.OrdinalIgnoreCase))
        {
            return new WakeWordDetectionResult(
                IsDetected: true,
                Alias: wakeChoices.FirstOrDefault() ?? options.WakePhrase,
                Confidence: minimumConfidence,
                ShouldArmOnly: true,
                MatchOffset: TimeSpan.Zero,
                MatchDuration: TimeSpan.Zero);
        }

        var normalizedText = VoiceRecognitionText.NormalizeTranscript(trimmedOutput, options);
        var extractedText = tryExtractWakeCommand(normalizedText);

        if (extractedText.Matched)
        {
            return new WakeWordDetectionResult(
                IsDetected: true,
                Alias: extractedText.Alias,
                Confidence: minimumConfidence,
                ShouldArmOnly: string.IsNullOrWhiteSpace(extractedText.InlineCommand),
                MatchOffset: TimeSpan.Zero,
                MatchDuration: TimeSpan.Zero,
                RecognizedText: normalizedText,
                InlineCommand: extractedText.InlineCommand);
        }

        try
        {
            using var document = JsonDocument.Parse(trimmedOutput);
            var root = document.RootElement;
            var isDetected = TryGetBoolean(root, "detected")
                || TryGetBoolean(root, "isDetected")
                || TryGetBoolean(root, "wakeDetected");

            if (!isDetected)
            {
                return WakeWordDetectionResult.None;
            }

            var confidence = TryGetDouble(root, "confidence") ?? minimumConfidence;
            var alias = TryGetString(root, "alias")
                ?? wakeChoices.FirstOrDefault()
                ?? options.WakePhrase;
            var recognizedText = TryGetString(root, "recognizedText")
                ?? TryGetString(root, "text")
                ?? string.Empty;
            var inlineCommand = TryGetString(root, "inlineCommand")
                ?? TryGetString(root, "command")
                ?? string.Empty;
            var shouldArmOnly = TryGetBoolean(root, "shouldArmOnly");

            if (!string.IsNullOrWhiteSpace(recognizedText))
            {
                var normalized = VoiceRecognitionText.NormalizeTranscript(recognizedText, options);
                var extracted = tryExtractWakeCommand(normalized);

                if (extracted.Matched)
                {
                    alias = string.IsNullOrWhiteSpace(alias) ? extracted.Alias : alias;

                    if (string.IsNullOrWhiteSpace(inlineCommand))
                    {
                        inlineCommand = extracted.InlineCommand;
                    }
                }

                recognizedText = normalized;
            }

            if (string.IsNullOrWhiteSpace(inlineCommand))
            {
                shouldArmOnly = true;
            }

            return new WakeWordDetectionResult(
                IsDetected: true,
                Alias: alias,
                Confidence: confidence,
                ShouldArmOnly: shouldArmOnly,
                MatchOffset: TimeSpan.FromMilliseconds(TryGetDouble(root, "matchOffsetMs") ?? 0),
                MatchDuration: TimeSpan.FromMilliseconds(TryGetDouble(root, "matchDurationMs") ?? 0),
                RecognizedText: recognizedText,
                InlineCommand: inlineCommand);
        }
        catch
        {
            return WakeWordDetectionResult.None;
        }
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static bool TryGetBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var value) && value,
            JsonValueKind.Number => element.TryGetInt32(out var numericValue) && numericValue != 0,
            _ => false
        };
    }

    private static double? TryGetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out var value)
                ? value
                : null;
    }
}
