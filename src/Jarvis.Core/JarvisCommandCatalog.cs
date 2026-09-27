using System.Text.RegularExpressions;

namespace Jarvis.Core;

public static partial class JarvisCommandCatalog
{
    public static IReadOnlyList<string> HelpPhrases { get; } =
    [
        "help",
        "commands",
        "show commands",
        "show me commands",
        "what can you do",
        "what are your commands"
    ];

    public static IReadOnlyList<string> StatusPhrases { get; } =
    [
        "status",
        "capabilities",
        "system status",
        "what is your status",
        "whats your status"
    ];

    public static IReadOnlyList<string> TimePhrases { get; } =
    [
        "time",
        "date",
        "what time is it",
        "whats the time",
        "tell me the time",
        "what day is it"
    ];

    public static IReadOnlyList<string> NotesPhrases { get; } =
    [
        "notes",
        "memories",
        "show notes",
        "show memories",
        "show my notes",
        "show my memories"
    ];

    public static IReadOnlyList<string> PreferencePhrases { get; } =
    [
        "preferences",
        "profile",
        "memory profile",
        "user profile",
        "show preferences",
        "show my preferences"
    ];

    public static IReadOnlyList<string> PrivacyPhrases { get; } =
    [
        "privacy",
        "privacy help",
        "privacy boundaries",
        "memory privacy"
    ];

    public static IReadOnlyList<string> ReminderPhrases { get; } =
    [
        "reminders",
        "show reminders",
        "list reminders",
        "what are my reminders"
    ];

    public static IReadOnlyList<string> WeatherPhrases { get; } =
    [
        "weather",
        "what is the weather",
        "whats the weather",
        "what's the weather",
        "how is the weather",
        "hows the weather",
        "how's the weather"
    ];

    public static IReadOnlyList<string> SystemPhrases { get; } =
    [
        "system",
        "system state",
        "computer status",
        "machine status",
        "device status"
    ];

    public static IReadOnlyList<string> ComputerPhrases { get; } =
    [
        "computer",
        "computer context",
        "desktop context",
        "active window",
        "what app am i in",
        "what am i looking at",
        "what is happening on my computer",
        "whats happening on my computer",
        "what's happening on my computer",
        "what is going on on my computer",
        "whats going on on my computer",
        "what's going on on my computer"
    ];

    public static IReadOnlyList<string> AppsPhrases { get; } =
    [
        "apps",
        "running apps",
        "what apps are open",
        "what apps are running",
        "show open apps",
        "show running apps",
        "whats open",
        "what's open"
    ];

    public static IReadOnlyList<string> WindowsPhrases { get; } =
    [
        "windows",
        "list windows",
        "show windows",
        "visible windows",
        "what windows are open",
        "show open windows"
    ];

    public static IReadOnlyList<string> NetworkOverviewPhrases { get; } =
    [
        "network",
        "local network"
    ];

    public static IReadOnlyList<string> NetworkScanPhrases { get; } =
    [
        "network scan",
        "network devices",
        "scan my network",
        "devices on my network",
        "discover devices on my network"
    ];

    public static IReadOnlyList<string> NetworkHealthPhrases { get; } =
    [
        "network health",
        "check network health",
        "check my network"
    ];

    public static IReadOnlyList<string> ExitPhrases { get; } =
    [
        "exit",
        "quit",
        "shutdown",
        "close jarvis",
        "goodbye"
    ];

    public static IReadOnlyList<string> WriteDownPrefixes { get; } =
    [
        "write this down",
        "write that down",
        "write it down",
        "write down",
        "jot this down",
        "jot that down",
        "jot down"
    ];

    // "put the system to sleep", "put my laptop in sleep mode", "go to sleep", "sleep".
    [GeneratedRegex(@"^(?:(?:go|enter) (?:to )?)?sleep(?: mode)?$|^put (?:the |my |this )?(?:system|computer|laptop|pc|machine) (?:to sleep|in(?:to)? sleep(?: mode)?)$")]
    private static partial Regex SleepRegex();

    // "note ..." and "make a note ..." mean a Sticky Note; see TryCanonicalizeStickyNoteCommand.
    public static IReadOnlyList<string> RememberPrefixes { get; } =
    [
        "remember",
        "remember that"
    ];

    public static IReadOnlyList<string> RecallPrefixes { get; } =
    [
        "recall",
        "what do you remember about",
        "do you remember",
        "search memory for",
        "search memories for"
    ];

    public static IReadOnlyList<string> ReminderPrefixes { get; } =
    [
        "remind me to",
        "remind me about",
        "set reminder to",
        "set a reminder to"
    ];

    public static IReadOnlyList<string> RunPrefixes { get; } =
    [
        "run",
        "shell",
        "execute",
        "execute command",
        "run command"
    ];

    public static IReadOnlyList<string> OpenAppPrefixes { get; } =
    [
        "open app",
        "launch",
        "launch app",
        "start app",
        "start",
        "start up",
        "bring up",
        "pull up"
    ];

    public static IReadOnlyList<string> OpenPathPrefixes { get; } =
    [
        "open path",
        "open file",
        "open folder"
    ];

    public static IReadOnlyList<string> FocusAppPrefixes { get; } =
    [
        "focus app",
        "focus window",
        "switch to",
        "activate app",
        "bring to front"
    ];

    public static IReadOnlyList<string> CloseAppPrefixes { get; } =
    [
        "close app",
        "close window",
        "quit app"
    ];

    public static IReadOnlyList<string> WindowStatePrefixes { get; } =
    [
        "window state",
        "minimize app",
        "maximize app",
        "restore app",
        "minimize window",
        "maximize window",
        "restore window"
    ];

    public static IReadOnlyList<string> HotkeyPrefixes { get; } =
    [
        "send hotkey",
        "press key",
        "send shortcut",
        "shortcut"
    ];

    public static IReadOnlyList<string> WindowsPrefixes { get; } =
    [
        "windows",
        "list windows",
        "show windows",
        "visible windows"
    ];

    public static IReadOnlyList<string> MoveWindowPrefixes { get; } =
    [
        "move window",
        "move app",
        "position window"
    ];

    public static IReadOnlyList<string> ResizeWindowPrefixes { get; } =
    [
        "resize window",
        "resize app"
    ];

    public static IReadOnlyList<string> SnapWindowPrefixes { get; } =
    [
        "snap window",
        "snap app",
        "dock window"
    ];

    public static IReadOnlyList<string> ClipboardPrefixes { get; } =
    [
        "clipboard",
        "show clipboard",
        "get clipboard",
        "read clipboard",
        "clipboard show"
    ];

    public static IReadOnlyList<string> SetClipboardPrefixes { get; } =
    [
        "set clipboard",
        "copy to clipboard",
        "clipboard set"
    ];

    public static IReadOnlyList<string> ClearClipboardPrefixes { get; } =
    [
        "clear clipboard",
        "clipboard clear",
        "empty clipboard"
    ];

    public static IReadOnlyList<string> PasteClipboardPrefixes { get; } =
    [
        "paste clipboard into",
        "paste clipboard",
        "clipboard paste"
    ];

    public static IReadOnlyList<string> ListFilesPrefixes { get; } =
    [
        "list files",
        "show files",
        "list folder"
    ];

    public static IReadOnlyList<string> ReadFilePrefixes { get; } =
    [
        "read file",
        "show file",
        "preview file"
    ];

    public static IReadOnlyList<string> FindFilesPrefixes { get; } =
    [
        "find files",
        "find file",
        "search files"
    ];

    public static IReadOnlyList<string> WeatherPrefixes { get; } =
    [
        "weather in",
        "what is the weather in",
        "whats the weather in",
        "what's the weather in",
        "how is the weather in",
        "hows the weather in",
        "how's the weather in"
    ];

    public static IReadOnlyList<string> BrowsePrefixes { get; } =
    [
        "browse",
        "open website",
        "go to",
        "visit"
    ];

    public static IReadOnlyList<string> SearchWebPrefixes { get; } =
    [
        "search web",
        "web search"
    ];

    public static IReadOnlyList<string> ClickPrefixes { get; } =
    [
        "click element ",
        "click ",
        "tap "
    ];

    public static IReadOnlyList<string> TypePrefixes { get; } =
    [
        "type into ",
        "type ",
        "types into ",
        "types ",
        "enter into ",
        "enter ",
        "input into ",
        "input ",
        "write into ",
        "write "
    ];

    public static IReadOnlyList<string> GetTextPrefixes { get; } =
    [
        "get element text ",
        "read element ",
        "what text ",
        "element text "
    ];

    public static IReadOnlyList<string> ScrollPrefixes { get; } =
    [
        "scroll window ",
        "scroll ",
        "move down ",
        "move up "
    ];

    public static IReadOnlyList<string> WaitPrefixes { get; } =
    [
        "wait ",
        "pause ",
        "hold "
    ];

    public static string NormalizeToCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var trimmed = StripLeadingConversationalPrefix(input.Trim());
        var normalized = NormalizeForMatching(trimmed);

        if (ContainsExact(HelpPhrases, normalized))
        {
            return "help";
        }

        if (ContainsExact(StatusPhrases, normalized))
        {
            return "status";
        }

        if (ContainsExact(TimePhrases, normalized))
        {
            return "time";
        }

        if (ContainsExact(NotesPhrases, normalized))
        {
            return "notes";
        }

        if (ContainsExact(PreferencePhrases, normalized))
        {
            return "preferences";
        }

        if (ContainsExact(PrivacyPhrases, normalized))
        {
            return "privacy";
        }

        if (ContainsExact(ReminderPhrases, normalized))
        {
            return "reminders";
        }

        if (ContainsExact(WeatherPhrases, normalized))
        {
            return "weather";
        }

        if (ContainsExact(SystemPhrases, normalized))
        {
            return "system";
        }

        if (ContainsExact(ComputerPhrases, normalized))
        {
            return "computer";
        }

        if (ContainsExact(NetworkOverviewPhrases, normalized))
        {
            return "network";
        }

        if (ContainsExact(NetworkScanPhrases, normalized))
        {
            return "network scan";
        }

        if (ContainsExact(NetworkHealthPhrases, normalized))
        {
            return "network health";
        }

        if (ContainsExact(AppsPhrases, normalized))
        {
            return "apps";
        }

        if (ContainsExact(WindowsPhrases, normalized))
        {
            return "windows";
        }

        if (ContainsExact(ExitPhrases, normalized))
        {
            return "exit";
        }

        // Check sleep and dictation before natural-language routing, which accepts questions and commas.
        // Dictation also takes precedence over the "note..." memory prefixes.
        if (SleepRegex().IsMatch(normalized))
        {
            return "sleep";
        }

        // Before Sticky Notes, which would take "take notes on this lecture" as a note.
        if (TryCanonicalizeLectureNotesCommand(trimmed, out var lectureNotesCommand))
        {
            return lectureNotesCommand;
        }

        // Note-taking goes to Sticky Notes, even when Notepad is named ("open notepad and make a note to ...").
        // First, so "write this down in notepad" is a note rather than typing "this down" into Notepad.
        if (TryCanonicalizeStickyNoteCommand(trimmed, out var stickyNoteCommand))
        {
            return stickyNoteCommand;
        }

        if (TryCanonicalizePageCommand(trimmed, out var pageCommand))
        {
            return pageCommand;
        }

        // Before "write down" (a Sticky Note) and before " and " sends the request to the planner:
        // naming an app ("open notepad and write hello") means type it there.
        if (TryCanonicalizeWriteIntoAppCommand(trimmed, out var writeIntoAppCommand))
        {
            return writeIntoAppCommand;
        }

        // Also ahead of the natural-language check: these are usually questions ("what's on today?").
        if (TryCanonicalizeCalendarCommand(trimmed, out var calendarCommand))
        {
            return calendarCommand;
        }

        if (TryCanonicalizeVpnCommand(trimmed, out var vpnCommand))
        {
            return vpnCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, WriteDownPrefixes, "write down", out var writeDownCommand))
        {
            return writeDownCommand;
        }

        if (ShouldPreserveNaturalLanguage(trimmed))
        {
            return trimmed;
        }

        if (TryCanonicalizeOpenCommand(trimmed, out var openCommand))
        {
            return openCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, RememberPrefixes, "remember", out var rememberCommand))
        {
            return rememberCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, RecallPrefixes, "recall", out var recallCommand))
        {
            return recallCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, ReminderPrefixes, "reminders", out var reminderCommand))
        {
            return reminderCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, RunPrefixes, "run", out var runCommand))
        {
            return runCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, OpenAppPrefixes, "open app", out var openAppCommand))
        {
            return openAppCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, FocusAppPrefixes, "focus app", out var focusAppCommand))
        {
            return focusAppCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, CloseAppPrefixes, "close app", out var closeAppCommand))
        {
            return closeAppCommand;
        }

        if (TryCanonicalizeWindowStateCommand(trimmed, out var windowStateCommand))
        {
            return windowStateCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, HotkeyPrefixes, "send hotkey", out var hotkeyCommand))
        {
            return hotkeyCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, WindowsPrefixes, "windows", out var windowsCommand))
        {
            return windowsCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, MoveWindowPrefixes, "move window", out var moveWindowCommand))
        {
            return moveWindowCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, ResizeWindowPrefixes, "resize window", out var resizeWindowCommand))
        {
            return resizeWindowCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, SnapWindowPrefixes, "snap window", out var snapWindowCommand))
        {
            return snapWindowCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, OpenPathPrefixes, "open path", out var openPathCommand))
        {
            return openPathCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, ListFilesPrefixes, "list files", out var listFilesCommand))
        {
            return listFilesCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, ReadFilePrefixes, "read file", out var readFileCommand))
        {
            return readFileCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, FindFilesPrefixes, "find files", out var findFilesCommand))
        {
            return findFilesCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, WeatherPrefixes, "weather", out var weatherCommand))
        {
            return weatherCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, BrowsePrefixes, "browse", out var browseCommand))
        {
            return browseCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, SearchWebPrefixes, "search web", out var searchWebCommand))
        {
            return searchWebCommand;
        }

        if (TryCanonicalizeClickCommand(trimmed, out var clickCommand))
        {
            return clickCommand;
        }

        if (TryCanonicalizeTypeCommand(trimmed, out var typeCommand))
        {
            return typeCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, GetTextPrefixes, "get element text", out var getTextCommand))
        {
            return getTextCommand;
        }

        if (TryCanonicalizeScrollCommand(trimmed, out var scrollCommand))
        {
            return scrollCommand;
        }

        if (TryCanonicalizeMediaCommand(trimmed, out var mediaCommand))
        {
            return mediaCommand;
        }

        if (TryCanonicalizeClipboardCommand(trimmed, out var clipboardCommand))
        {
            return clipboardCommand;
        }

        if (TryCanonicalizePayloadCommand(trimmed, WaitPrefixes, "wait", out var waitCommand))
        {
            return waitCommand;
        }

        return trimmed;
    }

    public static string NormalizeVoiceDirective(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // Speech transcription adds sentence punctuation ("Open Spotify."). A trailing period would
        // otherwise make "Spotify." look like a web address. Question and exclamation marks are kept
        // because they signal natural language rather than a command.
        var trimmed = StripLeadingConversationalPrefix(input.Trim().TrimEnd('.', ',', ';', ':', ' '));

        // Speech transcription sometimes runs the verb into the name: "OpenDell", "OpenEA".
        trimmed = GluedOpenRegex().Replace(trimmed, "$1 ");

        // "Open Movies and TV" or "Open Node.js" are app names even though they contain words or dots
        // that would otherwise send them down the natural-language or file-path routes.
        if (TryCanonicalizeOpenKnownApplication(trimmed, out var knownAppCommand))
        {
            return knownAppCommand;
        }

        var normalizedCommand = NormalizeToCommand(trimmed);

        if (!string.Equals(normalizedCommand, trimmed, StringComparison.Ordinal))
        {
            return normalizedCommand;
        }

        return TryCanonicalizeImplicitOpenAppCommand(trimmed, out var command)
            ? command
            : trimmed;
    }

    /// <summary>
    /// Lets the command translator ask whether some text is the exact name of an installed app. Set once
    /// at startup by the desktop service. Null (the default) means only the built-in rules apply.
    /// </summary>
    public static Func<string, bool>? KnownApplicationNameCheck { get; set; }

    [GeneratedRegex("^([Oo]pen)(?=[A-Z0-9])")]
    private static partial Regex GluedOpenRegex();

    // "open (up) (my) notepad and (then) write/type (down/out) hello world"
    [GeneratedRegex(@"^open (?:up )?(?:my |the )?(?<app>[^,]+?),? and (?:then )?(?:write|type)(?: down| out)?(?: in it| into it)?[,:]? (?<text>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex OpenAppAndWriteRegex();

    // "write/type (down) hello world in/into/on (my) notepad"; the app is the last phrase, so a text
    // that itself says "in" ("meet in the lobby in notepad") still splits at the final one.
    [GeneratedRegex(@"^(?:write|type)(?: down| out)? (?<text>.+) (?:in|into|on) (?:my |the |a )?(?<app>[^,]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex WriteInAppRegex();

    // "can you make a quick note that ...", "take a note: ...", "jot this down ...", "note that ...",
    // "open my sticky notes and make a note to ...". The note's words follow in "rest".
    [GeneratedRegex(@"^(?:(?:hey|ok|okay|so|um|uh|alright|all\s+right)[,\s]+)*(?:(?:can|could|would|will)\s+you\s+(?:please\s+)?|i\s+(?:need|want)\s+(?:you\s+)?to\s+|please\s+|go\s+ahead\s+and\s+|let'?s\s+)?(?:open\s+(?:up\s+)?(?:my\s+|the\s+)?(?:sticky\s+notes?|notepad|notes?)\s+and\s+(?:then\s+)?)?(?:(?:make|take|write|jot|add|create|leave|start|drop)\s+(?:me\s+)?(?:a\s+|an\s+|another\s+|one\s+)?(?:quick\s+|new\s+|little\s+|short\s+)?(?:sticky\s+)?note\b(?:\s+to\s+self\b)?|(?:write|jot|put|note)\s+(?:(?:this|that|it)\s+)?down\b|note(?=\s+that\b|\s*:)|sticky\s+note\b)(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex StickyNoteRequestRegex();

    // "put milk and eggs on a sticky note", "add call mom to my sticky notes"
    [GeneratedRegex(@"^(?:(?:can|could|would|will)\s+you\s+(?:please\s+)?|please\s+)?(?:add|put|write)\s+(?<rest>.+?)\s+(?:to|on|in|onto|into)\s+(?:a\s+|my\s+|the\s+)?(?:new\s+)?sticky\s+notes?$", RegexOptions.IgnoreCase)]
    private static partial Regex OnStickyNoteRegex();

    // What comes between the request and the note itself: "in my sticky notes", "that says", "to remind me to".
    [GeneratedRegex(@"^[\s,:;-]*(?:(?:in|on|to|into|onto)\s+(?:my\s+|the\s+|a\s+)?(?:sticky\s+notes?|notepad|notes?(?:\s+app)?)\b)?[\s,:;-]*(?:(?:that\s+says|which\s+says|saying|to\s+say|to\s+remind\s+me(?:\s+to|\s+that)?|reminding\s+me(?:\s+to|\s+that)?|that|about|for\s+me|to|of)\b[\s,:;-]*)?", RegexOptions.IgnoreCase)]
    private static partial Regex NoteLeadInRegex();

    private static bool TryCanonicalizeStickyNoteCommand(string input, out string command)
    {
        command = string.Empty;

        // Possibly canonical already, so the lead-in is left alone (a note may start with "to" or "that");
        // only a trailing "in notepad" or "on my sticky notes" is dropped.
        if (input.StartsWith("write down ", StringComparison.OrdinalIgnoreCase))
        {
            var text = Regex.Replace(
                input["write down ".Length..].TrimEnd('.', '!', ' '),
                @"\s+(?:in|on|to|into|onto)\s+(?:my\s+|the\s+|a\s+)?(?:sticky\s+notes?|notepad|notes?(?:\s+app)?)$",
                string.Empty,
                RegexOptions.IgnoreCase).Trim();
            command = text.Length == 0 ? "write down" : $"write down {text}";
            return true;
        }

        var match = OnStickyNoteRegex().Match(input.TrimEnd('.', '!', ' '));

        if (!match.Success)
        {
            match = StickyNoteRequestRegex().Match(input);
        }

        if (!match.Success)
        {
            return false;
        }

        var note = NoteLeadInRegex().Replace(match.Groups["rest"].Value, string.Empty, 1).Trim();
        command = note.Length == 0 ? "write down" : $"write down {note}";
        return true;
    }

    private const string PageWords = @"(?:page|article|story|post|blog(?:\s+post)?|web\s*page|website|site|tab)";

    // "read this article to me", "read me the page", "read it out loud"
    [GeneratedRegex(@"^(?:(?:can|could|would|will)\s+you\s+(?:please\s+)?|please\s+)?read\s+(?:me\s+)?(?:(?:(?:this|the|that|current)\s+(?:whole\s+|full\s+)?" + PageWords + @"(?:\s+i'?m\s+on)?)|this|it|that)(?:\s+(?:to\s+me|for\s+me|out\s+loud|aloud))*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReadPageRegex();

    // "summarize this page", "give me a quick summary of the article", "tldr", "summarize the reviews on this page"
    [GeneratedRegex(@"^(?:(?:can|could|would|will)\s+you\s+(?:please\s+)?|please\s+)?(?:summari[sz]e|sum\s+up|recap|give\s+me\s+(?:a\s+|the\s+)?(?:quick\s+|short\s+|brief\s+)?(?:summary|rundown|gist|tl;?dr)(?:\s+of)?|tl;?dr|what'?s\s+the\s+(?:gist|tl;?dr|summary)(?:\s+of)?)(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SummarizePageRegex();

    // "what's this article about", "what does this page say about pricing"
    [GeneratedRegex(@"^(?:what'?s|what\s+is|whats)\s+(?:this|the)\s+" + PageWords + @"\s+(?:about|saying)$|^what\s+does\s+(?:this|the)\s+" + PageWords + @"\s+say(?:\s+about\s+(?<focus>.+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex PageQuestionRegex();

    [GeneratedRegex(@"(?:\s+(?:on|of|from|in|for))?\s*\b(?:this|the|that|current)\s+" + PageWords + @"\b(?:\s+i'?m\s+on)?", RegexOptions.IgnoreCase)]
    private static partial Regex PageReferenceRegex();

    private static bool TryCanonicalizePageCommand(string input, out string command)
    {
        command = string.Empty;
        var text = input.Trim().TrimEnd('?', '.', '!', ' ');

        if (ReadPageRegex().IsMatch(text))
        {
            command = "page read";
            return true;
        }

        var question = PageQuestionRegex().Match(text);

        if (question.Success)
        {
            command = $"page summarize {question.Groups["focus"].Value}".Trim();
            return true;
        }

        var summarize = SummarizePageRegex().Match(text);

        if (!summarize.Success)
        {
            return false;
        }

        var rest = summarize.Groups["rest"].Value;
        var namesPage = PageReferenceRegex().IsMatch(rest);
        var focus = PageReferenceRegex().Replace(rest, " ");
        focus = Regex.Replace(focus, @"^\s*(?:of|on|about|for\s+me)\b|\bfor\s+me\s*$", " ", RegexOptions.IgnoreCase).Trim();
        focus = Regex.IsMatch(focus, @"^(?:this|it|that)?$", RegexOptions.IgnoreCase) ? string.Empty : focus;

        // "summarize my notes" is not about a web page; with no page named, only a bare "summarize this" is.
        if (!namesPage && focus.Length > 0)
        {
            return false;
        }

        command = $"page summarize {focus}".Trim();
        return true;
    }

    // "what's on my calendar tomorrow", "read me my schedule", "how does my calendar look on friday"
    [GeneratedRegex(@"^(?:what(?:'s|s| is)|what do i have|do i have anything|read|tell|give|show|check|open|how(?:'s| does| is))\b.*\b(?:calendar|schedule|agenda)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CalendarQuestionRegex();

    // "what do I have tomorrow", "what's going on today": no calendar word, so the rest must be a day.
    [GeneratedRegex(@"^(?:what(?:'s|s| is)|what do i have|do i have anything|anything)\s+(?:(?:going\s+on|happening|planned|scheduled|on)\s+)?(?<when>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DayQuestionRegex();

    // "add dentist to my calendar tomorrow at 3", "schedule a meeting with Sam friday at 2"
    [GeneratedRegex(@"^(?:please\s+)?(?<verb>add|schedule|put|create|book|set\s+up)\s+(?<rest>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CalendarAddRegex();

    private static bool TryCanonicalizeCalendarCommand(string input, out string command)
    {
        command = string.Empty;
        var text = input.Trim().TrimEnd('?', '.', '!', ' ');
        var now = DateTime.Now;

        var add = CalendarAddRegex().Match(text);

        if (add.Success)
        {
            var rest = add.Groups["rest"].Value;

            // "add milk to the list" is not an event: it needs the calendar named, an event word, or "schedule".
            if (add.Groups["verb"].Value.Equals("schedule", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(rest, @"\bcalendar\b|^(?:an?\s+|the\s+)?(?:new\s+)?(?:event|appointment)\b", RegexOptions.IgnoreCase))
            {
                command = $"calendar add {rest}";
                return true;
            }

            return false;
        }

        if (CalendarQuestionRegex().IsMatch(text))
        {
            var day = CalendarText.FindDate(text, now) ?? DateOnly.FromDateTime(now);
            command = $"calendar {day:yyyy-MM-dd}";
            return true;
        }

        var dayQuestion = DayQuestionRegex().Match(text);

        if (dayQuestion.Success && CalendarText.TryParseDay(dayQuestion.Groups["when"].Value, now, out var askedDay))
        {
            command = $"calendar {askedDay:yyyy-MM-dd}";
            return true;
        }

        return false;
    }

    // "start lecture notes for IT Professions", "record my biology lecture", "take notes on this meeting".
    // Up to four words may name it before the kind ("my IT Professions lecture"), but no preposition, so
    // "record a reminder for class" isn't a recording.
    [GeneratedRegex(@"^(?:please\s+)?(?:start|begin|record|take)\s+(?:(?:taking|recording)\s+)?(?:notes\s+(?:on|for|in|during|of)\s+)?(?:(?:this|the|my|our|a|an|today'?s)\s+)?(?<before>(?:(?!(?:for|to|about|with|in|on|at|of)\b)[\w'&.-]+\s+){0,4}?)(?<kind>lecture|class|meeting)(?:\s+(?:notes|recording))?(?:\s+(?:for|in|on|called|named|about|of)\s+(?:(?:the|my|our)\s+)?(?<after>.+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex LectureNotesStartRegex();

    // "stop recording", "end the meeting notes", "stop taking notes", "class is over"
    [GeneratedRegex(@"^(?:please\s+)?(?:stop|end|finish|wrap\s+up)\s+(?:taking\s+)?(?:(?:the|my|this)\s+)?(?:(?:lecture|class|meeting)\s+)?(?:notes|recording)$|^(?:the\s+)?(?:lecture|class|meeting)\s+is\s+over$", RegexOptions.IgnoreCase)]
    private static partial Regex LectureNotesStopRegex();

    private static bool TryCanonicalizeLectureNotesCommand(string input, out string command)
    {
        command = string.Empty;
        var text = input.Trim().TrimEnd('?', '.', '!', ' ');

        if (LectureNotesStopRegex().IsMatch(text))
        {
            command = "lecture notes stop";
            return true;
        }

        var start = LectureNotesStartRegex().Match(text);

        // "start the meeting" or "take the class" asks for no recording.
        if (!start.Success || !Regex.IsMatch(text, @"\b(?:notes|record|recording)\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        var isMeeting = start.Groups["kind"].Value.Equals("meeting", StringComparison.OrdinalIgnoreCase);
        var name = (start.Groups["after"].Success ? start.Groups["after"].Value : start.Groups["before"].Value).Trim();

        if (name.Length == 0)
        {
            name = isMeeting ? "Meeting" : "Lecture";
        }
        else if (isMeeting && !name.Contains("meeting", StringComparison.OrdinalIgnoreCase))
        {
            name += " Meeting";
        }

        command = $"lecture notes start {name}";
        return true;
    }

    // "turn on my vpn", "disconnect the vpn", "is my vpn on?"
    private static bool TryCanonicalizeVpnCommand(string input, out string command)
    {
        command = string.Empty;
        var text = input.Trim().TrimEnd('?', '.', '!', ' ');

        // "what is a vpn" is a question for the planner, and "turn on the vpn and open chrome" is two requests.
        if (!Regex.IsMatch(text, @"\bvpn\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"\ba\s+vpn\b|\s+and\s+", RegexOptions.IgnoreCase))
        {
            return false;
        }

        // A question first: "is my vpn on" asks, it doesn't turn it on.
        command = Regex.IsMatch(text, @"^(?:is|am\s+i|are\s+(?:we|you)|check|what'?s|what\s+is|how'?s|how\s+is)\b", RegexOptions.IgnoreCase) ? "vpn status"
            : Regex.IsMatch(text, @"\b(?:off|disconnect|disable|stop|deactivate|kill)\b", RegexOptions.IgnoreCase) ? "vpn off"
            : Regex.IsMatch(text, @"\b(?:on|connect|enable|start|activate)\b", RegexOptions.IgnoreCase) ? "vpn on"
            : string.Empty;
        return command.Length > 0;
    }

    private static bool TryCanonicalizeWriteIntoAppCommand(string input, out string command)
    {
        command = string.Empty;
        var isKnownApplication = KnownApplicationNameCheck;

        // Already canonical; re-reading "type into notepad open it in chrome" would retarget it.
        if (isKnownApplication is null
            || input.StartsWith("type into ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("write into ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = OpenAppAndWriteRegex().Match(input);

        if (!match.Success)
        {
            match = WriteInAppRegex().Match(input);
        }

        if (!match.Success)
        {
            return false;
        }

        var app = match.Groups["app"].Value.Trim();
        var text = match.Groups["text"].Value.Trim().Trim('"');

        // Sticky Notes needs a new note opened before typing, which the write-down tool does.
        if (app.StartsWith("sticky note", StringComparison.OrdinalIgnoreCase) && text.Length > 0)
        {
            command = $"write down {text}";
            return true;
        }

        // Only an exact installed-app name counts; otherwise the words were not an app ("write it in pen").
        if (text.Length == 0 || app.Length > 40 || !isKnownApplication(app))
        {
            return false;
        }

        command = $"type into {(app.Contains(' ') ? $"\"{app}\"" : app)} {text}";
        return true;
    }

    private static bool TryCanonicalizeOpenKnownApplication(string input, out string command)
    {
        command = string.Empty;

        var isKnownApplication = KnownApplicationNameCheck;

        if (isKnownApplication is null || !input.StartsWith("open ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = StripTrailingPoliteness(StripLeadingFillerWords(input["open ".Length..].Trim())).Trim();

        if (name.Length == 0 || name.Length > 80 || !isKnownApplication(name))
        {
            return false;
        }

        command = $"open app {name}";
        return true;
    }

    private static bool ShouldPreserveNaturalLanguage(string value)
    {
        var trimmed = value.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var normalized = NormalizeForMatching(trimmed);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length >= 9)
        {
            return true;
        }

        var clauseSignals = new[]
        {
            " and ",
            " then ",
            " after ",
            " before ",
            " while ",
            " but ",
            " also ",
            " because ",
            " so that ",
            " if ",
            " once "
        };

        if (clauseSignals.Any(signal => normalized.Contains(signal, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return trimmed.IndexOfAny(['?', '!', ';']) >= 0;
    }

    private static bool TryCanonicalizeOpenCommand(string input, out string command)
    {
        if (!input.StartsWith("open ", StringComparison.OrdinalIgnoreCase))
        {
            command = string.Empty;
            return false;
        }

        var remainder = input["open ".Length..].TrimStart(' ', ',', ':', ';', '-');

        // Already canonical ("open path x.txt"): leave it for the open-path prefixes below. Otherwise
        // normalizing it a second time would produce "open path path x.txt".
        if (OpenPathPrefixes.Any(prefix => input.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)))
        {
            command = string.Empty;
            return false;
        }

        remainder = StripLeadingFillerWords(remainder);

        if (string.IsNullOrWhiteSpace(remainder))
        {
            command = "open";
            return true;
        }

        if (LooksLikeUrl(remainder))
        {
            command = $"browse {remainder}";
            return true;
        }

        if (LooksLikePath(remainder))
        {
            command = $"open path {remainder}";
            return true;
        }

        if (!LooksLikeSimpleLaunchTarget(remainder))
        {
            command = string.Empty;
            return false;
        }

        command = $"open app {remainder}";
        return true;
    }

    private static bool TryCanonicalizeClickCommand(string input, out string command)
    {
        return TryCanonicalizeSinglePayloadCommand(input, ClickPrefixes, "click element", out command);
    }

    private static bool TryCanonicalizeWindowStateCommand(string input, out string command)
    {
        if (TryCanonicalizeSinglePayloadCommand(input, ["window state"], "window state", out command))
        {
            return true;
        }

        if (TryCanonicalizeDirectionalWindowStateCommand(input, "minimize app", "minimize", out command)
            || TryCanonicalizeDirectionalWindowStateCommand(input, "minimize window", "minimize", out command)
            || TryCanonicalizeDirectionalWindowStateCommand(input, "maximize app", "maximize", out command)
            || TryCanonicalizeDirectionalWindowStateCommand(input, "maximize window", "maximize", out command)
            || TryCanonicalizeDirectionalWindowStateCommand(input, "restore app", "restore", out command)
            || TryCanonicalizeDirectionalWindowStateCommand(input, "restore window", "restore", out command))
        {
            return true;
        }

        command = string.Empty;
        return false;
    }

    private static bool TryCanonicalizeTypeCommand(string input, out string command)
    {
        if (TryCanonicalizeSinglePayloadCommand(
                input,
                ["type into ", "types into ", "enter into ", "input into ", "write into "],
                "type into",
                out command))
        {
            return true;
        }

        foreach (var prefix in new[] { "type ", "types ", "enter ", "input ", "write " })
        {
            if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = input[prefix.Length..].TrimStart(' ', ',', ':', ';', '-');

            if (string.IsNullOrWhiteSpace(remainder))
            {
                command = "type into";
                return true;
            }

            var separatorIndex = remainder.LastIndexOf(" into ", StringComparison.OrdinalIgnoreCase);

            // No target named ("write it in pen"): the first word would be taken as a window. Let the
            // planner read it instead; an app named with "in"/"on" was already handled above.
            if (separatorIndex <= 0)
            {
                command = string.Empty;
                return false;
            }

            var text = remainder[..separatorIndex].Trim();
            var target = remainder[(separatorIndex + " into ".Length)..].Trim();

            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(target))
            {
                command = string.Empty;
                return false;
            }

            command = $"type into {target} {text}";
            return true;
        }

        command = string.Empty;
        return false;
    }

    private static bool TryCanonicalizeMediaCommand(string input, out string command)
    {
        if (TryCanonicalizePayloadCommand(input, ["media"], "media", out command))
        {
            return true;
        }

        foreach (var (prefix, action) in new[]
                 {
                     ("play media", "play"),
                     ("pause media", "pause"),
                     ("resume media", "resume"),
                     ("toggle media", "toggle"),
                     ("stop media", "stop"),
                     ("next track", "next"),
                     ("previous track", "previous"),
                     ("prev track", "previous"),
                     ("skip track", "next"),
                     ("mute audio", "mute"),
                     ("toggle mute", "mute")
                 })
        {
            if (!StartsWithCommand(input, prefix))
            {
                continue;
            }

            command = $"media {action}";
            return true;
        }

        foreach (var prefix in new[] { "volume up", "volume down" })
        {
            if (!StartsWithCommand(input, prefix))
            {
                continue;
            }

            var remainder = input[prefix.Length..].TrimStart(' ', ',', ':', ';', '-');
            command = string.IsNullOrWhiteSpace(remainder)
                ? $"media {prefix}"
                : $"media {prefix} {remainder}";
            return true;
        }

        if (string.Equals(NormalizeForMatching(input), "mute", StringComparison.Ordinal))
        {
            command = "media mute";
            return true;
        }

        command = string.Empty;
        return false;
    }

    private static bool TryCanonicalizeClipboardCommand(string input, out string command)
    {
        if (TryCanonicalizePayloadCommand(input, SetClipboardPrefixes, "clipboard set", out command))
        {
            return true;
        }

        if (TryCanonicalizePayloadCommand(input, PasteClipboardPrefixes, "clipboard paste", out command))
        {
            if (command.StartsWith("clipboard paste into ", StringComparison.OrdinalIgnoreCase))
            {
                command = "clipboard paste " + command["clipboard paste into ".Length..].TrimStart();
            }

            return true;
        }

        if (ContainsExact(ClearClipboardPrefixes, NormalizeForMatching(input)))
        {
            command = "clipboard clear";
            return true;
        }

        if (ContainsExact(ClipboardPrefixes, NormalizeForMatching(input)))
        {
            command = "clipboard";
            return true;
        }

        command = string.Empty;
        return false;
    }

    private static bool TryCanonicalizeScrollCommand(string input, out string command)
    {
        if (TryCanonicalizeSinglePayloadCommand(input, ["scroll window ", "scroll "], "scroll", out command))
        {
            return true;
        }

        if (TryCanonicalizeDirectionalPayloadCommand(input, "move down ", "down", out command))
        {
            return true;
        }

        if (TryCanonicalizeDirectionalPayloadCommand(input, "move up ", "up", out command))
        {
            return true;
        }

        command = string.Empty;
        return false;
    }

    private static bool TryCanonicalizeSinglePayloadCommand(
        string input,
        IReadOnlyList<string> prefixes,
        string canonicalVerb,
        out string command)
    {
        foreach (var prefix in prefixes)
        {
            if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = input[prefix.Length..].TrimStart(' ', ',', ':', ';', '-');
            command = string.IsNullOrWhiteSpace(remainder)
                ? canonicalVerb
                : $"{canonicalVerb} {remainder}";
            return true;
        }

        command = string.Empty;
        return false;
    }

    private static bool TryCanonicalizeDirectionalPayloadCommand(
        string input,
        string prefix,
        string direction,
        out string command)
    {
        if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            command = string.Empty;
            return false;
        }

        var remainder = input[prefix.Length..].TrimStart(' ', ',', ':', ';', '-');

        if (string.IsNullOrWhiteSpace(remainder))
        {
            command = string.Empty;
            return false;
        }

        command = $"scroll {remainder} {direction}";
        return true;
    }

    private static bool TryCanonicalizeDirectionalWindowStateCommand(
        string input,
        string prefix,
        string state,
        out string command)
    {
        if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            command = string.Empty;
            return false;
        }

        var remainder = input[prefix.Length..].TrimStart(' ', ',', ':', ';', '-');

        if (string.IsNullOrWhiteSpace(remainder))
        {
            command = string.Empty;
            return false;
        }

        command = $"window state {remainder} {state}";
        return true;
    }

    private static bool TryCanonicalizePayloadCommand(
        string input,
        IReadOnlyList<string> prefixes,
        string canonicalVerb,
        out string command)
    {
        foreach (var prefix in prefixes)
        {
            if (!input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = input[prefix.Length..].TrimStart(' ', ',', ':', ';', '-');

            if (string.Equals(canonicalVerb, "open app", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(remainder)
                && !LooksLikeSimpleLaunchTarget(remainder))
            {
                command = string.Empty;
                return false;
            }

            command = string.IsNullOrWhiteSpace(remainder)
                ? canonicalVerb
                : $"{canonicalVerb} {remainder}";
            return true;
        }

        command = string.Empty;
        return false;
    }

    private static bool TryCanonicalizeImplicitOpenAppCommand(string input, out string command)
    {
        command = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var candidate = StripLeadingFillerWords(StripTrailingPoliteness(input.Trim()));

        if (string.IsNullOrWhiteSpace(candidate)
            || LooksLikeUrl(candidate)
            || LooksLikePath(candidate)
            || candidate.Length > 64)
        {
            return false;
        }

        // A sentence break means more than a name was said ("notepad. There we go"): let the planner read it.
        if (candidate.IndexOfAny(['?', '!', ':', ';']) >= 0 || candidate.Contains(". ", StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = NormalizeForMatching(candidate);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0 || words.Length > 5)
        {
            return false;
        }

        var blockedLeadingWords = new[]
        {
            "analyze",
            "app",
            "apps",
            "browse",
            "can",
            "computer",
            "could",
            "email",
            "exit",
            "find",
            "go",
            "help",
            "how",
            "launch",
            "list",
            "open",
            "please",
            "read",
            "recall",
            "remember",
            "research",
            "run",
            "screen",
            "search",
            "show",
            "start",
            "system",
            "task",
            "tasks",
            "tell",
            "time",
            "weather",
            "what",
            "when",
            "where",
            "which",
            "who",
            "why",
            "would",
            "will"
        };

        if (blockedLeadingWords.Contains(words[0], StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // Without this, any short phrase became "open app <phrase>": "Close Notepad" or "I'm glad it works".
        if (KnownApplicationNameCheck is { } isKnownApplication && !isKnownApplication(candidate))
        {
            return false;
        }

        command = $"open app {candidate}";
        return true;
    }

    private static bool LooksLikeSimpleLaunchTarget(string value)
    {
        var candidate = StripTrailingPoliteness(value.Trim());

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        // A sentence break means more than a name was said ("notepad. There we go"): let the planner read it.
        if (candidate.Length > 64
            || candidate.IndexOfAny(['?', '!', ':', ';']) >= 0
            || candidate.Contains(". ", StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = NormalizeForMatching(candidate);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0 || words.Length > 5)
        {
            return false;
        }

        var followOnSignals = new[]
        {
            "and",
            "then",
            "after",
            "before",
            "while",
            "but",
            "also",
            "because",
            "if",
            "once",
            "search",
            "find",
            "tell",
            "show",
            "check",
            "read",
            "write",
            "type",
            "click",
            "scroll",
            "play",
            "pause",
            "what",
            "when",
            "where",
            "why",
            "how",
            "who",
            "whether",
            "that"
        };

        return !words.Any(word => followOnSignals.Contains(word, StringComparer.OrdinalIgnoreCase));
    }

    private static bool ContainsExact(IReadOnlyList<string> phrases, string normalizedInput)
    {
        return phrases.Any(phrase => NormalizeForMatching(phrase) == normalizedInput);
    }

    private static bool StartsWithCommand(string input, string value)
    {
        return input.Equals(value, StringComparison.OrdinalIgnoreCase)
            || input.StartsWith(value + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripLeadingFillerWords(string value)
    {
        var trimmed = value.Trim();

        foreach (var prefix in new[] { "my ", "the ", "up ", "the app ", "app ", "application " })
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].TrimStart();
            }
        }

        return trimmed;
    }

    private static string StripTrailingPoliteness(string value)
    {
        var trimmed = value.Trim();

        while (true)
        {
            var changed = false;

            foreach (var suffix in new[] { " please", " thanks", " thank you", " for me" })
            {
                if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                trimmed = trimmed[..^suffix.Length].TrimEnd(' ', ',', ':', ';', '-');
                changed = true;
                break;
            }

            if (!changed)
            {
                return trimmed;
            }
        }
    }

    private static string StripLeadingConversationalPrefix(string value)
    {
        var current = value.Trim();
        var prefixes = new[]
        {
            "please ",
            "please, ",
            "can you please ",
            "can you ",
            "could you please ",
            "could you ",
            "would you please ",
            "would you ",
            "will you ",
            "i want you to ",
            "i need you to ",
            "help me ",
            "jarvis please ",
            "jarvis, please ",
            "jarvis can you ",
            "jarvis, can you "
        };

        while (true)
        {
            var changed = false;

            foreach (var prefix in prefixes)
            {
                if (!current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                current = current[prefix.Length..].TrimStart(' ', ',', ':', ';', '-');
                changed = true;
                break;
            }

            if (!changed)
            {
                return current;
            }
        }
    }

    private static bool LooksLikeUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || (value.Contains('.') && !value.Contains(' ') && !LooksLikePath(value));
    }

    private static bool LooksLikePath(string value)
    {
        return value.Contains('\\')
            || value.Contains('/')
            || value.StartsWith("~", StringComparison.Ordinal)
            || value.StartsWith("%", StringComparison.Ordinal)
            || value.StartsWith(".", StringComparison.Ordinal)
            || value.Contains(":\\", StringComparison.Ordinal)
            // A real extension has no spaces; "notepad. There we go" is two spoken sentences, not a file.
            || (Path.HasExtension(value) && !Path.GetExtension(value).Any(char.IsWhiteSpace))
            || value.Equals("desktop", StringComparison.OrdinalIgnoreCase)
            || value.Equals("documents", StringComparison.OrdinalIgnoreCase)
            || value.Equals("downloads", StringComparison.OrdinalIgnoreCase)
            || value.Equals("pictures", StringComparison.OrdinalIgnoreCase)
            || value.Equals("music", StringComparison.OrdinalIgnoreCase)
            || value.Equals("videos", StringComparison.OrdinalIgnoreCase)
            || value.Equals("workspace", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForMatching(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray();

        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
