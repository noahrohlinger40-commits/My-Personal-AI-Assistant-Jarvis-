using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.Core;

internal static partial class Win32UiAutomation
{
    private const uint KeyeventfUnicode = 0x0004;

    public static async Task<string> SendTextAsync(string targetApp, string text)
    {
        if (!TryResolveWindow(targetApp, out var window))
        {
            return $"Window matching '{targetApp}' was not found.";
        }

        if (!TryBringWindowToFront(window.Handle))
        {
            return $"Unable to focus '{DescribeWindow(window)}' before sending text.";
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return "Usage: send text <window> <text>";
        }

        await Task.Delay(120).ConfigureAwait(false);

        if (!TrySendUnicodeText(text, out var error))
        {
            return error;
        }

        return $"Sent {DescribeTextLength(text)} to '{DescribeWindow(window)}'.";
    }

    private static bool TrySendUnicodeText(string text, out string error)
    {
        var inputs = new List<INPUT>();

        foreach (var rune in text.EnumerateRunes())
        {
            var characters = rune.Utf16SequenceLength == 1
                ? [char.ConvertFromUtf32(rune.Value)[0]]
                : char.ConvertFromUtf32(rune.Value).ToCharArray();

            foreach (var character in characters)
            {
                if (character == '\r')
                {
                    continue;
                }

                if (character == '\n')
                {
                    inputs.Add(CreateKeyboardInput(0x0D, keyUp: false));
                    inputs.Add(CreateKeyboardInput(0x0D, keyUp: true));
                    continue;
                }

                inputs.Add(CreateUnicodeKeyboardInput(character, keyUp: false));
                inputs.Add(CreateUnicodeKeyboardInput(character, keyUp: true));
            }
        }

        if (inputs.Count == 0)
        {
            error = "No text was available to send.";
            return false;
        }

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());

        if (sent != inputs.Count)
        {
            error = $"Windows only dispatched {sent} of {inputs.Count} text input events.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static INPUT CreateUnicodeKeyboardInput(char character, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = character,
                    dwFlags = KeyeventfUnicode | (keyUp ? KeyeventfKeyup : 0)
                }
            }
        };
    }
}
