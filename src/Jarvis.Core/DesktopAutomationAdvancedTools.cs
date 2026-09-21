using System.Globalization;

namespace Jarvis.Core;

public sealed class WindowsTool : IAssistantTool
{
    public string Name => "windows";

    public string Description => "List visible desktop windows. `windows` or `windows code`.";

    public bool CanHandle(string input) => UiCommandParsing.TryParseWindowsQuery(input, out _);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseWindowsQuery(input, out var target))
        {
            return new ToolResult("Usage: windows [filter]");
        }

        var result = await context.DesktopAutomation.ListOpenWindowsAsync(target, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Listed windows." : "Window lookup failed.");
    }
}

public sealed class MoveWindowTool : IAssistantTool
{
    public string Name => "move window";

    public string Description => "Move a window to screen coordinates. `move window notepad 120 80`.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || ToolInputHelper.StartsWithCommand(input, "move app")
            || ToolInputHelper.StartsWithCommand(input, "position window");

        return matches
            && (!UiCommandParsing.TryParseMoveWindow(input, out var target, out _, out _)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseMoveWindow(input, out var target, out var x, out var y))
        {
            return new ToolResult("Usage: move window <window> <x> <y>");
        }

        var result = await context.DesktopAutomation.MoveWindowAsync(target, x, y, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Moved window." : "Window move failed.");
    }
}

public sealed class ResizeWindowTool : IAssistantTool
{
    public string Name => "resize window";

    public string Description => "Resize a window in pixels. `resize window notepad 1280 900`.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || ToolInputHelper.StartsWithCommand(input, "resize app");

        return matches
            && (!UiCommandParsing.TryParseResizeWindow(input, out var target, out _, out _)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseResizeWindow(input, out var target, out var width, out var height))
        {
            return new ToolResult("Usage: resize window <window> <width> <height>");
        }

        var result = await context.DesktopAutomation.ResizeWindowAsync(target, width, height, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Resized window." : "Window resize failed.");
    }
}

public sealed class SnapWindowTool : IAssistantTool
{
    public string Name => "snap window";

    public string Description => "Snap a window into a desktop region. `snap window code left` or `snap window code top right`.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || ToolInputHelper.StartsWithCommand(input, "snap app")
            || ToolInputHelper.StartsWithCommand(input, "dock window");

        return matches
            && (!UiCommandParsing.TryParseSnapWindow(input, out var target, out _)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseSnapWindow(input, out var target, out var position))
        {
            return new ToolResult("Usage: snap window <window> <left|right|top|bottom|top-left|top-right|bottom-left|bottom-right|center>");
        }

        var result = await context.DesktopAutomation.SnapWindowAsync(target, position, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Snapped window." : "Window snap failed.");
    }
}

public sealed class MediaTool : IAssistantTool
{
    public string Name => "media";

    public string Description => "Send system media keys. `media next`, `media pause`, `volume up 3`, or `mute`.";

    public bool CanHandle(string input) => UiCommandParsing.TryParseMediaCommand(input, out _);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseMediaCommand(input, out var command))
        {
            return new ToolResult("Usage: media <play|pause|toggle|stop|next|previous|volume up|volume down|mute> [count]");
        }

        var result = await context.DesktopAutomation.ControlMediaAsync(command, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Sent media command." : "Media command failed.");
    }
}

public sealed class ClipboardTool : IAssistantTool
{
    public string Name => "clipboard";

    public string Description => "Inspect or change the clipboard. Use `clipboard`, `clipboard history`, `clipboard restore 2`, `set clipboard hello`, or `paste clipboard into notepad`.";

    public bool CanHandle(string input) => UiCommandParsing.TryParseClipboardCommand(input, out _, out _);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseClipboardCommand(input, out var action, out var payload))
        {
            return new ToolResult("Usage: clipboard | set clipboard <text> | clear clipboard | paste clipboard into <window>");
        }

        var result = action switch
        {
            "show" => await context.DesktopAutomation.GetClipboardAsync(cancellationToken),
            "history" => await context.DesktopAutomation.GetClipboardHistoryAsync(cancellationToken),
            "restore" when int.TryParse(payload, out var index) => await context.DesktopAutomation.RestoreClipboardHistoryAsync(index, cancellationToken),
            "restore" => "Usage: clipboard restore <history-index>",
            "set" => await context.DesktopAutomation.SetClipboardAsync(payload, cancellationToken),
            "clear" => await context.DesktopAutomation.ClearClipboardAsync(cancellationToken),
            "paste" when !string.IsNullOrWhiteSpace(payload) => await context.DesktopAutomation.PasteClipboardAsync(payload, cancellationToken),
            "paste" => "Usage: paste clipboard into <window>",
            _ => "Usage: clipboard | clipboard history | clipboard restore <index> | set clipboard <text> | clear clipboard | paste clipboard into <window>"
        };

        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Handled clipboard action." : "Clipboard action failed.");
    }
}

internal static partial class UiCommandParsing
{
    public static bool TryParseWindowsQuery(string input, out string target)
    {
        target = ToolInputHelper.GetCommandTail(input, "windows", "list windows", "show windows", "visible windows");
        return ToolInputHelper.StartsWithCommand(input, "windows")
            || ToolInputHelper.StartsWithCommand(input, "list windows")
            || ToolInputHelper.StartsWithCommand(input, "show windows")
            || ToolInputHelper.StartsWithCommand(input, "visible windows");
    }

    public static bool TryParseMoveWindow(string input, out string target, out int x, out int y)
    {
        var remainder = ToolInputHelper.GetCommandTail(input, "move window", "move app", "position window");
        return TryParseTargetAndTrailingIntegers(remainder, out target, out x, out y);
    }

    public static bool TryParseResizeWindow(string input, out string target, out int width, out int height)
    {
        var remainder = ToolInputHelper.GetCommandTail(input, "resize window", "resize app");
        return TryParseTargetAndTrailingIntegers(remainder, out target, out width, out height)
            && width > 0
            && height > 0;
    }

    public static bool TryParseSnapWindow(string input, out string target, out string position)
    {
        var remainder = ToolInputHelper.GetCommandTail(input, "snap window", "snap app", "dock window");
        target = string.Empty;
        position = string.Empty;

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var structuredParts = remainder
            .Split(["::"], 2, StringSplitOptions.None | StringSplitOptions.TrimEntries)
            .Select(part => part.Trim().Trim('"'))
            .ToArray();

        if (structuredParts.Length == 2)
        {
            target = structuredParts[0];
            return TryNormalizeSnapPosition(structuredParts[1], out position);
        }

        foreach (var candidateLength in new[] { 2, 1 })
        {
            var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length <= candidateLength)
            {
                continue;
            }

            var candidatePosition = string.Join(' ', parts[^candidateLength..]);

            if (!TryNormalizeSnapPosition(candidatePosition, out position))
            {
                continue;
            }

            target = string.Join(' ', parts[..^candidateLength]).Trim().Trim('"');
            return !string.IsNullOrWhiteSpace(target);
        }

        return false;
    }

    public static bool TryParseMediaCommand(string input, out string command)
    {
        command = string.Empty;

        if (ToolInputHelper.StartsWithCommand(input, "media"))
        {
            command = ToolInputHelper.GetCommandTail(input, "media");
            return !string.IsNullOrWhiteSpace(command);
        }

        foreach (var direct in new[]
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
            if (!ToolInputHelper.StartsWithCommand(input, direct.Item1))
            {
                continue;
            }

            command = direct.Item2;
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(input, "volume up")
            || ToolInputHelper.StartsWithCommand(input, "volume down"))
        {
            var prefix = ToolInputHelper.StartsWithCommand(input, "volume up") ? "volume up" : "volume down";
            var tail = ToolInputHelper.GetCommandTail(input, prefix);
            command = string.IsNullOrWhiteSpace(tail)
                ? prefix
                : $"{prefix} {tail}";
            return true;
        }

        if (ToolInputHelper.MatchesExact(input, "mute"))
        {
            command = "mute";
            return true;
        }

        return false;
    }

    public static bool TryParseClipboardCommand(string input, out string action, out string payload)
    {
        action = string.Empty;
        payload = string.Empty;

        if (ToolInputHelper.MatchesExact(input, "clipboard history", "show clipboard history", "list clipboard history"))
        {
            action = "history";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(input, "clipboard restore")
            || ToolInputHelper.StartsWithCommand(input, "restore clipboard"))
        {
            action = "restore";
            payload = ToolInputHelper.GetCommandTail(input, "clipboard restore", "restore clipboard");
            return true;
        }

        if (ToolInputHelper.MatchesExact(input, "clipboard", "show clipboard", "get clipboard", "read clipboard", "clipboard show"))
        {
            action = "show";
            return true;
        }

        if (ToolInputHelper.MatchesExact(input, "clear clipboard", "clipboard clear", "empty clipboard"))
        {
            action = "clear";
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(input, "set clipboard")
            || ToolInputHelper.StartsWithCommand(input, "copy to clipboard")
            || ToolInputHelper.StartsWithCommand(input, "clipboard set"))
        {
            action = "set";
            payload = ToolInputHelper.GetCommandTail(input, "set clipboard", "copy to clipboard", "clipboard set");
            return !string.IsNullOrWhiteSpace(payload);
        }

        if (ToolInputHelper.StartsWithCommand(input, "paste clipboard into")
            || ToolInputHelper.StartsWithCommand(input, "paste clipboard")
            || ToolInputHelper.StartsWithCommand(input, "clipboard paste"))
        {
            action = "paste";
            payload = ToolInputHelper.GetCommandTail(input, "paste clipboard into", "paste clipboard", "clipboard paste");

            if (payload.StartsWith("into ", StringComparison.OrdinalIgnoreCase))
            {
                payload = payload["into ".Length..].Trim();
            }

            payload = payload.Trim().Trim('"');
            return true;
        }

        return false;
    }

    private static bool TryParseTargetAndTrailingIntegers(string remainder, out string target, out int first, out int second)
    {
        target = string.Empty;
        first = 0;
        second = 0;

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
        {
            return false;
        }

        if (!int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out first)
            || !int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out second))
        {
            return false;
        }

        target = string.Join(' ', parts[..^2]).Trim().Trim('"');
        return !string.IsNullOrWhiteSpace(target);
    }

    private static bool TryNormalizeSnapPosition(string value, out string position)
    {
        position = string.Join(
            " ",
            value
                .Trim()
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        switch (position)
        {
            case "left":
            case "right":
            case "top":
            case "bottom":
            case "center":
                return true;
            case "top left":
                position = "top-left";
                return true;
            case "top right":
                position = "top-right";
                return true;
            case "bottom left":
                position = "bottom-left";
                return true;
            case "bottom right":
                position = "bottom-right";
                return true;
            default:
                return false;
        }
    }
}
