using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.Core;

internal static partial class Win32UiAutomation
{
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint WM_VSCROLL = 0x0115;
    private const uint BM_CLICK = 0x00F5;
    private const uint SB_LINEDOWN = 1;
    private const uint SB_LINEUP = 0;
    private const int SwShow = 5;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const int SwMaximize = 3;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;

    private static readonly IReadOnlyDictionary<string, ParsedKeyDefinition> NamedKeys = BuildNamedKeys();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageText(IntPtr hWnd, uint msg, int nMaxCount, StringBuilder lpString);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, ref RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static Task<string> FocusWindowAsync(string targetApp)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return Task.FromResult($"Window matching '{targetApp}' was not found.");
        }

        return Task.FromResult(
            TryBringWindowToFront(window.Handle)
                ? $"Focused '{DescribeWindow(window)}'."
                : $"Unable to focus '{DescribeWindow(window)}'.");
    }

    public static Task<string> CloseWindowAsync(string targetApp)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return Task.FromResult($"Window matching '{targetApp}' was not found.");
        }

        _ = TryBringWindowToFront(window.Handle);
        var closed = PostMessage(window.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

        return Task.FromResult(
            closed
                ? $"Sent a close request to '{DescribeWindow(window)}'."
                : $"Unable to close '{DescribeWindow(window)}'.");
    }

    public static Task<string> SetWindowStateAsync(string targetApp, string state)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return Task.FromResult($"Window matching '{targetApp}' was not found.");
        }

        if (!TryResolveWindowState(state, out var command, out var description))
        {
            return Task.FromResult("Usage: window state <window> <minimize|maximize|restore>");
        }

        _ = ShowWindowAsync(window.Handle, command);

        if (command is SwRestore or SwMaximize)
        {
            _ = TryBringWindowToFront(window.Handle);
        }

        return Task.FromResult($"{description} '{DescribeWindow(window)}'.");
    }

    public static Task<string> ClickElementAsync(string targetApp, string elementName)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return Task.FromResult($"Window matching '{targetApp}' was not found.");
        }

        _ = TryBringWindowToFront(window.Handle);

        var targetHandle = window.Handle;
        var targetDescription = DescribeWindow(window);

        if (!string.IsNullOrWhiteSpace(elementName))
        {
            if (!TryResolveElement(window.Handle, elementName, out var element))
            {
                return Task.FromResult($"Element matching '{elementName.Trim()}' was not found in '{DescribeWindow(window)}'.");
            }

            targetHandle = element.Handle;
            targetDescription = DescribeElement(element);

            if (element.IsButtonLike)
            {
                _ = SendMessage(targetHandle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                return Task.FromResult($"Clicked '{targetDescription}' in '{DescribeWindow(window)}'.");
            }
        }

        var rect = new RECT();
        _ = GetClientRect(targetHandle, ref rect);
        var point = new POINT { X = rect.Right / 2, Y = rect.Bottom / 2 };

        _ = SendMessage(targetHandle, WM_LBUTTONDOWN, IntPtr.Zero, EncodePoint(point));
        _ = SendMessage(targetHandle, WM_LBUTTONUP, IntPtr.Zero, EncodePoint(point));

        return Task.FromResult(
            string.IsNullOrWhiteSpace(elementName)
                ? $"Clicked the center of '{DescribeWindow(window)}'."
                : $"Clicked '{targetDescription}' in '{DescribeWindow(window)}'.");
    }

    public static Task<string> TypeIntoElementAsync(string targetApp, string elementName, string text)
    {
        // Without a named field, type at the cursor like a keyboard. WM_SETTEXT below replaces the
        // control's entire contents (a whole Notepad document), and when it finds no edit control it
        // renames the window instead, reporting success either way.
        if (string.IsNullOrWhiteSpace(elementName))
        {
            return SendTextAsync(targetApp, text);
        }

        if (!TryResolveWindow(targetApp, out var window))
        {
            return Task.FromResult($"Window matching '{targetApp}' was not found.");
        }

        _ = TryBringWindowToFront(window.Handle);

        if (!TryResolveElement(window.Handle, elementName, out var element))
        {
            return Task.FromResult($"Element matching '{elementName.Trim()}' was not found in '{DescribeWindow(window)}'.");
        }

        var targetHandle = element.Handle;
        var targetDescription = DescribeElement(element);
        var textHandle = Marshal.StringToHGlobalUni(text);

        try
        {
            _ = SendMessage(targetHandle, WM_SETTEXT, IntPtr.Zero, textHandle);
        }
        finally
        {
            Marshal.FreeHGlobal(textHandle);
        }

        return Task.FromResult($"Typed {DescribeTextLength(text)} into '{targetDescription}' in '{DescribeWindow(window)}'.");
    }

    public static Task<string> GetElementTextAsync(string targetApp, string elementName)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return Task.FromResult($"Window matching '{targetApp}' was not found.");
        }

        var targetHandle = FindBestTextHandle(window.Handle);
        var targetDescription = DescribeWindow(window);

        if (!string.IsNullOrWhiteSpace(elementName))
        {
            if (!TryResolveElement(window.Handle, elementName, out var element))
            {
                return Task.FromResult($"Element matching '{elementName.Trim()}' was not found in '{DescribeWindow(window)}'.");
            }

            targetHandle = element.Handle;
            targetDescription = DescribeElement(element);
        }

        var length = (int)SendMessage(targetHandle, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);

        if (length == 0)
        {
            return Task.FromResult(
                string.IsNullOrWhiteSpace(elementName)
                    ? $"No text detected in '{DescribeWindow(window)}'."
                    : $"No text detected in '{targetDescription}' in '{DescribeWindow(window)}'.");
        }

        var builder = new StringBuilder(length + 1);
        _ = SendMessageText(targetHandle, WM_GETTEXT, builder.Capacity, builder);
        return Task.FromResult(builder.ToString());
    }

    public static Task<string> ScrollWindowAsync(string targetApp, string direction)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return Task.FromResult($"Window matching '{targetApp}' was not found.");
        }

        _ = TryBringWindowToFront(window.Handle);
        var targetHandle = FindBestTextHandle(window.Handle);
        var scrollCode = string.Equals(direction, "down", StringComparison.OrdinalIgnoreCase)
            ? SB_LINEDOWN
            : SB_LINEUP;

        _ = SendMessage(targetHandle, WM_VSCROLL, (IntPtr)scrollCode, IntPtr.Zero);
        return Task.FromResult($"Scrolled '{DescribeWindow(window)}' {direction}.");
    }

    public static async Task<string> SendHotkeyAsync(string targetApp, string hotkey)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return $"Window matching '{targetApp}' was not found.";
        }

        if (!TryParseHotkey(hotkey, out var parsedHotkey, out var parseError))
        {
            return parseError;
        }

        if (!TryBringWindowToFront(window.Handle))
        {
            return $"Unable to focus '{DescribeWindow(window)}' before sending the hotkey.";
        }

        await Task.Delay(120).ConfigureAwait(false);

        if (!TrySendHotkey(parsedHotkey, out var sendError))
        {
            return sendError;
        }

        return $"Sent {parsedHotkey.DisplayText} to '{DescribeWindow(window)}'.";
    }

    public static async Task<string> WaitAsync(double seconds, CancellationToken cancellationToken)
    {
        await Task.Delay((int)(seconds * 1000), cancellationToken);
        return $"Waited {seconds}s";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // MOUSEINPUT is the union's largest member. Without it INPUT is 32 bytes instead of 40 on 64-bit
    // Windows, and SendInput rejects every call because cbSize is wrong.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly record struct WindowMatch(IntPtr Handle, string Title, string ProcessName, RECT Bounds, bool IsForeground);
    private readonly record struct ElementMatch(IntPtr Handle, string ClassName, string Text, bool IsButtonLike);

    private readonly record struct ParsedKeyDefinition(ushort VirtualKey, bool IsModifier, string DisplayText);

    private sealed record ParsedHotkey(
        IReadOnlyList<ParsedKeyDefinition> Modifiers,
        ParsedKeyDefinition Key,
        string DisplayText);

    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    private static bool TryResolveWindow(string target, out WindowMatch window)
    {
        window = default;

        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var normalizedTarget = NormalizeForMatching(target);
        var bestScore = 0;

        foreach (var candidate in EnumerateTopLevelWindows())
        {
            var score = ScoreWindow(target, normalizedTarget, candidate.Title, candidate.ProcessName);

            if (score <= bestScore)
            {
                continue;
            }

            window = candidate;
            bestScore = score;
        }

        return bestScore > 0;
    }

    private static int ScoreWindow(string rawTarget, string normalizedTarget, string title, string processName)
    {
        var normalizedTitle = NormalizeForMatching(title);
        var normalizedProcessName = NormalizeForMatching(processName);
        var score = 0;

        if (string.Equals(title, rawTarget, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 120);
        }

        if (string.Equals(processName, rawTarget, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 110);
        }

        if (string.Equals(normalizedTitle, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 100);
        }

        if (string.Equals(normalizedProcessName, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 95);
        }

        if (!string.IsNullOrWhiteSpace(normalizedTitle)
            && normalizedTitle.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 80);
        }

        if (!string.IsNullOrWhiteSpace(normalizedProcessName)
            && normalizedProcessName.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 72);
        }

        return score;
    }

    private static IntPtr FindBestTextHandle(IntPtr rootHandle)
    {
        var bestHandle = rootHandle;
        var bestScore = 0;

        _ = EnumChildWindows(
            rootHandle,
            (childHandle, _) =>
            {
                var className = ReadClassName(childHandle);
                var text = ReadWindowText(childHandle);
                var score = ScoreChildHandle(className, text);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestHandle = childHandle;
                }

                return true;
            },
            IntPtr.Zero);

        return bestHandle;
    }

    private static bool TryResolveElement(IntPtr rootHandle, string elementName, out ElementMatch element)
    {
        element = default;

        if (string.IsNullOrWhiteSpace(elementName))
        {
            return false;
        }

        var rawTarget = elementName.Trim();
        var normalizedTarget = NormalizeForMatching(rawTarget);
        var bestScore = 0;
        var bestElement = default(ElementMatch);

        _ = EnumChildWindows(
            rootHandle,
            (childHandle, _) =>
            {
                var className = ReadClassName(childHandle);
                var text = ReadWindowText(childHandle);
                var score = ScoreElementHandle(rawTarget, normalizedTarget, className, text);

                if (score <= bestScore)
                {
                    return true;
                }

                bestElement = new ElementMatch(
                    childHandle,
                    className,
                    text,
                    IsButtonLikeClass(className));
                bestScore = score;
                return true;
            },
            IntPtr.Zero);

        element = bestElement;
        return bestScore > 0;
    }

    private static int ScoreChildHandle(string className, string text)
    {
        var normalizedClassName = NormalizeForMatching(className);
        var score = 0;

        if (normalizedClassName.Contains("edit", StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 100);
        }

        if (normalizedClassName.Contains("text", StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 70);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            score = Math.Max(score, 40);
        }

        return score;
    }

    private static int ScoreElementHandle(string rawTarget, string normalizedTarget, string className, string text)
    {
        var normalizedText = NormalizeForMatching(text);
        var normalizedClassName = NormalizeForMatching(className);
        var score = 0;

        if (!string.IsNullOrWhiteSpace(text))
        {
            if (string.Equals(text, rawTarget, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 160);
            }

            if (string.Equals(normalizedText, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 150);
            }

            if (!string.IsNullOrWhiteSpace(normalizedTarget)
                && normalizedText.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 120);
            }
        }

        var roleHint = ResolveElementRoleHint(normalizedTarget);

        if (roleHint == "button" && IsButtonLikeClass(className))
        {
            score = Math.Max(score, 100);
        }
        else if (roleHint == "field" && IsEditableClass(className))
        {
            score = Math.Max(score, 100);
        }
        else if (roleHint == "text" && IsTextDisplayClass(className))
        {
            score = Math.Max(score, 90);
        }

        if (!string.IsNullOrWhiteSpace(normalizedText)
            && !string.IsNullOrWhiteSpace(normalizedTarget))
        {
            var targetTokens = normalizedTarget.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var matchingTokens = targetTokens.Count(token =>
                token.Length >= 3
                && normalizedText.Contains(token, StringComparison.OrdinalIgnoreCase));

            score = Math.Max(score, matchingTokens * 15);
        }

        if (!string.IsNullOrWhiteSpace(normalizedClassName)
            && !string.IsNullOrWhiteSpace(normalizedTarget)
            && normalizedClassName.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            score = Math.Max(score, 70);
        }

        return score;
    }

    private static bool TryBringWindowToFront(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            _ = ShowWindowAsync(handle, IsIconic(handle) ? SwRestore : SwShow);
            return SetForegroundWindow(handle);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveWindowState(string state, out int command, out string description)
    {
        var normalized = NormalizeForMatching(state);

        switch (normalized)
        {
            case "minimize":
            case "minimized":
                command = SwMinimize;
                description = "Minimized";
                return true;
            case "maximize":
            case "maximized":
                command = SwMaximize;
                description = "Maximized";
                return true;
            case "restore":
            case "restored":
                command = SwRestore;
                description = "Restored";
                return true;
            default:
                command = 0;
                description = string.Empty;
                return false;
        }
    }

    private static bool TryParseHotkey(string hotkey, out ParsedHotkey parsedHotkey, out string error)
    {
        parsedHotkey = null!;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(hotkey))
        {
            error = "Usage: send hotkey <window> <key-combo>";
            return false;
        }

        var tokens = NormalizeHotkey(hotkey)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            error = "Usage: send hotkey <window> <key-combo>";
            return false;
        }

        var resolvedKeys = new List<ParsedKeyDefinition>();

        foreach (var token in tokens)
        {
            if (!TryResolveKey(token, out var key))
            {
                error = $"Unsupported hotkey token '{token}'.";
                return false;
            }

            resolvedKeys.Add(key);
        }

        var modifiers = resolvedKeys
            .Where(key => key.IsModifier)
            .DistinctBy(key => key.VirtualKey)
            .ToArray();
        var nonModifiers = resolvedKeys
            .Where(key => !key.IsModifier)
            .ToArray();

        if (nonModifiers.Length == 0)
        {
            error = "A hotkey must include at least one non-modifier key.";
            return false;
        }

        if (nonModifiers.Length > 1)
        {
            error = "Only single-key shortcuts are supported here. Chain multiple key presses with a workflow such as `focus app ...; send hotkey ...; wait 0.2; send hotkey ...`.";
            return false;
        }

        var displayParts = modifiers
            .Select(key => key.DisplayText)
            .Concat([nonModifiers[0].DisplayText]);

        parsedHotkey = new ParsedHotkey(modifiers, nonModifiers[0], string.Join('+', displayParts));
        return true;
    }

    private static bool TryResolveKey(string token, out ParsedKeyDefinition key)
    {
        if (NamedKeys.TryGetValue(token, out key))
        {
            return true;
        }

        if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
        {
            var character = char.ToUpperInvariant(token[0]);
            key = new ParsedKeyDefinition(character, false, character.ToString());
            return true;
        }

        key = default;
        return false;
    }

    private static bool TrySendHotkey(ParsedHotkey hotkey, out string error)
    {
        var inputs = new List<INPUT>();

        foreach (var modifier in hotkey.Modifiers)
        {
            inputs.Add(CreateKeyboardInput(modifier.VirtualKey, keyUp: false));
        }

        inputs.Add(CreateKeyboardInput(hotkey.Key.VirtualKey, keyUp: false));
        inputs.Add(CreateKeyboardInput(hotkey.Key.VirtualKey, keyUp: true));

        foreach (var modifier in hotkey.Modifiers.Reverse())
        {
            inputs.Add(CreateKeyboardInput(modifier.VirtualKey, keyUp: true));
        }

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());

        if (sent != inputs.Count)
        {
            error = $"Windows only dispatched {sent} of {inputs.Count} keyboard events for {hotkey.DisplayText}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static INPUT CreateKeyboardInput(ushort virtualKey, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? KeyeventfKeyup : 0
                }
            }
        };
    }

    private static IReadOnlyDictionary<string, ParsedKeyDefinition> BuildNamedKeys()
    {
        var keys = new Dictionary<string, ParsedKeyDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = new(0x11, true, "Ctrl"),
            ["shift"] = new(0x10, true, "Shift"),
            ["alt"] = new(0x12, true, "Alt"),
            ["win"] = new(0x5B, true, "Win"),
            ["tab"] = new(0x09, false, "Tab"),
            ["enter"] = new(0x0D, false, "Enter"),
            ["esc"] = new(0x1B, false, "Esc"),
            ["space"] = new(0x20, false, "Space"),
            ["backspace"] = new(0x08, false, "Backspace"),
            ["delete"] = new(0x2E, false, "Delete"),
            ["del"] = new(0x2E, false, "Delete"),
            ["insert"] = new(0x2D, false, "Insert"),
            ["home"] = new(0x24, false, "Home"),
            ["end"] = new(0x23, false, "End"),
            ["pageup"] = new(0x21, false, "PageUp"),
            ["pagedown"] = new(0x22, false, "PageDown"),
            ["up"] = new(0x26, false, "Up"),
            ["down"] = new(0x28, false, "Down"),
            ["left"] = new(0x25, false, "Left"),
            ["right"] = new(0x27, false, "Right")
        };

        for (var index = 1; index <= 24; index++)
        {
            keys[$"f{index}"] = new((ushort)(0x70 + index - 1), false, $"F{index}");
        }

        return keys;
    }

    private static string NormalizeHotkey(string hotkey)
    {
        var normalized = hotkey.Trim().ToLowerInvariant();

        foreach (var replacement in HotkeyReplacements)
        {
            normalized = normalized.Replace(replacement.From, replacement.To, StringComparison.Ordinal);
        }

        normalized = normalized.Replace('+', ' ');
        normalized = normalized.Replace('-', ' ');

        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static readonly (string From, string To)[] HotkeyReplacements =
    [
        ("control", "ctrl"),
        ("windows", "win"),
        ("command", "win"),
        ("option", "alt"),
        ("return", "enter"),
        ("escape", "esc"),
        ("page up", "pageup"),
        ("page down", "pagedown"),
        ("arrow up", "up"),
        ("arrow down", "down"),
        ("arrow left", "left"),
        ("arrow right", "right")
    ];

    private static string ReadClassName(IntPtr handle)
    {
        var builder = new StringBuilder(128);
        _ = GetClassName(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string ReadWindowText(IntPtr handle)
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

    private static string ResolveElementRoleHint(string normalizedTarget)
    {
        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return string.Empty;
        }

        if (normalizedTarget.Contains("button", StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.Contains("tab", StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.Contains("menu", StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.Contains("link", StringComparison.OrdinalIgnoreCase))
        {
            return "button";
        }

        if (normalizedTarget.Contains("field", StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.Contains("input", StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.Contains("textbox", StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.Contains("text box", StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.Contains("search", StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.Contains("edit", StringComparison.OrdinalIgnoreCase))
        {
            return "field";
        }

        if (normalizedTarget.Contains("label", StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.Contains("text", StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.Contains("caption", StringComparison.OrdinalIgnoreCase))
        {
            return "text";
        }

        return string.Empty;
    }

    private static bool IsButtonLikeClass(string className)
    {
        var normalizedClassName = NormalizeForMatching(className);
        return normalizedClassName.Contains("button", StringComparison.OrdinalIgnoreCase)
            || normalizedClassName.Contains("tab", StringComparison.OrdinalIgnoreCase)
            || normalizedClassName.Contains("menu", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEditableClass(string className)
    {
        var normalizedClassName = NormalizeForMatching(className);
        return normalizedClassName.Contains("edit", StringComparison.OrdinalIgnoreCase)
            || normalizedClassName.Contains("combo", StringComparison.OrdinalIgnoreCase)
            || normalizedClassName.Contains("rich", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextDisplayClass(string className)
    {
        var normalizedClassName = NormalizeForMatching(className);
        return normalizedClassName.Contains("static", StringComparison.OrdinalIgnoreCase)
            || normalizedClassName.Contains("text", StringComparison.OrdinalIgnoreCase)
            || IsEditableClass(className);
    }

    private static string DescribeElement(ElementMatch element)
    {
        if (!string.IsNullOrWhiteSpace(element.Text))
        {
            return element.Text;
        }

        return string.IsNullOrWhiteSpace(element.ClassName)
            ? "unnamed control"
            : element.ClassName;
    }

    private static string DescribeWindow(WindowMatch window)
    {
        return string.IsNullOrWhiteSpace(window.Title)
            ? window.ProcessName
            : window.Title;
    }

    private static string NormalizeForMatching(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray();

        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string DescribeTextLength(string text)
    {
        return text.Length == 1
            ? "1 character"
            : $"{text.Length} characters";
    }

    private static IntPtr EncodePoint(POINT point)
    {
        var x = point.X & 0xFFFF;
        var y = (point.Y & 0xFFFF) << 16;
        return (IntPtr)(x | y);
    }
}
