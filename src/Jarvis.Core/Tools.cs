using System.Diagnostics;
using System.Text;

namespace Jarvis.Core;

public sealed class ToolContext
{
    public ToolContext(
        JarvisOptions options,
        IMemoryStore memoryStore,
        ITranscriptStore transcriptStore,
        IDesktopAutomationService desktopAutomation,
        IWeatherService weatherService,
        IUserStateProvider userStateProvider,
        IWebResearchService webResearchService,
        IVisualContextService visualContextService,
        string workspaceRoot,
        bool plannerAvailable,
        string plannerStatus,
        Func<string, bool, CancellationToken, Task<ToolResult>> automationCommandExecutor)
    {
        Options = options;
        MemoryStore = memoryStore;
        TranscriptStore = transcriptStore;
        DesktopAutomation = desktopAutomation;
        WeatherService = weatherService;
        UserStateProvider = userStateProvider;
        WebResearchService = webResearchService;
        VisualContextService = visualContextService;
        WorkspaceRoot = workspaceRoot;
        PlannerAvailable = plannerAvailable;
        PlannerStatus = plannerStatus;
        AutomationCommandExecutor = automationCommandExecutor;
    }

    public JarvisOptions Options { get; }

    public IMemoryStore MemoryStore { get; }

    public ITranscriptStore TranscriptStore { get; }

    public IDesktopAutomationService DesktopAutomation { get; }

    public IWeatherService WeatherService { get; }

    public IUserStateProvider UserStateProvider { get; }

    public IWebResearchService WebResearchService { get; }

    public IVisualContextService VisualContextService { get; }

    public string WorkspaceRoot { get; }

    public bool PlannerAvailable { get; }

    public string PlannerStatus { get; }

    public Func<string, bool, CancellationToken, Task<ToolResult>> AutomationCommandExecutor { get; }

    public Task<ToolResult> ExecuteAutomationCommandAsync(
        string command,
        bool bypassApproval,
        CancellationToken cancellationToken) =>
        AutomationCommandExecutor(command, bypassApproval, cancellationToken);
}

public sealed record ToolResult(
    string ResponseText,
    bool ShouldExit = false,
    bool Succeeded = true,
    string VerificationText = "",
    string SummaryText = "");

public interface IAssistantTool
{
    string Name { get; }

    string Description { get; }

    bool CanHandle(string input);

    Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken);
}

public sealed class HelpTool : IAssistantTool
{
    private readonly Func<IReadOnlyList<IAssistantTool>> _getTools;

    public HelpTool(Func<IReadOnlyList<IAssistantTool>> getTools)
    {
        _getTools = getTools;
    }

    public string Name => "help";

    public string Description => "Show the available directives.";

    public bool CanHandle(string input) =>
        input.Equals("help", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("commands", StringComparison.OrdinalIgnoreCase);

    public Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var lines = _getTools()
            .Select(tool => $"- {tool.Name}: {tool.Description}")
            .ToList();

        lines.Insert(0, "Available directives:");
        lines.Add("- voice corrections: Show learned speech-correction pairs plus recent rejected transcripts.");
        lines.Add("- I said <corrected>, not <heard>: Teach Jarvis how to correct a recurring transcription mistake.");

        return Task.FromResult(new ToolResult(string.Join(Environment.NewLine, lines)));
    }
}

public sealed class StatusTool : IAssistantTool
{
    public string Name => "status";

    public string Description => "Report the current runtime mode and enabled capabilities.";

    public bool CanHandle(string input) =>
        input.Equals("status", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("capabilities", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("what can you do", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var memoryCount = await context.MemoryStore.CountAsync(cancellationToken);
        var transcriptCount = await context.TranscriptStore.CountAsync(cancellationToken);
        var profile = await context.MemoryStore.GetProfileAsync(cancellationToken);
        var preferredAddress = AssistantPersonaConfiguration.ResolvePreferredUserAddress(context.Options, profile);
        var pronunciationHints = AssistantPersonaConfiguration.ResolvePronunciationHints(context.Options);

        var response = string.Join(
            Environment.NewLine,
            $"Mode: {(context.PlannerAvailable ? "agent runtime" : "bootstrap shell")}",
            $"Assistant: {context.Options.AssistantName}",
            $"Assistant persona: {AssistantPersonaConfiguration.BuildStatusSummary(context.Options)}",
            $"Presence preset: {AssistantPersonaConfiguration.ResolvePresencePreset(context.Options)}",
            $"Response style: {AssistantPersonaConfiguration.ResolveResponseStyle(context.Options)}",
            $"Small talk: {(context.Options.AssistantSmallTalkEnabled ? "brief and allowed when invited" : "minimal by default")}",
            $"Preferred user address: {(string.IsNullOrWhiteSpace(preferredAddress) ? "not set" : preferredAddress)}",
            $"Pronunciation hints: {(pronunciationHints.Count == 0 ? "none" : string.Join(" | ", pronunciationHints.Take(3)))}",
            $"Wake phrase: {context.Options.WakePhrase}",
            $"Voice output: {(context.Options.VoiceEnabled ? "enabled" : "disabled")}",
            $"Speech recognition: {(context.Options.SpeechRecognitionEnabled ? "enabled" : "disabled")}",
            $"Speech provider: {context.Options.SpeechRecognitionProvider}",
            $"Wake word gate: {(context.Options.WakeWordEnabled ? "enabled" : "disabled")}",
            $"Recognizer culture: {context.Options.RecognizerCulture}",
            "Desktop automation: enabled for app discovery, visible-window discovery, window management, browser, form, and Chrome DOM workflows, file create/edit/move/copy/delete actions, clipboard history and restore, notifications, macros with rollback support, media controls, Win32 UI control, local network discovery, live computer context, system state, and app adapters for Spotify, VS Code, Office, Chrome, Explorer, Discord, and Slack",
            $"Visual grounding: screen and window capture, monitor inventory, OCR-style text extraction, structured UI analysis, and document parsing for text/PDF/image inputs ({(string.IsNullOrWhiteSpace(context.Options.PlannerVisionModel) ? "planner vision route inherits the default model" : context.Options.PlannerVisionModel)})",
            $"Ambient sensing: {(context.Options.AmbientContextEnabled ? "enabled" : "disabled")} | webcam {(string.IsNullOrWhiteSpace(context.Options.AmbientWebcamFramePath) ? "not configured" : "configured")} | room sensors {(string.IsNullOrWhiteSpace(context.Options.AmbientRoomSensorSnapshotPath) ? "not configured" : "configured")} | face recognition {(context.Options.AmbientFaceRecognitionEnabled ? (context.Options.AmbientFaceRecognitionConsentGranted ? "enabled with consent" : "blocked until consent is granted") : "disabled")} | spatial cues {(context.Options.SpatialAudioCuesEnabled ? "enabled" : "disabled")} | far-field {(context.Options.FarFieldListeningEnabled ? "enabled" : "disabled")}",
            $"Shell execution: {(context.Options.ShellExecutionEnabled ? "enabled" : "disabled")}",
            $"Action safety: {(context.Options.SafetyRequireApprovalForMajorActions ? "approval required for shell changes" : "approval disabled")} | {(context.Options.SafetyBlockProtectedSystemPaths ? "protected system paths blocked" : "protected system path blocking disabled")}",
            $"Planner: {context.PlannerStatus}",
            $"Natural language understanding: {(context.PlannerAvailable ? "multi-step, structured tool-calling, verification-aware, and context-aware" : "command-driven only")}",
            $"Web research: {(context.Options.PlannerWebResearchEnabled ? "enabled" : "disabled")}",
            "Additional context: short-term session state, recent turns, recent tool results, learned preferences, local user state, reminders, and optional visual understanding",
            $"Default weather location: {(string.IsNullOrWhiteSpace(context.Options.DefaultWeatherLocation) ? "not set" : context.Options.DefaultWeatherLocation)}",
            $"Stored memories: {memoryCount} | learned preferences: {profile.PreferenceCount} | summaries: {profile.SummaryCount} | structured: {profile.StructuredMemoryCount} | pending reminders: {profile.PendingReminderCount}",
            $"Transcript turns: {transcriptCount}",
            context.PlannerAvailable
                ? "Remaining gaps for movie-style behavior: stronger email/task integrations, deeper app-state automation, and a richer HUD."
                : "Missing layers for movie-style behavior: a stronger streaming voice pipeline, LLM planning, deeper desktop automation, and a richer HUD.");

        return new ToolResult(response);
    }
}

public sealed class TimeTool : IAssistantTool
{
    public string Name => "time";

    public string Description => "Report the current local date and time.";

    public bool CanHandle(string input) =>
        input.Equals("time", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("date", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("what time is it", StringComparison.OrdinalIgnoreCase);

    public Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var response = $"Local time is {now:dddd, MMMM d, yyyy h:mm tt zzz}.";
        return Task.FromResult(new ToolResult(response));
    }
}

public sealed class RememberTool : IAssistantTool
{
    public string Name => "remember";

    public string Description => "Store a note with `remember <note>`. Use `remember private <note>` to keep it off disk.";

    public bool CanHandle(string input) =>
        input.StartsWith("remember ", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("note ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var content = input.StartsWith("note ", StringComparison.OrdinalIgnoreCase)
            ? input["note ".Length..].Trim()
            : input["remember ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            return new ToolResult("Usage: remember <note>");
        }

        var isPrivate = content.StartsWith("private ", StringComparison.OrdinalIgnoreCase)
            || content.StartsWith("privately ", StringComparison.OrdinalIgnoreCase)
            || content.StartsWith("ephemeral ", StringComparison.OrdinalIgnoreCase);

        if (isPrivate)
        {
            var privateContent = content
                .Replace("private ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("privately ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("ephemeral ", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            return string.IsNullOrWhiteSpace(privateContent)
                ? new ToolResult("Usage: remember private <note>")
                : new ToolResult(
                    "Noted privately. I did not persist that memory.",
                    VerificationText: "The remember command was explicitly marked private.",
                    SummaryText: "Private memory was not persisted.");
        }

        var retentionPolicy = string.Empty;
        var userApprovedRetention = false;

        foreach (var prefix in new[] { "forever ", "long-term ", "long term ", "short-term ", "short term " })
        {
            if (!content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            retentionPolicy = prefix.StartsWith("short", StringComparison.OrdinalIgnoreCase)
                ? "short"
                : prefix.StartsWith("forever", StringComparison.OrdinalIgnoreCase)
                    ? "forever"
                    : "long";
            content = content[prefix.Length..].Trim();
            userApprovedRetention = true;
            break;
        }

        if (MemoryAutomation.LooksSensitive(content))
        {
            return new ToolResult(
                "That looks sensitive, so I did not persist it. Use an off-the-record request instead if you still want help with it.",
                Succeeded: false,
                VerificationText: "Sensitive content was detected and blocked from persistence.",
                SummaryText: "Blocked persistence for sensitive memory content.");
        }

        var memoryContext = await ToolInputHelper.BuildInteractionContextAsync(context, "manual remember", cancellationToken);
        await context.MemoryStore.AddAsync(
            new MemoryWriteRequest(
                content,
                Kind: "note",
                Category: string.Empty,
                Source: "manual",
                Tags: ["manual", "memory"],
                Context: memoryContext,
                RetentionPolicy: retentionPolicy,
                UserApprovedRetention: userApprovedRetention),
            cancellationToken);

        var retentionSummary = string.IsNullOrWhiteSpace(retentionPolicy)
            ? string.Empty
            : $" with {retentionPolicy} retention";
        return new ToolResult(
            $"Stored memory{retentionSummary}: {content}",
            VerificationText: "The memory note was written to the local store.",
            SummaryText: "Stored one memory note.");
    }
}

public sealed class RecallTool : IAssistantTool
{
    public string Name => "recall";

    public string Description => "Search memory semantically with `recall <term>` or `what do you remember about <term>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("recall", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("what do you remember about", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var query = input.StartsWith("what do you remember about", StringComparison.OrdinalIgnoreCase)
            ? input["what do you remember about".Length..].Trim()
            : input["recall".Length..].Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            return new ToolResult("Usage: recall <term>");
        }

        var queryContext = await ToolInputHelper.BuildMemoryQueryContextAsync(context, query, cancellationToken);
        var notes = await context.MemoryStore.SearchAsync(query, queryContext, 5, cancellationToken);

        if (notes.Count == 0)
        {
            return new ToolResult($"No relevant memory matches found for \"{query}\".");
        }

        var lines = notes.Select(ToolInputHelper.RenderMemoryLine);
        return new ToolResult("Relevant memories:" + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }
}

public sealed class ListMemoryTool : IAssistantTool
{
    public string Name => "notes";

    public string Description => "List recent memories plus upcoming reminders.";

    public bool CanHandle(string input) =>
        input.Equals("notes", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("memories", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var notes = await context.MemoryStore.GetRecentAsync(8, cancellationToken);
        var reminders = await context.MemoryStore.GetPendingRemindersAsync(4, cancellationToken);

        if (notes.Count == 0 && reminders.Count == 0)
        {
            return new ToolResult("No memories stored yet.");
        }

        var sections = new List<string>();

        if (reminders.Count > 0)
        {
            sections.Add("Upcoming reminders:");
            sections.AddRange(reminders.Select(ToolInputHelper.RenderReminderLine));
        }

        if (notes.Count > 0)
        {
            if (sections.Count > 0)
            {
                sections.Add(string.Empty);
            }

            sections.Add("Recent memories:");
            sections.AddRange(notes.Select(ToolInputHelper.RenderMemoryLine));
        }

        return new ToolResult(string.Join(Environment.NewLine, sections));
    }
}

public sealed class ReminderTool : IAssistantTool
{
    public string Name => "reminders";

    public string Description => "Create or list reminder memories with `remind me to ... at ...` or `reminders`.";

    public bool CanHandle(string input) =>
        input.Equals("reminders", StringComparison.OrdinalIgnoreCase)
        || input.Equals("show reminders", StringComparison.OrdinalIgnoreCase)
        || input.Equals("list reminders", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("reminders ", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("remind me ", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("set reminder ", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("set a reminder ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (input.Equals("reminders", StringComparison.OrdinalIgnoreCase)
            || input.Equals("show reminders", StringComparison.OrdinalIgnoreCase)
            || input.Equals("list reminders", StringComparison.OrdinalIgnoreCase))
        {
            var reminders = await context.MemoryStore.GetPendingRemindersAsync(8, cancellationToken);

            if (reminders.Count == 0)
            {
                return new ToolResult("No pending reminders.");
            }

            return new ToolResult("Pending reminders:" + Environment.NewLine + string.Join(Environment.NewLine, reminders.Select(ToolInputHelper.RenderReminderLine)));
        }

        var reminderInput = input.StartsWith("reminders ", StringComparison.OrdinalIgnoreCase)
            ? "remind me to " + input["reminders ".Length..].Trim()
            : input;
        var memoryContext = await ToolInputHelper.BuildInteractionContextAsync(context, "manual reminder", cancellationToken);
        var reminder = MemoryAutomation.BuildReminderMemory(reminderInput, memoryContext, DateTimeOffset.Now);

        if (reminder is null)
        {
            return new ToolResult(
                "Usage: remind me to <task> at <time>, remind me to <task> tomorrow at <time>, or remind me to <task> in <number> minutes/hours/days.",
                Succeeded: false,
                VerificationText: "The reminder request could not be parsed into a date and task.",
                SummaryText: "Reminder parsing failed.");
        }

        await context.MemoryStore.AddAsync(reminder, cancellationToken);
        return new ToolResult(
            $"Stored reminder: {reminder.ReminderText} at {reminder.ReminderAtUtc?.ToLocalTime():dddd, MMMM d h:mm tt}.",
            VerificationText: "The reminder memory was written to the local store.",
            SummaryText: "Stored one reminder.");
    }
}

public sealed class ForgetMemoryTool : IAssistantTool
{
    public string Name => "forget";

    public string Description => "Forget a stored memory with `forget <id-or-phrase>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("forget ", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("delete memory ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "forget", "delete memory");

        if (string.IsNullOrWhiteSpace(target))
        {
            return new ToolResult("Usage: forget <id-or-phrase>");
        }

        var notes = await context.MemoryStore.GetAllAsync(cancellationToken);
        var directMatch = notes.FirstOrDefault(note =>
            string.Equals(note.Id, target, StringComparison.OrdinalIgnoreCase)
            || note.Id.StartsWith(target, StringComparison.OrdinalIgnoreCase));

        var match = directMatch;

        if (match is null)
        {
            var searchContext = await ToolInputHelper.BuildMemoryQueryContextAsync(context, target, cancellationToken);
            var searched = await context.MemoryStore.SearchAsync(target, searchContext, 3, cancellationToken);

            if (searched.Count == 0)
            {
                return new ToolResult($"No stored memory matched \"{target}\".");
            }

            if (searched.Count > 1
                && !string.Equals(searched[0].Content, target, StringComparison.OrdinalIgnoreCase))
            {
                var lines = searched.Select(ToolInputHelper.RenderMemoryLine);
                return new ToolResult(
                    "More than one memory matched. Use a longer phrase or the memory id prefix:" + Environment.NewLine + string.Join(Environment.NewLine, lines),
                    Succeeded: false,
                    VerificationText: "The forget request was ambiguous.",
                    SummaryText: "Memory forget request was ambiguous.");
            }

            match = searched[0];
        }

        var forgot = await context.MemoryStore.ForgetAsync(match.Id, "user request", cancellationToken);

        return forgot
            ? new ToolResult(
                $"Forgot memory {match.Id[..8]}: {match.Content}",
                VerificationText: "The selected memory was marked forgotten.",
                SummaryText: "Forgot one memory.")
            : new ToolResult(
                $"Unable to forget memory {match.Id[..8]}.",
                Succeeded: false,
                VerificationText: "The selected memory could not be updated.",
                SummaryText: "Memory forget failed.");
    }
}

public sealed class MemoryProfileTool : IAssistantTool
{
    public string Name => "preferences";

    public string Description => "Show learned preferences, habits, routines, structured memories, and pending reminders.";

    public bool CanHandle(string input) =>
        input.Equals("preferences", StringComparison.OrdinalIgnoreCase)
        || input.Equals("profile", StringComparison.OrdinalIgnoreCase)
        || input.Equals("memory profile", StringComparison.OrdinalIgnoreCase)
        || input.Equals("user profile", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var profile = await context.MemoryStore.GetProfileAsync(cancellationToken);
        return new ToolResult(profile.ToDisplayText());
    }
}

public sealed class PrivacyTool : IAssistantTool
{
    public string Name => "privacy";

    public string Description => "Explain memory privacy boundaries and off-the-record behavior.";

    public bool CanHandle(string input) =>
        input.Equals("privacy", StringComparison.OrdinalIgnoreCase)
        || input.Equals("privacy help", StringComparison.OrdinalIgnoreCase)
        || input.Equals("privacy boundaries", StringComparison.OrdinalIgnoreCase)
        || input.Equals("memory privacy", StringComparison.OrdinalIgnoreCase);

    public Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var response = string.Join(
            Environment.NewLine,
            "Privacy boundaries:",
            "- Use `remember private <note>` to keep a note off disk.",
            "- Prefix a request with `off the record` to skip transcript logging and automatic memory capture for that turn.",
            "- Sensitive content such as passwords, tokens, API keys, SSNs, OTP codes, and card-like numbers is blocked from automatic persistence.",
            "- Memory retention can be shortened, extended, edited, or forgotten later from the memory UI.");

        return Task.FromResult(new ToolResult(response));
    }
}

public sealed class ExportMemoriesTool : IAssistantTool
{
    public string Name => "export memories";

    public string Description => "Export memories to JSON with `export memories <path>`.";

    public bool CanHandle(string input) =>
        input.Equals("export memories", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("export memories ", StringComparison.OrdinalIgnoreCase)
        || input.Equals("export memory", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("export memory ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var requestedPath = ToolInputHelper.GetCommandTail(input, "export memories", "export memory");
        var targetPath = ToolInputHelper.ResolveTransferPath(
            context,
            requestedPath,
            "memories",
            "json");
        var exportPath = await context.MemoryStore.ExportAsync(targetPath, cancellationToken);

        return new ToolResult(
            $"Exported memories to {exportPath}",
            VerificationText: "The memory store wrote a JSON export bundle.",
            SummaryText: "Exported memories.");
    }
}

public sealed class ImportMemoriesTool : IAssistantTool
{
    public string Name => "import memories";

    public string Description => "Import memories from JSON or JSONL with `import memories <path>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("import memories ", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("import memory ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var requestedPath = ToolInputHelper.GetCommandTail(input, "import memories", "import memory");

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return new ToolResult("Usage: import memories <path>");
        }

        var result = await context.MemoryStore.ImportAsync(ToolInputHelper.ResolveTransferPath(context, requestedPath, "memories", "json"), cancellationToken);
        return new ToolResult(
            result.SummaryText,
            VerificationText: "The memory store processed the import file.",
            SummaryText: result.SummaryText);
    }
}

public sealed class ExportTranscriptsTool : IAssistantTool
{
    public string Name => "export transcripts";

    public string Description => "Export transcript history with `export transcripts <path>`.";

    public bool CanHandle(string input) =>
        input.Equals("export transcripts", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("export transcripts ", StringComparison.OrdinalIgnoreCase)
        || input.Equals("export transcript", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("export transcript ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var requestedPath = ToolInputHelper.GetCommandTail(input, "export transcripts", "export transcript");
        var targetPath = ToolInputHelper.ResolveTransferPath(
            context,
            requestedPath,
            "transcripts",
            "jsonl");
        var exportPath = await context.TranscriptStore.ExportAsync(targetPath, cancellationToken);

        return new ToolResult(
            $"Exported transcripts to {exportPath}",
            VerificationText: "The transcript store wrote the export file.",
            SummaryText: "Exported transcripts.");
    }
}

public sealed class ImportTranscriptsTool : IAssistantTool
{
    public string Name => "import transcripts";

    public string Description => "Import transcript history from JSON or JSONL with `import transcripts <path>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("import transcripts ", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("import transcript ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var requestedPath = ToolInputHelper.GetCommandTail(input, "import transcripts", "import transcript");

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return new ToolResult("Usage: import transcripts <path>");
        }

        var result = await context.TranscriptStore.ImportAsync(ToolInputHelper.ResolveTransferPath(context, requestedPath, "transcripts", "jsonl"), cancellationToken);
        return new ToolResult(
            result.SummaryText,
            VerificationText: "The transcript store processed the import file.",
            SummaryText: result.SummaryText);
    }
}

public sealed class WeatherTool : IAssistantTool
{
    public string Name => "weather";

    public string Description => "Check current weather with `weather <location>` or use `defaultWeatherLocation` in settings.";

    public bool CanHandle(string input) =>
        input.Equals("weather", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("weather ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "weather");

        if (string.IsNullOrWhiteSpace(target))
        {
            target = context.Options.DefaultWeatherLocation;
        }

        return new ToolResult(await context.WeatherService.GetCurrentWeatherAsync(target, cancellationToken));
    }
}

public sealed class SystemStateTool : IAssistantTool
{
    public string Name => "system";

    public string Description => "Show computer status, drives, and running process count.";

    public bool CanHandle(string input) =>
        input.Equals("system", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("system state", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("computer status", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("machine status", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        return new ToolResult(await context.DesktopAutomation.GetSystemStatusAsync(cancellationToken));
    }
}

public sealed class ComputerContextTool : IAssistantTool
{
    public string Name => "computer";

    public string Description => "Show live desktop context including the active window, visible apps, workspace entries, and system state.";

    public bool CanHandle(string input) =>
        input.Equals("computer", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("computer context", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("desktop context", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("active window", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("what app am i in", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("what is happening on my computer", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("whats happening on my computer", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("what is going on on my computer", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("whats going on on my computer", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        return new ToolResult(await context.DesktopAutomation.GetComputerContextAsync(cancellationToken));
    }
}

public sealed class NetworkTool : IAssistantTool
{
    public string Name => "network";

    public string Description => "Inspect the local network. Use `network`, `network scan`, `network health`, or `network <ip-or-host>`.";

    public bool CanHandle(string input)
    {
        var normalized = input.Trim();

        return normalized.Equals("network", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("network ", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("local network", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("network devices", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("network scan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("network health", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("scan my network", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("devices on my network", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("discover devices on my network", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("check network health", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("check my network", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ParseNetworkTarget(input);
        var response = await context.DesktopAutomation.GetLocalNetworkStatusAsync(target, cancellationToken);

        return new ToolResult(
            response,
            VerificationText: "The local network monitor completed an on-demand discovery or health check.",
            SummaryText: response.ReplaceLineEndings(" ").Trim());
    }

    private static string ParseNetworkTarget(string input)
    {
        var normalized = input.Trim();

        if (normalized.Equals("scan my network", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("devices on my network", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("discover devices on my network", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("network scan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("network devices", StringComparison.OrdinalIgnoreCase))
        {
            return "scan";
        }

        if (normalized.Equals("network health", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("check network health", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("check my network", StringComparison.OrdinalIgnoreCase))
        {
            return "health";
        }

        if (normalized.Equals("network", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("local network", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return ToolInputHelper.GetCommandTail(normalized, "network");
    }
}

public sealed class ApplicationsTool : IAssistantTool
{
    public string Name => "apps";

    public string Description => "List discovered applications or search them with `apps <name>`.";

    public bool CanHandle(string input) =>
        input.Equals("apps", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("running apps", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("what apps are open", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("whats open", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("apps ", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("find app", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("search apps", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("list apps", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "apps", "find app", "search apps", "list apps", "running apps");
        return new ToolResult(await context.DesktopAutomation.ListApplicationsAsync(target, cancellationToken));
    }
}

public sealed class OpenApplicationTool : IAssistantTool
{
    public string Name => "open app";

    public string Description => "Focus a running app or launch a discovered application with `open app <name>` or `launch <name>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("open app", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("launch", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("start app", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "open app", "launch app", "launch", "start app");
        return new ToolResult(await context.DesktopAutomation.OpenApplicationAsync(target, cancellationToken));
    }
}

public sealed class OpenPathTool : IAssistantTool
{
    public string Name => "open path";

    public string Description => "Open a file or folder with `open path <target>` or `open file <target>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("open path", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("open file", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("open folder", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "open path", "open file", "open folder");
        return new ToolResult(await context.DesktopAutomation.OpenPathAsync(target, cancellationToken));
    }
}

public sealed class ListFilesTool : IAssistantTool
{
    public string Name => "list files";

    public string Description => "List files in a directory with `list files <path>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("list files", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("show files", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("list folder", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "list files", "show files", "list folder");
        return new ToolResult(await context.DesktopAutomation.ListFilesAsync(target, cancellationToken));
    }
}

public sealed class ReadFileTool : IAssistantTool
{
    public string Name => "read file";

    public string Description => "Preview a text file with `read file <path>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("read file", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("show file", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("preview file", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "read file", "show file", "preview file");
        return new ToolResult(await context.DesktopAutomation.ReadFilePreviewAsync(target, cancellationToken));
    }
}

public sealed class FindFilesTool : IAssistantTool
{
    public string Name => "find files";

    public string Description => "Search the workspace for matching files with `find files <term>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("find files", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("find file", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("search files", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "find files", "find file", "search files");
        return new ToolResult(await context.DesktopAutomation.FindFilesAsync(target, cancellationToken));
    }
}

public sealed class BrowseTool : IAssistantTool
{
    public string Name => "browse";

    public string Description => "Open a website with `browse <url>` or `go to <site>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("browse", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("open website", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("go to", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("visit", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "browse", "open website", "go to", "visit");
        return new ToolResult(await context.DesktopAutomation.BrowseAsync(target, cancellationToken));
    }
}

public sealed class SearchWebTool : IAssistantTool
{
    public string Name => "search web";

    public string Description => "Run a browser search with `search web <query>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("search web", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("web search", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "search web", "web search");
        return new ToolResult(await context.DesktopAutomation.SearchWebAsync(target, cancellationToken));
    }
}

public sealed class EmailTool : IAssistantTool
{
    public string Name => "email";

    public string Description => "Show email-aware planning context from the local user-state snapshot.";

    public bool CanHandle(string input) =>
        input.Equals("email", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("emails", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("inbox", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("email context", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("show email", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("show emails", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("list emails", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.UserStateProvider.GetSnapshotAsync(cancellationToken);

        if (snapshot.EmailItems.Length == 0)
        {
            return new ToolResult(
                "No email context is available in the local user-state snapshot.",
                Succeeded: false,
                VerificationText: "The user-state snapshot did not contain email items.",
                SummaryText: "No email items are available.");
        }

        var lines = snapshot.EmailItems
            .Take(8)
            .Select(item =>
            {
                var unread = item.IsUnread ? " | unread" : string.Empty;
                return $"- {item.Title} | {item.Detail}{unread}";
            });

        return new ToolResult(
            "Email context:" + Environment.NewLine + string.Join(Environment.NewLine, lines),
            VerificationText: $"Loaded {snapshot.EmailItems.Length} email item(s) from the local user-state snapshot.",
            SummaryText: $"Loaded {snapshot.EmailItems.Length} email item(s).");
    }
}

public sealed class TasksTool : IAssistantTool
{
    public string Name => "tasks";

    public string Description => "Show task-aware planning context from the current user-state snapshot.";

    public bool CanHandle(string input) =>
        input.Equals("tasks", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("todo", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("task", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("task context", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("show tasks", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("list tasks", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("show todo", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.UserStateProvider.GetSnapshotAsync(cancellationToken);

        if (snapshot.TaskItems.Length == 0)
        {
            return new ToolResult(
                "No task context is available in the local user-state snapshot.",
                Succeeded: false,
                VerificationText: "The user-state snapshot did not contain task items.",
                SummaryText: "No task items are available.");
        }

        var lines = snapshot.TaskItems
            .Take(8)
            .Select(item => $"- {item.Title} | {item.Detail}{(string.IsNullOrWhiteSpace(item.DueAt) ? string.Empty : $" | {item.DueAt}")}");

        return new ToolResult(
            "Task context:" + Environment.NewLine + string.Join(Environment.NewLine, lines),
            VerificationText: $"Loaded {snapshot.TaskItems.Length} task item(s) from the local user-state snapshot.",
            SummaryText: $"Loaded {snapshot.TaskItems.Length} task item(s).");
    }
}

public sealed class ResearchTool : IAssistantTool
{
    public string Name => "research";

    public string Description => "Run web research with citations using `research <query>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("research ", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("web research ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var query = ToolInputHelper.GetCommandTail(input, "research", "web research");

        if (string.IsNullOrWhiteSpace(query))
        {
            return new ToolResult(
                "Usage: research <topic>",
                Succeeded: false,
                VerificationText: "No research query was provided.",
                SummaryText: "Missing research query.");
        }

        var result = await context.WebResearchService.ResearchAsync(
            query,
            context.Options.PlannerWebResearchMaxResults,
            cancellationToken);

        if (result.Citations.Length == 0)
        {
            return new ToolResult(
                result.SummaryText,
                Succeeded: !result.IsOffline,
                VerificationText: result.IsOffline ? "The research service could not reach the public web." : "The research service returned no sources.",
                SummaryText: result.SummaryText);
        }

        var lines = result.Citations.Select(citation =>
            $"- {citation.Title} | {citation.Url} | {citation.Snippet}");

        return new ToolResult(
            $"Web research for \"{query}\":{Environment.NewLine}{string.Join(Environment.NewLine, lines)}",
            VerificationText: $"Collected {result.Citations.Length} citation(s) for the query.",
            SummaryText: $"Collected {result.Citations.Length} web citation(s).");
    }
}

public sealed class ScreenTool : IAssistantTool
{
    public string Name => "screen";

    public string Description => "Monitor- and window-aware screen tooling. Use `screen`, `screen monitors`, `screen capture [target]`, `screen ocr [target]`, or `screen ui [target]`.";

    public bool CanHandle(string input) =>
        ToolInputHelper.StartsWithCommand(input, "screen") ||
        ToolInputHelper.StartsWithCommand(input, "analyze screen") ||
        ToolInputHelper.StartsWithCommand(input, "inspect screen") ||
        ToolInputHelper.MatchesExact(input, "monitors", "show monitors", "list monitors", "displays", "show displays", "list displays") ||
        ToolInputHelper.StartsWithCommand(input, "screenshot") ||
        ToolInputHelper.StartsWithCommand(input, "take screenshot") ||
        ToolInputHelper.StartsWithCommand(input, "capture screen") ||
        ToolInputHelper.StartsWithCommand(input, "screen capture") ||
        ToolInputHelper.StartsWithCommand(input, "screen ocr") ||
        ToolInputHelper.StartsWithCommand(input, "screen text") ||
        ToolInputHelper.StartsWithCommand(input, "ocr") ||
        ToolInputHelper.StartsWithCommand(input, "extract text from screen") ||
        ToolInputHelper.StartsWithCommand(input, "detect ui") ||
        ToolInputHelper.StartsWithCommand(input, "screen ui") ||
        ToolInputHelper.StartsWithCommand(input, "screen elements");

    public Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (ToolInputHelper.MatchesExact(input, "monitors", "show monitors", "list monitors", "displays", "show displays", "list displays"))
        {
            return context.VisualContextService.ListDisplaysAsync(cancellationToken);
        }

        if (ToolInputHelper.StartsWithCommand(input, "screenshot")
            || ToolInputHelper.StartsWithCommand(input, "take screenshot")
            || ToolInputHelper.StartsWithCommand(input, "capture screen")
            || ToolInputHelper.StartsWithCommand(input, "screen capture"))
        {
            var target = ToolInputHelper.GetCommandTail(input, "screenshot", "take screenshot", "capture screen", "screen capture");
            return context.VisualContextService.CaptureScreenAsync(target, cancellationToken);
        }

        if (ToolInputHelper.StartsWithCommand(input, "screen ocr")
            || ToolInputHelper.StartsWithCommand(input, "screen text")
            || ToolInputHelper.StartsWithCommand(input, "ocr")
            || ToolInputHelper.StartsWithCommand(input, "extract text from screen"))
        {
            var target = ToolInputHelper.GetCommandTail(input, "screen ocr", "screen text", "ocr", "extract text from screen");
            return context.VisualContextService.ExtractScreenTextAsync(target, cancellationToken);
        }

        if (ToolInputHelper.StartsWithCommand(input, "detect ui")
            || ToolInputHelper.StartsWithCommand(input, "screen ui")
            || ToolInputHelper.StartsWithCommand(input, "screen elements"))
        {
            var target = ToolInputHelper.GetCommandTail(input, "detect ui", "screen ui", "screen elements");
            return context.VisualContextService.DetectScreenUiAsync(target, cancellationToken);
        }

        var question = ToolInputHelper.GetCommandTail(input, "screen", "analyze screen", "inspect screen");
        return context.VisualContextService.AnalyzeCurrentScreenAsync(question, cancellationToken);
    }
}

public sealed class AmbientTool : IAssistantTool
{
    public string Name => "ambient";

    public string Description => "Inspect webcam-fed room context and sensor hooks. Use `ambient`, `ambient webcam`, `ambient presence`, `ambient face`, or `ambient room`.";

    public bool CanHandle(string input) =>
        ToolInputHelper.StartsWithCommand(input, "ambient") ||
        ToolInputHelper.StartsWithCommand(input, "webcam") ||
        ToolInputHelper.StartsWithCommand(input, "camera") ||
        ToolInputHelper.StartsWithCommand(input, "presence") ||
        ToolInputHelper.StartsWithCommand(input, "gesture") ||
        ToolInputHelper.StartsWithCommand(input, "face recognition") ||
        ToolInputHelper.StartsWithCommand(input, "face") ||
        ToolInputHelper.StartsWithCommand(input, "room sensors") ||
        ToolInputHelper.StartsWithCommand(input, "far field") ||
        ToolInputHelper.StartsWithCommand(input, "spatial audio");

    public Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (ToolInputHelper.MatchesExact(input, "ambient"))
        {
            return context.VisualContextService.AnalyzeAmbientContextAsync(string.Empty, cancellationToken);
        }

        if (ToolInputHelper.StartsWithCommand(input, "ambient"))
        {
            var payload = ToolInputHelper.GetCommandTail(input, "ambient");
            return context.VisualContextService.AnalyzeAmbientContextAsync(payload, cancellationToken);
        }

        if (ToolInputHelper.StartsWithCommand(input, "webcam")
            || ToolInputHelper.StartsWithCommand(input, "camera"))
        {
            var payload = ToolInputHelper.GetCommandTail(input, "webcam", "camera");
            return context.VisualContextService.AnalyzeAmbientContextAsync(
                string.IsNullOrWhiteSpace(payload) ? "webcam" : $"webcam {payload}",
                cancellationToken);
        }

        if (ToolInputHelper.StartsWithCommand(input, "presence")
            || ToolInputHelper.StartsWithCommand(input, "gesture")
            || ToolInputHelper.StartsWithCommand(input, "face recognition")
            || ToolInputHelper.StartsWithCommand(input, "face")
            || ToolInputHelper.StartsWithCommand(input, "room sensors")
            || ToolInputHelper.StartsWithCommand(input, "far field")
            || ToolInputHelper.StartsWithCommand(input, "spatial audio"))
        {
            return context.VisualContextService.AnalyzeAmbientContextAsync(input.Trim(), cancellationToken);
        }

        return context.VisualContextService.AnalyzeAmbientContextAsync(string.Empty, cancellationToken);
    }
}

public sealed class AnalyzeImageTool : IAssistantTool
{
    public string Name => "analyze image";

    public string Description => "Analyze an image file. Format: `analyze image <path>` or `analyze image <path> :: <question>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("analyze image", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("inspect image", StringComparison.OrdinalIgnoreCase);

    public Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var payload = ToolInputHelper.GetCommandTail(input, "analyze image", "inspect image");
        return context.VisualContextService.AnalyzeImageAsync(payload, cancellationToken);
    }
}

public sealed class AnalyzeDocumentTool : IAssistantTool
{
    public string Name => "analyze document";

    public string Description => "Analyze a text, PDF, or image-based document. Format: `analyze document <path>` or `analyze document <path> :: <question>`.";

    public bool CanHandle(string input) =>
        input.StartsWith("analyze document", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("inspect document", StringComparison.OrdinalIgnoreCase);

    public Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var payload = ToolInputHelper.GetCommandTail(input, "analyze document", "inspect document");
        return context.VisualContextService.AnalyzeDocumentAsync(payload, cancellationToken);
    }
}

public sealed class ShellTool : IAssistantTool
{
    public string Name => "run";

    public string Description => "Run a PowerShell command with `run <command>` when enabled. Read-only commands run directly; major changes require approval.";

    public bool CanHandle(string input) =>
        input.StartsWith("run ", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("shell ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!context.Options.ShellExecutionEnabled)
        {
            return new ToolResult(
                "Shell execution is disabled. Set `shellExecutionEnabled` to `true` in jarvis.settings.json to allow it.",
                Succeeded: false,
                VerificationText: "The runtime blocked shell execution because it is disabled in settings.",
                SummaryText: "Shell execution is disabled.");
        }

        var command = input.StartsWith("shell ", StringComparison.OrdinalIgnoreCase)
            ? input["shell ".Length..].Trim()
            : input["run ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(command))
        {
            return new ToolResult(
                "Usage: run <PowerShell command>",
                Succeeded: false,
                VerificationText: "No PowerShell command was provided.",
                SummaryText: "Missing PowerShell command.");
        }

        var output = await ExecutePowerShellAsync(command, context.WorkspaceRoot, cancellationToken);
        var failed = output.StartsWith("Command failed", StringComparison.OrdinalIgnoreCase)
            || output.StartsWith("Shell process failed", StringComparison.OrdinalIgnoreCase);

        return new ToolResult(
            output,
            Succeeded: !failed,
            VerificationText: failed
                ? "PowerShell returned a failure signal."
                : "PowerShell returned a success signal.",
            SummaryText: output.ReplaceLineEndings(" ").Trim());
    }

    private static async Task<string> ExecutePowerShellAsync(string command, string workspaceRoot, CancellationToken cancellationToken)
    {
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encodedCommand}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workspaceRoot
        };

        using var process = Process.Start(startInfo);

        if (process is null)
        {
            return "Shell process failed to start.";
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var combined = string.Join(
            Environment.NewLine,
            new[] { stdout.Trim(), stderr.Trim() }.Where(text => !string.IsNullOrWhiteSpace(text)));

        var trimmedOutput = TrimOutput(combined);

        if (process.ExitCode == 0)
        {
            return string.IsNullOrWhiteSpace(trimmedOutput)
                ? "Command completed successfully."
                : "Command output:" + Environment.NewLine + trimmedOutput;
        }

        return string.IsNullOrWhiteSpace(trimmedOutput)
            ? $"Command failed with exit code {process.ExitCode}."
            : $"Command failed with exit code {process.ExitCode}:{Environment.NewLine}{trimmedOutput}";
    }

    private static string TrimOutput(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        const int maxChars = 2000;
        var value = text.Trim();

        if (value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars] + Environment.NewLine + "[output truncated]";
    }
}

public sealed class ExitTool : IAssistantTool
{
    public string Name => "exit";

    public string Description => "Shutdown the assistant shell.";

    public bool CanHandle(string input) =>
        input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("shutdown", StringComparison.OrdinalIgnoreCase);

    public Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ToolResult("Shutting down.", true));
    }
}

internal static class ToolInputHelper
{
    public static string GetCommandTail(string input, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return input[prefix.Length..].TrimStart(' ', ',', ':', ';', '-');
        }

        return string.Empty;
    }

    public static bool MatchesExact(string input, params string[] values)
    {
        return values.Any(value => input.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public static bool StartsWithCommand(string input, string value)
    {
        return input.Equals(value, StringComparison.OrdinalIgnoreCase)
            || input.StartsWith(value + " ", StringComparison.OrdinalIgnoreCase);
    }

    public static string RenderMemoryLine(MemoryNote note)
    {
        var details = new List<string>();

        details.Add("id " + ShortId(note.Id));

        if (!string.IsNullOrWhiteSpace(note.Kind) && !string.Equals(note.Kind, "note", StringComparison.OrdinalIgnoreCase))
        {
            details.Add(note.Kind);
        }

        if (!string.IsNullOrWhiteSpace(note.Category))
        {
            details.Add(note.Category);
        }

        if (note.Context is not null)
        {
            if (!string.IsNullOrWhiteSpace(note.Context.ActiveApp))
            {
                details.Add("app " + note.Context.ActiveApp.Trim());
            }

            if (!string.IsNullOrWhiteSpace(note.Context.Location))
            {
                details.Add("location " + note.Context.Location.Trim());
            }

            if (!string.IsNullOrWhiteSpace(note.Context.TimeOfDay))
            {
                details.Add(note.Context.TimeOfDay.Trim());
            }
        }

        if (note.ReminderAtUtc is DateTimeOffset reminderAtUtc)
        {
            details.Add("due " + reminderAtUtc.ToLocalTime().ToString("MM-dd h:mm tt"));
        }

        if (!string.IsNullOrWhiteSpace(note.RetentionPolicy))
        {
            details.Add("retention " + note.RetentionPolicy.Trim());
        }

        var suffix = details.Count == 0 ? string.Empty : " | " + string.Join(" | ", details);
        return $"- {note.CreatedAtUtc:yyyy-MM-dd HH:mm}Z | {note.Content}{suffix}";
    }

    public static string RenderReminderLine(MemoryNote note)
    {
        var due = note.ReminderAtUtc?.ToLocalTime().ToString("yyyy-MM-dd h:mm tt") ?? "unscheduled";
        var text = string.IsNullOrWhiteSpace(note.ReminderText) ? note.Content : note.ReminderText;
        return $"- {ShortId(note.Id)} | {text} | due {due} | status {MemoryPolicies.NormalizeReminderStatus(note.ReminderStatus)}";
    }

    public static async Task<InteractionContextSnapshot> BuildInteractionContextAsync(
        ToolContext context,
        string recentActivity,
        CancellationToken cancellationToken)
    {
        try
        {
            var desktopContext = await context.DesktopAutomation.GetContextSnapshotAsync(cancellationToken);
            return MemoryAutomation.BuildInteractionContext(
                desktopContext,
                ResolveLocation(context),
                [recentActivity]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new InteractionContextSnapshot(
                DateTimeOffset.UtcNow,
                string.Empty,
                ResolveLocation(context),
                string.Empty,
                string.Empty,
                [],
                [recentActivity]);
        }
    }

    public static async Task<MemoryQueryContext> BuildMemoryQueryContextAsync(
        ToolContext context,
        string query,
        CancellationToken cancellationToken)
    {
        var interactionContext = await BuildInteractionContextAsync(context, query, cancellationToken);
        return new MemoryQueryContext(
            query,
            interactionContext.TimeOfDay,
            interactionContext.Location,
            interactionContext.ActiveApp,
            interactionContext.ActiveWindow,
            interactionContext.SafeRecentActivity);
    }

    public static string ResolveTransferPath(
        ToolContext context,
        string requestedPath,
        string prefix,
        string defaultExtension)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return Path.Combine(
                context.WorkspaceRoot,
                "data",
                "exports",
                $"{prefix}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{defaultExtension.TrimStart('.')}");
        }

        var trimmed = Environment.ExpandEnvironmentVariables(requestedPath.Trim().Trim('"'));

        if (trimmed.StartsWith('~'))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            trimmed = Path.Combine(userProfile, trimmed[1..].TrimStart('\\', '/'));
        }

        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(context.WorkspaceRoot, trimmed));
    }

    private static string ResolveLocation(ToolContext context)
    {
        return string.IsNullOrWhiteSpace(context.Options.DefaultWeatherLocation)
            ? string.Empty
            : context.Options.DefaultWeatherLocation.Trim();
    }

    private static string ShortId(string id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? "n/a"
            : id.Length <= 8
                ? id
                : id[..8];
    }
}
