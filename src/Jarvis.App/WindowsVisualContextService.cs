using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed class WindowsVisualContextService : IVisualContextService
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".bmp",
        ".webp",
        ".tif",
        ".tiff"
    };

    private static readonly HashSet<string> SupportedTextDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".json",
        ".cs",
        ".csproj",
        ".sln",
        ".xml",
        ".yml",
        ".yaml",
        ".html",
        ".css",
        ".js",
        ".ts",
        ".tsx",
        ".jsx",
        ".ps1",
        ".cmd",
        ".bat",
        ".csv",
        ".log",
        ".ini",
        ".config",
        ".toml"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions IndentedJsonOptions = new(JsonOptions)
    {
        WriteIndented = true
    };

    private readonly string _workspaceRoot;
    private readonly JarvisOptions _options;
    private readonly IModelGateway _modelGateway;

    public WindowsVisualContextService(string workspaceRoot, JarvisOptions options, IModelGateway modelGateway)
    {
        _workspaceRoot = workspaceRoot;
        _options = options;
        _modelGateway = modelGateway;
    }

    public async Task<ToolResult> AnalyzeCurrentScreenAsync(string input, CancellationToken cancellationToken)
    {
        CapturedScreen capture;

        try
        {
            capture = CaptureScreen(ParseScreenRequest(input));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed("Visual analysis failed.", exception.Message, "The visual context service could not capture the requested display.");
        }

        var question = string.IsNullOrWhiteSpace(capture.Request.Question)
            ? "Describe the captured display, the visible UI, notable text, and any state that matters right now."
            : capture.Request.Question;

        if (!_modelGateway.IsAvailable)
        {
            return BuildCaptureResult(
                capture,
                "Captured a fresh screenshot, but no vision-capable model is configured. Ask `screen ocr`, `screen ui`, or `analyze image` again after wiring a planner vision model.",
                succeeded: true);
        }

        var prompt = string.Join(
            Environment.NewLine,
            BuildCapturePromptContext(capture),
            $"Question: {question}");

        try
        {
            var content = await CompleteVisionPromptAsync(
                "You are Jarvis. Analyze the supplied desktop screenshot. Stay grounded in the image and metadata. Call out the active window, visible applications, meaningful text, and actionable UI state. If something is uncertain, say so plainly.",
                [ChatMessageContentPart.FromText(prompt), await BuildImagePartAsync(capture.FilePath, cancellationToken)],
                cancellationToken);

            content = string.IsNullOrWhiteSpace(content)
                ? $"I captured {capture.TargetLabel}, but the vision model returned an empty response."
                : content.Trim();

            return new ToolResult(
                $"Visual analysis for {capture.FilePath}:{Environment.NewLine}{content}",
                VerificationText: $"Captured {capture.TargetLabel} to {capture.FilePath} and analyzed it with the vision route.",
                SummaryText: FirstSentence(content));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BuildCaptureResult(
                capture,
                $"Captured the screenshot, but model analysis failed: {TrimError(exception.Message)}",
                succeeded: false);
        }
    }

    public Task<ToolResult> CaptureScreenAsync(string input, CancellationToken cancellationToken)
    {
        try
        {
            var capture = CaptureScreen(ParseScreenRequest(input));
            return Task.FromResult(BuildCaptureResult(capture, "Capture completed.", succeeded: true));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(Failed("Screen capture failed.", exception.Message, "The visual context service could not save the requested screenshot."));
        }
    }

    public async Task<ToolResult> ExtractScreenTextAsync(string input, CancellationToken cancellationToken)
    {
        CapturedScreen capture;

        try
        {
            capture = CaptureScreen(ParseScreenRequest(input));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed("Screen OCR failed.", exception.Message, "The visual context service could not capture the requested display.");
        }

        if (!_modelGateway.IsAvailable)
        {
            return BuildCaptureResult(
                capture,
                "OCR-style extraction requires a vision-capable model route. The screenshot was still captured for later inspection.",
                succeeded: false);
        }

        var prompt = string.Join(
            Environment.NewLine,
            BuildCapturePromptContext(capture),
            string.IsNullOrWhiteSpace(capture.Request.Question)
                ? "Extract the visible text in reading order and summarize what the text suggests about the current task."
                : $"Additional instruction: {capture.Request.Question}");

        try
        {
            var content = await CompleteVisionPromptAsync(
                "You are Jarvis. Perform OCR-style extraction on the supplied desktop screenshot. Return only JSON with keys `summary`, `fullText`, and `textBlocks`. Each `textBlocks` item must contain `text`, `region`, and `purpose`.",
                [ChatMessageContentPart.FromText(prompt), await BuildImagePartAsync(capture.FilePath, cancellationToken)],
                cancellationToken);

            var extraction = ParseJson<OcrExtractionContract>(content);

            if (extraction is null)
            {
                return new ToolResult(
                    $"Screen OCR for {capture.FilePath}:{Environment.NewLine}{content}",
                    VerificationText: $"Captured {capture.TargetLabel} to {capture.FilePath} and asked the vision route to extract text.",
                    SummaryText: "Extracted visible screen text.");
            }

            var lines = new List<string>
            {
                $"Screen OCR for {capture.FilePath}:",
                $"- Summary: {Coalesce(extraction.Summary, "No OCR summary returned.")}",
                $"- Full text: {Coalesce(extraction.FullText, "No text returned.")}"
            };

            if (extraction.TextBlocks is { Length: > 0 })
            {
                lines.Add("- Text blocks:");

                foreach (var block in extraction.TextBlocks.Take(10))
                {
                    lines.Add($"  - {Coalesce(block.Region, "unknown region")} | {Coalesce(block.Purpose, "unspecified")} | {Coalesce(block.Text, "(empty)")}");
                }
            }

            return new ToolResult(
                string.Join(Environment.NewLine, lines),
                VerificationText: $"Captured {capture.TargetLabel} to {capture.FilePath} and extracted OCR-style text with the vision route.",
                SummaryText: FirstSentence(Coalesce(extraction.Summary, extraction.FullText)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BuildCaptureResult(
                capture,
                $"Captured the screenshot, but OCR-style extraction failed: {TrimError(exception.Message)}",
                succeeded: false);
        }
    }

    public async Task<ToolResult> DetectScreenUiAsync(string input, CancellationToken cancellationToken)
    {
        CapturedScreen capture;

        try
        {
            capture = CaptureScreen(ParseScreenRequest(input));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed("Structured UI detection failed.", exception.Message, "The visual context service could not capture the requested display.");
        }

        if (!_modelGateway.IsAvailable)
        {
            return BuildCaptureResult(
                capture,
                "Structured UI extraction requires a vision-capable model route. The screenshot was still captured for later inspection.",
                succeeded: false);
        }

        var prompt = string.Join(
            Environment.NewLine,
            BuildCapturePromptContext(capture),
            string.IsNullOrWhiteSpace(capture.Request.Question)
                ? "Identify the major UI elements, controls, visible text, and likely interaction targets."
                : $"Additional instruction: {capture.Request.Question}");

        try
        {
            var content = await CompleteVisionPromptAsync(
                "You are Jarvis. Detect structured UI elements from the supplied desktop screenshot. Return only JSON with keys `summary`, `applicationGuess`, and `uiElements`. Each `uiElements` item must contain `label`, `role`, `text`, `state`, `region`, and `importance`.",
                [ChatMessageContentPart.FromText(prompt), await BuildImagePartAsync(capture.FilePath, cancellationToken)],
                cancellationToken);

            var detection = ParseJson<UiDetectionContract>(content);

            if (detection is null)
            {
                return new ToolResult(
                    $"Structured UI detection for {capture.FilePath}:{Environment.NewLine}{content}",
                    VerificationText: $"Captured {capture.TargetLabel} to {capture.FilePath} and asked the vision route to detect structured UI elements.",
                    SummaryText: "Detected screen UI elements.");
            }

            var lines = new List<string>
            {
                $"Structured UI detection for {capture.FilePath}:",
                $"- Summary: {Coalesce(detection.Summary, "No UI summary returned.")}",
                $"- Application guess: {Coalesce(detection.ApplicationGuess, "Unknown")}"
            };

            if (detection.UiElements is { Length: > 0 })
            {
                lines.Add("- UI elements:");

                foreach (var element in detection.UiElements.Take(12))
                {
                    lines.Add($"  - {Coalesce(element.Region, "unknown region")} | {Coalesce(element.Role, "control")} | {Coalesce(element.Label, "unnamed")} | text={Coalesce(element.Text, "(none)")} | state={Coalesce(element.State, "unknown")} | importance={Coalesce(element.Importance, "normal")}");
                }
            }

            return new ToolResult(
                string.Join(Environment.NewLine, lines),
                VerificationText: $"Captured {capture.TargetLabel} to {capture.FilePath} and extracted structured UI elements with the vision route.",
                SummaryText: FirstSentence(Coalesce(detection.Summary, detection.ApplicationGuess)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BuildCaptureResult(
                capture,
                $"Captured the screenshot, but structured UI detection failed: {TrimError(exception.Message)}",
                succeeded: false);
        }
    }

    public Task<ToolResult> ListDisplaysAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displays = GetDisplays();
            var activeWindow = TryGetActiveWindowSnapshot();
            var activeDisplay = ResolveActiveDisplay(displays, activeWindow);
            var lines = new List<string>
            {
                "Display inventory:",
                $"- Displays detected: {displays.Length}",
                $"- Active display: {(activeDisplay is null ? "Unknown" : DescribeDisplay(activeDisplay))}"
            };

            foreach (var display in displays)
            {
                var isActive = activeDisplay is not null && display.Index == activeDisplay.Index ? " | active" : string.Empty;
                lines.Add($"- {DescribeDisplay(display)}{isActive}");
            }

            return Task.FromResult(new ToolResult(
                string.Join(Environment.NewLine, lines),
                VerificationText: $"Enumerated {displays.Length} connected display(s).",
                SummaryText: $"Detected {displays.Length} display(s)."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(Failed("Display inventory failed.", exception.Message, "The visual context service could not enumerate displays."));
        }
    }

    public async Task<ToolResult> AnalyzeAmbientContextAsync(string input, CancellationToken cancellationToken)
    {
        var normalized = input.Trim();

        if (string.IsNullOrWhiteSpace(normalized) || normalized.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return BuildAmbientStatusResult();
        }

        if (StartsWithCommand(normalized, "webcam") || StartsWithCommand(normalized, "camera"))
        {
            var payload = TrimCommandPrefix(normalized, "webcam", "camera");
            return await AnalyzeAmbientFrameAsync(payload, "Describe the room scene, visible people, gestures, and anything relevant for ambient activation.", "Ambient webcam analysis", cancellationToken);
        }

        if (StartsWithCommand(normalized, "presence") || StartsWithCommand(normalized, "gesture"))
        {
            return await AnalyzePresenceAsync(normalized, cancellationToken);
        }

        if (StartsWithCommand(normalized, "face recognition") || StartsWithCommand(normalized, "face"))
        {
            return await AnalyzeFaceRecognitionAsync(normalized, cancellationToken);
        }

        if (StartsWithCommand(normalized, "room sensors") || StartsWithCommand(normalized, "room"))
        {
            return await AnalyzeRoomSensorsAsync(TrimCommandPrefix(normalized, "room sensors", "room"), cancellationToken);
        }

        return BuildAmbientStatusResult();
    }

    public async Task<ToolResult> AnalyzeImageAsync(string input, CancellationToken cancellationToken)
    {
        var payload = ParsePayload(input);

        if (string.IsNullOrWhiteSpace(payload.Path))
        {
            return new ToolResult(
                "Usage: analyze image <path> or analyze image <path> :: <question>",
                Succeeded: false,
                VerificationText: "No image path was provided.",
                SummaryText: "Missing image path.");
        }

        var resolvedPath = ResolvePath(payload.Path);

        if (!File.Exists(resolvedPath))
        {
            return new ToolResult(
                $"Image file not found: {resolvedPath}",
                Succeeded: false,
                VerificationText: "The requested image path does not exist.",
                SummaryText: "Image file not found.");
        }

        if (!SupportedImageExtensions.Contains(Path.GetExtension(resolvedPath)))
        {
            return new ToolResult(
                $"Unsupported image type: {resolvedPath}",
                Succeeded: false,
                VerificationText: "The file extension is not in the supported image list.",
                SummaryText: "Unsupported image type.");
        }

        if (!_modelGateway.IsAvailable)
        {
            return Unavailable("Image understanding is unavailable because no model provider is configured.");
        }

        var question = string.IsNullOrWhiteSpace(payload.Question)
            ? "Describe the image and call out anything visually important."
            : payload.Question;

        return await AnalyzeSingleImageAsync(
            resolvedPath,
            question,
            $"Read the image from {resolvedPath} and analyzed it with the vision route.",
            "Visual analysis",
            cancellationToken);
    }

    public async Task<ToolResult> AnalyzeDocumentAsync(string input, CancellationToken cancellationToken)
    {
        var payload = ParsePayload(input);

        if (string.IsNullOrWhiteSpace(payload.Path))
        {
            return new ToolResult(
                "Usage: analyze document <path> or analyze document <path> :: <question>",
                Succeeded: false,
                VerificationText: "No document path was provided.",
                SummaryText: "Missing document path.");
        }

        var resolvedPath = ResolvePath(payload.Path);

        if (!File.Exists(resolvedPath))
        {
            return new ToolResult(
                $"Document not found: {resolvedPath}",
                Succeeded: false,
                VerificationText: "The requested document path does not exist.",
                SummaryText: "Document file not found.");
        }

        var extension = Path.GetExtension(resolvedPath);

        if (SupportedImageExtensions.Contains(extension))
        {
            return await AnalyzeImageDocumentAsync(resolvedPath, payload.Question, cancellationToken);
        }

        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await AnalyzePdfDocumentAsync(resolvedPath, payload.Question, cancellationToken);
        }

        if (!SupportedTextDocumentExtensions.Contains(extension))
        {
            return new ToolResult(
                $"Unsupported document type: {resolvedPath}",
                Succeeded: false,
                VerificationText: "The file extension is not in the supported text-document list.",
                SummaryText: "Unsupported document type.");
        }

        return await AnalyzeTextDocumentAsync(resolvedPath, payload.Question, cancellationToken);
    }

    private async Task<ToolResult> AnalyzeTextDocumentAsync(string resolvedPath, string question, CancellationToken cancellationToken)
    {
        var documentText = await File.ReadAllTextAsync(resolvedPath, cancellationToken);

        if (string.IsNullOrWhiteSpace(documentText))
        {
            return new ToolResult(
                $"Document is empty: {resolvedPath}",
                Succeeded: false,
                VerificationText: "The file was read successfully but did not contain text.",
                SummaryText: "Document is empty.");
        }

        var metadataLines = new[]
        {
            $"Document type: text {Path.GetExtension(resolvedPath)}",
            $"Characters: {documentText.Length}",
            $"Lines: {CountLines(documentText)}"
        };

        return await AnalyzeStructuredTextDocumentAsync(
            resolvedPath,
            "text document",
            documentText,
            question,
            metadataLines,
            cancellationToken);
    }

    private async Task<ToolResult> AnalyzePdfDocumentAsync(string resolvedPath, string question, CancellationToken cancellationToken)
    {
        PdfExtractionResult extraction;

        try
        {
            extraction = ExtractPdfContent(resolvedPath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ToolResult(
                $"PDF analysis failed: {TrimError(exception.Message)}",
                Succeeded: false,
                VerificationText: $"The runtime could not parse the PDF at {resolvedPath}.",
                SummaryText: "PDF analysis failed.");
        }

        var metadataLines = new List<string>
        {
            "Document type: PDF",
            extraction.PageCount > 0 ? $"Estimated pages: {extraction.PageCount}" : "Estimated pages: unknown",
            $"Extraction confidence: {extraction.Confidence}"
        };

        if (!string.IsNullOrWhiteSpace(extraction.Title))
        {
            metadataLines.Add($"Title: {extraction.Title}");
        }

        if (!string.IsNullOrWhiteSpace(extraction.Author))
        {
            metadataLines.Add($"Author: {extraction.Author}");
        }

        if (!string.IsNullOrWhiteSpace(extraction.Note))
        {
            metadataLines.Add($"Extraction note: {extraction.Note}");
        }

        if (string.IsNullOrWhiteSpace(extraction.Text))
        {
            var lines = new List<string> { $"PDF analysis for {resolvedPath}:" };
            lines.AddRange(metadataLines.Select(line => $"- {line}"));
            lines.Add("- Extracted text preview: No embedded text could be recovered from this PDF. It may be image-heavy or scanned.");

            return new ToolResult(
                string.Join(Environment.NewLine, lines),
                Succeeded: false,
                VerificationText: $"Inspected the PDF structure at {resolvedPath}, but no usable embedded text was recovered.",
                SummaryText: "PDF extraction produced no usable text.");
        }

        return await AnalyzeStructuredTextDocumentAsync(
            resolvedPath,
            "PDF",
            extraction.Text,
            question,
            metadataLines,
            cancellationToken);
    }

    private async Task<ToolResult> AnalyzeImageDocumentAsync(string resolvedPath, string question, CancellationToken cancellationToken)
    {
        if (!_modelGateway.IsAvailable)
        {
            return Unavailable("Image-based document OCR requires a vision-capable model route.");
        }

        var effectiveQuestion = string.IsNullOrWhiteSpace(question)
            ? "Extract the document text, identify the document type, and summarize the important details."
            : question.Trim();

        try
        {
            var content = await CompleteVisionPromptAsync(
                "You are Jarvis. The supplied image is a document, scan, receipt, form, slide, or OCR-heavy page. Perform OCR and visual document understanding. Return only JSON with keys `summary`, `documentType`, `confidence`, `extractedTextPreview`, `caveats`, `keyPoints`, `actionItems`, `sections`, and `keyFields`. Each `sections` item must contain `heading` and `purpose`. Each `keyFields` item must contain `label` and `value`.",
                [ChatMessageContentPart.FromText($"Question: {effectiveQuestion}{Environment.NewLine}Path: {resolvedPath}"),
                 await BuildImagePartAsync(resolvedPath, cancellationToken)],
                cancellationToken);

            var analysis = ParseJson<DocumentAnalysisContract>(content);

            if (analysis is null)
            {
                return new ToolResult(
                    $"Document OCR for {resolvedPath}:{Environment.NewLine}{content}",
                    VerificationText: $"Read the image-based document from {resolvedPath} and analyzed it with the vision route.",
                    SummaryText: "Analyzed image-based document.");
            }

            return BuildStructuredDocumentResult(
                resolvedPath,
                analysis,
                "image document",
                $"Read the image-based document from {resolvedPath} and analyzed it with the vision route.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ToolResult(
                $"Document OCR failed: {TrimError(exception.Message)}",
                Succeeded: false,
                VerificationText: $"The visual context service could not analyze the document image at {resolvedPath}.",
                SummaryText: "Document OCR failed.");
        }
    }

    private async Task<ToolResult> AnalyzeStructuredTextDocumentAsync(
        string resolvedPath,
        string sourceKind,
        string documentText,
        string question,
        IReadOnlyList<string> metadataLines,
        CancellationToken cancellationToken)
    {
        var excerpt = TruncateDocumentText(documentText, 16000);

        if (!_modelGateway.IsAvailable)
        {
            return new ToolResult(
                BuildDocumentPreview(resolvedPath, sourceKind, excerpt, metadataLines),
                VerificationText: "The file was read successfully without model summarization.",
                SummaryText: $"Loaded a document preview from {resolvedPath}.");
        }

        var effectiveQuestion = string.IsNullOrWhiteSpace(question)
            ? "Summarize the document and highlight the most important details."
            : question.Trim();

        var messages = new[]
        {
            new ChatMessage("system", [ChatMessageContentPart.FromText("You are Jarvis. Analyze the supplied document excerpt and return only JSON with keys `summary`, `documentType`, `confidence`, `extractedTextPreview`, `caveats`, `keyPoints`, `actionItems`, `sections`, and `keyFields`. Each `sections` item must contain `heading` and `purpose`. Each `keyFields` item must contain `label` and `value`. Keep the analysis grounded in the provided text; if OCR or PDF extraction appears incomplete, say so in `caveats`.")]),
            new ChatMessage("user", [ChatMessageContentPart.FromText(
                string.Join(Environment.NewLine, BuildDocumentPromptLines(resolvedPath, effectiveQuestion, metadataLines, excerpt)))])
        };

        try
        {
            var response = await _modelGateway.CompleteAsync(
                new ChatCompletionRequest(messages, Temperature: 0.1),
                ModelRoute.Fast,
                cancellationToken);

            var content = response.Content?.Trim() ?? string.Empty;
            var analysis = ParseJson<DocumentAnalysisContract>(content);

            if (analysis is null)
            {
                var fallback = string.IsNullOrWhiteSpace(content)
                    ? $"I read the document at {resolvedPath}, but the analysis model returned an empty response."
                    : content;

                return new ToolResult(
                    $"Document analysis for {resolvedPath}:{Environment.NewLine}{fallback}",
                    VerificationText: $"Read the document from {resolvedPath} and analyzed the excerpt with the fast model route.",
                    SummaryText: FirstSentence(fallback));
            }

            return BuildStructuredDocumentResult(
                resolvedPath,
                analysis,
                sourceKind,
                $"Read the document from {resolvedPath} and analyzed the excerpt with the fast model route.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ToolResult(
                $"Document analysis failed: {TrimError(exception.Message)}",
                Succeeded: false,
                VerificationText: $"The document was read, but model analysis failed for {resolvedPath}.",
                SummaryText: "Document analysis failed.");
        }
    }

    private static ToolResult BuildStructuredDocumentResult(
        string resolvedPath,
        DocumentAnalysisContract analysis,
        string sourceKind,
        string verificationText)
    {
        var lines = new List<string>
        {
            $"Document analysis for {resolvedPath}:",
            $"- Source: {sourceKind}",
            $"- Document type: {Coalesce(analysis.DocumentType, "Unknown")}",
            $"- Summary: {Coalesce(analysis.Summary, "No summary returned.")}",
            $"- Confidence: {Coalesce(analysis.Confidence, "Unknown")}"
        };

        if (!string.IsNullOrWhiteSpace(analysis.ExtractedTextPreview))
        {
            lines.Add($"- Extracted text preview: {analysis.ExtractedTextPreview.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(analysis.Caveats))
        {
            lines.Add($"- Caveats: {analysis.Caveats.Trim()}");
        }

        if (analysis.KeyPoints is { Length: > 0 })
        {
            lines.Add("- Key points:");
            lines.AddRange(analysis.KeyPoints.Where(item => !string.IsNullOrWhiteSpace(item)).Take(8).Select(item => $"  - {item.Trim()}"));
        }

        if (analysis.KeyFields is { Length: > 0 })
        {
            lines.Add("- Key fields:");
            lines.AddRange(analysis.KeyFields
                .Where(item => !string.IsNullOrWhiteSpace(item.Label) || !string.IsNullOrWhiteSpace(item.Value))
                .Take(10)
                .Select(item => $"  - {Coalesce(item.Label, "field")}: {Coalesce(item.Value, "(missing)")}"));
        }

        if (analysis.Sections is { Length: > 0 })
        {
            lines.Add("- Sections:");
            lines.AddRange(analysis.Sections
                .Where(item => !string.IsNullOrWhiteSpace(item.Heading) || !string.IsNullOrWhiteSpace(item.Purpose))
                .Take(10)
                .Select(item => $"  - {Coalesce(item.Heading, "untitled")} | {Coalesce(item.Purpose, "no purpose returned")}"));
        }

        if (analysis.ActionItems is { Length: > 0 })
        {
            lines.Add("- Action items:");
            lines.AddRange(analysis.ActionItems.Where(item => !string.IsNullOrWhiteSpace(item)).Take(8).Select(item => $"  - {item.Trim()}"));
        }

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: verificationText,
            SummaryText: FirstSentence(Coalesce(analysis.Summary, analysis.DocumentType)));
    }

    private static string BuildDocumentPreview(
        string resolvedPath,
        string sourceKind,
        string excerpt,
        IReadOnlyList<string> metadataLines)
    {
        var lines = new List<string>
        {
            $"Document preview for {resolvedPath}:",
            $"- Source: {sourceKind}"
        };

        lines.AddRange(metadataLines.Select(line => $"- {line}"));
        lines.Add($"- Extracted text preview: {BuildPreviewText(excerpt, 1600)}");
        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyList<string> BuildDocumentPromptLines(
        string resolvedPath,
        string question,
        IReadOnlyList<string> metadataLines,
        string excerpt)
    {
        var lines = new List<string>
        {
            $"Question: {question}",
            $"Path: {resolvedPath}"
        };

        lines.AddRange(metadataLines);
        lines.Add($"Document excerpt:{Environment.NewLine}{excerpt}");
        return lines;
    }

    private static string TruncateDocumentText(string documentText, int maxCharacters)
    {
        var normalized = NormalizeExtractedDocumentText(documentText);

        if (normalized.Length <= maxCharacters)
        {
            return normalized;
        }

        return normalized[..maxCharacters].TrimEnd() + Environment.NewLine + "[document truncated]";
    }

    private static string BuildPreviewText(string documentText, int maxCharacters)
    {
        var normalized = NormalizeExtractedDocumentText(documentText);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "No text preview available.";
        }

        return normalized.Length <= maxCharacters
            ? normalized
            : normalized[..maxCharacters].TrimEnd() + "...";
    }

    private static int CountLines(string documentText)
    {
        if (string.IsNullOrEmpty(documentText))
        {
            return 0;
        }

        return documentText.Count(character => character == '\n') + 1;
    }

    private static PdfExtractionResult ExtractPdfContent(string resolvedPath)
    {
        var bytes = File.ReadAllBytes(resolvedPath);
        var rawText = Encoding.Latin1.GetString(bytes);
        var pageCount = Regex.Matches(rawText, @"/Type\s*/Page\b", RegexOptions.CultureInvariant).Count;
        var fragments = new List<string>();

        foreach (var stream in EnumeratePdfStreams(bytes))
        {
            if (stream.Dictionary.Contains("/Subtype /Image", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var decoded = DecodePdfStream(stream.Data, stream.Dictionary);

            if (decoded.Length == 0)
            {
                continue;
            }

            var extracted = ExtractTextFromPdfStream(decoded);

            if (!string.IsNullOrWhiteSpace(extracted))
            {
                fragments.Add(extracted);
            }
        }

        var extractedText = NormalizeExtractedDocumentText(string.Join(Environment.NewLine, fragments));

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            extractedText = NormalizeExtractedDocumentText(ExtractTextFromPdfStream(bytes));
        }

        var confidence = DeterminePdfExtractionConfidence(extractedText);
        var note = confidence switch
        {
            "high" => "Embedded text extraction recovered a substantial amount of readable PDF text.",
            "medium" => "Embedded text extraction recovered partial PDF text; layout and tables may be incomplete.",
            _ => "This PDF appears image-heavy, scanned, or encoded with non-extractable text. Results may be incomplete."
        };

        return new PdfExtractionResult(
            pageCount,
            TryExtractPdfMetadata(rawText, "Title"),
            TryExtractPdfMetadata(rawText, "Author"),
            extractedText,
            confidence,
            note);
    }

    private static IEnumerable<PdfStreamCandidate> EnumeratePdfStreams(byte[] bytes)
    {
        var streamToken = Encoding.ASCII.GetBytes("stream");
        var endStreamToken = Encoding.ASCII.GetBytes("endstream");
        var index = 0;

        while (TryFindBytes(bytes, streamToken, index, out var streamIndex))
        {
            if (!TryFindBytes(bytes, endStreamToken, streamIndex + streamToken.Length, out var endStreamIndex))
            {
                yield break;
            }

            var dataStart = streamIndex + streamToken.Length;

            if (dataStart < bytes.Length && bytes[dataStart] == (byte)'\r')
            {
                dataStart++;
            }

            if (dataStart < bytes.Length && bytes[dataStart] == (byte)'\n')
            {
                dataStart++;
            }

            var dataEnd = endStreamIndex;

            while (dataEnd > dataStart && (bytes[dataEnd - 1] == (byte)'\r' || bytes[dataEnd - 1] == (byte)'\n'))
            {
                dataEnd--;
            }

            if (dataEnd > dataStart)
            {
                var dictionaryStart = FindLastBytes(bytes, Encoding.ASCII.GetBytes("<<"), streamIndex);
                var dictionaryEnd = FindLastBytes(bytes, Encoding.ASCII.GetBytes(">>"), streamIndex);

                if (dictionaryStart >= 0 && dictionaryEnd >= dictionaryStart)
                {
                    var dictionaryBytes = bytes[dictionaryStart..(dictionaryEnd + 2)];
                    var streamBytes = bytes[dataStart..dataEnd];
                    yield return new PdfStreamCandidate(Encoding.Latin1.GetString(dictionaryBytes), streamBytes);
                }
            }

            index = endStreamIndex + endStreamToken.Length;
        }
    }

    private static byte[] DecodePdfStream(byte[] data, string dictionaryText)
    {
        if (data.Length == 0)
        {
            return [];
        }

        var current = data;

        foreach (var filter in ExtractPdfFilters(dictionaryText))
        {
            current = filter switch
            {
                "ASCII85Decode" => DecodeAscii85(current),
                "ASCIIHexDecode" => DecodeAsciiHex(current),
                "FlateDecode" => DecodeFlate(current),
                _ => []
            };

            if (current.Length == 0)
            {
                return [];
            }
        }

        return current;
    }

    private static IReadOnlyList<string> ExtractPdfFilters(string dictionaryText)
    {
        var filterMatch = Regex.Match(
            dictionaryText,
            @"/Filter\s*(?<filters>\[[^\]]+\]|/[A-Za-z0-9]+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        if (!filterMatch.Success)
        {
            return [];
        }

        return Regex.Matches(filterMatch.Groups["filters"].Value, @"/([A-Za-z0-9]+)", RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .ToArray();
    }

    private static byte[] DecodeFlate(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            try
            {
                using var input = new MemoryStream(data);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                return output.ToArray();
            }
            catch
            {
                return [];
            }
        }
    }

    private static byte[] DecodeAsciiHex(byte[] data)
    {
        var hex = new StringBuilder();

        foreach (var value in data)
        {
            var character = (char)value;

            if (character == '>')
            {
                break;
            }

            if (Uri.IsHexDigit(character))
            {
                hex.Append(character);
            }
        }

        if (hex.Length == 0)
        {
            return [];
        }

        if (hex.Length % 2 != 0)
        {
            hex.Append('0');
        }

        var result = new byte[hex.Length / 2];

        for (var index = 0; index < result.Length; index++)
        {
            result[index] = Convert.ToByte(hex.ToString(index * 2, 2), 16);
        }

        return result;
    }

    private static byte[] DecodeAscii85(byte[] data)
    {
        var text = Encoding.ASCII.GetString(data);
        var bytes = new List<byte>();
        var group = new List<int>(5);

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || character == '<' || character == '~')
            {
                continue;
            }

            if (character == 'z' && group.Count == 0)
            {
                bytes.AddRange([0, 0, 0, 0]);
                continue;
            }

            if (character == '>')
            {
                break;
            }

            if (character < '!' || character > 'u')
            {
                continue;
            }

            group.Add(character - '!');

            if (group.Count != 5)
            {
                continue;
            }

            AppendAscii85Group(bytes, group, 4);
            group.Clear();
        }

        if (group.Count > 0)
        {
            var originalCount = group.Count;

            while (group.Count < 5)
            {
                group.Add('u' - '!');
            }

            AppendAscii85Group(bytes, group, Math.Max(0, originalCount - 1));
        }

        return bytes.ToArray();
    }

    private static void AppendAscii85Group(List<byte> bytes, IReadOnlyList<int> group, int byteCount)
    {
        uint value = 0;

        for (var index = 0; index < 5; index++)
        {
            value = value * 85u + (uint)group[index];
        }

        var decoded = new[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        };

        for (var index = 0; index < byteCount && index < decoded.Length; index++)
        {
            bytes.Add(decoded[index]);
        }
    }

    private static string ExtractTextFromPdfStream(byte[] data)
    {
        var text = Encoding.Latin1.GetString(data);
        var fragments = new List<string>();

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '(')
            {
                if (TryReadPdfLiteralString(text, ref index, out var literal)
                    && IsLikelyReadableDocumentText(literal))
                {
                    fragments.Add(literal);
                }

                continue;
            }

            if (text[index] == '<'
                && index + 1 < text.Length
                && text[index + 1] != '<'
                && TryReadPdfHexString(text, ref index, out var hexString)
                && IsLikelyReadableDocumentText(hexString))
            {
                fragments.Add(hexString);
            }
        }

        return NormalizeExtractedDocumentText(string.Join(" ", fragments));
    }

    private static bool TryReadPdfLiteralString(string text, ref int index, out string value)
    {
        value = string.Empty;

        if (index >= text.Length || text[index] != '(')
        {
            return false;
        }

        var builder = new StringBuilder();
        var depth = 0;

        for (index++; index < text.Length; index++)
        {
            var character = text[index];

            if (character == '\\')
            {
                if (index + 1 >= text.Length)
                {
                    break;
                }

                var next = text[++index];

                if (next is >= '0' and <= '7')
                {
                    var octal = new StringBuilder();
                    octal.Append(next);

                    for (var count = 0; count < 2 && index + 1 < text.Length && text[index + 1] is >= '0' and <= '7'; count++)
                    {
                        octal.Append(text[++index]);
                    }

                    builder.Append((char)Convert.ToInt32(octal.ToString(), 8));
                    continue;
                }

                builder.Append(next switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'b' => '\b',
                    'f' => '\f',
                    _ => next
                });
                continue;
            }

            if (character == '(')
            {
                depth++;
                builder.Append(character);
                continue;
            }

            if (character == ')')
            {
                if (depth == 0)
                {
                    value = NormalizeExtractedDocumentText(builder.ToString());
                    return true;
                }

                depth--;
                builder.Append(character);
                continue;
            }

            builder.Append(character);
        }

        return false;
    }

    private static bool TryReadPdfHexString(string text, ref int index, out string value)
    {
        value = string.Empty;

        if (index >= text.Length || text[index] != '<')
        {
            return false;
        }

        var hex = new StringBuilder();

        for (index++; index < text.Length; index++)
        {
            var character = text[index];

            if (character == '>')
            {
                break;
            }

            if (Uri.IsHexDigit(character))
            {
                hex.Append(character);
            }
        }

        if (hex.Length == 0)
        {
            return false;
        }

        if (hex.Length % 2 != 0)
        {
            hex.Append('0');
        }

        var bytes = new byte[hex.Length / 2];

        for (var offset = 0; offset < bytes.Length; offset++)
        {
            bytes[offset] = Convert.ToByte(hex.ToString(offset * 2, 2), 16);
        }

        value = NormalizeExtractedDocumentText(DecodePdfTextBytes(bytes));
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string DecodePdfTextBytes(byte[] bytes)
    {
        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }
        }

        return Encoding.Latin1.GetString(bytes);
    }

    private static string NormalizeExtractedDocumentText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = value
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .ReplaceLineEndings(" ");

        sanitized = Regex.Replace(sanitized, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        return sanitized;
    }

    private static bool IsLikelyReadableDocumentText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeExtractedDocumentText(value);

        if (normalized.Length < 3)
        {
            return false;
        }

        var letterOrDigitCount = normalized.Count(char.IsLetterOrDigit);
        return letterOrDigitCount >= Math.Max(3, normalized.Length / 3);
    }

    private static string DeterminePdfExtractionConfidence(string extractedText)
    {
        var normalized = NormalizeExtractedDocumentText(extractedText);
        var signal = normalized.Count(char.IsLetterOrDigit);

        return signal switch
        {
            >= 500 => "high",
            >= 140 => "medium",
            _ => "low"
        };
    }

    private static string TryExtractPdfMetadata(string rawText, string fieldName)
    {
        var match = Regex.Match(
            rawText,
            $@"/{Regex.Escape(fieldName)}\s*\((?<value>(?:\\.|[^\\)])*)\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success)
        {
            return string.Empty;
        }

        try
        {
            return NormalizeExtractedDocumentText(Regex.Unescape(match.Groups["value"].Value));
        }
        catch
        {
            return NormalizeExtractedDocumentText(match.Groups["value"].Value);
        }
    }

    private static bool TryFindBytes(byte[] source, byte[] pattern, int startIndex, out int index)
    {
        for (var offset = startIndex; offset <= source.Length - pattern.Length; offset++)
        {
            var matched = true;

            for (var patternIndex = 0; patternIndex < pattern.Length; patternIndex++)
            {
                if (source[offset + patternIndex] != pattern[patternIndex])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                index = offset;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static int FindLastBytes(byte[] source, byte[] pattern, int endIndexExclusive)
    {
        for (var offset = Math.Min(endIndexExclusive - pattern.Length, source.Length - pattern.Length); offset >= 0; offset--)
        {
            var matched = true;

            for (var patternIndex = 0; patternIndex < pattern.Length; patternIndex++)
            {
                if (source[offset + patternIndex] != pattern[patternIndex])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return offset;
            }
        }

        return -1;
    }

    private async Task<ToolResult> AnalyzeAmbientFrameAsync(
        string input,
        string defaultQuestion,
        string prefix,
        CancellationToken cancellationToken)
    {
        if (!_options.AmbientContextEnabled)
        {
            return DisabledAmbientResult();
        }

        var request = ResolveAmbientImageRequest(input);

        if (string.IsNullOrWhiteSpace(request.ImagePath))
        {
            return new ToolResult(
                "Ambient webcam input is not configured. Set `ambientWebcamFramePath` or pass an explicit image path.",
                Succeeded: false,
                VerificationText: "No ambient webcam frame path was configured or supplied.",
                SummaryText: "Ambient webcam input is not configured.");
        }

        var resolvedPath = ResolvePath(request.ImagePath);

        if (!File.Exists(resolvedPath))
        {
            return new ToolResult(
                $"Ambient webcam frame not found: {resolvedPath}",
                Succeeded: false,
                VerificationText: "The configured ambient webcam frame path does not exist.",
                SummaryText: "Ambient webcam frame not found.");
        }

        if (!SupportedImageExtensions.Contains(Path.GetExtension(resolvedPath)))
        {
            return new ToolResult(
                $"Ambient webcam frame must be an image file: {resolvedPath}",
                Succeeded: false,
                VerificationText: "The configured webcam frame path did not resolve to a supported image.",
                SummaryText: "Ambient webcam frame is not a supported image.");
        }

        if (!_modelGateway.IsAvailable)
        {
            return new ToolResult(
                $"Ambient webcam frame is available at {resolvedPath}, but no vision-capable model is configured.",
                Succeeded: false,
                VerificationText: $"Resolved the ambient webcam frame path to {resolvedPath}, but no vision route is available.",
                SummaryText: "Ambient webcam analysis is unavailable.");
        }

        var question = string.IsNullOrWhiteSpace(request.Question)
            ? defaultQuestion
            : request.Question;
        var sensorSummary = await LoadRoomSensorSummaryAsync(cancellationToken);
        var prompt = string.Join(
            Environment.NewLine,
            $"Ambient context enabled: {_options.AmbientContextEnabled}",
            $"Presence detection: {_options.AmbientPresenceDetectionEnabled}",
            $"Gesture detection: {_options.AmbientGestureDetectionEnabled}",
            $"Spatial audio cues: {_options.SpatialAudioCuesEnabled}",
            $"Far-field listening: {_options.FarFieldListeningEnabled}",
            string.IsNullOrWhiteSpace(sensorSummary) ? "Room sensors: not configured or no current snapshot." : $"Room sensors:{Environment.NewLine}{sensorSummary}",
            $"Question: {question}");

        return await AnalyzeSingleImageAsync(
            resolvedPath,
            prompt,
            $"Read the ambient webcam frame from {resolvedPath} and analyzed it with the vision route.",
            prefix,
            cancellationToken);
    }

    private async Task<ToolResult> AnalyzePresenceAsync(string input, CancellationToken cancellationToken)
    {
        if (!_options.AmbientContextEnabled)
        {
            return DisabledAmbientResult();
        }

        var isGestureRequest = input.Contains("gesture", StringComparison.OrdinalIgnoreCase);

        if (isGestureRequest && !_options.AmbientGestureDetectionEnabled)
        {
            return new ToolResult(
                "Gesture detection is disabled. Set `ambientGestureDetectionEnabled` to `true` to allow ambient gesture analysis.",
                Succeeded: false,
                VerificationText: "The runtime blocked ambient gesture analysis because it is disabled in settings.",
                SummaryText: "Ambient gesture detection is disabled.");
        }

        if (!isGestureRequest && !_options.AmbientPresenceDetectionEnabled)
        {
            return new ToolResult(
                "Presence detection is disabled. Set `ambientPresenceDetectionEnabled` to `true` to allow ambient presence analysis.",
                Succeeded: false,
                VerificationText: "The runtime blocked ambient presence analysis because it is disabled in settings.",
                SummaryText: "Ambient presence detection is disabled.");
        }

        var request = ResolveAmbientImageRequest(TrimCommandPrefix(input, "presence", "gesture"));

        if (string.IsNullOrWhiteSpace(request.ImagePath))
        {
            return new ToolResult(
                "Ambient presence analysis needs `ambientWebcamFramePath` or an explicit image path.",
                Succeeded: false,
                VerificationText: "No webcam frame was available for ambient presence analysis.",
                SummaryText: "Ambient webcam input is not configured.");
        }

        var resolvedPath = ResolvePath(request.ImagePath);

        if (!File.Exists(resolvedPath))
        {
            return new ToolResult(
                $"Ambient webcam frame not found: {resolvedPath}",
                Succeeded: false,
                VerificationText: "The configured ambient webcam frame path does not exist.",
                SummaryText: "Ambient webcam frame not found.");
        }

        if (!_modelGateway.IsAvailable)
        {
            return new ToolResult(
                $"Ambient webcam frame is available at {resolvedPath}, but presence analysis requires a vision-capable model.",
                Succeeded: false,
                VerificationText: "No vision route is available for ambient presence analysis.",
                SummaryText: "Ambient presence analysis is unavailable.");
        }

        var sensorSummary = await LoadRoomSensorSummaryAsync(cancellationToken);
        var question = string.IsNullOrWhiteSpace(request.Question)
            ? (isGestureRequest
                ? "Assess visible gestures or hand signals that could be used as triggers."
                : "Assess whether someone is present, their posture, and their approximate distance from the camera.")
            : request.Question;

        var content = await CompleteVisionPromptAsync(
            "You are Jarvis. Analyze the webcam frame and optional room-sensor snapshot for ambient activation. Return only JSON with keys `summary`, `someonePresent`, `peopleCount`, `primaryPosture`, `approximateDistance`, `gesture`, `activationRecommendation`, `confidence`, and `sensorNotes`. Do not infer identity.",
            [ChatMessageContentPart.FromText(
                string.Join(
                    Environment.NewLine,
                    string.IsNullOrWhiteSpace(sensorSummary) ? "Room sensors: unavailable." : $"Room sensors:{Environment.NewLine}{sensorSummary}",
                    $"Question: {question}")),
             await BuildImagePartAsync(resolvedPath, cancellationToken)],
            cancellationToken);

        var assessment = ParseJson<PresenceAssessmentContract>(content);

        if (assessment is null)
        {
            return new ToolResult(
                $"Ambient presence analysis for {resolvedPath}:{Environment.NewLine}{content}",
                VerificationText: $"Read the ambient webcam frame from {resolvedPath} and analyzed it with the vision route.",
                SummaryText: "Analyzed ambient presence.");
        }

        var lines = new List<string>
        {
            $"Ambient presence analysis for {resolvedPath}:",
            $"- Summary: {Coalesce(assessment.Summary, "No presence summary returned.")}",
            $"- Someone present: {assessment.SomeonePresent}",
            $"- People count: {assessment.PeopleCount}",
            $"- Primary posture: {Coalesce(assessment.PrimaryPosture, "Unknown")}",
            $"- Approximate distance: {Coalesce(assessment.ApproximateDistance, "Unknown")}",
            $"- Gesture: {Coalesce(assessment.Gesture, "None detected")}",
            $"- Activation recommendation: {Coalesce(assessment.ActivationRecommendation, "No recommendation returned.")}",
            $"- Confidence: {Coalesce(assessment.Confidence, "Unknown")}"
        };

        if (!string.IsNullOrWhiteSpace(assessment.SensorNotes))
        {
            lines.Add($"- Sensor notes: {assessment.SensorNotes}");
        }

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: $"Read the ambient webcam frame from {resolvedPath} and analyzed presence signals with the vision route.",
            SummaryText: FirstSentence(Coalesce(assessment.Summary, assessment.ActivationRecommendation)));
    }

    private async Task<ToolResult> AnalyzeFaceRecognitionAsync(string input, CancellationToken cancellationToken)
    {
        if (!_options.AmbientContextEnabled)
        {
            return DisabledAmbientResult();
        }

        if (!_options.AmbientFaceRecognitionEnabled)
        {
            return new ToolResult(
                "Face recognition is disabled. Enable `ambientFaceRecognitionEnabled` only if you actually need it.",
                Succeeded: false,
                VerificationText: "The runtime blocked face recognition because it is disabled in settings.",
                SummaryText: "Face recognition is disabled.");
        }

        if (!_options.AmbientFaceRecognitionConsentGranted)
        {
            return new ToolResult(
                "Face recognition remains blocked until you explicitly grant consent with `ambientFaceRecognitionConsentGranted`.",
                Succeeded: false,
                VerificationText: "The runtime blocked face recognition because consent has not been granted in settings.",
                SummaryText: "Face recognition is blocked until consent is granted.");
        }

        var profiles = LoadFaceProfiles();

        if (profiles.Count == 0)
        {
            return new ToolResult(
                "Face recognition needs an enrolled profile list in `ambientFaceProfilesPath` with reference images.",
                Succeeded: false,
                VerificationText: "No usable face-recognition profiles were available.",
                SummaryText: "No face-recognition profiles are configured.");
        }

        var request = ResolveAmbientImageRequest(TrimCommandPrefix(input, "face recognition", "face"));

        if (string.IsNullOrWhiteSpace(request.ImagePath))
        {
            return new ToolResult(
                "Face recognition needs `ambientWebcamFramePath` or an explicit image path.",
                Succeeded: false,
                VerificationText: "No ambient webcam frame was available for face recognition.",
                SummaryText: "Ambient webcam input is not configured.");
        }

        var resolvedPath = ResolvePath(request.ImagePath);

        if (!File.Exists(resolvedPath))
        {
            return new ToolResult(
                $"Ambient webcam frame not found: {resolvedPath}",
                Succeeded: false,
                VerificationText: "The configured ambient webcam frame path does not exist.",
                SummaryText: "Ambient webcam frame not found.");
        }

        if (!_modelGateway.IsAvailable)
        {
            return new ToolResult(
                $"Ambient webcam frame is available at {resolvedPath}, but face recognition requires a vision-capable model.",
                Succeeded: false,
                VerificationText: "No vision route is available for face recognition.",
                SummaryText: "Face recognition is unavailable.");
        }

        var question = string.IsNullOrWhiteSpace(request.Question)
            ? "Compare the current webcam frame against the enrolled profiles and report only likely matches."
            : request.Question;
        var userParts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.FromText(
                string.Join(
                    Environment.NewLine,
                    "The first image is the current webcam frame.",
                    "Each later image is a labeled reference profile that the user explicitly enrolled.",
                    "Prefer `unknown` over a weak guess.",
                    $"Question: {question}")),
            ChatMessageContentPart.FromText("Current webcam frame:"),
            await BuildImagePartAsync(resolvedPath, cancellationToken)
        };

        foreach (var profile in profiles.Take(4))
        {
            userParts.Add(ChatMessageContentPart.FromText($"Reference profile: {profile.Name}. Notes: {Coalesce(profile.Notes, "None")}"));
            userParts.Add(await BuildImagePartAsync(profile.ResolvedImagePath, cancellationToken));
        }

        var content = await CompleteVisionPromptAsync(
            "You are Jarvis. Compare the webcam frame against the enrolled face reference images. Return only JSON with keys `summary`, `faceCount`, `unknownFaceCount`, `matches`, and `caution`. Each `matches` item must contain `name`, `confidence`, and `rationale`. Only report strong-enough matches. Do not infer sensitive traits.",
            userParts,
            cancellationToken);

        var result = ParseJson<FaceRecognitionContract>(content);

        if (result is null)
        {
            return new ToolResult(
                $"Face recognition analysis for {resolvedPath}:{Environment.NewLine}{content}",
                VerificationText: $"Compared the ambient webcam frame at {resolvedPath} against {profiles.Count} enrolled face profile(s) with the vision route.",
                SummaryText: "Compared the webcam frame against enrolled face profiles.");
        }

        var lines = new List<string>
        {
            $"Face recognition analysis for {resolvedPath}:",
            $"- Summary: {Coalesce(result.Summary, "No face-recognition summary returned.")}",
            $"- Faces detected: {result.FaceCount}",
            $"- Unknown faces: {result.UnknownFaceCount}"
        };

        if (result.Matches is { Length: > 0 })
        {
            lines.Add("- Matches:");

            foreach (var match in result.Matches.Take(6))
            {
                lines.Add($"  - {Coalesce(match.Name, "unknown")} | confidence={Coalesce(match.Confidence, "unknown")} | {Coalesce(match.Rationale, "No rationale returned.")}");
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Caution))
        {
            lines.Add($"- Caution: {result.Caution}");
        }

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: $"Compared the ambient webcam frame at {resolvedPath} against {profiles.Count} enrolled face profile(s) with the vision route.",
            SummaryText: FirstSentence(Coalesce(result.Summary, result.Caution)));
    }

    private async Task<ToolResult> AnalyzeRoomSensorsAsync(string input, CancellationToken cancellationToken)
    {
        if (!_options.AmbientContextEnabled)
        {
            return DisabledAmbientResult();
        }

        var request = ResolveRoomSensorRequest(input);

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return new ToolResult(
                "Room-sensor input is not configured. Set `ambientRoomSensorSnapshotPath` or pass an explicit path.",
                Succeeded: false,
                VerificationText: "No room-sensor snapshot path was configured or supplied.",
                SummaryText: "Room-sensor input is not configured.");
        }

        var resolvedPath = ResolvePath(request.Path);

        if (!File.Exists(resolvedPath))
        {
            return new ToolResult(
                $"Room-sensor snapshot not found: {resolvedPath}",
                Succeeded: false,
                VerificationText: "The configured room-sensor snapshot path does not exist.",
                SummaryText: "Room-sensor snapshot not found.");
        }

        var sensorText = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        var preview = FormatSensorPreview(sensorText);

        if (!_modelGateway.IsAvailable)
        {
            return new ToolResult(
                $"Room sensor snapshot from {resolvedPath}:{Environment.NewLine}{preview}",
                VerificationText: $"Loaded the room-sensor snapshot from {resolvedPath} without model summarization.",
                SummaryText: "Loaded the room-sensor snapshot.");
        }

        var question = string.IsNullOrWhiteSpace(request.Question)
            ? "Summarize the room-sensor snapshot and call out anything relevant for ambient activation."
            : request.Question;

        var messages = new[]
        {
            new ChatMessage("system", [ChatMessageContentPart.FromText("You are Jarvis. Analyze the supplied room-sensor snapshot and answer the user's question concisely.")]),
            new ChatMessage("user", [ChatMessageContentPart.FromText(
                $"Question: {question}{Environment.NewLine}Path: {resolvedPath}{Environment.NewLine}Snapshot:{Environment.NewLine}{preview}")])
        };

        try
        {
            var response = await _modelGateway.CompleteAsync(
                new ChatCompletionRequest(messages, Temperature: 0.1),
                ModelRoute.Fast,
                cancellationToken);

            var content = string.IsNullOrWhiteSpace(response.Content)
                ? $"I read the room-sensor snapshot at {resolvedPath}, but the analysis model returned an empty response."
                : response.Content.Trim();

            return new ToolResult(
                $"Room sensor analysis for {resolvedPath}:{Environment.NewLine}{content}",
                VerificationText: $"Read the room-sensor snapshot from {resolvedPath} and summarized it with the fast model route.",
                SummaryText: FirstSentence(content));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ToolResult(
                $"Room sensor analysis failed: {TrimError(exception.Message)}",
                Succeeded: false,
                VerificationText: $"The room-sensor snapshot was read, but model analysis failed for {resolvedPath}.",
                SummaryText: "Room sensor analysis failed.");
        }
    }

    private async Task<ToolResult> AnalyzeSingleImageAsync(
        string filePath,
        string question,
        string verificationText,
        string prefix,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await CompleteVisionPromptAsync(
                "You are Jarvis. Analyze the supplied image and answer the user's question concisely.",
                [ChatMessageContentPart.FromText(question), await BuildImagePartAsync(filePath, cancellationToken)],
                cancellationToken);

            content = string.IsNullOrWhiteSpace(content)
                ? $"The vision model inspected {filePath}, but it returned an empty response."
                : content.Trim();

            return new ToolResult(
                $"{prefix} for {filePath}:{Environment.NewLine}{content}",
                VerificationText: verificationText,
                SummaryText: FirstSentence(content));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ToolResult(
                $"{prefix} failed: {TrimError(exception.Message)}",
                Succeeded: false,
                VerificationText: $"The visual context service could not analyze {filePath}.",
                SummaryText: $"{prefix} failed.");
        }
    }

    private async Task<string> CompleteVisionPromptAsync(
        string systemPrompt,
        IReadOnlyList<ChatMessageContentPart> userParts,
        CancellationToken cancellationToken)
    {
        var messages = new[]
        {
            new ChatMessage("system", [ChatMessageContentPart.FromText(systemPrompt)]),
            new ChatMessage("user", userParts.ToArray())
        };

        var response = await _modelGateway.CompleteAsync(
            new ChatCompletionRequest(messages, Temperature: 0.1),
            ModelRoute.Vision,
            cancellationToken);

        return response.Content?.Trim() ?? string.Empty;
    }

    private async Task<ChatMessageContentPart> BuildImagePartAsync(string filePath, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(filePath);

        if (extension is ".tif" or ".tiff")
        {
            using var bitmap = new Bitmap(filePath);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return ChatMessageContentPart.FromImageUrl(BuildDataUrl("rendered.png", stream.ToArray()));
        }

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return ChatMessageContentPart.FromImageUrl(BuildDataUrl(filePath, bytes));
    }

    private CapturedScreen CaptureScreen(ScreenRequest request)
    {
        var displays = GetDisplays();

        if (displays.Length == 0)
        {
            throw new InvalidOperationException("No displays were detected.");
        }

        var activeWindow = TryGetActiveWindowSnapshot();
        var targetWindow = ResolveTargetWindow(request, activeWindow);
        var targetDisplays = ResolveTargetDisplays(request, displays, activeWindow, targetWindow);
        var bounds = request.TargetKind switch
        {
            ScreenTargetKind.AllDisplays => CombineBounds(targetDisplays.Select(display => display.Bounds)),
            ScreenTargetKind.ActiveWindow or ScreenTargetKind.NamedWindow => targetWindow?.Bounds
                ?? throw new InvalidOperationException("The requested window could not be resolved for capture."),
            _ => targetDisplays[0].Bounds
        };

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The requested capture target did not resolve to visible bounds.");
        }

        var captureDirectory = ResolveCaptureDirectory();
        Directory.CreateDirectory(captureDirectory);
        var filePath = Path.Combine(captureDirectory, BuildCaptureFileName(request, targetDisplays, targetWindow));

        using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            bitmap.Save(filePath, ImageFormat.Png);
        }

        PruneCaptureDirectory(captureDirectory);

        return new CapturedScreen(
            request,
            filePath,
            bounds,
            displays,
            targetDisplays,
            activeWindow,
            targetWindow,
            BuildTargetLabel(request, targetDisplays, targetWindow));
    }

    private ToolResult BuildCaptureResult(CapturedScreen capture, string detail, bool succeeded)
    {
        var lines = new List<string>
        {
            $"Capture saved: {capture.FilePath}",
            $"- Target: {capture.TargetLabel}",
            $"- Bounds: {capture.Bounds.Left},{capture.Bounds.Top} {capture.Bounds.Width}x{capture.Bounds.Height}",
            $"- Displays detected: {capture.AllDisplays.Length}",
            $"- Active window: {(capture.ActiveWindow is null ? "Unavailable" : $"{capture.ActiveWindow.ProcessName} (PID {capture.ActiveWindow.ProcessId}) | {capture.ActiveWindow.Title}")}",
            $"- Target window: {(capture.TargetWindow is null ? "N/A" : $"{capture.TargetWindow.ProcessName} (PID {capture.TargetWindow.ProcessId}) | {capture.TargetWindow.Title}")}",
            $"- Detail: {detail}"
        };

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            Succeeded: succeeded,
            VerificationText: $"Saved a screenshot for {capture.TargetLabel} to {capture.FilePath}.",
            SummaryText: $"Captured {capture.TargetLabel}.");
    }

    private ToolResult BuildAmbientStatusResult()
    {
        var webcamStatus = DescribeConfiguredFile(_options.AmbientWebcamFramePath, _options.AmbientWebcamFreshnessSeconds, "Webcam frame");
        var roomSensorStatus = DescribeConfiguredFile(_options.AmbientRoomSensorSnapshotPath, _options.AmbientWebcamFreshnessSeconds, "Room sensor snapshot");
        var faceProfiles = LoadFaceProfiles();
        var lines = new List<string>
        {
            "Ambient context:",
            $"- Ambient sensing: {(_options.AmbientContextEnabled ? "enabled" : "disabled")}",
            $"- {webcamStatus}",
            $"- Presence detection: {(_options.AmbientPresenceDetectionEnabled ? "enabled" : "disabled")}",
            $"- Gesture detection: {(_options.AmbientGestureDetectionEnabled ? "enabled" : "disabled")}",
            $"- Face recognition: {BuildFaceRecognitionStatus(faceProfiles.Count)}",
            $"- {roomSensorStatus}",
            $"- Spatial audio cues: {(_options.SpatialAudioCuesEnabled ? "enabled" : "disabled")}",
            $"- Far-field listening: {(_options.FarFieldListeningEnabled ? "enabled" : "disabled")}",
            $"- Displays detected: {GetDisplays().Length}"
        };

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: "Loaded the ambient capability settings and current input-source status.",
            SummaryText: "Loaded the ambient context status.");
    }

    private ToolResult DisabledAmbientResult()
    {
        return new ToolResult(
            "Ambient sensing is disabled. Set `ambientContextEnabled` to `true` in jarvis.settings.json to enable webcam and room-sensor context.",
            Succeeded: false,
            VerificationText: "The runtime blocked ambient sensing because it is disabled in settings.",
            SummaryText: "Ambient sensing is disabled.");
    }

    private string ResolveCaptureDirectory()
    {
        return ResolvePath(string.IsNullOrWhiteSpace(_options.VisualCaptureDirectory)
            ? Path.Combine("data", "captures")
            : _options.VisualCaptureDirectory);
    }

    private static ScreenRequest ParseScreenRequest(string input)
    {
        var trimmed = input.Trim();
        var (payload, explicitQuestion) = ParsePayload(trimmed);
        var target = DetectScreenTarget(payload);
        var question = string.IsNullOrWhiteSpace(explicitQuestion)
            ? ExtractScreenQuestion(payload, target)
            : explicitQuestion.Trim();

        return new ScreenRequest(target.Kind, target.DisplayIndex, target.WindowTarget, question, trimmed);
    }

    private AmbientImageRequest ResolveAmbientImageRequest(string input)
    {
        var trimmed = input.Trim();
        var (payloadPath, explicitQuestion) = ParsePayload(trimmed);

        if (string.IsNullOrWhiteSpace(payloadPath))
        {
            return new AmbientImageRequest(_options.AmbientWebcamFramePath, explicitQuestion);
        }

        var looksLikePath = LooksLikePathCandidate(payloadPath);

        if (looksLikePath)
        {
            return new AmbientImageRequest(payloadPath, explicitQuestion);
        }

        if (string.IsNullOrWhiteSpace(_options.AmbientWebcamFramePath))
        {
            return new AmbientImageRequest(payloadPath, explicitQuestion);
        }

        var combinedQuestion = string.IsNullOrWhiteSpace(explicitQuestion)
            ? payloadPath
            : $"{payloadPath} {explicitQuestion}".Trim();

        return new AmbientImageRequest(_options.AmbientWebcamFramePath, combinedQuestion);
    }

    private RoomSensorRequest ResolveRoomSensorRequest(string input)
    {
        var trimmed = input.Trim();
        var (payloadPath, explicitQuestion) = ParsePayload(trimmed);

        if (string.IsNullOrWhiteSpace(payloadPath))
        {
            return new RoomSensorRequest(_options.AmbientRoomSensorSnapshotPath, explicitQuestion);
        }

        var looksLikePath = LooksLikePathCandidate(payloadPath);

        if (looksLikePath)
        {
            return new RoomSensorRequest(payloadPath, explicitQuestion);
        }

        if (string.IsNullOrWhiteSpace(_options.AmbientRoomSensorSnapshotPath))
        {
            return new RoomSensorRequest(payloadPath, explicitQuestion);
        }

        var combinedQuestion = string.IsNullOrWhiteSpace(explicitQuestion)
            ? payloadPath
            : $"{payloadPath} {explicitQuestion}".Trim();

        return new RoomSensorRequest(_options.AmbientRoomSensorSnapshotPath, combinedQuestion);
    }

    private static ScreenTargetRequest DetectScreenTarget(string input)
    {
        var trimmed = input.Trim();
        var normalized = NormalizeForMatching(input);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ScreenTargetRequest(ScreenTargetKind.ActiveDisplay, 0, string.Empty, string.Empty);
        }

        if (normalized.Contains("all displays", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("all monitors", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("whole desktop", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("entire desktop", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("virtual screen", StringComparison.OrdinalIgnoreCase))
        {
            return new ScreenTargetRequest(ScreenTargetKind.AllDisplays, 0, string.Empty, trimmed);
        }

        if (normalized.Contains("primary display", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("primary monitor", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("main display", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("main monitor", StringComparison.OrdinalIgnoreCase))
        {
            return new ScreenTargetRequest(ScreenTargetKind.PrimaryDisplay, 0, string.Empty, trimmed);
        }

        if (normalized.Contains("active display", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("active monitor", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("current display", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("current monitor", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("focused display", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("focused monitor", StringComparison.OrdinalIgnoreCase))
        {
            return new ScreenTargetRequest(ScreenTargetKind.ActiveDisplay, 0, string.Empty, trimmed);
        }

        if (StartsWithCommand(trimmed, "active window")
            || StartsWithCommand(trimmed, "current window")
            || StartsWithCommand(trimmed, "focused window")
            || StartsWithCommand(trimmed, "foreground window"))
        {
            return new ScreenTargetRequest(ScreenTargetKind.ActiveWindow, 0, string.Empty, trimmed);
        }

        if (StartsWithCommand(trimmed, "window"))
        {
            var windowTarget = TrimCommandPrefix(trimmed, "window");
            return new ScreenTargetRequest(
                string.IsNullOrWhiteSpace(windowTarget) ? ScreenTargetKind.ActiveWindow : ScreenTargetKind.NamedWindow,
                0,
                windowTarget,
                trimmed);
        }

        if (StartsWithCommand(trimmed, "app"))
        {
            var windowTarget = TrimCommandPrefix(trimmed, "app");
            return new ScreenTargetRequest(
                string.IsNullOrWhiteSpace(windowTarget) ? ScreenTargetKind.ActiveWindow : ScreenTargetKind.NamedWindow,
                0,
                windowTarget,
                trimmed);
        }

        var match = Regex.Match(normalized, @"\b(?:display|monitor|screen)\s*(\d+)\b", RegexOptions.IgnoreCase);

        if (match.Success && int.TryParse(match.Groups[1].Value, out var displayIndex) && displayIndex > 0)
        {
            return new ScreenTargetRequest(ScreenTargetKind.SpecificDisplay, displayIndex, string.Empty, trimmed);
        }

        return new ScreenTargetRequest(ScreenTargetKind.ActiveDisplay, 0, string.Empty, string.Empty);
    }

    private static string ExtractScreenQuestion(string payload, ScreenTargetRequest target)
    {
        if (!string.IsNullOrWhiteSpace(target.ConsumedInput))
        {
            var remainder = payload.Length >= target.ConsumedInput.Length
                ? payload[target.ConsumedInput.Length..].TrimStart(' ', ',', ':', ';', '-')
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(remainder))
            {
                return remainder;
            }
        }

        return LooksLikeTargetOnly(payload) ? string.Empty : payload.Trim();
    }

    private static bool LooksLikeTargetOnly(string input)
    {
        var normalized = NormalizeForMatching(input);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "screen",
            "screens",
            "display",
            "displays",
            "monitor",
            "monitors",
            "window",
            "windows",
            "app",
            "all",
            "primary",
            "main",
            "active",
            "current",
            "focused",
            "foreground",
            "capture",
            "screenshot",
            "ocr",
            "text",
            "ui",
            "elements"
        };

        return tokens.All(token => allowed.Contains(token) || int.TryParse(token, out _));
    }

    private static DisplayDescriptor[] GetDisplays()
    {
        return Screen.AllScreens
            .Select((screen, index) => new DisplayDescriptor(index + 1, screen.DeviceName, screen.Primary, screen.Bounds, screen.WorkingArea))
            .OrderBy(display => display.Index)
            .ToArray();
    }

    private static DisplayDescriptor[] ResolveTargetDisplays(
        ScreenRequest request,
        IReadOnlyList<DisplayDescriptor> displays,
        ActiveWindowSnapshot? activeWindow,
        ActiveWindowSnapshot? targetWindow)
    {
        if (request.TargetKind is ScreenTargetKind.ActiveWindow or ScreenTargetKind.NamedWindow)
        {
            if (targetWindow is not null)
            {
                var windowDisplays = displays
                    .Where(display => display.Bounds.IntersectsWith(targetWindow.Bounds) || display.Bounds.Contains(targetWindow.Bounds.Location))
                    .ToArray();

                if (windowDisplays.Length > 0)
                {
                    return windowDisplays;
                }
            }

            return [ResolveActiveDisplay(displays, activeWindow) ?? displays.FirstOrDefault(display => display.IsPrimary) ?? displays[0]];
        }

        return request.TargetKind switch
        {
            ScreenTargetKind.AllDisplays => displays.ToArray(),
            ScreenTargetKind.PrimaryDisplay => [displays.FirstOrDefault(display => display.IsPrimary) ?? displays[0]],
            ScreenTargetKind.SpecificDisplay => [displays.FirstOrDefault(display => display.Index == request.DisplayIndex)
                ?? throw new InvalidOperationException($"Display {request.DisplayIndex} was not found. Use `screen monitors` to inspect the current display list.")],
            _ => [ResolveActiveDisplay(displays, activeWindow) ?? displays.FirstOrDefault(display => display.IsPrimary) ?? displays[0]]
        };
    }

    private static ActiveWindowSnapshot? ResolveTargetWindow(ScreenRequest request, ActiveWindowSnapshot? activeWindow)
    {
        return request.TargetKind switch
        {
            ScreenTargetKind.ActiveWindow => activeWindow,
            ScreenTargetKind.NamedWindow => string.IsNullOrWhiteSpace(request.WindowTarget) ? null : TryFindWindowSnapshot(request.WindowTarget),
            _ => null
        };
    }

    private static DisplayDescriptor? ResolveActiveDisplay(
        IReadOnlyList<DisplayDescriptor> displays,
        ActiveWindowSnapshot? activeWindow)
    {
        if (activeWindow is not null)
        {
            foreach (var display in displays)
            {
                if (display.Bounds.IntersectsWith(activeWindow.Bounds)
                    || display.Bounds.Contains(activeWindow.Bounds.Location))
                {
                    return display;
                }
            }
        }

        var cursor = Cursor.Position;
        return displays.FirstOrDefault(display => display.Bounds.Contains(cursor));
    }

    private static Rectangle CombineBounds(IEnumerable<Rectangle> rectangles)
    {
        var bounds = Rectangle.Empty;

        foreach (var rectangle in rectangles)
        {
            bounds = bounds == Rectangle.Empty
                ? rectangle
                : Rectangle.Union(bounds, rectangle);
        }

        return bounds;
    }

    private static string BuildCaptureFileName(
        ScreenRequest request,
        IReadOnlyList<DisplayDescriptor> targetDisplays,
        ActiveWindowSnapshot? targetWindow)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var label = request.TargetKind switch
        {
            ScreenTargetKind.AllDisplays => "all-displays",
            ScreenTargetKind.PrimaryDisplay => "primary",
            ScreenTargetKind.SpecificDisplay => $"display-{request.DisplayIndex}",
            ScreenTargetKind.ActiveWindow => "active-window",
            ScreenTargetKind.NamedWindow => "window-" + SanitizeFileSegment(
                string.IsNullOrWhiteSpace(targetWindow?.ProcessName)
                    ? request.WindowTarget
                    : $"{targetWindow.ProcessName}-{targetWindow.Title}"),
            _ => targetDisplays.Count > 0 ? $"display-{targetDisplays[0].Index}" : "active"
        };

        return $"screen-{label}-{timestamp}.png";
    }

    private static string BuildTargetLabel(
        ScreenRequest request,
        IReadOnlyList<DisplayDescriptor> targetDisplays,
        ActiveWindowSnapshot? targetWindow)
    {
        if (request.TargetKind == ScreenTargetKind.AllDisplays)
        {
            return $"all displays ({targetDisplays.Count})";
        }

        if (request.TargetKind is ScreenTargetKind.ActiveWindow or ScreenTargetKind.NamedWindow)
        {
            if (targetWindow is null)
            {
                return request.TargetKind == ScreenTargetKind.ActiveWindow
                    ? "active window"
                    : $"window {request.WindowTarget}";
            }

            var title = string.IsNullOrWhiteSpace(targetWindow.Title) ? targetWindow.ProcessName : targetWindow.Title;
            return request.TargetKind == ScreenTargetKind.ActiveWindow
                ? $"active window ({title})"
                : $"window {title} [{targetWindow.ProcessName}]";
        }

        var display = targetDisplays.FirstOrDefault();

        if (display is null)
        {
            return "display";
        }

        return request.TargetKind switch
        {
            ScreenTargetKind.PrimaryDisplay => $"primary display {display.Index} ({display.DeviceName})",
            ScreenTargetKind.SpecificDisplay => $"display {display.Index} ({display.DeviceName})",
            _ => $"active display {display.Index} ({display.DeviceName})"
        };
    }

    private string BuildCapturePromptContext(CapturedScreen capture)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Capture target: {capture.TargetLabel}");
        builder.AppendLine($"Capture bounds: {capture.Bounds.Left},{capture.Bounds.Top} {capture.Bounds.Width}x{capture.Bounds.Height}");
        builder.AppendLine($"Displays detected: {capture.AllDisplays.Length}");

        foreach (var display in capture.TargetDisplays)
        {
            builder.AppendLine($"Target display: {DescribeDisplay(display)}");
        }

        if (capture.ActiveWindow is not null)
        {
            builder.AppendLine(
                $"Active window: {capture.ActiveWindow.ProcessName} (PID {capture.ActiveWindow.ProcessId}) | {capture.ActiveWindow.Title} | bounds {capture.ActiveWindow.Bounds.Left},{capture.ActiveWindow.Bounds.Top} {capture.ActiveWindow.Bounds.Width}x{capture.ActiveWindow.Bounds.Height}");
        }
        else
        {
            builder.AppendLine("Active window: unavailable");
        }

        if (capture.TargetWindow is not null)
        {
            builder.AppendLine(
                $"Target window: {capture.TargetWindow.ProcessName} (PID {capture.TargetWindow.ProcessId}) | {capture.TargetWindow.Title} | bounds {capture.TargetWindow.Bounds.Left},{capture.TargetWindow.Bounds.Top} {capture.TargetWindow.Bounds.Width}x{capture.TargetWindow.Bounds.Height}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string DescribeDisplay(DisplayDescriptor display)
    {
        var primary = display.IsPrimary ? " | primary" : string.Empty;
        return $"Display {display.Index}{primary} | {display.DeviceName} | bounds {display.Bounds.Left},{display.Bounds.Top} {display.Bounds.Width}x{display.Bounds.Height} | work area {display.WorkingArea.Left},{display.WorkingArea.Top} {display.WorkingArea.Width}x{display.WorkingArea.Height}";
    }

    private static string SanitizeFileSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "window";
        }

        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value
            .ToLowerInvariant()
            .Select(character => invalidChars.Contains(character) || char.IsWhiteSpace(character) ? '-' : character)
            .ToArray());

        sanitized = Regex.Replace(sanitized, "-{2,}", "-", RegexOptions.CultureInvariant).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "window" : sanitized[..Math.Min(40, sanitized.Length)];
    }

    private static ActiveWindowSnapshot? TryGetActiveWindowSnapshot()
    {
        try
        {
            var handle = GetForegroundWindow();

            if (handle == IntPtr.Zero)
            {
                return null;
            }

            _ = GetWindowThreadProcessId(handle, out var processId);

            if (processId == 0)
            {
                return null;
            }

            var title = ReadWindowTitle(handle);
            var bounds = GetWindowBounds(handle);
            using var process = Process.GetProcessById((int)processId);
            return new ActiveWindowSnapshot(handle, process.ProcessName, (int)processId, title, bounds);
        }
        catch
        {
            return null;
        }
    }

    private static ActiveWindowSnapshot? TryFindWindowSnapshot(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var normalizedQuery = NormalizeForMatching(query);
        ActiveWindowSnapshot? bestMatch = null;
        var bestScore = 0;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.HasExited)
                {
                    continue;
                }

                var handle = process.MainWindowHandle;
                var title = process.MainWindowTitle;

                if (handle == IntPtr.Zero || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var score = ScoreWindowMatch(query, normalizedQuery, title, process.ProcessName);

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                bestMatch = new ActiveWindowSnapshot(handle, process.ProcessName, process.Id, title, GetWindowBounds(handle));
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return bestMatch;
    }

    private static int ScoreWindowMatch(string rawQuery, string normalizedQuery, string title, string processName)
    {
        var normalizedTitle = NormalizeForMatching(title);
        var normalizedProcessName = NormalizeForMatching(processName);
        var score = 0;

        if (string.Equals(title, rawQuery, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 120);
        }

        if (string.Equals(processName, rawQuery, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 110);
        }

        if (string.Equals(normalizedTitle, normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 100);
        }

        if (string.Equals(normalizedProcessName, normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 96);
        }

        if (normalizedTitle.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 82);
        }

        if (normalizedProcessName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 74);
        }

        return score;
    }

    private static Rectangle GetWindowBounds(IntPtr handle)
    {
        return GetWindowRect(handle, out var rect)
            ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : Rectangle.Empty;
    }

    private static string ReadWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);

        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private string BuildFaceRecognitionStatus(int profileCount)
    {
        if (!_options.AmbientFaceRecognitionEnabled)
        {
            return "disabled";
        }

        if (!_options.AmbientFaceRecognitionConsentGranted)
        {
            return "blocked until explicit consent is granted";
        }

        return profileCount == 0
            ? "enabled with consent, but no enrolled profiles are configured"
            : $"enabled with consent and {profileCount} enrolled profile(s)";
    }

    private string DescribeConfiguredFile(string configuredPath, int freshnessSeconds, string label)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return $"{label}: not configured";
        }

        var resolvedPath = ResolvePath(configuredPath);

        if (!File.Exists(resolvedPath))
        {
            return $"{label}: configured but missing at {resolvedPath}";
        }

        var lastWrite = File.GetLastWriteTimeUtc(resolvedPath);
        var age = DateTimeOffset.UtcNow - lastWrite;
        var freshness = age <= TimeSpan.FromSeconds(Math.Max(5, freshnessSeconds)) ? "fresh" : "stale";
        return $"{label}: {resolvedPath} ({freshness}, updated {Math.Max(0, (int)age.TotalSeconds)}s ago)";
    }

    private async Task<string> LoadRoomSensorSummaryAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AmbientRoomSensorSnapshotPath))
        {
            return string.Empty;
        }

        var resolvedPath = ResolvePath(_options.AmbientRoomSensorSnapshotPath);

        if (!File.Exists(resolvedPath))
        {
            return string.Empty;
        }

        var content = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        return FormatSensorPreview(content);
    }

    private IReadOnlyList<FaceProfile> LoadFaceProfiles()
    {
        if (string.IsNullOrWhiteSpace(_options.AmbientFaceProfilesPath))
        {
            return [];
        }

        var resolvedCatalogPath = ResolvePath(_options.AmbientFaceProfilesPath);

        if (!File.Exists(resolvedCatalogPath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(resolvedCatalogPath));
            JsonElement profilesElement;

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                profilesElement = document.RootElement;
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("profiles", out var nestedProfiles)
                && nestedProfiles.ValueKind == JsonValueKind.Array)
            {
                profilesElement = nestedProfiles;
            }
            else
            {
                return [];
            }

            var profiles = new List<FaceProfile>();

            foreach (var item in profilesElement.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var nameElement)
                    || nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!item.TryGetProperty("imagePath", out var pathElement)
                    || pathElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var resolvedImagePath = ResolvePath(pathElement.GetString() ?? string.Empty);

                if (!File.Exists(resolvedImagePath))
                {
                    continue;
                }

                var notes = item.TryGetProperty("notes", out var notesElement) && notesElement.ValueKind == JsonValueKind.String
                    ? notesElement.GetString() ?? string.Empty
                    : string.Empty;

                profiles.Add(new FaceProfile(nameElement.GetString() ?? "unknown", resolvedImagePath, notes));
            }

            return profiles;
        }
        catch
        {
            return [];
        }
    }

    private void PruneCaptureDirectory(string captureDirectory)
    {
        var retentionCount = Math.Max(1, _options.VisualCaptureRetentionCount);

        try
        {
            var files = new DirectoryInfo(captureDirectory)
                .EnumerateFiles("screen-*.png", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(retentionCount)
                .ToArray();

            foreach (var file in files)
            {
                file.Delete();
            }
        }
        catch
        {
        }
    }

    private string ResolvePath(string target)
    {
        var trimmed = target.Trim().Trim('"');

        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        if (trimmed.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            trimmed = Path.Combine(home, trimmed[1..].TrimStart('\\', '/'));
        }

        return Path.GetFullPath(Path.Combine(_workspaceRoot, trimmed));
    }

    private static (string Path, string Question) ParsePayload(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (string.Empty, string.Empty);
        }

        var parts = input.Split("::", 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (input.Trim(), string.Empty);
    }

    private static string BuildDataUrl(string filePath, byte[] bytes)
    {
        var mimeType = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "image/png"
        };

        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    private static ToolResult Unavailable(string message)
    {
        return new ToolResult(
            message,
            Succeeded: false,
            VerificationText: "No routed model provider is available for visual understanding.",
            SummaryText: "Visual understanding is unavailable.");
    }

    private static ToolResult Failed(string heading, string error, string verification)
    {
        return new ToolResult(
            $"{heading} {TrimError(error)}",
            Succeeded: false,
            VerificationText: verification,
            SummaryText: heading);
    }

    private static string FirstSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.ReplaceLineEndings(" ").Trim();
        var punctuationIndex = normalized.IndexOfAny(['.', '!', '?']);

        if (punctuationIndex <= 0)
        {
            return normalized.Length <= 160 ? normalized : normalized[..160] + "...";
        }

        return normalized[..(punctuationIndex + 1)];
    }

    private static string TrimError(string text)
    {
        const int maxLength = 220;
        var value = text.Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static string NormalizeForMatching(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray();

        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool StartsWithCommand(string input, string value)
    {
        return input.Equals(value, StringComparison.OrdinalIgnoreCase)
            || input.StartsWith(value + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimCommandPrefix(string input, params string[] commands)
    {
        foreach (var command in commands)
        {
            if (StartsWithCommand(input, command))
            {
                return input[command.Length..].TrimStart(' ', ',', ':', ';', '-');
            }
        }

        return input.Trim();
    }

    private static bool LooksLikePathCandidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains('\\')
            || value.Contains('/')
            || value.StartsWith("~", StringComparison.Ordinal)
            || value.Contains(":\\", StringComparison.Ordinal))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(Path.GetExtension(value));
    }

    private static string FormatSensorPreview(string sensorText)
    {
        if (string.IsNullOrWhiteSpace(sensorText))
        {
            return "No sensor snapshot content.";
        }

        try
        {
            using var document = JsonDocument.Parse(sensorText);
            return JsonSerializer.Serialize(document.RootElement, IndentedJsonOptions);
        }
        catch (JsonException)
        {
            return sensorText.Length <= 4000
                ? sensorText.Trim()
                : sensorText[..4000].TrimEnd() + Environment.NewLine + "[sensor snapshot truncated]";
        }
    }

    private static T? ParseJson<T>(string content) where T : class
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var candidate = ExtractJson(content);

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(candidate, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractJson(string content)
    {
        var trimmed = content.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = trimmed.IndexOf('\n');

            if (firstBreak >= 0)
            {
                trimmed = trimmed[(firstBreak + 1)..];
                var fenceIndex = trimmed.LastIndexOf("```", StringComparison.Ordinal);

                if (fenceIndex >= 0)
                {
                    trimmed = trimmed[..fenceIndex];
                }
            }
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');

        return firstBrace >= 0 && lastBrace > firstBrace
            ? trimmed[firstBrace..(lastBrace + 1)]
            : trimmed;
    }

    private static string Coalesce(string? primary, string? fallback)
    {
        return string.IsNullOrWhiteSpace(primary)
            ? (fallback ?? string.Empty)
            : primary.Trim();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed record ScreenRequest(ScreenTargetKind TargetKind, int DisplayIndex, string WindowTarget, string Question, string RawInput);

    private sealed record ScreenTargetRequest(ScreenTargetKind Kind, int DisplayIndex, string WindowTarget, string ConsumedInput);

    private sealed record DisplayDescriptor(int Index, string DeviceName, bool IsPrimary, Rectangle Bounds, Rectangle WorkingArea);

    private sealed record ActiveWindowSnapshot(IntPtr Handle, string ProcessName, int ProcessId, string Title, Rectangle Bounds);

    private sealed record CapturedScreen(
        ScreenRequest Request,
        string FilePath,
        Rectangle Bounds,
        DisplayDescriptor[] AllDisplays,
        DisplayDescriptor[] TargetDisplays,
        ActiveWindowSnapshot? ActiveWindow,
        ActiveWindowSnapshot? TargetWindow,
        string TargetLabel);

    private sealed record AmbientImageRequest(string ImagePath, string Question);

    private sealed record RoomSensorRequest(string Path, string Question);

    private sealed record FaceProfile(string Name, string ResolvedImagePath, string Notes);

    private sealed record PdfStreamCandidate(string Dictionary, byte[] Data);

    private sealed record PdfExtractionResult(int PageCount, string Title, string Author, string Text, string Confidence, string Note);

    private sealed record OcrExtractionContract(string Summary, string FullText, OcrTextBlockContract[] TextBlocks);

    private sealed record OcrTextBlockContract(string Text, string Region, string Purpose);

    private sealed record UiDetectionContract(string Summary, string ApplicationGuess, UiElementContract[] UiElements);

    private sealed record UiElementContract(string Label, string Role, string Text, string State, string Region, string Importance);

    private sealed record DocumentAnalysisContract(
        string Summary,
        string DocumentType,
        string Confidence,
        string ExtractedTextPreview,
        string Caveats,
        string[] KeyPoints,
        string[] ActionItems,
        DocumentSectionContract[] Sections,
        DocumentFieldContract[] KeyFields);

    private sealed record DocumentSectionContract(string Heading, string Purpose);

    private sealed record DocumentFieldContract(string Label, string Value);

    private sealed record PresenceAssessmentContract(
        string Summary,
        bool SomeonePresent,
        int PeopleCount,
        string PrimaryPosture,
        string ApproximateDistance,
        string Gesture,
        string ActivationRecommendation,
        string Confidence,
        string SensorNotes);

    private sealed record FaceRecognitionContract(
        string Summary,
        int FaceCount,
        int UnknownFaceCount,
        FaceRecognitionMatchContract[] Matches,
        string Caution);

    private sealed record FaceRecognitionMatchContract(string Name, string Confidence, string Rationale);

    private enum ScreenTargetKind
    {
        ActiveDisplay,
        PrimaryDisplay,
        AllDisplays,
        SpecificDisplay,
        ActiveWindow,
        NamedWindow
    }
}
