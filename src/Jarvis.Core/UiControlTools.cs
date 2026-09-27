using System.Globalization;

namespace Jarvis.Core;

public sealed class FocusAppTool : IAssistantTool
{
    public string Name => "focus app";

    public string Description => "Focus a running window. `focus app notepad`.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || ToolInputHelper.StartsWithCommand(input, "focus window")
            || ToolInputHelper.StartsWithCommand(input, "switch to")
            || ToolInputHelper.StartsWithCommand(input, "activate app")
            || ToolInputHelper.StartsWithCommand(input, "bring to front");

        return matches
            && (!UiCommandParsing.TryParseFocusTarget(input, out var target)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseFocusTarget(input, out var target))
        {
            return new ToolResult("Usage: focus app <window>");
        }

        var result = await context.DesktopAutomation.FocusApplicationAsync(target, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Focused app." : "Focus failed.");
    }
}

public sealed class CloseAppTool : IAssistantTool
{
    public string Name => "close app";

    public string Description => "Send a close request to a window. `close app notepad`. Approval may be required.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || ToolInputHelper.StartsWithCommand(input, "close window")
            || ToolInputHelper.StartsWithCommand(input, "quit app");

        return matches
            && (!UiCommandParsing.TryParseCloseTarget(input, out var target)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseCloseTarget(input, out var target))
        {
            return new ToolResult("Usage: close app <window>");
        }

        var result = await context.DesktopAutomation.CloseApplicationAsync(target, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Sent close request." : "Close failed.");
    }
}

public sealed class WindowStateTool : IAssistantTool
{
    public string Name => "window state";

    public string Description => "Minimize, maximize, or restore a window. `window state notepad maximize`.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || ToolInputHelper.StartsWithCommand(input, "minimize app")
            || ToolInputHelper.StartsWithCommand(input, "maximize app")
            || ToolInputHelper.StartsWithCommand(input, "restore app")
            || ToolInputHelper.StartsWithCommand(input, "minimize window")
            || ToolInputHelper.StartsWithCommand(input, "maximize window")
            || ToolInputHelper.StartsWithCommand(input, "restore window");

        return matches
            && (!UiCommandParsing.TryParseWindowState(input, out var target, out _)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseWindowState(input, out var target, out var state))
        {
            return new ToolResult("Usage: window state <window> <minimize|maximize|restore>");
        }

        var result = await context.DesktopAutomation.SetWindowStateAsync(target, state, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Changed window state." : "Window state change failed.");
    }
}

public sealed class SendHotkeyTool : IAssistantTool
{
    public string Name => "send hotkey";

    public string Description => "Send a key combo to a window. `send hotkey notepad ctrl+s`. Approval may be required.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || ToolInputHelper.StartsWithCommand(input, "press key")
            || ToolInputHelper.StartsWithCommand(input, "send shortcut")
            || ToolInputHelper.StartsWithCommand(input, "shortcut");

        return matches
            && (!UiCommandParsing.TryParseHotkeyCommand(input, out var target, out _)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseHotkeyCommand(input, out var target, out var hotkey))
        {
            return new ToolResult("Usage: send hotkey <window> <key-combo>");
        }

        var result = await context.DesktopAutomation.SendHotkeyAsync(target, hotkey, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Sent hotkey." : "Hotkey failed.");
    }
}

public sealed class ClickElementTool : IAssistantTool
{
    public string Name => "click element";

    public string Description => "Click the center of a target window or a named control. `click element notepad` or `click element notepad :: save`.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || input.StartsWith("click ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("tap ", StringComparison.OrdinalIgnoreCase);

        return matches
            && (!UiCommandParsing.TryParseClickTarget(input, out var target, out var elementName)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target, elementName));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseClickTarget(input, out var target, out var elementName))
        {
            return new ToolResult("Usage: click element <window> [:: element]");
        }

        var result = await context.DesktopAutomation.ClickElementAsync(target, elementName, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, VerificationText: result, SummaryText: success ? "Clicked UI target." : "Click failed.");
    }
}

public sealed class TypeIntoElementTool : IAssistantTool
{
    public string Name => "type into";

    public string Description => "Type text into a target window or named field. `type into notepad hello` or `type into notepad :: search :: hello`. Sensitive content requires approval.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || input.StartsWith("type ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("types ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("enter ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("input ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("write ", StringComparison.OrdinalIgnoreCase);

        return matches
            && (!UiCommandParsing.TryParseTypeInto(input, out var target, out var elementName, out _)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target, elementName));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseTypeInto(input, out var target, out var elementName, out var text))
        {
            return new ToolResult("Usage: type into <window> <text> or type into <window> :: <element> :: <text>");
        }

        var result = await context.DesktopAutomation.TypeIntoElementAsync(target, elementName, text, cancellationToken);

        // "Open Notepad and write hello": start the app when it is not running, then give its window a
        // few seconds to appear. Limited to exact app names so a stray word never launches something.
        if (result.StartsWith("Window matching", StringComparison.OrdinalIgnoreCase)
            && JarvisCommandCatalog.KnownApplicationNameCheck?.Invoke(target) == true)
        {
            var opened = await context.DesktopAutomation.OpenApplicationAsync(target, cancellationToken);

            for (var attempt = 0; attempt < 20 && result.StartsWith("Window matching", StringComparison.OrdinalIgnoreCase); attempt++)
            {
                // The first wait is longer: a window can appear before the app has loaded its document,
                // and keys typed then are lost (Notepad saved an empty file in testing).
                // ponytail: fixed settle time; poll the app's readiness if a slow app still drops keys.
                await Task.Delay(attempt == 0 ? 1500 : 500, cancellationToken);
                result = await context.DesktopAutomation.TypeIntoElementAsync(target, elementName, text, cancellationToken);
            }

            if (result.StartsWith("Window matching", StringComparison.OrdinalIgnoreCase))
            {
                result = $"Unable to type into '{target}': its window did not appear. {opened}";
            }
        }

        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, Succeeded: success, VerificationText: result, SummaryText: success ? "Typed text." : "Type failed.");
    }
}

public sealed class GetElementTextTool : IAssistantTool
{
    public string Name => "get element text";

    public string Description => "Get text from a target window or named control. `get element text notepad` or `get element text notepad :: status bar`.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || input.StartsWith("read element ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("element text ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("what text ", StringComparison.OrdinalIgnoreCase);

        return matches
            && (!UiCommandParsing.TryParseGetTextTarget(input, out var target, out var elementName)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target, elementName));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseGetTextTarget(input, out var target, out var elementName))
        {
            return new ToolResult("Usage: get element text <window> [:: element]");
        }

        var result = await context.DesktopAutomation.GetElementTextAsync(target, elementName, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result) && !string.IsNullOrWhiteSpace(result);
        return new ToolResult(
            result,
            VerificationText: success ? $"Retrieved text from '{target}'{(string.IsNullOrWhiteSpace(elementName) ? string.Empty : $" :: '{elementName}'")} ({result.Length} chars)." : result,
            SummaryText: success ? "Got element text." : "Text retrieval failed.");
    }
}

public sealed class ScrollWindowTool : IAssistantTool
{
    public string Name => "scroll";

    public string Description => "Scroll a target window. `scroll notepad down` or `scroll notepad up 3`.";

    public bool CanHandle(string input)
    {
        var matches = ToolInputHelper.StartsWithCommand(input, Name)
            || input.StartsWith("move down ", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("move up ", StringComparison.OrdinalIgnoreCase);

        return matches
            && (!UiCommandParsing.TryParseScrollCommand(input, out var target, out _, out _)
                || !UiCommandParsing.LooksLikeContextualReferenceTarget(target));
    }

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseScrollCommand(input, out var target, out var direction, out var count))
        {
            return new ToolResult("Usage: scroll <window> <up|down> [count]");
        }

        string result = string.Empty;

        for (var index = 0; index < count; index++)
        {
            result = await context.DesktopAutomation.ScrollWindowAsync(target, direction, cancellationToken);

            if (UiCommandParsing.LooksLikeFailure(result))
            {
                return new ToolResult(result, VerificationText: result, SummaryText: "Scroll failed.");
            }
        }

        var response = count == 1
            ? result
            : $"Scrolled '{target}' {direction} {count} times.";
        return new ToolResult(response, VerificationText: response, SummaryText: "Scrolled window.");
    }
}

public sealed class WaitTool : IAssistantTool
{
    public string Name => "wait";

    public string Description => "Pause execution for a short duration. `wait 2.5`.";

    public bool CanHandle(string input) =>
        ToolInputHelper.StartsWithCommand(input, Name)
        || input.StartsWith("pause ", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("hold ", StringComparison.OrdinalIgnoreCase);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!UiCommandParsing.TryParseWaitSeconds(input, out var seconds) || seconds < 0 || seconds > 30)
        {
            return new ToolResult("Usage: wait <seconds> (0-30)");
        }

        var result = await context.DesktopAutomation.WaitAsync(seconds, cancellationToken);
        return new ToolResult(result, VerificationText: "Wait completed.", SummaryText: $"Waited {seconds}s.");
    }
}

internal static partial class UiCommandParsing
{
    public static bool TryParseFocusTarget(string input, out string target)
    {
        target = ToolInputHelper.GetCommandTail(input, "focus app", "focus window", "switch to", "activate app", "bring to front");
        return !string.IsNullOrWhiteSpace(target);
    }

    public static bool TryParseCloseTarget(string input, out string target)
    {
        target = ToolInputHelper.GetCommandTail(input, "close app", "close window", "quit app");
        return !string.IsNullOrWhiteSpace(target);
    }

    public static bool TryParseWindowState(string input, out string target, out string state)
    {
        if (TryParseFixedStateTarget(input, "minimize", out target, out state, "minimize app", "minimize window")
            || TryParseFixedStateTarget(input, "maximize", out target, out state, "maximize app", "maximize window")
            || TryParseFixedStateTarget(input, "restore", out target, out state, "restore app", "restore window"))
        {
            return true;
        }

        if (!TryParseTargetAndPayload(input, out target, out state, "window state"))
        {
            return false;
        }

        return TryNormalizeWindowState(state, out state);
    }

    public static bool TryParseHotkeyCommand(string input, out string target, out string hotkey)
    {
        return TryParseTargetAndPayload(input, out target, out hotkey, "send hotkey", "press key", "send shortcut", "shortcut");
    }

    public static bool TryParseClickTarget(string input, out string target)
    {
        return TryParseClickTarget(input, out target, out _);
    }

    public static bool TryParseClickTarget(string input, out string target, out string elementName)
    {
        var remainder = ToolInputHelper.GetCommandTail(input, "click element", "click", "tap");
        return TryParseTargetAndOptionalElement(remainder, out target, out elementName);
    }

    public static bool TryParseTypeInto(string input, out string target, out string text)
    {
        var parsed = TryParseTypeInto(input, out target, out _, out text);
        return parsed;
    }

    public static bool TryParseTypeInto(string input, out string target, out string elementName, out string text)
    {
        var remainder = ToolInputHelper.GetCommandTail(input, "type into", "types into", "enter into", "input into", "write into");
        if (TryParseStructuredElementPayload(remainder, out target, out elementName, out text))
        {
            return true;
        }

        if (TryParseTargetAndPayload(input, out target, out text, "type into", "types into", "enter into", "input into", "write into"))
        {
            elementName = string.Empty;
            return true;
        }

        foreach (var prefix in new[] { "type", "types", "enter", "input", "write" })
        {
            if (!ToolInputHelper.StartsWithCommand(input, prefix))
            {
                continue;
            }

            var naturalRemainder = ToolInputHelper.GetCommandTail(input, prefix);
            var separatorIndex = naturalRemainder.LastIndexOf(" into ", StringComparison.OrdinalIgnoreCase);

            if (separatorIndex <= 0)
            {
                target = string.Empty;
                elementName = string.Empty;
                text = string.Empty;
                return false;
            }

            text = naturalRemainder[..separatorIndex].Trim().Trim('"');
            return TryParseTargetAndOptionalElement(
                naturalRemainder[(separatorIndex + " into ".Length)..],
                out target,
                out elementName)
                && !string.IsNullOrWhiteSpace(text);
        }

        target = string.Empty;
        elementName = string.Empty;
        text = string.Empty;
        return false;
    }

    public static bool TryParseGetTextTarget(string input, out string target)
    {
        return TryParseGetTextTarget(input, out target, out _);
    }

    public static bool TryParseGetTextTarget(string input, out string target, out string elementName)
    {
        var remainder = ToolInputHelper.GetCommandTail(input, "get element text", "read element", "element text", "what text");
        return TryParseTargetAndOptionalElement(remainder, out target, out elementName);
    }

    public static bool TryParseScrollCommand(string input, out string target, out string direction, out int count)
    {
        direction = "down";
        count = 1;
        var remainder = string.Empty;

        if (ToolInputHelper.StartsWithCommand(input, "move down"))
        {
            remainder = ToolInputHelper.GetCommandTail(input, "move down");
        }
        else if (ToolInputHelper.StartsWithCommand(input, "move up"))
        {
            remainder = ToolInputHelper.GetCommandTail(input, "move up");
            direction = "up";
        }
        else
        {
            remainder = ToolInputHelper.GetCommandTail(input, "scroll", "scroll window");
        }

        if (string.IsNullOrWhiteSpace(remainder))
        {
            target = string.Empty;
            return false;
        }

        var tokens = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        if (tokens.Count == 0)
        {
            target = string.Empty;
            return false;
        }

        if (tokens.Count > 1
            && int.TryParse(tokens[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount))
        {
            count = Math.Clamp(parsedCount, 1, 10);
            tokens.RemoveAt(tokens.Count - 1);
        }

        if (tokens.Count > 1
            && (tokens[^1].Equals("up", StringComparison.OrdinalIgnoreCase)
                || tokens[^1].Equals("down", StringComparison.OrdinalIgnoreCase)))
        {
            direction = tokens[^1].ToLowerInvariant();
            tokens.RemoveAt(tokens.Count - 1);
        }

        target = string.Join(' ', tokens).Trim();
        return !string.IsNullOrWhiteSpace(target);
    }

    public static bool TryParseWaitSeconds(string input, out double seconds)
    {
        var value = ToolInputHelper.GetCommandTail(input, "wait", "pause", "hold");

        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out seconds)
            || double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out seconds);
    }

    public static bool LooksLikeFailure(string responseText)
    {
        return string.IsNullOrWhiteSpace(responseText)
            || responseText.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || responseText.StartsWith("usage:", StringComparison.OrdinalIgnoreCase)
            || responseText.StartsWith("unable ", StringComparison.OrdinalIgnoreCase)
            || responseText.StartsWith("unsupported ", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeContextualReferenceTarget(string target, string elementName = "")
    {
        return LooksLikeContextualSelector(target)
            || (!string.IsNullOrWhiteSpace(elementName) && LooksLikeContextualSelector(elementName));
    }

    public static bool TryParseTargetAndPayload(string input, out string target, out string payload, params string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (!ToolInputHelper.StartsWithCommand(input, prefix))
            {
                continue;
            }

            var remainder = ToolInputHelper.GetCommandTail(input, prefix);
            return TrySplitTargetAndPayload(remainder, out target, out payload);
        }

        target = string.Empty;
        payload = string.Empty;
        return false;
    }

    private static bool TryParseFixedStateTarget(string input, string fixedState, out string target, out string state, params string[] prefixes)
    {
        target = ToolInputHelper.GetCommandTail(input, prefixes);
        state = fixedState;
        return !string.IsNullOrWhiteSpace(target);
    }

    private static bool TryNormalizeWindowState(string value, out string state)
    {
        state = value.Trim().ToLowerInvariant();

        if (state is "minimize" or "minimized")
        {
            state = "minimize";
            return true;
        }

        if (state is "maximize" or "maximized")
        {
            state = "maximize";
            return true;
        }

        if (state is "restore" or "restored")
        {
            state = "restore";
            return true;
        }

        return false;
    }

    private static bool TrySplitTargetAndPayload(string remainder, out string target, out string payload)
    {
        target = string.Empty;
        payload = string.Empty;

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var trimmed = remainder.Trim();

        if (trimmed[0] is '"' or '\'')
        {
            var quote = trimmed[0];
            var closingIndex = trimmed.IndexOf(quote, 1);

            if (closingIndex <= 1)
            {
                return false;
            }

            target = trimmed[1..closingIndex].Trim();
            payload = trimmed[(closingIndex + 1)..].Trim();
            return !string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(payload);
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return false;
        }

        target = parts[0].Trim();
        payload = parts[1].Trim();
        return !string.IsNullOrWhiteSpace(target) && !string.IsNullOrWhiteSpace(payload);
    }

    private static bool TryParseTargetAndOptionalElement(string remainder, out string target, out string elementName)
    {
        target = string.Empty;
        elementName = string.Empty;

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var structuredParts = remainder
            .Split(["::"], 2, StringSplitOptions.None | StringSplitOptions.TrimEntries)
            .Select(part => part.Trim().Trim('"'))
            .ToArray();

        target = structuredParts[0];
        elementName = structuredParts.Length > 1 ? structuredParts[1] : string.Empty;
        return !string.IsNullOrWhiteSpace(target);
    }

    private static bool LooksLikeContextualSelector(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = string.Join(
            " ",
            value
                .Trim()
                .Trim('"')
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized is "it"
            or "that"
            or "this"
            or "them"
            or "that one"
            or "this one"
            or "the one"
            or "the other one"
            or "the earlier one"
            or "the previous one"
            or "the first one"
            or "the second one"
            or "the third one"
            or "the last one"
            or "the latest one"
            or "the recent one")
        {
            return true;
        }

        foreach (var prefix in new[]
                 {
                     "that ",
                     "this ",
                     "the other ",
                     "the earlier ",
                     "the previous ",
                     "the first ",
                     "the second ",
                     "the third ",
                     "the last ",
                     "the latest ",
                     "the recent ",
                     "the newest ",
                     "other ",
                     "earlier ",
                     "previous "
                 })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4
            && (normalized.Contains(" it", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains(" that", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains(" this", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseStructuredElementPayload(string remainder, out string target, out string elementName, out string payload)
    {
        target = string.Empty;
        elementName = string.Empty;
        payload = string.Empty;

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var structuredParts = remainder
            .Split(["::"], 3, StringSplitOptions.None | StringSplitOptions.TrimEntries)
            .Select(part => part.Trim().Trim('"'))
            .ToArray();

        if (structuredParts.Length < 3)
        {
            return false;
        }

        target = structuredParts[0];
        elementName = structuredParts[1];
        payload = structuredParts[2];
        return !string.IsNullOrWhiteSpace(target)
            && !string.IsNullOrWhiteSpace(elementName)
            && !string.IsNullOrWhiteSpace(payload);
    }
}
