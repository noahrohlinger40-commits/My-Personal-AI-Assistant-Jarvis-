namespace Jarvis.Core;

internal sealed partial class StructuredAssistantPlanner
{
    private async Task<ChatCompletionResponse> ExecuteModelCallAsync(
        ChatCompletionRequest request,
        string operation,
        string sessionId,
        IProgress<AssistantActivityEvent>? activity,
        ModelRoute route,
        CancellationToken cancellationToken)
    {
        var totalAttempts = Math.Max(1, _options.PlannerRetryCount + 1);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= totalAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await _modelGateway.CompleteAsync(request, route, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastError = exception;

                if (attempt >= totalAttempts)
                {
                    break;
                }

                activity?.Report(new AssistantActivityEvent(
                    AssistantActivityKind.Warning,
                    $"Model call for {operation} failed. Retrying {attempt} of {totalAttempts - 1}.",
                    sessionId,
                    Detail: TrimError(exception.Message)));

                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }

        throw lastError ?? new InvalidOperationException($"The model call for {operation} failed.");
    }
}
