using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.Core;

internal static partial class Win32UiAutomation
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int ClipboardPreviewLimit = 4000;
    private const ushort VkVolumeMute = 0xAD;
    private const ushort VkVolumeDown = 0xAE;
    private const ushort VkVolumeUp = 0xAF;
    private const ushort VkMediaNextTrack = 0xB0;
    private const ushort VkMediaPrevTrack = 0xB1;
    private const ushort VkMediaStop = 0xB2;
    private const ushort VkMediaPlayPause = 0xB3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static Task<string> ListWindowsAsync(string target)
    {
        var windows = EnumerateTopLevelWindows();
        IEnumerable<WindowMatch> filtered = windows;

        if (!string.IsNullOrWhiteSpace(target))
        {
            var normalizedTarget = NormalizeForMatching(target);
            filtered = windows
                .Select(window => new
                {
                    Window = window,
                    Score = ScoreWindow(target, normalizedTarget, window.Title, window.ProcessName)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Window.IsForeground)
                .ThenBy(item => item.Window.Title, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Window);
        }
        else
        {
            filtered = windows
                .OrderByDescending(window => window.IsForeground)
                .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase);
        }

        var matches = filtered.Take(20).ToArray();

        if (matches.Length == 0)
        {
            return Task.FromResult(
                string.IsNullOrWhiteSpace(target)
                    ? "No visible desktop windows were found."
                    : $"No visible windows matched '{target.Trim()}'.");
        }

        var lines = new List<string>
        {
            string.IsNullOrWhiteSpace(target)
                ? "Open windows:"
                : $"Open windows matching '{target.Trim()}':"
        };

        foreach (var window in matches)
        {
            var bounds = DescribeBounds(window.Bounds);
            var active = window.IsForeground ? "[active] " : string.Empty;
            lines.Add($"- {active}{DescribeWindow(window)} | {window.ProcessName} | {bounds}");
        }

        if (filtered.Skip(matches.Length).Any())
        {
            lines.Add("- Additional windows omitted from the preview.");
        }

        return Task.FromResult(string.Join(Environment.NewLine, lines));
    }

    public static Task<string> MoveWindowAsync(string targetApp, int x, int y)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return Task.FromResult($"Window matching '{targetApp}' was not found.");
        }

        var width = Math.Max(120, window.Bounds.Right - window.Bounds.Left);
        var height = Math.Max(120, window.Bounds.Bottom - window.Bounds.Top);

        _ = ShowWindowAsync(window.Handle, SwRestore);

        if (!MoveWindow(window.Handle, x, y, width, height, true))
        {
            return Task.FromResult($"Unable to move '{DescribeWindow(window)}'.");
        }

        _ = TryBringWindowToFront(window.Handle);
        return Task.FromResult($"Moved '{DescribeWindow(window)}' to {x},{y} with size {width}x{height}.");
    }

    public static Task<string> ResizeWindowAsync(string targetApp, int width, int height)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return Task.FromResult($"Window matching '{targetApp}' was not found.");
        }

        width = Math.Max(160, width);
        height = Math.Max(120, height);

        _ = ShowWindowAsync(window.Handle, SwRestore);

        if (!MoveWindow(window.Handle, window.Bounds.Left, window.Bounds.Top, width, height, true))
        {
            return Task.FromResult($"Unable to resize '{DescribeWindow(window)}'.");
        }

        _ = TryBringWindowToFront(window.Handle);
        return Task.FromResult($"Resized '{DescribeWindow(window)}' to {width}x{height}.");
    }

    public static Task<string> SnapWindowAsync(string targetApp, string position)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return Task.FromResult($"Window matching '{targetApp}' was not found.");
        }

        if (!TryResolveSnapBounds(window.Handle, position, out var normalizedPosition, out var x, out var y, out var width, out var height))
        {
            return Task.FromResult("Usage: snap window <window> <left|right|top|bottom|top-left|top-right|bottom-left|bottom-right|center>");
        }

        _ = ShowWindowAsync(window.Handle, SwRestore);

        if (!MoveWindow(window.Handle, x, y, width, height, true))
        {
            return Task.FromResult($"Unable to snap '{DescribeWindow(window)}'.");
        }

        _ = TryBringWindowToFront(window.Handle);
        return Task.FromResult($"Snapped '{DescribeWindow(window)}' to {normalizedPosition} ({width}x{height} @ {x},{y}).");
    }

    public static Task<string> ControlMediaAsync(string command)
    {
        if (!TryResolveMediaCommand(command, out var virtualKey, out var count, out var description))
        {
            return Task.FromResult("Usage: media <play|pause|toggle|stop|next|previous|volume up|volume down|mute> [count]");
        }

        for (var index = 0; index < count; index++)
        {
            if (!TrySendVirtualKey(virtualKey, out var error))
            {
                return Task.FromResult(error);
            }
        }

        var response = count == 1
            ? $"Sent media command: {description}."
            : $"Sent media command: {description} {count} times.";
        return Task.FromResult(response);
    }

    public static Task<string> GetClipboardTextAsync()
    {
        if (!TryReadClipboardText(out var text, out var error))
        {
            return Task.FromResult(error);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult("Clipboard is empty or does not contain text.");
        }

        var preview = text.Length <= ClipboardPreviewLimit
            ? text
            : text[..ClipboardPreviewLimit].TrimEnd() + Environment.NewLine + "[clipboard text truncated]";
        return Task.FromResult($"Clipboard text ({text.Length} chars):{Environment.NewLine}{preview}");
    }

    public static Task<string> SetClipboardTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult("Usage: set clipboard <text>");
        }

        return Task.FromResult(
            TryWriteClipboardText(text, out var error)
                ? $"Copied {DescribeTextLength(text)} to the clipboard."
                : error);
    }

    public static Task<string> ClearClipboardAsync()
    {
        return Task.FromResult(
            TryClearClipboard(out var error)
                ? "Cleared the clipboard."
                : error);
    }

    public static bool TryGetClipboardText(out string text, out string error)
    {
        return TryReadClipboardText(out text, out error);
    }

    public static async Task<string> PasteClipboardAsync(string targetApp)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return $"Window matching '{targetApp}' was not found.";
        }

        if (!TryBringWindowToFront(window.Handle))
        {
            return $"Unable to focus '{DescribeWindow(window)}' before pasting.";
        }

        await Task.Delay(120).ConfigureAwait(false);

        if (!TryParseHotkey("ctrl+v", out var pasteHotkey, out var parseError))
        {
            return parseError;
        }

        if (!TrySendHotkey(pasteHotkey, out var sendError))
        {
            return sendError;
        }

        return $"Pasted clipboard contents into '{DescribeWindow(window)}'.";
    }

    private static IReadOnlyList<WindowMatch> EnumerateTopLevelWindows()
    {
        var windows = new List<WindowMatch>();
        var foreground = GetForegroundWindow();

        _ = EnumWindows(
            (handle, _) =>
            {
                if (handle == IntPtr.Zero || !IsWindowVisible(handle))
                {
                    return true;
                }

                var title = ReadWindowText(handle);

                if (string.IsNullOrWhiteSpace(title) || !GetWindowRect(handle, out var bounds))
                {
                    return true;
                }

                var processName = TryGetProcessName(handle);

                if (string.IsNullOrWhiteSpace(processName))
                {
                    return true;
                }

                windows.Add(new WindowMatch(handle, title, processName, bounds, handle == foreground));
                return true;
            },
            IntPtr.Zero);

        return windows;
    }

    private static string TryGetProcessName(IntPtr handle)
    {
        try
        {
            _ = GetWindowThreadProcessId(handle, out var processId);

            if (processId == 0)
            {
                return string.Empty;
            }

            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryResolveSnapBounds(
        IntPtr handle,
        string position,
        out string normalizedPosition,
        out int x,
        out int y,
        out int width,
        out int height)
    {
        normalizedPosition = NormalizeSnapPosition(position);
        x = 0;
        y = 0;
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(normalizedPosition) || !TryGetMonitorWorkArea(handle, out var workArea))
        {
            return false;
        }

        var fullWidth = Math.Max(320, workArea.Right - workArea.Left);
        var fullHeight = Math.Max(240, workArea.Bottom - workArea.Top);
        var halfWidth = Math.Max(200, fullWidth / 2);
        var halfHeight = Math.Max(160, fullHeight / 2);

        switch (normalizedPosition)
        {
            case "left":
                x = workArea.Left;
                y = workArea.Top;
                width = halfWidth;
                height = fullHeight;
                return true;
            case "right":
                x = workArea.Right - halfWidth;
                y = workArea.Top;
                width = halfWidth;
                height = fullHeight;
                return true;
            case "top":
                x = workArea.Left;
                y = workArea.Top;
                width = fullWidth;
                height = halfHeight;
                return true;
            case "bottom":
                x = workArea.Left;
                y = workArea.Bottom - halfHeight;
                width = fullWidth;
                height = halfHeight;
                return true;
            case "top-left":
                x = workArea.Left;
                y = workArea.Top;
                width = halfWidth;
                height = halfHeight;
                return true;
            case "top-right":
                x = workArea.Right - halfWidth;
                y = workArea.Top;
                width = halfWidth;
                height = halfHeight;
                return true;
            case "bottom-left":
                x = workArea.Left;
                y = workArea.Bottom - halfHeight;
                width = halfWidth;
                height = halfHeight;
                return true;
            case "bottom-right":
                x = workArea.Right - halfWidth;
                y = workArea.Bottom - halfHeight;
                width = halfWidth;
                height = halfHeight;
                return true;
            case "center":
                width = Math.Max(420, (int)Math.Round(fullWidth * 0.8));
                height = Math.Max(320, (int)Math.Round(fullHeight * 0.8));
                x = workArea.Left + Math.Max(0, (fullWidth - width) / 2);
                y = workArea.Top + Math.Max(0, (fullHeight - height) / 2);
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetMonitorWorkArea(IntPtr handle, out RECT workArea)
    {
        workArea = default;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);

        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MONITORINFO
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        workArea = monitorInfo.rcWork;
        return true;
    }

    private static string NormalizeSnapPosition(string position)
    {
        var normalized = NormalizeForMatching(position);

        return normalized switch
        {
            "left" or "snap left" or "dock left" => "left",
            "right" or "snap right" or "dock right" => "right",
            "top" or "up" => "top",
            "bottom" or "down" => "bottom",
            "top left" or "upper left" => "top-left",
            "top right" or "upper right" => "top-right",
            "bottom left" or "lower left" => "bottom-left",
            "bottom right" or "lower right" => "bottom-right",
            "center" or "middle" => "center",
            _ => string.Empty
        };
    }

    private static bool TryResolveMediaCommand(string command, out ushort virtualKey, out int count, out string description)
    {
        var normalized = NormalizeForMatching(command);
        count = 1;

        if (string.IsNullOrWhiteSpace(normalized))
        {
            virtualKey = 0;
            description = string.Empty;
            return false;
        }

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (parts.Count > 1
            && int.TryParse(parts[^1], out var parsedCount))
        {
            count = Math.Clamp(parsedCount, 1, 10);
            parts.RemoveAt(parts.Count - 1);
            normalized = string.Join(' ', parts);
        }

        switch (normalized)
        {
            case "play":
            case "pause":
            case "resume":
            case "toggle":
            case "play pause":
            case "toggle playback":
                virtualKey = VkMediaPlayPause;
                description = "play/pause";
                return true;
            case "stop":
                virtualKey = VkMediaStop;
                description = "stop";
                return true;
            case "next":
            case "next track":
            case "skip":
            case "skip track":
                virtualKey = VkMediaNextTrack;
                description = "next track";
                return true;
            case "previous":
            case "previous track":
            case "prev":
            case "prev track":
            case "back track":
                virtualKey = VkMediaPrevTrack;
                description = "previous track";
                return true;
            case "volume up":
            case "louder":
                virtualKey = VkVolumeUp;
                description = "volume up";
                return true;
            case "volume down":
            case "quieter":
                virtualKey = VkVolumeDown;
                description = "volume down";
                return true;
            case "mute":
            case "toggle mute":
            case "mute audio":
                virtualKey = VkVolumeMute;
                description = "mute";
                return true;
            default:
                virtualKey = 0;
                description = string.Empty;
                return false;
        }
    }

    private static bool TrySendVirtualKey(ushort virtualKey, out string error)
    {
        var inputs = new[]
        {
            CreateKeyboardInput(virtualKey, keyUp: false),
            CreateKeyboardInput(virtualKey, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        if (sent != inputs.Length)
        {
            error = $"Windows only dispatched {sent} of {inputs.Length} key events.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadClipboardText(out string text, out string error)
    {
        text = string.Empty;

        if (!TryOpenClipboard(out error))
        {
            return false;
        }

        try
        {
            if (!IsClipboardFormatAvailable(CfUnicodeText))
            {
                return true;
            }

            var handle = GetClipboardData(CfUnicodeText);

            if (handle == IntPtr.Zero)
            {
                error = "Clipboard text is currently unavailable.";
                return false;
            }

            var pointer = GlobalLock(handle);

            if (pointer == IntPtr.Zero)
            {
                error = "Unable to read clipboard text.";
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(pointer) ?? string.Empty;
                return true;
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    private static bool TryWriteClipboardText(string text, out string error)
    {
        error = string.Empty;

        if (!TryOpenClipboard(out error))
        {
            return false;
        }

        IntPtr globalHandle = IntPtr.Zero;

        try
        {
            if (!EmptyClipboard())
            {
                error = "Unable to clear the clipboard before writing.";
                return false;
            }

            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            globalHandle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);

            if (globalHandle == IntPtr.Zero)
            {
                error = "Unable to allocate clipboard memory.";
                return false;
            }

            var target = GlobalLock(globalHandle);

            if (target == IntPtr.Zero)
            {
                error = "Unable to lock clipboard memory.";
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                _ = GlobalUnlock(globalHandle);
            }

            if (SetClipboardData(CfUnicodeText, globalHandle) == IntPtr.Zero)
            {
                error = "Unable to update clipboard text.";
                return false;
            }

            globalHandle = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (globalHandle != IntPtr.Zero)
            {
                _ = GlobalFree(globalHandle);
            }

            _ = CloseClipboard();
        }
    }

    private static bool TryClearClipboard(out string error)
    {
        if (!TryOpenClipboard(out error))
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                error = "Unable to clear the clipboard.";
                return false;
            }

            return true;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    private static bool TryOpenClipboard(out string error)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                error = string.Empty;
                return true;
            }

            Thread.Sleep(40);
        }

        error = "Unable to access the clipboard right now.";
        return false;
    }

    private static string DescribeBounds(RECT bounds)
    {
        var width = Math.Max(0, bounds.Right - bounds.Left);
        var height = Math.Max(0, bounds.Bottom - bounds.Top);
        return $"{width}x{height} @ {bounds.Left},{bounds.Top}";
    }
}
