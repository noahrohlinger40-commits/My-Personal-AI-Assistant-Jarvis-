using System.Text.Json;

namespace Jarvis.Core;

public sealed record PendingApprovalAction(
    string ToolName,
    string NormalizedCommand,
    string OriginalInput,
    string Summary,
    string ApprovalPrompt,
    DateTimeOffset CreatedAtUtc);

public interface IPendingApprovalStore
{
    Task<PendingApprovalAction?> GetAsync(CancellationToken cancellationToken);

    Task SaveAsync(PendingApprovalAction action, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public sealed class JsonPendingApprovalStore : IPendingApprovalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonPendingApprovalStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<PendingApprovalAction?> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<PendingApprovalAction>(stream, JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PendingApprovalAction action, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            EnsureParentDirectoryExists();

            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, action, JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureParentDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

internal enum SafetyDecisionKind
{
    Allow,
    RequireApproval,
    Block
}

internal sealed record SafetyDecision(
    SafetyDecisionKind Kind,
    string ResponseText = "",
    string VerificationText = "",
    string SummaryText = "");

public sealed class ToolSafetyService
{
    private static readonly string[] AlwaysBlockedSignals =
    {
        " diskpart ",
        " format-volume ",
        " clear-disk ",
        " initialize-disk ",
        " bcdedit ",
        " cipher /w",
        " vssadmin delete ",
        " wbadmin delete ",
        " manage-bde -off ",
        " manage-bde -on "
    };

    private static readonly string[] StateChangingSignals =
    {
        " remove-item ",
        " del ",
        " erase ",
        " rm ",
        " rmdir ",
        " rd ",
        " clear-content ",
        " set-content ",
        " add-content ",
        " out-file ",
        " new-item ",
        " move-item ",
        " copy-item ",
        " rename-item ",
        " set-item ",
        " clear-item ",
        " set-itemproperty ",
        " new-itemproperty ",
        " remove-itemproperty ",
        " reg add ",
        " reg delete ",
        " reg import ",
        " reg export ",
        " start-process ",
        " stop-process ",
        " taskkill ",
        " stop-service ",
        " start-service ",
        " restart-service ",
        " new-service ",
        " set-service ",
        " sc.exe ",
        " sc ",
        " shutdown ",
        " restart-computer ",
        " stop-computer ",
        " set-executionpolicy ",
        " netsh ",
        " winget ",
        " choco ",
        " msiexec ",
        " npm install ",
        " npm uninstall ",
        " pnpm add ",
        " pnpm remove ",
        " yarn add ",
        " yarn remove ",
        " pip install ",
        " pip uninstall ",
        " dotnet add ",
        " dotnet remove ",
        " dotnet tool install ",
        " dotnet tool uninstall ",
        " git clone ",
        " git checkout ",
        " git switch ",
        " git clean ",
        " git reset ",
        " git stash pop ",
        " invoke-webrequest ",
        " curl ",
        " bitsadmin ",
        " schtasks /create ",
        " schtasks /delete ",
        " attrib ",
        " icacls ",
        " takeown "
    };

    private static readonly string[] ReadOnlyStarts =
    {
        "get-",
        "dir",
        "ls",
        "pwd",
        "cd",
        "whoami",
        "hostname",
        "echo",
        "write-output",
        "type",
        "cat",
        "gc",
        "get-content",
        "get-childitem",
        "get-item",
        "get-location",
        "select-string",
        "rg",
        "where",
        "where-object",
        "test-path",
        "resolve-path",
        "measure-object",
        "compare-object",
        "sort-object",
        "group-object",
        "git status",
        "git diff",
        "git log",
        "git show",
        "dotnet --info",
        "dotnet --list",
        "node -v",
        "npm -v",
        "python --version",
        "py --version",
        "wsl -l",
        "openclaw --version"
    };

    private readonly JarvisOptions _options;
    private readonly string[] _protectedFragments;

    public ToolSafetyService(JarvisOptions options)
    {
        _options = options;
        _protectedFragments = BuildProtectedFragments();
    }

    internal SafetyDecision Assess(string toolName, string normalizedCommand)
    {
        if (string.Equals(toolName, "chrome", StringComparison.OrdinalIgnoreCase))
        {
            return AssessChromeCommand(normalizedCommand);
        }

        if (string.Equals(toolName, "macro", StringComparison.OrdinalIgnoreCase))
        {
            return AssessMacroCommand(normalizedCommand);
        }

        if (string.Equals(toolName, "memory", StringComparison.OrdinalIgnoreCase))
        {
            return AssessMemoryCommand(normalizedCommand);
        }

        if (toolName is "write file" or "append file" or "replace in file" or "move path" or "copy path" or "delete path")
        {
            return AssessFileMutationCommand(toolName, normalizedCommand);
        }

        if (string.Equals(toolName, "close app", StringComparison.OrdinalIgnoreCase))
        {
            return AssessCloseApp(normalizedCommand);
        }

        if (string.Equals(toolName, "send hotkey", StringComparison.OrdinalIgnoreCase))
        {
            return AssessSendHotkey(normalizedCommand);
        }

        if (string.Equals(toolName, "type into", StringComparison.OrdinalIgnoreCase))
        {
            return AssessSensitiveUiTyping(normalizedCommand);
        }

        if (string.Equals(toolName, "clipboard", StringComparison.OrdinalIgnoreCase))
        {
            return AssessClipboardCommand(normalizedCommand);
        }

        if (string.Equals(toolName, "code", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "discord", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "slack", StringComparison.OrdinalIgnoreCase))
        {
            return AssessSensitiveAppTextEntry(toolName, normalizedCommand);
        }

        if (!string.Equals(toolName, "run", StringComparison.OrdinalIgnoreCase))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        var shellCommand = ExtractShellCommand(normalizedCommand);

        if (string.IsNullOrWhiteSpace(shellCommand))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        var normalized = NormalizeCommand(shellCommand);

        if (ContainsAny(normalized, AlwaysBlockedSignals))
        {
            return Block(
                shellCommand,
                "That shell command is blocked because it uses a high-risk system operation.",
                "Blocked dangerous shell command.");
        }

        if (_options.SafetyBlockProtectedSystemPaths
            && TargetsProtectedLocation(normalized)
            && LooksStateChanging(normalized))
        {
            return Block(
                shellCommand,
                "That shell command is blocked because it would change a protected system location or HKLM registry scope.",
                "Blocked command targeting a protected system location.");
        }

        if (!_options.SafetyRequireApprovalForMajorActions || IsReadOnlyCommand(normalized, shellCommand))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        var impact = DescribeImpact(normalized);
        var summary = $"Approval required for a major shell action: {impact}.";
        var response = string.Join(
            Environment.NewLine,
            "Approval required before I run that shell command.",
            $"Planned change: {impact}.",
            $"Command: {TrimForDisplay(shellCommand)}",
            "Reply `approve` to continue or `deny` to cancel.");

        return new SafetyDecision(
            SafetyDecisionKind.RequireApproval,
            response,
            $"Execution is paused pending user approval for `{TrimForInline(shellCommand)}`.",
            summary);
    }

    private static SafetyDecision AssessSensitiveUiTyping(string normalizedCommand)
    {
        if (!UiCommandParsing.TryParseTypeInto(normalizedCommand, out var target, out var text)
            || string.IsNullOrWhiteSpace(text)
            || !LooksLikeSensitiveUiPayload(text))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        var response = string.Join(
            Environment.NewLine,
            "Approval required before I type that sensitive text into a window.",
            $"Target: {TrimForDisplay(target)}",
            $"Detected content: {DescribeSensitivePayload(text)}",
            "Reply `approve` to continue or `deny` to cancel.");

        return new SafetyDecision(
            SafetyDecisionKind.RequireApproval,
            response,
            $"Typing sensitive text into `{TrimForInline(target)}` is paused pending approval.",
            "Approval required for sensitive UI typing.");
    }

    private SafetyDecision AssessCloseApp(string normalizedCommand)
    {
        if (!_options.SafetyRequireApprovalForMajorActions
            || !UiCommandParsing.TryParseCloseTarget(normalizedCommand, out var target)
            || string.IsNullOrWhiteSpace(target))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        return RequireUiApproval(
            $"close `{TrimForDisplay(target)}`",
            $"Closing `{TrimForInline(target)}` is paused pending approval.",
            "Approval required before I close that app window.",
            "Approval required for closing an app window.");
    }

    private SafetyDecision AssessSendHotkey(string normalizedCommand)
    {
        if (!_options.SafetyRequireApprovalForMajorActions
            || !UiCommandParsing.TryParseHotkeyCommand(normalizedCommand, out var target, out var hotkey)
            || string.IsNullOrWhiteSpace(target)
            || string.IsNullOrWhiteSpace(hotkey))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        return RequireUiApproval(
            $"send `{TrimForDisplay(hotkey)}` to `{TrimForDisplay(target)}`",
            $"Sending `{TrimForInline(hotkey)}` to `{TrimForInline(target)}` is paused pending approval.",
            "Approval required before I send that hotkey.",
            "Approval required for a UI hotkey action.");
    }

    private SafetyDecision AssessClipboardCommand(string normalizedCommand)
    {
        if (!UiCommandParsing.TryParseClipboardCommand(normalizedCommand, out var action, out var payload))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        if (string.Equals(action, "set", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(payload)
            && LooksLikeSensitiveUiPayload(payload))
        {
            return RequireUiApproval(
                $"copy sensitive-looking text to the clipboard ({DescribeSensitivePayload(payload)})",
                "Copying sensitive-looking text to the clipboard is paused pending approval.",
                "Approval required before I copy that sensitive text to the clipboard.",
                "Approval required for sensitive clipboard content.");
        }

        if (!_options.SafetyRequireApprovalForMajorActions
            || !string.Equals(action, "paste", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(payload))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        return RequireUiApproval(
            $"paste clipboard contents into `{TrimForDisplay(payload)}`",
            $"Pasting clipboard contents into `{TrimForInline(payload)}` is paused pending approval.",
            "Approval required before I paste clipboard contents into that window.",
            "Approval required for a clipboard paste action.");
    }

    private static SafetyDecision AssessSensitiveAppTextEntry(string toolName, string normalizedCommand)
    {
        if (!AppIntegrationParsing.TryExtractSensitiveTextPayload(toolName, normalizedCommand, out var targetApp, out var text)
            || !LooksLikeSensitiveUiPayload(text))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        return RequireUiApproval(
            $"send sensitive-looking text to `{TrimForDisplay(targetApp)}` ({DescribeSensitivePayload(text)})",
            $"Sending sensitive-looking text to `{TrimForInline(targetApp)}` is paused pending approval.",
            "Approval required before I send that sensitive text into the app.",
            "Approval required for sensitive app text entry.");
    }

    private SafetyDecision AssessChromeCommand(string normalizedCommand)
    {
        if (!AppIntegrationParsing.TryParseChrome(normalizedCommand, out var command))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        if (command.Action == "login" && !string.IsNullOrWhiteSpace(command.Payload))
        {
            return RequireUiApproval(
                "fill login credentials into Chrome",
                "Chrome login automation is paused pending approval.",
                "Approval required before I fill login credentials in Chrome.",
                "Approval required for Chrome login automation.");
        }

        if ((command.Action == "fill" || command.Action == "form")
            && !string.IsNullOrWhiteSpace(command.Payload)
            && LooksLikeSensitiveUiPayload(command.Payload))
        {
            return RequireUiApproval(
                $"fill sensitive-looking text into Chrome ({DescribeSensitivePayload(command.Payload)})",
                "Sensitive Chrome form-fill content is paused pending approval.",
                "Approval required before I fill that sensitive text in Chrome.",
                "Approval required for sensitive Chrome form fill.");
        }

        if (command.Action == "dom-type"
            && ChromeDevToolsAutomation.TryParseSelectorAndText(command.Payload, out var selector, out var domText)
            && (LooksLikeSensitiveUiPayload(domText)
                || selector.Contains("password", StringComparison.OrdinalIgnoreCase)))
        {
            return RequireUiApproval(
                $"fill sensitive-looking text into Chrome DOM target `{TrimForDisplay(selector)}`",
                "Sensitive Chrome DOM text entry is paused pending approval.",
                "Approval required before I fill that sensitive text in Chrome.",
                "Approval required for sensitive Chrome DOM text entry.");
        }

        return new SafetyDecision(SafetyDecisionKind.Allow);
    }

    private SafetyDecision AssessMacroCommand(string normalizedCommand)
    {
        if (!AutomationWorkflowParsing.TryParseMacro(normalizedCommand, out var command))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        if (command.Action is not ("run" or "rollback"))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        return RequireUiApproval(
            $"{command.Action} macro `{TrimForDisplay(command.Name)}`",
            $"Macro `{TrimForInline(command.Name)}` is paused pending approval.",
            "Approval required before I run that automation macro.",
            "Approval required for macro execution.");
    }

    private SafetyDecision AssessMemoryCommand(string normalizedCommand)
    {
        if (!_options.SafetyRequireApprovalForMajorActions
            || !MemoryCommandParsing.TryParse(normalizedCommand, out var command))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        var impact = command.Action switch
        {
            "set-retention" => "change memory retention",
            "set-importance" => "change memory importance",
            "merge" => "merge two memories and forget the secondary copy",
            "maintain-apply" => "apply memory maintenance changes",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(impact))
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        return RequireUiApproval(
            impact,
            "The memory update is paused pending approval.",
            "Approval required before I change persisted memory data.",
            "Approval required for a memory mutation command.");
    }

    private SafetyDecision AssessFileMutationCommand(string toolName, string normalizedCommand)
    {
        var hasValidPayload = toolName switch
        {
            "write file" => AutomationWorkflowParsing.TryParsePathAndContent(normalizedCommand, out _, out _, "write file", "create file", "save file"),
            "append file" => AutomationWorkflowParsing.TryParsePathAndContent(normalizedCommand, out _, out _, "append file", "add to file"),
            "replace in file" => AutomationWorkflowParsing.TryParseReplaceInFile(normalizedCommand, out _, out _, out _),
            "move path" => AutomationWorkflowParsing.TryParseSourceAndDestination(normalizedCommand, out _, out _, "move path", "move file", "move folder"),
            "copy path" => AutomationWorkflowParsing.TryParseSourceAndDestination(normalizedCommand, out _, out _, "copy path", "copy file", "copy folder"),
            "delete path" => !string.IsNullOrWhiteSpace(ToolInputHelper.GetCommandTail(normalizedCommand, "delete path", "delete file", "delete folder")),
            _ => false
        };

        if (!hasValidPayload)
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        var flattened = NormalizeCommand(normalizedCommand);

        if (_options.SafetyBlockProtectedSystemPaths
            && TargetsProtectedLocation(flattened))
        {
            return new SafetyDecision(
                SafetyDecisionKind.Block,
                string.Join(
                    Environment.NewLine,
                    "Blocked for safety.",
                    "That file operation targets a protected system location.",
                    $"Command: {TrimForDisplay(normalizedCommand)}"),
                $"The runtime blocked `{TrimForInline(normalizedCommand)}`.",
                "Blocked protected file operation.");
        }

        if (!_options.SafetyRequireApprovalForMajorActions)
        {
            return new SafetyDecision(SafetyDecisionKind.Allow);
        }

        var impact = toolName switch
        {
            "write file" => "create or overwrite a file",
            "append file" => "append to a file",
            "replace in file" => "edit file contents",
            "move path" => "move a file or folder",
            "copy path" => "copy a file or folder",
            "delete path" => "delete a file or folder",
            _ => "change files on disk"
        };

        var response = string.Join(
            Environment.NewLine,
            "Approval required before I change files on disk.",
            $"Planned change: {impact}.",
            $"Command: {TrimForDisplay(normalizedCommand)}",
            "Reply `approve` to continue or `deny` to cancel.");

        return new SafetyDecision(
            SafetyDecisionKind.RequireApproval,
            response,
            $"The file change `{TrimForInline(normalizedCommand)}` is paused pending approval.",
            "Approval required for a file mutation command.");
    }

    private static bool LooksLikeSensitiveUiPayload(string value)
    {
        if (MemoryAutomation.LooksSensitive(value))
        {
            return true;
        }

        var trimmed = value.Trim();

        if (trimmed.Length < 10 || trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var hasLower = trimmed.Any(char.IsLower);
        var hasUpper = trimmed.Any(char.IsUpper);
        var hasDigit = trimmed.Any(char.IsDigit);
        var hasSymbol = trimmed.Any(character => char.IsPunctuation(character) || char.IsSymbol(character));

        return hasDigit && hasLower && (hasUpper || hasSymbol);
    }

    private static SafetyDecision RequireUiApproval(string impact, string verificationText, string header, string summary)
    {
        var response = string.Join(
            Environment.NewLine,
            header,
            $"Planned action: {impact}.",
            "Reply `approve` to continue or `deny` to cancel.");

        return new SafetyDecision(
            SafetyDecisionKind.RequireApproval,
            response,
            verificationText,
            summary);
    }

    public bool LooksLikeApprovalResponse(string input)
    {
        var normalized = NormalizeInput(input);
        return normalized is "approve"
            or "approved"
            or "yes"
            or "yes do it"
            or "yes proceed"
            or "proceed"
            or "go ahead"
            or "do it"
            or "run it"
            or "continue";
    }

    public bool LooksLikeDenialResponse(string input)
    {
        var normalized = NormalizeInput(input);
        return normalized is "deny"
            or "denied"
            or "no"
            or "no stop"
            or "stop"
            or "cancel"
            or "cancel it"
            or "dont do it"
            or "do not do it";
    }

    private SafetyDecision Block(string shellCommand, string reason, string summary)
    {
        var response = string.Join(
            Environment.NewLine,
            "Blocked for safety.",
            reason,
            $"Command: {TrimForDisplay(shellCommand)}",
            "Jarvis will not use shell access to change Windows, System32, Program Files, ProgramData, or HKLM registry locations.");

        return new SafetyDecision(
            SafetyDecisionKind.Block,
            response,
            $"The runtime blocked `{TrimForInline(shellCommand)}`.",
            summary);
    }

    private static string ExtractShellCommand(string normalizedCommand)
    {
        if (normalizedCommand.StartsWith("run ", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCommand["run ".Length..].Trim();
        }

        if (normalizedCommand.StartsWith("shell ", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCommand["shell ".Length..].Trim();
        }

        return string.Empty;
    }

    private static string NormalizeCommand(string command)
    {
        var flattened = command.ReplaceLineEndings(" ");
        var tokens = flattened.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return $" {string.Join(" ", tokens).ToLowerInvariant()} ";
    }

    private static string NormalizeInput(string input)
    {
        var flattened = input.ReplaceLineEndings(" ");
        var tokens = flattened
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim(' ', '\t', '\r', '\n', '.', ',', '!', '?', ':', ';', '"', '\''))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
        return string.Join(" ", tokens).ToLowerInvariant();
    }

    private static bool ContainsAny(string normalized, IEnumerable<string> patterns) =>
        patterns.Any(pattern => normalized.Contains(pattern, StringComparison.Ordinal));

    private bool TargetsProtectedLocation(string normalized) =>
        _protectedFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));

    private static bool LooksStateChanging(string normalized)
    {
        if (normalized.Contains(" >>", StringComparison.Ordinal)
            || normalized.Contains(" >", StringComparison.Ordinal))
        {
            return true;
        }

        return ContainsAny(normalized, StateChangingSignals);
    }

    private static bool IsReadOnlyCommand(string normalized, string originalCommand)
    {
        if (LooksStateChanging(normalized))
        {
            return false;
        }

        var trimmed = originalCommand.Trim();
        return ReadOnlyStarts.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeImpact(string normalized)
    {
        var categories = new List<string>();

        if (ContainsAny(normalized, new[] { " remove-item ", " del ", " erase ", " rm ", " rmdir ", " rd " }))
        {
            categories.Add("delete files or folders");
        }

        if (ContainsAny(normalized, new[] { " set-content ", " add-content ", " out-file ", " new-item ", " move-item ", " copy-item ", " rename-item ", " >", " >>" }))
        {
            categories.Add("create or modify files");
        }

        if (ContainsAny(normalized, new[] { " winget ", " choco ", " msiexec ", " npm install ", " npm uninstall ", " pnpm add ", " pnpm remove ", " yarn add ", " yarn remove ", " pip install ", " pip uninstall ", " dotnet add ", " dotnet remove ", " dotnet tool install ", " dotnet tool uninstall " }))
        {
            categories.Add("install or remove software");
        }

        if (ContainsAny(normalized, new[] { " reg add ", " reg delete ", " reg import ", " set-itemproperty ", " new-itemproperty ", " remove-itemproperty ", " hklm: ", " registry::hkey_local_machine " }))
        {
            categories.Add("change registry settings");
        }

        if (ContainsAny(normalized, new[] { " start-process ", " stop-process ", " taskkill ", " stop-service ", " start-service ", " restart-service ", " new-service ", " set-service ", " sc.exe ", " sc ", " schtasks /create ", " schtasks /delete " }))
        {
            categories.Add("change running apps, processes, or services");
        }

        if (ContainsAny(normalized, new[] { " shutdown ", " restart-computer ", " stop-computer ", " set-executionpolicy ", " netsh ", " bcdedit " }))
        {
            categories.Add("change system or network behavior");
        }

        if (categories.Count == 0)
        {
            categories.Add("change the machine state");
        }

        return string.Join(", ", categories.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string DescribeSensitivePayload(string value)
    {
        var count = value.Trim().Length;
        return count == 1
            ? "1 character of sensitive-looking text"
            : $"{count} characters of sensitive-looking text";
    }

    private string[] BuildProtectedFragments()
    {
        var fragments = new HashSet<string>(StringComparer.Ordinal)
        {
            " c:\\windows ",
            " c:\\windows\\system32 ",
            " c:\\windows\\syswow64 ",
            " $env:windir ",
            " ${env:windir} ",
            " %windir% ",
            " system32 ",
            " syswow64 ",
            " hklm: ",
            " registry::hkey_local_machine "
        };

        AddFolderFragment(fragments, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        AddFolderFragment(fragments, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"));
        AddFolderFragment(fragments, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddFolderFragment(fragments, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddFolderFragment(fragments, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        fragments.Add(" c:\\program files ");
        fragments.Add(" c:\\program files (x86) ");
        fragments.Add(" c:\\programdata ");
        fragments.Add(" $env:programfiles ");
        fragments.Add(" ${env:programfiles} ");
        fragments.Add(" %programfiles% ");
        fragments.Add(" $env:programdata ");
        fragments.Add(" ${env:programdata} ");
        fragments.Add(" %programdata% ");

        return fragments.ToArray();
    }

    private static void AddFolderFragment(ISet<string> target, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = path.Trim().Replace('/', '\\').ToLowerInvariant();
        target.Add($" {normalized} ");
    }

    private static string TrimForDisplay(string shellCommand)
    {
        const int maxLength = 320;
        var flattened = shellCommand.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..maxLength] + "...";
    }

    private static string TrimForInline(string shellCommand)
    {
        const int maxLength = 120;
        var flattened = shellCommand.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..maxLength] + "...";
    }
}
