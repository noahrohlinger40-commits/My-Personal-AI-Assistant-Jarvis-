using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Jarvis.Core;

public enum ModelRoute
{
    Fast,
    Reasoning,
    Vision
}

public sealed record ChatMessageContentPart(string Type, string Text = "", string Url = "")
{
    public static ChatMessageContentPart FromText(string text) => new("text", text ?? string.Empty);

    public static ChatMessageContentPart FromImageUrl(string url) => new("image_url", string.Empty, url ?? string.Empty);
}

public sealed record ChatMessage(
    string Role,
    ChatMessageContentPart[] Content,
    string ToolCallId = "",
    ChatToolCall[]? ToolCalls = null);

public sealed record ChatToolDefinition(
    string Name,
    string Description,
    string InputDescription = "Optional tool input.",
    string PurposeDescription = "Why this tool is being used for the current step.",
    string ExpectedEvidenceDescription = "What concrete result should confirm this tool call succeeded.");

public sealed record ChatToolCall(string Id, string Name, string ArgumentsJson);

public sealed record ChatCompletionRequest(
    ChatMessage[] Messages,
    ChatToolDefinition[]? Tools = null,
    double Temperature = 0.2,
    string ModelOverride = "",
    string User = "");

public sealed record ChatCompletionResponse(
    string Content,
    ChatToolCall[] ToolCalls,
    string Model,
    string ProviderLabel);

public interface IModelGateway
{
    bool IsAvailable { get; }

    string Status { get; }

    Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        ModelRoute route,
        CancellationToken cancellationToken);
}

public static class ModelGatewayFactory
{
    public static IModelGateway Create(string workspaceRoot, JarvisOptions options)
    {
        if (!options.PlannerEnabled)
        {
            return new DisabledModelGateway("disabled in settings");
        }

        var profiles = BuildProfiles(options);

        if (profiles.Count == 0)
        {
            return new DisabledModelGateway("no usable model provider is configured");
        }

        return new OpenAiCompatibleModelGateway(workspaceRoot, options, profiles);
    }

    private static List<ResolvedProviderProfile> BuildProfiles(JarvisOptions options)
    {
        var profiles = new List<ResolvedProviderProfile>();

        var primary = BuildPrimaryProfile(options);

        if (primary is not null)
        {
            profiles.Add(primary);
        }

        foreach (var fallback in options.PlannerFallbackProviders)
        {
            var resolved = BuildResolvedProfile(fallback);

            if (resolved is not null)
            {
                profiles.Add(resolved);
            }
        }

        return profiles;
    }

    private static ResolvedProviderProfile? BuildPrimaryProfile(JarvisOptions options)
    {
        var provider = NormalizeProvider(options.PlannerProvider);

        if (!AllowsEmptyBaseUrl(provider)
            && string.IsNullOrWhiteSpace(options.PlannerBaseUrl))
        {
            return null;
        }

        var requestModel = ResolveRequestModel(
            provider,
            options.PlannerModel,
            options.PlannerReasoningModel);

        var reasoningModel = string.IsNullOrWhiteSpace(options.PlannerReasoningModel)
            ? requestModel
            : options.PlannerReasoningModel.Trim();

        return new ResolvedProviderProfile(
            "primary",
            provider,
            options.PlannerBaseUrl.Trim(),
            requestModel,
            options.PlannerFastModel.Trim(),
            reasoningModel,
            options.PlannerVisionModel.Trim(),
            options.PlannerFastReasoningEffort.Trim(),
            options.PlannerReasoningEffort.Trim(),
            options.PlannerVisionReasoningEffort.Trim(),
            ResolveApiKey(
                options.PlannerApiKey,
                options.PlannerApiKeyCredentialTarget,
                options.PlannerApiKeyEnvironmentVariable),
            options.PlannerFallbackModels
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Select(model => model.Trim())
                .ToArray());
    }

    private static ResolvedProviderProfile? BuildResolvedProfile(PlannerProviderProfile profile)
    {
        var provider = NormalizeProvider(profile.Provider);

        if (!AllowsEmptyBaseUrl(provider)
            && string.IsNullOrWhiteSpace(profile.BaseUrl))
        {
            return null;
        }

        var requestModel = ResolveRequestModel(
            provider,
            profile.Model,
            profile.ReasoningModel);

        return new ResolvedProviderProfile(
            string.IsNullOrWhiteSpace(profile.Name) ? "fallback" : profile.Name.Trim(),
            provider,
            profile.BaseUrl.Trim(),
            requestModel,
            profile.FastModel.Trim(),
            profile.ReasoningModel.Trim(),
            profile.VisionModel.Trim(),
            profile.FastReasoningEffort.Trim(),
            profile.ReasoningEffort.Trim(),
            profile.VisionReasoningEffort.Trim(),
            ResolveApiKey(
                profile.ApiKey,
                profile.ApiKeyCredentialTarget,
                profile.ApiKeyEnvironmentVariable),
            profile.FallbackModels
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Select(model => model.Trim())
                .ToArray());
    }

    private static string ResolveApiKey(string directValue, string credentialTarget, string envVar) =>
        ApiKeyResolver.Resolve(directValue, credentialTarget, envVar);

    private static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return "openai-compatible";
        }

        var value = provider.Trim().ToLowerInvariant();
        return value switch
        {
            "openai" => "openai",
            "local" => "local",
            "openclaw" => "openclaw",
            "openclaw-local" => "openclaw-local",
            _ => "openai-compatible"
        };
    }

    private static string ResolveRequestModel(
        string provider,
        string configuredModel,
        string reasoningModel)
    {
        if (string.Equals(provider, "openclaw-local", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(configuredModel))
            {
                return configuredModel.Trim();
            }

            return string.IsNullOrWhiteSpace(reasoningModel)
                ? string.Empty
                : reasoningModel.Trim();
        }

        if (string.Equals(provider, "openclaw", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(configuredModel))
            {
                var trimmedConfiguredModel = configuredModel.Trim();

                if (LooksLikeOpenClawAgentTarget(trimmedConfiguredModel))
                {
                    return trimmedConfiguredModel;
                }
            }

            return "openclaw/default";
        }

        if (!string.IsNullOrWhiteSpace(configuredModel))
        {
            return configuredModel.Trim();
        }

        return string.IsNullOrWhiteSpace(reasoningModel)
            ? string.Empty
            : reasoningModel.Trim();
    }

    private static bool LooksLikeOpenClawAgentTarget(string model)
    {
        return model.StartsWith("openclaw/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(model, "openclaw", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("openclaw:", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("agent:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AllowsEmptyBaseUrl(string provider) =>
        string.Equals(provider, "openclaw-local", StringComparison.OrdinalIgnoreCase);
}

internal sealed class DisabledModelGateway : IModelGateway
{
    public DisabledModelGateway(string reason)
    {
        Status = $"disabled ({reason})";
    }

    public bool IsAvailable => false;

    public string Status { get; }

    public Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        ModelRoute route,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Model gateway is not available.");
    }
}

internal sealed class OpenAiCompatibleModelGateway : IModelGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _workspaceRoot;
    private readonly JarvisOptions _options;
    private readonly IReadOnlyList<ResolvedProviderProfile> _profiles;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, ResponsesConversationState> _responsesSessions = new(StringComparer.Ordinal);

    public OpenAiCompatibleModelGateway(string workspaceRoot, JarvisOptions options, IReadOnlyList<ResolvedProviderProfile> profiles)
    {
        _workspaceRoot = workspaceRoot;
        _options = options;
        _profiles = profiles;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
    }

    public bool IsAvailable => _profiles.Count > 0;

    public string Status =>
        string.Join(
            " | ",
            _profiles.Select(profile =>
            {
                if (IsOpenClawLocal(profile))
                {
                    var defaultModel = string.IsNullOrWhiteSpace(ResolveRequestModel(profile))
                        ? "config-default"
                        : ResolveRequestModel(profile);
                    var localFastModel = string.IsNullOrWhiteSpace(profile.FastModel) ? defaultModel : profile.FastModel;
                    var localReasoningModel = string.IsNullOrWhiteSpace(profile.ReasoningModel) ? defaultModel : profile.ReasoningModel;
                    return $"{profile.Name}:{profile.Provider} default={defaultModel} fast={localFastModel} reasoning={localReasoningModel}";
                }

                if (IsOpenClaw(profile))
                {
                    var agent = ResolveRequestModel(profile);
                    var openClawFastModel = string.IsNullOrWhiteSpace(profile.FastModel) ? "agent-default" : profile.FastModel;
                    var openClawReasoningModel = string.IsNullOrWhiteSpace(profile.ReasoningModel) ? "agent-default" : profile.ReasoningModel;
                    return $"{profile.Name}:{profile.Provider} agent={agent} fast={openClawFastModel} reasoning={openClawReasoningModel}";
                }

                var fastModel = string.IsNullOrWhiteSpace(profile.FastModel) ? "n/a" : profile.FastModel;
                var reasoningModel = string.IsNullOrWhiteSpace(profile.ReasoningModel) ? "n/a" : profile.ReasoningModel;
                return $"{profile.Name}:{profile.Provider} fast={fastModel} reasoning={reasoningModel}";
            }));

    public async Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        ModelRoute route,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("No model providers are available.");
        }

        var failures = new List<string>();

        foreach (var candidate in ResolveCandidates(route, request.ModelOverride))
        {
            try
            {
                return await SendAsync(candidate, request, route, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add($"{candidate.Profile.Name}/{candidate.Model}: {TrimError(exception.Message)}");
            }
        }

        throw new InvalidOperationException(
            "All planner model candidates failed. " + string.Join(" | ", failures));
    }

    private IEnumerable<ModelCandidate> ResolveCandidates(ModelRoute route, string modelOverride)
    {
        foreach (var profile in _profiles)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(modelOverride))
            {
                if (seen.Add(modelOverride.Trim()))
                {
                    yield return new ModelCandidate(profile, modelOverride.Trim());
                }

                continue;
            }

            foreach (var model in GetRouteModels(profile, route))
            {
                if (seen.Add(model))
                {
                    yield return new ModelCandidate(profile, model);
                }
            }
        }
    }

    private IEnumerable<string> GetRouteModels(ResolvedProviderProfile profile, ModelRoute route)
    {
        if (IsOpenClawLocal(profile))
        {
            foreach (var model in GetOpenClawLocalRouteModels(profile, route))
            {
                yield return model;
            }

            foreach (var fallbackModel in profile.FallbackModels)
            {
                yield return fallbackModel;
            }

            yield break;
        }

        if (IsOpenClaw(profile))
        {
            foreach (var model in GetOpenClawRouteModels(profile, route))
            {
                yield return model;
            }

            foreach (var fallbackModel in profile.FallbackModels)
            {
                yield return fallbackModel;
            }

            yield break;
        }

        switch (route)
        {
            case ModelRoute.Fast:
                if (!string.IsNullOrWhiteSpace(profile.FastModel))
                {
                    yield return profile.FastModel;
                }

                if (!string.IsNullOrWhiteSpace(profile.ReasoningModel)
                    && !string.Equals(profile.ReasoningModel, profile.FastModel, StringComparison.OrdinalIgnoreCase))
                {
                    yield return profile.ReasoningModel;
                }

                break;

            case ModelRoute.Vision:
                if (!string.IsNullOrWhiteSpace(profile.VisionModel))
                {
                    yield return profile.VisionModel;
                }

                if (!string.IsNullOrWhiteSpace(profile.ReasoningModel)
                    && !string.Equals(profile.ReasoningModel, profile.VisionModel, StringComparison.OrdinalIgnoreCase))
                {
                    yield return profile.ReasoningModel;
                }

                break;

            default:
                if (!string.IsNullOrWhiteSpace(profile.ReasoningModel))
                {
                    yield return profile.ReasoningModel;
                }

                if (!string.IsNullOrWhiteSpace(profile.FastModel)
                    && !string.Equals(profile.FastModel, profile.ReasoningModel, StringComparison.OrdinalIgnoreCase))
                {
                    yield return profile.FastModel;
                }

                break;
        }

        foreach (var fallbackModel in profile.FallbackModels)
        {
            yield return fallbackModel;
        }
    }

    private IEnumerable<string> GetOpenClawLocalRouteModels(ResolvedProviderProfile profile, ModelRoute route)
    {
        var yielded = false;

        switch (route)
        {
            case ModelRoute.Fast:
                if (!string.IsNullOrWhiteSpace(profile.FastModel))
                {
                    yielded = true;
                    yield return profile.FastModel;
                }

                if (!string.IsNullOrWhiteSpace(profile.ReasoningModel)
                    && !string.Equals(profile.ReasoningModel, profile.FastModel, StringComparison.OrdinalIgnoreCase))
                {
                    yielded = true;
                    yield return profile.ReasoningModel;
                }

                break;

            case ModelRoute.Vision:
                if (!string.IsNullOrWhiteSpace(profile.VisionModel))
                {
                    yielded = true;
                    yield return profile.VisionModel;
                }

                if (!string.IsNullOrWhiteSpace(profile.ReasoningModel)
                    && !string.Equals(profile.ReasoningModel, profile.VisionModel, StringComparison.OrdinalIgnoreCase))
                {
                    yielded = true;
                    yield return profile.ReasoningModel;
                }

                break;

            default:
                if (!string.IsNullOrWhiteSpace(profile.ReasoningModel))
                {
                    yielded = true;
                    yield return profile.ReasoningModel;
                }

                if (!string.IsNullOrWhiteSpace(profile.FastModel)
                    && !string.Equals(profile.FastModel, profile.ReasoningModel, StringComparison.OrdinalIgnoreCase))
                {
                    yielded = true;
                    yield return profile.FastModel;
                }

                break;
        }

        if (!yielded || !string.IsNullOrWhiteSpace(profile.RequestModel))
        {
            yield return ResolveRequestModel(profile);
        }
    }

    private IEnumerable<string> GetOpenClawRouteModels(ResolvedProviderProfile profile, ModelRoute route)
    {
        var agentTarget = ResolveRequestModel(profile);

        switch (route)
        {
            case ModelRoute.Fast:
                if (!string.IsNullOrWhiteSpace(profile.FastModel))
                {
                    yield return profile.FastModel;
                }

                if (!string.IsNullOrWhiteSpace(profile.ReasoningModel)
                    && !string.Equals(profile.ReasoningModel, profile.FastModel, StringComparison.OrdinalIgnoreCase))
                {
                    yield return profile.ReasoningModel;
                }

                break;

            case ModelRoute.Vision:
                if (!string.IsNullOrWhiteSpace(profile.VisionModel))
                {
                    yield return profile.VisionModel;
                }

                if (!string.IsNullOrWhiteSpace(profile.ReasoningModel)
                    && !string.Equals(profile.ReasoningModel, profile.VisionModel, StringComparison.OrdinalIgnoreCase))
                {
                    yield return profile.ReasoningModel;
                }

                break;

            default:
                if (!string.IsNullOrWhiteSpace(profile.ReasoningModel))
                {
                    yield return profile.ReasoningModel;
                }

                if (!string.IsNullOrWhiteSpace(profile.FastModel)
                    && !string.Equals(profile.FastModel, profile.ReasoningModel, StringComparison.OrdinalIgnoreCase))
                {
                    yield return profile.FastModel;
                }

                break;
        }

        yield return agentTarget;
    }

    private async Task<ChatCompletionResponse> SendAsync(
        ModelCandidate candidate,
        ChatCompletionRequest request,
        ModelRoute route,
        CancellationToken cancellationToken)
    {
        if (IsOpenClawLocal(candidate.Profile))
        {
            return await SendViaOpenClawLocalAsync(candidate, request, route, cancellationToken);
        }

        if (RequiresApiKey(candidate.Profile.BaseUrl, candidate.Profile.Provider)
            && string.IsNullOrWhiteSpace(candidate.Profile.ApiKey))
        {
            throw new InvalidOperationException($"provider {candidate.Profile.Name} has no API key");
        }

        if (UseResponsesApi(candidate.Profile))
        {
            return await SendViaResponsesApiAsync(candidate, request, route, cancellationToken);
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = IsOpenClaw(candidate.Profile)
                ? ResolveRequestModel(candidate.Profile)
                : candidate.Model,
            ["messages"] = request.Messages.Select(SerializeMessage).ToArray()
        };

        var reasoningEffort = ResolveReasoningEffort(candidate.Profile, route);

        if (ShouldSendReasoningEffort(candidate.Profile, reasoningEffort))
        {
            payload["reasoning_effort"] = reasoningEffort;
        }

        if (ShouldIncludeTemperature(candidate.Profile, reasoningEffort))
        {
            payload["temperature"] = request.Temperature;
        }

        var localMaxTokens = ResolveLocalMaxTokens(candidate.Profile, route);

        if (localMaxTokens > 0)
        {
            payload["max_tokens"] = localMaxTokens;
        }

        if (!string.IsNullOrWhiteSpace(request.User))
        {
            payload["user"] = request.User.Trim();
        }

        if (request.Tools is { Length: > 0 })
        {
            payload["tools"] = request.Tools.Select(SerializeTool).ToArray();
            payload["tool_choice"] = "auto";
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            BuildEndpoint(candidate.Profile.BaseUrl))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(candidate.Profile.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", candidate.Profile.ApiKey);
        }

        if (IsOpenClaw(candidate.Profile))
        {
            var agentTarget = ResolveRequestModel(candidate.Profile);

            if (!string.Equals(candidate.Model, agentTarget, StringComparison.OrdinalIgnoreCase))
            {
                message.Headers.TryAddWithoutValidation("x-openclaw-model", candidate.Model);
            }
        }

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {TrimError(body)}");
        }

        using var document = JsonDocument.Parse(body);
        return ExtractCompletion(document.RootElement, candidate);
    }

    private async Task<ChatCompletionResponse> SendViaResponsesApiAsync(
        ModelCandidate candidate,
        ChatCompletionRequest request,
        ModelRoute route,
        CancellationToken cancellationToken)
    {
        var reasoningEffort = ResolveReasoningEffort(candidate.Profile, route);
        var sessionKey = BuildResponsesSessionKey(candidate.Profile, request.User);
        var state = string.IsNullOrWhiteSpace(sessionKey)
            ? null
            : _responsesSessions.TryGetValue(sessionKey, out var existingState)
                ? existingState
                : null;
        var (previousResponseId, inputItems) = BuildResponsesInput(request.Messages, candidate.Model, state);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = candidate.Model,
            ["input"] = inputItems
        };

        if (!string.IsNullOrWhiteSpace(previousResponseId))
        {
            payload["previous_response_id"] = previousResponseId;
        }

        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            payload["reasoning"] = new Dictionary<string, object?>
            {
                ["effort"] = reasoningEffort
            };
        }

        if (ShouldIncludeTemperature(candidate.Profile, reasoningEffort))
        {
            payload["temperature"] = request.Temperature;
        }

        if (request.Tools is { Length: > 0 })
        {
            payload["tools"] = request.Tools.Select(SerializeResponsesTool).ToArray();
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            BuildResponsesEndpoint(candidate.Profile.BaseUrl))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(candidate.Profile.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", candidate.Profile.ApiKey);
        }

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {TrimError(body)}");
        }

        using var document = JsonDocument.Parse(body);
        var completion = ExtractResponsesCompletion(document.RootElement, candidate);

        if (!string.IsNullOrWhiteSpace(sessionKey)
            && document.RootElement.TryGetProperty("id", out var responseIdElement)
            && responseIdElement.ValueKind == JsonValueKind.String)
        {
            var responseId = responseIdElement.GetString()?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(responseId))
            {
                _responsesSessions[sessionKey] = new ResponsesConversationState(
                    responseId,
                    request.Messages.Length,
                    candidate.Model);
            }
        }

        return completion;
    }

    private async Task<ChatCompletionResponse> SendViaOpenClawLocalAsync(
        ModelCandidate candidate,
        ChatCompletionRequest request,
        ModelRoute route,
        CancellationToken cancellationToken)
    {
        if (request.Messages.Any(message =>
                message.Content.Any(part => string.Equals(part.Type, "image_url", StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("openclaw-local does not support vision prompts.");
        }

        var helperPath = Path.Combine(_workspaceRoot, "scripts", "openclaw-local.mjs");

        if (!File.Exists(helperPath))
        {
            throw new InvalidOperationException($"openclaw-local helper was not found at {helperPath}");
        }

        var payload = new
        {
            workspaceRoot = _workspaceRoot,
            sessionId = string.IsNullOrWhiteSpace(request.User) ? Guid.NewGuid().ToString("n") : request.User.Trim(),
            model = candidate.Model,
            thinking = ResolveOpenClawThinking(route),
            timeoutMs = ResolveOpenClawTimeout(route),
            temperature = request.Temperature,
            messages = request.Messages,
            tools = request.Tools ?? []
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveNodeExecutablePath(),
            WorkingDirectory = _workspaceRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(helperPath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the openclaw-local helper process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"openclaw-local exited with code {process.ExitCode}: {TrimError(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr)}");
        }

        OpenClawLocalGatewayResponse? response;

        try
        {
            response = JsonSerializer.Deserialize<OpenClawLocalGatewayResponse>(stdout, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"openclaw-local returned invalid JSON: {TrimError(exception.Message)} | output={TrimError(stdout)}");
        }

        if (response is null)
        {
            throw new InvalidOperationException("openclaw-local returned an empty response.");
        }

        var toolCalls = (response.ToolCalls ?? [])
            .Where(toolCall => !string.IsNullOrWhiteSpace(toolCall.Name))
            .Select(toolCall => new ChatToolCall(
                Guid.NewGuid().ToString("n"),
                toolCall.Name.Trim(),
                toolCall.Arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? "{}"
                    : toolCall.Arguments.GetRawText()))
            .ToArray();

        return new ChatCompletionResponse(
            response.Content?.Trim() ?? string.Empty,
            toolCalls,
            string.IsNullOrWhiteSpace(candidate.Model) ? (response.Model?.Trim() ?? string.Empty) : candidate.Model,
            $"{candidate.Profile.Name}/{candidate.Profile.Provider}");
    }

    private static object SerializeMessage(ChatMessage message)
    {
        object content = message.Content.Length == 1 && string.Equals(message.Content[0].Type, "text", StringComparison.OrdinalIgnoreCase)
            ? message.Content[0].Text
            : message.Content.Select(SerializeContentPart).ToArray();

        var payload = new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = content
        };

        if (!string.IsNullOrWhiteSpace(message.ToolCallId))
        {
            payload["tool_call_id"] = message.ToolCallId;
        }

        if (message.ToolCalls is { Length: > 0 })
        {
            payload["tool_calls"] = message.ToolCalls.Select(toolCall => new Dictionary<string, object?>
            {
                ["id"] = toolCall.Id,
                ["type"] = "function",
                ["function"] = new Dictionary<string, object?>
                {
                    ["name"] = toolCall.Name,
                    ["arguments"] = toolCall.ArgumentsJson
                }
            }).ToArray();
        }

        return payload;
    }

    private static object SerializeContentPart(ChatMessageContentPart part)
    {
        return string.Equals(part.Type, "image_url", StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, object?>
            {
                ["type"] = "image_url",
                ["image_url"] = new Dictionary<string, object?>
                {
                    ["url"] = part.Url
                }
            }
            : new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = part.Text
            };
    }

    private static object SerializeTool(ChatToolDefinition tool)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["input"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["description"] = tool.InputDescription
                        },
                        ["purpose"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["description"] = tool.PurposeDescription
                        },
                        ["expectedEvidence"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["description"] = tool.ExpectedEvidenceDescription
                        }
                    },
                    ["additionalProperties"] = false
                }
            }
        };
    }

    private static object SerializeResponsesTool(ChatToolDefinition tool)
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "function",
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["input"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = tool.InputDescription
                    },
                    ["purpose"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = tool.PurposeDescription
                    },
                    ["expectedEvidence"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = tool.ExpectedEvidenceDescription
                    }
                },
                ["additionalProperties"] = false
            },
            ["strict"] = true
        };
    }

    private static ChatCompletionResponse ExtractCompletion(JsonElement root, ModelCandidate candidate)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Model response did not contain choices.");
        }

        var message = choices[0].GetProperty("message");
        var content = ExtractMessageContent(message);
        var toolCalls = ExtractToolCalls(message);

        return new ChatCompletionResponse(
            content,
            toolCalls,
            candidate.Model,
            $"{candidate.Profile.Name}/{candidate.Profile.Provider}");
    }

    private static string ExtractMessageContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                string.Empty,
                content.EnumerateArray().Select(element =>
                    element.TryGetProperty("text", out var text)
                        ? text.GetString() ?? string.Empty
                        : string.Empty)),
            _ => string.Empty
        };
    }

    private static ChatToolCall[] ExtractToolCalls(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var toolCalls)
            || toolCalls.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<ChatToolCall>();

        foreach (var toolCall in toolCalls.EnumerateArray())
        {
            if (!toolCall.TryGetProperty("function", out var function))
            {
                continue;
            }

            var id = toolCall.TryGetProperty("id", out var idElement)
                ? idElement.GetString() ?? Guid.NewGuid().ToString("n")
                : Guid.NewGuid().ToString("n");

            var name = GetString(function, "name");
            var arguments = GetString(function, "arguments") ?? "{}";

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            results.Add(new ChatToolCall(id, name.Trim(), arguments));
        }

        return results.ToArray();
    }

    private static ChatCompletionResponse ExtractResponsesCompletion(JsonElement root, ModelCandidate candidate)
    {
        var contentParts = new List<string>();
        var toolCalls = new List<ChatToolCall>();

        if (root.TryGetProperty("output", out var output)
            && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var type = GetString(item, "type") ?? string.Empty;

                switch (type)
                {
                    case "message":
                        contentParts.Add(ExtractResponsesMessageContent(item));
                        break;

                    case "function_call":
                    {
                        var name = GetString(item, "name");
                        var arguments = GetString(item, "arguments") ?? "{}";
                        var callId = GetString(item, "call_id")
                            ?? GetString(item, "id")
                            ?? Guid.NewGuid().ToString("n");

                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            toolCalls.Add(new ChatToolCall(callId, name.Trim(), arguments));
                        }

                        break;
                    }
                }
            }
        }

        return new ChatCompletionResponse(
            string.Join(Environment.NewLine, contentParts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim(),
            toolCalls.ToArray(),
            candidate.Model,
            $"{candidate.Profile.Name}/{candidate.Profile.Provider}");
    }

    private static string ExtractResponsesMessageContent(JsonElement item)
    {
        if (!item.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var contentItem in content.EnumerateArray())
        {
            var type = GetString(contentItem, "type") ?? string.Empty;

            if (string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(GetString(contentItem, "text") ?? string.Empty);
                continue;
            }

            if (string.Equals(type, "refusal", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(GetString(contentItem, "refusal") ?? string.Empty);
                continue;
            }

            builder.Append(GetString(contentItem, "text") ?? string.Empty);
        }

        return builder.ToString().Trim();
    }

    private static string BuildEndpoint(string baseUrl)
    {
        var trimmed = NormalizeOpenAiCompatibleBaseUrl(baseUrl);

        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}/chat/completions";
    }

    private static string BuildResponsesEndpoint(string baseUrl)
    {
        var trimmed = NormalizeOpenAiCompatibleBaseUrl(baseUrl);

        if (trimmed.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[..^"/chat/completions".Length] + "/responses";
        }

        return $"{trimmed}/responses";
    }

    private static string NormalizeOpenAiCompatibleBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var path = uri.AbsolutePath.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(path))
        {
            return $"{trimmed}/v1";
        }

        return trimmed;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static bool RequiresApiKey(string baseUrl, string provider)
    {
        if (string.Equals(provider, "local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return true;
        }

        return !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimError(string text)
    {
        const int maxLength = 320;
        var value = text.Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static bool IsOpenClaw(ResolvedProviderProfile profile) =>
        string.Equals(profile.Provider, "openclaw", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenClawLocal(ResolvedProviderProfile profile) =>
        string.Equals(profile.Provider, "openclaw-local", StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenAi(ResolvedProviderProfile profile) =>
        string.Equals(profile.Provider, "openai", StringComparison.OrdinalIgnoreCase);

    private static bool UseResponsesApi(ResolvedProviderProfile profile) =>
        IsOpenAi(profile);

    private static string ResolveReasoningEffort(ResolvedProviderProfile profile, ModelRoute route)
    {
        var configuredValue = route switch
        {
            ModelRoute.Fast => profile.FastReasoningEffort,
            ModelRoute.Vision => profile.VisionReasoningEffort,
            _ => profile.ReasoningEffort
        };

        return NormalizeReasoningEffort(configuredValue);
    }

    private static string NormalizeReasoningEffort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();

        return normalized switch
        {
            "none" => "none",
            "minimal" => "minimal",
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            "xhigh" => "xhigh",
            _ => string.Empty
        };
    }

    private static bool ShouldSendReasoningEffort(ResolvedProviderProfile profile, string reasoningEffort) =>
        !string.IsNullOrWhiteSpace(reasoningEffort)
        && (IsOpenAi(profile) || IsOpenAiHost(profile.BaseUrl));

    private static bool ShouldIncludeTemperature(ResolvedProviderProfile profile, string reasoningEffort)
    {
        if (!ShouldSendReasoningEffort(profile, reasoningEffort))
        {
            return true;
        }

        return string.Equals(reasoningEffort, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenAiHost(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "api.openai.com", StringComparison.OrdinalIgnoreCase);
    }

    private static int ResolveLocalMaxTokens(ResolvedProviderProfile profile, ModelRoute route)
    {
        if (!IsLocalLoopbackProfile(profile))
        {
            return 0;
        }

        return route switch
        {
            ModelRoute.Fast => 128,
            ModelRoute.Vision => 192,
            _ => 384
        };
    }

    private static bool IsLocalLoopbackProfile(ResolvedProviderProfile profile)
    {
        if (string.Equals(profile.Provider, "local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(profile.BaseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildResponsesSessionKey(ResolvedProviderProfile profile, string user)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            return string.Empty;
        }

        return $"{profile.Name}:{user.Trim()}";
    }

    private static (string PreviousResponseId, object[] InputItems) BuildResponsesInput(
        ChatMessage[] messages,
        string model,
        ResponsesConversationState? state)
    {
        if (state is null
            || string.IsNullOrWhiteSpace(state.ResponseId)
            || !string.Equals(state.Model, model, StringComparison.OrdinalIgnoreCase)
            || messages.Length < state.MessageCount)
        {
            return (string.Empty, SerializeResponsesMessages(messages, includeAssistantMessages: true));
        }

        var deltaMessages = messages.Skip(state.MessageCount).ToArray();
        var deltaItems = SerializeResponsesMessages(deltaMessages, includeAssistantMessages: false);

        if (deltaItems.Length == 0)
        {
            return (string.Empty, SerializeResponsesMessages(messages, includeAssistantMessages: true));
        }

        return (state.ResponseId, deltaItems);
    }

    private static object[] SerializeResponsesMessages(
        IEnumerable<ChatMessage> messages,
        bool includeAssistantMessages)
    {
        var items = new List<object>();

        foreach (var message in messages)
        {
            items.AddRange(SerializeResponsesMessage(message, includeAssistantMessages));
        }

        return items.ToArray();
    }

    private static IEnumerable<object> SerializeResponsesMessage(ChatMessage message, bool includeAssistantMessages)
    {
        if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(message.ToolCallId))
            {
                yield return new Dictionary<string, object?>
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = message.ToolCallId,
                    ["output"] = ExtractPlainText(message.Content)
                };
            }

            yield break;
        }

        if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) && !includeAssistantMessages)
        {
            yield break;
        }

        if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
            && message.ToolCalls is { Length: > 0 })
        {
            var commentary = ExtractPlainText(message.Content);

            if (!string.IsNullOrWhiteSpace(commentary))
            {
                yield return new Dictionary<string, object?>
                {
                    ["role"] = "assistant",
                    ["phase"] = "commentary",
                    ["content"] = commentary
                };
            }

            foreach (var toolCall in message.ToolCalls)
            {
                yield return new Dictionary<string, object?>
                {
                    ["type"] = "function_call",
                    ["call_id"] = toolCall.Id,
                    ["name"] = toolCall.Name,
                    ["arguments"] = toolCall.ArgumentsJson
                };
            }

            yield break;
        }

        var payload = new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = SerializeResponsesContent(message.Content)
        };

        if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            payload["phase"] = "final_answer";
        }

        yield return payload;
    }

    private static object SerializeResponsesContent(ChatMessageContentPart[] parts)
    {
        if (parts.Length == 1
            && string.Equals(parts[0].Type, "text", StringComparison.OrdinalIgnoreCase))
        {
            return parts[0].Text;
        }

        return parts.Select(part =>
            string.Equals(part.Type, "image_url", StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, object?>
                {
                    ["type"] = "input_image",
                    ["image_url"] = part.Url
                }
                : new Dictionary<string, object?>
                {
                    ["type"] = "input_text",
                    ["text"] = part.Text
                }).ToArray();
    }

    private static string ExtractPlainText(ChatMessageContentPart[] parts)
    {
        return string.Join(
            Environment.NewLine,
            parts
                .Where(part => string.Equals(part.Type, "text", StringComparison.OrdinalIgnoreCase))
                .Select(part => part.Text))
            .Trim();
    }

    private static string ResolveRequestModel(ResolvedProviderProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.RequestModel))
        {
            return profile.RequestModel;
        }

        return IsOpenClaw(profile) ? "openclaw/default" : string.Empty;
    }

    private static string ResolveOpenClawThinking(ModelRoute route) =>
        route switch
        {
            ModelRoute.Fast => "low",
            ModelRoute.Vision => "medium",
            _ => "high"
        };

    private static int ResolveOpenClawTimeout(ModelRoute route) =>
        route switch
        {
            ModelRoute.Fast => 15_000,
            ModelRoute.Vision => 20_000,
            _ => 30_000
        };

    private static string ResolveNodeExecutablePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("OPENCLAW_NODE_PATH");

        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath.Trim();
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var candidates = new[]
        {
            Path.Combine(programFiles, "nodejs", "node.exe"),
            Path.Combine(programFilesX86, "nodejs", "node.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "node";
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

    private sealed record ModelCandidate(ResolvedProviderProfile Profile, string Model);

    private sealed record ResponsesConversationState(
        string ResponseId,
        int MessageCount,
        string Model);
}

internal sealed record ResolvedProviderProfile(
    string Name,
    string Provider,
    string BaseUrl,
    string RequestModel,
    string FastModel,
    string ReasoningModel,
    string VisionModel,
    string FastReasoningEffort,
    string ReasoningEffort,
    string VisionReasoningEffort,
    string ApiKey,
    string[] FallbackModels);

internal sealed record OpenClawLocalGatewayResponse(
    string? Content,
    OpenClawLocalGatewayToolCall[]? ToolCalls,
    string? Provider,
    string? Model);

internal sealed record OpenClawLocalGatewayToolCall(
    string Name,
    JsonElement Arguments);
