namespace Jarvis.Core;

public interface IVisualContextService
{
    Task<ToolResult> AnalyzeCurrentScreenAsync(string input, CancellationToken cancellationToken);

    Task<ToolResult> CaptureScreenAsync(string input, CancellationToken cancellationToken);

    Task<ToolResult> ExtractScreenTextAsync(string input, CancellationToken cancellationToken);

    Task<ToolResult> DetectScreenUiAsync(string input, CancellationToken cancellationToken);

    Task<ToolResult> ListDisplaysAsync(CancellationToken cancellationToken);

    Task<ToolResult> AnalyzeAmbientContextAsync(string input, CancellationToken cancellationToken);

    Task<ToolResult> AnalyzeImageAsync(string input, CancellationToken cancellationToken);

    Task<ToolResult> AnalyzeDocumentAsync(string input, CancellationToken cancellationToken);
}

public sealed class NullVisualContextService : IVisualContextService
{
    public Task<ToolResult> AnalyzeCurrentScreenAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolResult(
            "Visual understanding is unavailable because no vision-capable runtime is configured.",
            Succeeded: false,
            VerificationText: "No visual service is active.",
            SummaryText: "Visual understanding is unavailable."));
    }

    public Task<ToolResult> CaptureScreenAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolResult(
            "Screen capture is unavailable because no Windows visual service is active.",
            Succeeded: false,
            VerificationText: "No visual service is active.",
            SummaryText: "Screen capture is unavailable."));
    }

    public Task<ToolResult> ExtractScreenTextAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolResult(
            "Screen OCR is unavailable because no vision-capable runtime is configured.",
            Succeeded: false,
            VerificationText: "No visual service is active.",
            SummaryText: "Screen OCR is unavailable."));
    }

    public Task<ToolResult> DetectScreenUiAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolResult(
            "Structured UI detection is unavailable because no vision-capable runtime is configured.",
            Succeeded: false,
            VerificationText: "No visual service is active.",
            SummaryText: "UI detection is unavailable."));
    }

    public Task<ToolResult> ListDisplaysAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolResult(
            "Display inventory is unavailable because no Windows visual service is active.",
            Succeeded: false,
            VerificationText: "No visual service is active.",
            SummaryText: "Display inventory is unavailable."));
    }

    public Task<ToolResult> AnalyzeAmbientContextAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolResult(
            "Ambient sensing is unavailable because no ambient integration is configured.",
            Succeeded: false,
            VerificationText: "No ambient service is active.",
            SummaryText: "Ambient sensing is unavailable."));
    }

    public Task<ToolResult> AnalyzeImageAsync(string input, CancellationToken cancellationToken)
    {
        return AnalyzeCurrentScreenAsync(input, cancellationToken);
    }

    public Task<ToolResult> AnalyzeDocumentAsync(string input, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolResult(
            "Document understanding is unavailable because no analysis service is configured.",
            Succeeded: false,
            VerificationText: "No document analysis service is active.",
            SummaryText: "Document understanding is unavailable."));
    }
}
