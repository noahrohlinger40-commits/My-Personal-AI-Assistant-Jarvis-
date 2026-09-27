using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jarvis.Core;

namespace Jarvis.App;

internal static class AppBootstrapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<JarvisDesktopContext> CreateAsync(CancellationToken cancellationToken)
    {
        var workspaceRoot = ResolveWorkspaceRoot();
        var options = await JarvisSettingsLoader.LoadAsync(workspaceRoot, cancellationToken);
        // Started first and not awaited, so the speech model loads while the planner warms up.
        StartLocalSpeechServerInBackground(workspaceRoot, options);
        await EnsureLocalPlannerReadyAsync(options, cancellationToken);
        var desktopService = new DesktopAutomationService(workspaceRoot, options.ApplicationAliases);
        // Scan installed apps in the background so the first "open" command is instant.
        desktopService.WarmApplicationIndexInBackground();
        JarvisCommandCatalog.KnownApplicationNameCheck = desktopService.IsKnownApplicationName;
        IDesktopAutomationService desktopAutomation = desktopService;
        var assistant = await CreateAssistantAsync(workspaceRoot, options, desktopAutomation, cancellationToken);
        ISpeaker speaker = SpeakerFactory.Create(options);
        IVoiceRuntime voiceRuntime = VoiceRuntimeFactory.Create(options);

        return new JarvisDesktopContext(workspaceRoot, options, desktopAutomation, assistant, speaker, voiceRuntime);
    }

    public static async Task<JarvisAssistant> CreateAssistantAsync(
        string workspaceRoot,
        JarvisOptions options,
        IDesktopAutomationService desktopAutomation,
        CancellationToken cancellationToken)
    {
        var memoryStore = new JsonMemoryStore(Path.Combine(workspaceRoot, options.MemoryFilePath));
        IWeatherService weatherService = new OpenMeteoWeatherService();
        IUserStateProvider userStateProvider = new JsonUserStateProvider(Path.Combine(workspaceRoot, options.UserStateFilePath));
        IWebResearchService webResearchService = new DuckDuckGoWebResearchService();
        var transcriptStore = new JsonLineTranscriptStore(Path.Combine(workspaceRoot, options.TranscriptFilePath));
        IAgentStateStore agentStateStore = new JsonAgentStateStore(Path.Combine(workspaceRoot, options.AgentStateFilePath));
        IPendingApprovalStore pendingApprovalStore = new JsonPendingApprovalStore(Path.Combine(workspaceRoot, options.PendingApprovalFilePath));
        var modelGateway = ModelGatewayFactory.Create(workspaceRoot, options);
        IVisualContextService visualContextService = new WindowsVisualContextService(workspaceRoot, options, modelGateway);
        var planner = AssistantPlannerFactory.Create(options, modelGateway, agentStateStore, webResearchService);
        var toolSafety = new ToolSafetyService(options);
        var assistant = new JarvisAssistant(
            options,
            memoryStore,
            desktopAutomation,
            weatherService,
            userStateProvider,
            webResearchService,
            visualContextService,
            transcriptStore,
            agentStateStore,
            planner,
            pendingApprovalStore,
            toolSafety,
            workspaceRoot,
            modelGateway,
            new UiaBrowserPageReader(),
            new LectureRecorder(options));
        await assistant.InitializeAsync(cancellationToken);
        return assistant;
    }

    private static string ResolveWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var settingsPath = Path.Combine(current.FullName, "jarvis.settings.json");

            if (File.Exists(settingsPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static async Task EnsureLocalPlannerReadyAsync(JarvisOptions options, CancellationToken cancellationToken)
    {
        if (!options.PlannerEnabled || !LooksLikeLocalPlanner(options))
        {
            return;
        }

        var model = ResolvePlannerWarmupModel(options);

        if (string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        await EnsureLmStudioServerAndModelAsync(options, model, cancellationToken);

        // Per-request timeouts are applied below because the first reply from a large local model
        // can take far longer than a simple model-list request.
        using var httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        var plannerApiKey = ApiKeyResolver.Resolve(
            options.PlannerApiKey,
            options.PlannerApiKeyCredentialTarget,
            options.PlannerApiKeyEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(plannerApiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plannerApiKey);
        }

        var modelsEndpoint = BuildLocalPlannerEndpoint(options.PlannerBaseUrl, "models");
        var chatEndpoint = BuildLocalPlannerEndpoint(options.PlannerBaseUrl, "chat/completions");
        var deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30);
        var modelFound = false;

        while (DateTimeOffset.UtcNow < deadlineUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var modelsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                modelsTimeout.CancelAfter(TimeSpan.FromSeconds(12));
                var modelsPayload = await httpClient.GetStringAsync(modelsEndpoint, modelsTimeout.Token);

                if (ModelExists(modelsPayload, model))
                {
                    modelFound = true;
                    break;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Includes per-request timeouts: keep retrying until the overall deadline instead of aborting startup.
            }

            await Task.Delay(500, cancellationToken);
        }

        if (!modelFound)
        {
            throw new InvalidOperationException(
                $"The local planner model `{model}` is not ready at {modelsEndpoint}. Start LM Studio and load that model before launching Jarvis.");
        }

        var warmupPayload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "system",
                    ["content"] = "Answer immediately and briefly."
                },
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = "Reply with exactly: ok"
                }
            },
            ["temperature"] = 0,
            ["max_tokens"] = 96
        };

        using var warmupRequest = new HttpRequestMessage(HttpMethod.Post, chatEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(warmupPayload, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var warmupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        warmupTimeout.CancelAfter(TimeSpan.FromSeconds(120));
        using var warmupResponse = await httpClient.SendAsync(warmupRequest, warmupTimeout.Token);
        var warmupBody = await warmupResponse.Content.ReadAsStringAsync(warmupTimeout.Token);

        if (!warmupResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The local planner warm-up request failed at {chatEndpoint}: HTTP {(int)warmupResponse.StatusCode}. {TrimStartupError(warmupBody)}");
        }
    }

    private static async Task EnsureLmStudioServerAndModelAsync(
        JarvisOptions options,
        string model,
        CancellationToken cancellationToken)
    {
        var cliPath = ResolveLmStudioCliPath();

        if (string.IsNullOrWhiteSpace(cliPath))
        {
            return;
        }

        var plannerUri = new Uri(BuildLocalPlannerEndpoint(options.PlannerBaseUrl, string.Empty).TrimEnd('/'));
        var host = plannerUri.Host;
        var port = plannerUri.Port;

        // Starting the server can take many seconds, so skip it when the API is already answering.
        if (!await IsLocalApiReachableAsync(options.PlannerBaseUrl, cancellationToken))
        {
            await RunProcessAsync(
                cliPath,
                ["server", "start", "--port", port.ToString(), "--bind", host],
                20_000,
                cancellationToken);
        }

        var loadedModels = await RunProcessAsync(
            cliPath,
            ["ps", "--json"],
            10_000,
            cancellationToken);

        var targetContextLength = Math.Max(4096, options.PlannerLocalContextLength);
        var matchingModels = ParseLoadedLmStudioModels(loadedModels)
            .Where(candidate =>
                string.Equals(candidate.ModelKey, model, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Identifier, model, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingModels.Any(candidate =>
                string.Equals(candidate.Identifier, model, StringComparison.OrdinalIgnoreCase)
                && candidate.ContextLength >= targetContextLength))
        {
            return;
        }

        foreach (var loadedModel in matchingModels)
        {
            if (string.IsNullOrWhiteSpace(loadedModel.Identifier))
            {
                continue;
            }

            await RunProcessAsync(
                cliPath,
                ["unload", loadedModel.Identifier],
                30_000,
                cancellationToken);
        }

        await RunProcessAsync(
            cliPath,
            ["load", model, "--identifier", model, "--context-length", targetContextLength.ToString(), "-y"],
            120_000,
            cancellationToken);
    }

    /// <summary>
    /// Launches the bundled local Whisper server when speech recognition points at localhost and it
    /// is not already running. The server exits by itself when Jarvis exits. Does nothing if the
    /// local speech environment has not been set up (see scripts\Setup-LocalSpeech.ps1).
    /// </summary>
    private static void StartLocalSpeechServerInBackground(string workspaceRoot, JarvisOptions options)
    {
        if (!options.SpeechRecognitionEnabled
            || !Uri.TryCreate(options.SpeechRecognitionBaseUrl, UriKind.Absolute, out var speechUri)
            || !speechUri.IsLoopback
            || !(string.IsNullOrWhiteSpace(options.SpeechRecognitionProvider)
                || options.SpeechRecognitionProvider.Trim().ToLowerInvariant() is "auto" or "openai-compatible" or "openai" or "streaming"))
        {
            return;
        }

        // Lives outside the project on purpose: the project is often inside OneDrive, which corrupts
        // large model files and Python environments.
        var speechHome = Environment.GetEnvironmentVariable("JARVIS_SPEECH_HOME");

        if (string.IsNullOrWhiteSpace(speechHome))
        {
            speechHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Jarvis",
                "whisper");
        }

        var pythonPath = Path.Combine(speechHome, "venv", "Scripts", "python.exe");
        var serverScriptPath = Path.Combine(workspaceRoot, "scripts", "whisper-server.py");
        var logPath = Path.Combine(workspaceRoot, "data", "whisper-server.log");

        if (!File.Exists(pythonPath) || !File.Exists(serverScriptPath))
        {
            AppendSpeechServerLog(
                logPath,
                $"Local speech is not set up (looked for {pythonPath}). Run scripts\\Setup-LocalSpeech.ps1.");
            return;
        }

        // Off the startup path: probing a closed localhost port can take a couple of seconds on Windows.
        _ = Task.Run(async () =>
        {
            try
            {
                if (await IsLocalApiReachableAsync(options.SpeechRecognitionBaseUrl, CancellationToken.None))
                {
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    WorkingDirectory = workspaceRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add(serverScriptPath);
                startInfo.Environment["JARVIS_WHISPER_HOST"] = speechUri.Host;
                startInfo.Environment["JARVIS_WHISPER_PORT"] = speechUri.Port.ToString();
                startInfo.Environment["JARVIS_WHISPER_LOG"] = logPath;
                startInfo.Environment["JARVIS_WHISPER_MODELS"] = Path.Combine(speechHome, "models");
                startInfo.Environment["JARVIS_PARENT_PID"] = Environment.ProcessId.ToString();

                if (!string.IsNullOrWhiteSpace(options.SpeechRecognitionModel))
                {
                    startInfo.Environment["JARVIS_WHISPER_MODEL"] = options.SpeechRecognitionModel.Trim();
                }

                // The server keeps running on its own; only the local handle is released here.
                using var serverProcess = Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                AppendSpeechServerLog(logPath, $"Could not start the local speech server: {exception.Message}");
            }
        });
    }

    private static void AppendSpeechServerLog(string logPath, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTimeOffset.Now:u}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging is best effort.
        }
    }

    private static async Task<bool> IsLocalApiReachableAsync(string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var probeClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };

            // Any HTTP response, even an auth error, proves the server is up.
            using var response = await probeClient.GetAsync(
                BuildLocalPlannerEndpoint(baseUrl, "models"),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static bool LooksLikeLocalPlanner(JarvisOptions options)
    {
        if (string.Equals(options.PlannerProvider, "local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(options.PlannerBaseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePlannerWarmupModel(JarvisOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PlannerFastModel))
        {
            return options.PlannerFastModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.PlannerReasoningModel))
        {
            return options.PlannerReasoningModel.Trim();
        }

        return options.PlannerModel.Trim();
    }

    private static string ResolveLmStudioCliPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("LMSTUDIO_CLI_PATH");

        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath.Trim()))
        {
            return overridePath.Trim();
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidate = Path.Combine(userProfile, ".lmstudio", "bin", "lms.exe");

        if (File.Exists(candidate))
        {
            return candidate;
        }

        return string.Empty;
    }

    private static string BuildLocalPlannerEndpoint(string baseUrl, string relativePath)
    {
        var trimmedBaseUrl = baseUrl.Trim().TrimEnd('/');

        if (!Uri.TryCreate(trimmedBaseUrl, UriKind.Absolute, out var uri))
        {
            return $"{trimmedBaseUrl}/{relativePath}";
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(path)
            ? $"{trimmedBaseUrl}/v1"
            : trimmedBaseUrl;

        return $"{normalizedBaseUrl}/{relativePath}";
    }

    private static bool ModelExists(string modelsPayload, string model)
    {
        try
        {
            using var document = JsonDocument.Parse(modelsPayload);

            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idElement)
                    || idElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (string.Equals(idElement.GetString(), model, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static IReadOnlyList<LmStudioLoadedModel> ParseLoadedLmStudioModels(string payload)
    {
        var models = new List<LmStudioLoadedModel>();

        try
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return models;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var modelKey = item.TryGetProperty("modelKey", out var modelKeyElement)
                    && modelKeyElement.ValueKind == JsonValueKind.String
                    ? modelKeyElement.GetString() ?? string.Empty
                    : string.Empty;
                var identifier = item.TryGetProperty("identifier", out var identifierElement)
                    && identifierElement.ValueKind == JsonValueKind.String
                    ? identifierElement.GetString() ?? string.Empty
                    : string.Empty;
                var contextLength = item.TryGetProperty("contextLength", out var contextLengthElement)
                    && contextLengthElement.ValueKind == JsonValueKind.Number
                    && contextLengthElement.TryGetInt32(out var parsedContextLength)
                    ? parsedContextLength
                    : 0;

                if (string.IsNullOrWhiteSpace(modelKey) && string.IsNullOrWhiteSpace(identifier))
                {
                    continue;
                }

                models.Add(new LmStudioLoadedModel(modelKey, identifier, contextLength));
            }
        }
        catch (JsonException)
        {
        }

        return models;
    }

    private static string TrimStartupError(string text)
    {
        const int maxLength = 240;
        var value = text.Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static async Task<string> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start `{fileName}`.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var waitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(timeoutMs, cancellationToken);
        var completed = await Task.WhenAny(waitTask, timeoutTask);

        if (completed != waitTask)
        {
            TryKillProcess(process);
            throw new InvalidOperationException(
                $"Timed out while running `{fileName} {string.Join(" ", arguments)}`.");
        }

        await waitTask;
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var combined = string.Join(
            Environment.NewLine,
            new[] { stdout.Trim(), stderr.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"`{fileName} {string.Join(" ", arguments)}` failed: {TrimStartupError(combined)}");
        }

        return combined;
    }

    private static void TryKillProcess(Process process)
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

    private sealed record LmStudioLoadedModel(string ModelKey, string Identifier, int ContextLength);
}
