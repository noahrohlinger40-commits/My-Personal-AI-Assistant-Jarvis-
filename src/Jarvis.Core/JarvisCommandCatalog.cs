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

    public static IReadOnlyList<string> RememberPrefixes { get; } =
    [
        "remember",
        "remember that",
        "note",
        "note that",
        "take note",
        "make a note"
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

            if (separatorIndex <= 0)
            {
                command = $"type into {remainder}";
                return true;
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

        if (candidate.IndexOfAny(['?', '!', ':', ';']) >= 0)
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

        if (candidate.Length > 64 || candidate.IndexOfAny(['?', '!', ':', ';']) >= 0)
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
            || Path.HasExtension(value)
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
