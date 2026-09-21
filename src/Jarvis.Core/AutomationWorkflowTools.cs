using System.Text.Json;

namespace Jarvis.Core;

public sealed class WriteFileTool : IAssistantTool
{
    public string Name => "write file";

    public string Description => "Create or overwrite a text file. Format: `write file <path> :: <content>`.";

    public bool CanHandle(string input) =>
        ToolInputHelper.StartsWithCommand(input, Name) ||
        ToolInputHelper.StartsWithCommand(input, "create file") ||
        ToolInputHelper.StartsWithCommand(input, "save file");

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!AutomationWorkflowParsing.TryParsePathAndContent(input, out var path, out var content, "write file", "create file", "save file"))
        {
            return new ToolResult("Usage: write file <path> :: <content>", Succeeded: false, SummaryText: "Missing file path or content.");
        }

        var result = await context.DesktopAutomation.WriteFileAsync(path, content, append: false, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, Succeeded: success, VerificationText: result, SummaryText: success ? "Wrote file." : "Write file failed.");
    }
}

public sealed class AppendFileTool : IAssistantTool
{
    public string Name => "append file";

    public string Description => "Append text to a file. Format: `append file <path> :: <content>`.";

    public bool CanHandle(string input) =>
        ToolInputHelper.StartsWithCommand(input, Name) ||
        ToolInputHelper.StartsWithCommand(input, "add to file");

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!AutomationWorkflowParsing.TryParsePathAndContent(input, out var path, out var content, "append file", "add to file"))
        {
            return new ToolResult("Usage: append file <path> :: <content>", Succeeded: false, SummaryText: "Missing file path or content.");
        }

        var result = await context.DesktopAutomation.WriteFileAsync(path, content, append: true, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, Succeeded: success, VerificationText: result, SummaryText: success ? "Appended file." : "Append file failed.");
    }
}

public sealed class ReplaceInFileTool : IAssistantTool
{
    public string Name => "replace in file";

    public string Description => "Replace text in a file. Format: `replace in file <path> :: <find> :: <replace>`.";

    public bool CanHandle(string input) =>
        ToolInputHelper.StartsWithCommand(input, Name) ||
        ToolInputHelper.StartsWithCommand(input, "edit file");

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!AutomationWorkflowParsing.TryParseReplaceInFile(input, out var path, out var findText, out var replaceText))
        {
            return new ToolResult("Usage: replace in file <path> :: <find> :: <replace>", Succeeded: false, SummaryText: "Missing file replace arguments.");
        }

        var result = await context.DesktopAutomation.ReplaceInFileAsync(path, findText, replaceText, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, Succeeded: success, VerificationText: result, SummaryText: success ? "Edited file." : "Edit file failed.");
    }
}

public sealed class MovePathTool : IAssistantTool
{
    public string Name => "move path";

    public string Description => "Move a file or folder. Format: `move path <source> :: <destination>`.";

    public bool CanHandle(string input) =>
        ToolInputHelper.StartsWithCommand(input, Name) ||
        ToolInputHelper.StartsWithCommand(input, "move file") ||
        ToolInputHelper.StartsWithCommand(input, "move folder");

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!AutomationWorkflowParsing.TryParseSourceAndDestination(input, out var source, out var destination, "move path", "move file", "move folder"))
        {
            return new ToolResult("Usage: move path <source> :: <destination>", Succeeded: false, SummaryText: "Missing move source or destination.");
        }

        var result = await context.DesktopAutomation.MovePathAsync(source, destination, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, Succeeded: success, VerificationText: result, SummaryText: success ? "Moved path." : "Move path failed.");
    }
}

public sealed class CopyPathTool : IAssistantTool
{
    public string Name => "copy path";

    public string Description => "Copy a file or folder. Format: `copy path <source> :: <destination>`.";

    public bool CanHandle(string input) =>
        ToolInputHelper.StartsWithCommand(input, Name) ||
        ToolInputHelper.StartsWithCommand(input, "copy file") ||
        ToolInputHelper.StartsWithCommand(input, "copy folder");

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!AutomationWorkflowParsing.TryParseSourceAndDestination(input, out var source, out var destination, "copy path", "copy file", "copy folder"))
        {
            return new ToolResult("Usage: copy path <source> :: <destination>", Succeeded: false, SummaryText: "Missing copy source or destination.");
        }

        var result = await context.DesktopAutomation.CopyPathAsync(source, destination, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result);
        return new ToolResult(result, Succeeded: success, VerificationText: result, SummaryText: success ? "Copied path." : "Copy path failed.");
    }
}

public sealed class DeletePathTool : IAssistantTool
{
    public string Name => "delete path";

    public string Description => "Delete a file or folder. Format: `delete path <target>`.";

    public bool CanHandle(string input) =>
        ToolInputHelper.StartsWithCommand(input, Name) ||
        ToolInputHelper.StartsWithCommand(input, "delete file") ||
        ToolInputHelper.StartsWithCommand(input, "delete folder");

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "delete path", "delete file", "delete folder").Trim();

        if (string.IsNullOrWhiteSpace(target))
        {
            return new ToolResult("Usage: delete path <target>", Succeeded: false, SummaryText: "Missing delete target.");
        }

        var result = await context.DesktopAutomation.DeletePathAsync(target, cancellationToken);
        var success = !UiCommandParsing.LooksLikeFailure(result) && !result.StartsWith("Refusing ", StringComparison.OrdinalIgnoreCase);
        return new ToolResult(result, Succeeded: success, VerificationText: result, SummaryText: success ? "Deleted path." : "Delete path failed.");
    }
}

public sealed class AppStateTool : IAssistantTool
{
    public string Name => "app state";

    public string Description => "Inspect richer app state. Format: `app state <app>`.";

    public bool CanHandle(string input) =>
        ToolInputHelper.StartsWithCommand(input, Name) ||
        ToolInputHelper.StartsWithCommand(input, "application state");

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var target = ToolInputHelper.GetCommandTail(input, "app state", "application state");

        if (string.IsNullOrWhiteSpace(target))
        {
            return new ToolResult("Usage: app state <app>", Succeeded: false, SummaryText: "Missing app state target.");
        }

        var result = await context.DesktopAutomation.GetApplicationStateAsync(target, cancellationToken);
        var success = !result.StartsWith("No matching", StringComparison.OrdinalIgnoreCase)
            && !result.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase);
        return new ToolResult(result, Succeeded: success, VerificationText: result, SummaryText: success ? "Read app state." : "App state lookup failed.");
    }
}

public sealed class NotifyTool : IAssistantTool
{
    public string Name => "notify";

    public string Description => "Send or inspect Jarvis notifications. Use `notify <title> :: <message>`, `notifications`, or `clear notifications`.";

    public bool CanHandle(string input) => AutomationWorkflowParsing.TryParseNotification(input, out _);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!AutomationWorkflowParsing.TryParseNotification(input, out var command))
        {
            return new ToolResult("Usage: notify <title> :: <message> | notifications | clear notifications", Succeeded: false, SummaryText: "Missing notification command.");
        }

        if (command.Action == "send"
            && string.IsNullOrWhiteSpace(command.Title)
            && string.IsNullOrWhiteSpace(command.Message))
        {
            return new ToolResult("Usage: notify <title> :: <message> | notify <message>", Succeeded: false, SummaryText: "Missing notification content.");
        }

        var result = command.Action switch
        {
            "send" => await context.DesktopAutomation.SendNotificationAsync(command.Title, command.Message, cancellationToken),
            "list" => await context.DesktopAutomation.ListNotificationsAsync(cancellationToken),
            "clear" => await context.DesktopAutomation.ClearNotificationsAsync(cancellationToken),
            _ => "Usage: notify <title> :: <message> | notifications | clear notifications"
        };

        var success = !result.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase);
        return new ToolResult(result, Succeeded: success, VerificationText: result, SummaryText: success ? "Handled notification action." : "Notification action failed.");
    }
}

public sealed class MacroTool : IAssistantTool
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string Name => "macro";

    public string Description => "Store and run repeatable multi-step workflows. Use `macro save`, `macro run`, `macro rollback`, `macro show`, `macro list`, or `macro delete`.";

    public bool CanHandle(string input) => AutomationWorkflowParsing.TryParseMacro(input, out _);

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (!AutomationWorkflowParsing.TryParseMacro(input, out var command))
        {
            return new ToolResult(
                "Usage: macro save <name> :: <step1> ;; <step2> [:: rollback :: <undo1> ;; <undo2>] | macro run <name> | macro rollback <name> | macro show <name> | macro list | macro delete <name>",
                Succeeded: false,
                SummaryText: "Missing macro command.");
        }

        var store = new MacroStore(Path.Combine(context.WorkspaceRoot, "data", "automation", "macros.json"));
        var macros = await store.LoadAsync(cancellationToken);

        switch (command.Action)
        {
            case "list":
                return RenderMacroList(macros);
            case "show":
                return RenderMacroDetails(macros, command.Name);
            case "delete":
                return await DeleteMacroAsync(store, macros, command.Name, cancellationToken);
            case "save":
                return await SaveMacroAsync(store, macros, command, cancellationToken);
            case "set-rollback":
                return await SetRollbackAsync(store, macros, command, cancellationToken);
            case "run":
                return await RunMacroAsync(store, macros, command.Name, rollbackOnly: false, context, cancellationToken);
            case "rollback":
                return await RunMacroAsync(store, macros, command.Name, rollbackOnly: true, context, cancellationToken);
            default:
                return new ToolResult("Unsupported macro command.", Succeeded: false, SummaryText: "Unsupported macro command.");
        }
    }

    private static ToolResult RenderMacroList(IReadOnlyList<AutomationMacroDefinition> macros)
    {
        if (macros.Count == 0)
        {
            return new ToolResult("No macros are saved yet.", VerificationText: "The macro store is empty.", SummaryText: "No saved macros.");
        }

        var lines = macros
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                var status = item.LastRun is null
                    ? "never run"
                    : item.LastRun.Succeeded
                        ? $"last run ok {item.LastRun.RunAtUtc.ToLocalTime():MM-dd h:mm tt}"
                        : $"last run failed {item.LastRun.RunAtUtc.ToLocalTime():MM-dd h:mm tt}";
                return $"- {item.Name} | steps {item.Steps.Length} | rollback {item.RollbackSteps.Length} | {status}";
            });

        return new ToolResult(
            "Saved macros:" + Environment.NewLine + string.Join(Environment.NewLine, lines),
            VerificationText: $"Loaded {macros.Count} saved macro(s).",
            SummaryText: "Listed saved macros.");
    }

    private static ToolResult RenderMacroDetails(IReadOnlyList<AutomationMacroDefinition> macros, string name)
    {
        var macro = FindMacro(macros, name);

        if (macro is null)
        {
            return new ToolResult($"Macro not found: {name}", Succeeded: false, VerificationText: "The requested macro name was not found.", SummaryText: "Macro lookup failed.");
        }

        var lines = new List<string>
        {
            $"Macro: {macro.Name}",
            $"Created: {macro.CreatedAtUtc.ToLocalTime():yyyy-MM-dd h:mm tt}",
            $"Updated: {macro.UpdatedAtUtc.ToLocalTime():yyyy-MM-dd h:mm tt}",
            "Steps:"
        };

        lines.AddRange(macro.Steps.Select((step, index) => $"- {index + 1}. {step}"));

        if (macro.RollbackSteps.Length > 0)
        {
            lines.Add("Rollback:");
            lines.AddRange(macro.RollbackSteps.Select((step, index) => $"- {index + 1}. {step}"));
        }

        if (macro.LastRun is not null)
        {
            lines.Add($"Last run: {(macro.LastRun.Succeeded ? "success" : "failure")} at {macro.LastRun.RunAtUtc.ToLocalTime():yyyy-MM-dd h:mm tt}");
            lines.Add($"Last run summary: {macro.LastRun.Summary}");
        }

        return new ToolResult(string.Join(Environment.NewLine, lines), VerificationText: $"Loaded macro `{macro.Name}`.", SummaryText: "Showed macro details.");
    }

    private static async Task<ToolResult> DeleteMacroAsync(
        MacroStore store,
        IReadOnlyList<AutomationMacroDefinition> macros,
        string name,
        CancellationToken cancellationToken)
    {
        var macro = FindMacro(macros, name);

        if (macro is null)
        {
            return new ToolResult($"Macro not found: {name}", Succeeded: false, VerificationText: "The requested macro name was not found.", SummaryText: "Macro delete failed.");
        }

        var updated = macros
            .Where(item => !string.Equals(item.Name, macro.Name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        await store.SaveAsync(updated, cancellationToken);
        return new ToolResult($"Deleted macro: {macro.Name}", VerificationText: $"Removed macro `{macro.Name}` from the local store.", SummaryText: "Deleted macro.");
    }

    private static async Task<ToolResult> SaveMacroAsync(
        MacroStore store,
        IReadOnlyList<AutomationMacroDefinition> macros,
        MacroCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return new ToolResult("Macro name is required.", Succeeded: false, SummaryText: "Missing macro name.");
        }

        if (command.Steps.Length == 0)
        {
            return new ToolResult("At least one macro step is required.", Succeeded: false, SummaryText: "Missing macro steps.");
        }

        if (!ValidateMacroSteps(command.Steps, out var validationError)
            || !ValidateMacroSteps(command.RollbackSteps, out validationError))
        {
            return new ToolResult(validationError, Succeeded: false, VerificationText: validationError, SummaryText: "Macro validation failed.");
        }

        var existing = FindMacro(macros, command.Name);
        var now = DateTimeOffset.UtcNow;
        var saved = new AutomationMacroDefinition(
            command.Name.Trim(),
            command.Steps,
            command.RollbackSteps,
            existing?.CreatedAtUtc ?? now,
            now,
            existing?.LastRun);
        var updated = macros
            .Where(item => !string.Equals(item.Name, saved.Name, StringComparison.OrdinalIgnoreCase))
            .Append(saved)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await store.SaveAsync(updated, cancellationToken);

        var response = saved.RollbackSteps.Length == 0
            ? $"Saved macro `{saved.Name}` with {saved.Steps.Length} step(s)."
            : $"Saved macro `{saved.Name}` with {saved.Steps.Length} step(s) and {saved.RollbackSteps.Length} rollback step(s).";
        return new ToolResult(response, VerificationText: $"Persisted macro `{saved.Name}` to the local automation store.", SummaryText: "Saved macro.");
    }

    private static async Task<ToolResult> SetRollbackAsync(
        MacroStore store,
        IReadOnlyList<AutomationMacroDefinition> macros,
        MacroCommand command,
        CancellationToken cancellationToken)
    {
        var macro = FindMacro(macros, command.Name);

        if (macro is null)
        {
            return new ToolResult($"Macro not found: {command.Name}", Succeeded: false, VerificationText: "The requested macro name was not found.", SummaryText: "Rollback update failed.");
        }

        if (command.RollbackSteps.Length == 0)
        {
            return new ToolResult("At least one rollback step is required.", Succeeded: false, SummaryText: "Missing rollback steps.");
        }

        if (!ValidateMacroSteps(command.RollbackSteps, out var validationError))
        {
            return new ToolResult(validationError, Succeeded: false, VerificationText: validationError, SummaryText: "Rollback validation failed.");
        }

        var updatedMacro = macro with
        {
            RollbackSteps = command.RollbackSteps,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var updated = macros
            .Where(item => !string.Equals(item.Name, updatedMacro.Name, StringComparison.OrdinalIgnoreCase))
            .Append(updatedMacro)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await store.SaveAsync(updated, cancellationToken);

        return new ToolResult(
            $"Updated rollback steps for `{updatedMacro.Name}`.",
            VerificationText: $"Persisted {updatedMacro.RollbackSteps.Length} rollback step(s) for `{updatedMacro.Name}`.",
            SummaryText: "Updated macro rollback.");
    }

    private static async Task<ToolResult> RunMacroAsync(
        MacroStore store,
        IReadOnlyList<AutomationMacroDefinition> macros,
        string name,
        bool rollbackOnly,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var macro = FindMacro(macros, name);

        if (macro is null)
        {
            return new ToolResult($"Macro not found: {name}", Succeeded: false, VerificationText: "The requested macro name was not found.", SummaryText: "Macro run failed.");
        }

        var stepsToRun = rollbackOnly ? macro.RollbackSteps : macro.Steps;

        if (stepsToRun.Length == 0)
        {
            return new ToolResult(
                rollbackOnly
                    ? $"Macro `{macro.Name}` does not have rollback steps."
                    : $"Macro `{macro.Name}` does not have any steps.",
                Succeeded: false,
                SummaryText: "Macro run failed.");
        }

        var executed = new List<string>();
        var rollbackExecuted = new List<string>();
        ToolResult? failure = null;

        foreach (var step in stepsToRun)
        {
            var result = await context.ExecuteAutomationCommandAsync(step, bypassApproval: true, cancellationToken);
            executed.Add($"{step} => {SummarizeStep(result)}");

            if (!result.Succeeded || result.ShouldExit)
            {
                failure = result;
                break;
            }
        }

        if (!rollbackOnly && failure is not null && macro.RollbackSteps.Length > 0)
        {
            foreach (var rollbackStep in macro.RollbackSteps)
            {
                var rollbackResult = await context.ExecuteAutomationCommandAsync(rollbackStep, bypassApproval: true, cancellationToken);
                rollbackExecuted.Add($"{rollbackStep} => {SummarizeStep(rollbackResult)}");

                if (!rollbackResult.Succeeded || rollbackResult.ShouldExit)
                {
                    break;
                }
            }
        }

        var succeeded = failure is null;
        var summary = succeeded
            ? rollbackOnly
                ? $"Ran rollback for `{macro.Name}`."
                : $"Ran macro `{macro.Name}`."
            : rollbackExecuted.Count == 0
                ? $"Macro `{macro.Name}` failed."
                : $"Macro `{macro.Name}` failed and attempted rollback.";
        var lastRun = new AutomationMacroRunSummary(
            DateTimeOffset.UtcNow,
            succeeded,
            summary,
            executed.ToArray(),
            rollbackExecuted.ToArray());
        var updatedMacro = macro with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            LastRun = lastRun
        };
        var updatedMacros = macros
            .Where(item => !string.Equals(item.Name, updatedMacro.Name, StringComparison.OrdinalIgnoreCase))
            .Append(updatedMacro)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await store.SaveAsync(updatedMacros, cancellationToken);

        var lines = new List<string>
        {
            summary,
            "Executed steps:"
        };
        lines.AddRange(executed.Select(step => $"- {step}"));

        if (rollbackExecuted.Count > 0)
        {
            lines.Add("Rollback steps:");
            lines.AddRange(rollbackExecuted.Select(step => $"- {step}"));
        }

        if (failure is not null)
        {
            lines.Add("Failure detail:");
            lines.Add(failure.ResponseText);
        }

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            Succeeded: succeeded,
            VerificationText: summary,
            SummaryText: summary);
    }

    private static AutomationMacroDefinition? FindMacro(IEnumerable<AutomationMacroDefinition> macros, string name)
    {
        return macros.FirstOrDefault(item => string.Equals(item.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidateMacroSteps(IEnumerable<string> steps, out string error)
    {
        foreach (var step in steps)
        {
            var trimmed = step.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                error = "Macro steps cannot be empty.";
                return false;
            }

            if (ToolInputHelper.StartsWithCommand(trimmed, "macro")
                || ToolInputHelper.StartsWithCommand(trimmed, "workflow"))
            {
                error = "Nested macro execution is blocked to avoid runaway recursion.";
                return false;
            }

            if (ToolInputHelper.MatchesExact(trimmed, "approve", "deny", "exit", "quit", "shutdown"))
            {
                error = $"Unsupported macro step: {trimmed}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static string SummarizeStep(ToolResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.SummaryText))
        {
            return result.SummaryText.Trim();
        }

        var flattened = result.ResponseText.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= 120 ? flattened : flattened[..120] + "...";
    }

    private sealed class MacroStore
    {
        private readonly string _path;

        public MacroStore(string path)
        {
            _path = path;
        }

        public async Task<IReadOnlyList<AutomationMacroDefinition>> LoadAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return [];
                }

                await using var stream = File.OpenRead(_path);
                var document = await JsonSerializer.DeserializeAsync<List<AutomationMacroDefinition>>(stream, JsonOptions, cancellationToken);
                return document ?? [];
            }
            catch
            {
                return [];
            }
        }

        public async Task SaveAsync(IReadOnlyList<AutomationMacroDefinition> macros, CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, macros, JsonOptions, cancellationToken);
        }
    }
}

internal sealed record NotificationCommand(string Action, string Title = "", string Message = "");

internal sealed record MacroCommand(
    string Action,
    string Name = "",
    string[] Steps = null!,
    string[] RollbackSteps = null!);

internal sealed record AutomationMacroDefinition(
    string Name,
    string[] Steps,
    string[] RollbackSteps,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    AutomationMacroRunSummary? LastRun);

internal sealed record AutomationMacroRunSummary(
    DateTimeOffset RunAtUtc,
    bool Succeeded,
    string Summary,
    string[] ExecutedSteps,
    string[] RollbackStepsExecuted);

internal static class AutomationWorkflowParsing
{
    public static bool TryParsePathAndContent(string input, out string path, out string content, params string[] prefixes)
    {
        path = string.Empty;
        content = string.Empty;

        var remainder = ToolInputHelper.GetCommandTail(input, prefixes);

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var parts = remainder.Split(["::"], 2, StringSplitOptions.None | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return false;
        }

        path = parts[0].Trim().Trim('"');
        content = parts[1];
        return !string.IsNullOrWhiteSpace(path);
    }

    public static bool TryParseReplaceInFile(string input, out string path, out string findText, out string replaceText)
    {
        path = string.Empty;
        findText = string.Empty;
        replaceText = string.Empty;

        var remainder = ToolInputHelper.GetCommandTail(input, "replace in file", "edit file");

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var parts = remainder.Split(["::"], 3, StringSplitOptions.None | StringSplitOptions.TrimEntries);

        if (parts.Length < 3)
        {
            return false;
        }

        path = parts[0].Trim().Trim('"');
        findText = parts[1];
        replaceText = parts[2];
        return !string.IsNullOrWhiteSpace(path);
    }

    public static bool TryParseSourceAndDestination(string input, out string source, out string destination, params string[] prefixes)
    {
        source = string.Empty;
        destination = string.Empty;

        var remainder = ToolInputHelper.GetCommandTail(input, prefixes);

        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var parts = remainder.Split(["::"], 2, StringSplitOptions.None | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return false;
        }

        source = parts[0].Trim().Trim('"');
        destination = parts[1].Trim().Trim('"');
        return !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(destination);
    }

    public static bool TryParseNotification(string input, out NotificationCommand command)
    {
        if (ToolInputHelper.MatchesExact(input, "notifications", "show notifications", "list notifications"))
        {
            command = new NotificationCommand("list");
            return true;
        }

        if (ToolInputHelper.MatchesExact(input, "clear notifications", "notifications clear"))
        {
            command = new NotificationCommand("clear");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(input, "notify")
            || ToolInputHelper.StartsWithCommand(input, "notification"))
        {
            var payload = ToolInputHelper.GetCommandTail(input, "notify", "notification");

            if (string.IsNullOrWhiteSpace(payload))
            {
                command = new NotificationCommand("send");
                return true;
            }

            var parts = payload.Split(["::"], 2, StringSplitOptions.None | StringSplitOptions.TrimEntries);
            command = parts.Length == 1
                ? new NotificationCommand("send", "Jarvis", parts[0])
                : new NotificationCommand("send", string.IsNullOrWhiteSpace(parts[0]) ? "Jarvis" : parts[0], parts[1]);
            return true;
        }

        command = new NotificationCommand(string.Empty);
        return false;
    }

    public static bool TryParseMacro(string input, out MacroCommand command)
    {
        if (!TryGetMacroTail(input, out var tail))
        {
            command = new MacroCommand(string.Empty, Steps: [], RollbackSteps: []);
            return false;
        }

        if (string.IsNullOrWhiteSpace(tail) || tail.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            command = new MacroCommand("list", Steps: [], RollbackSteps: []);
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(tail, "show"))
        {
            command = new MacroCommand("show", ToolInputHelper.GetCommandTail(tail, "show"), [], []);
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(tail, "delete"))
        {
            command = new MacroCommand("delete", ToolInputHelper.GetCommandTail(tail, "delete"), [], []);
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(tail, "run"))
        {
            command = new MacroCommand("run", ToolInputHelper.GetCommandTail(tail, "run"), [], []);
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(tail, "rollback set"))
        {
            var payload = ToolInputHelper.GetCommandTail(tail, "rollback set");

            if (!TrySplitNameAndBlock(payload, out var name, out var rollbackBlock))
            {
                command = new MacroCommand("set-rollback", Steps: [], RollbackSteps: []);
                return true;
            }

            command = new MacroCommand(
                "set-rollback",
                name,
                [],
                SplitSteps(rollbackBlock));
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(tail, "rollback"))
        {
            command = new MacroCommand("rollback", ToolInputHelper.GetCommandTail(tail, "rollback"), [], []);
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(tail, "save"))
        {
            var payload = ToolInputHelper.GetCommandTail(tail, "save");

            if (!TrySplitNameAndBlock(payload, out var name, out var stepBlock))
            {
                command = new MacroCommand("save", Steps: [], RollbackSteps: []);
                return true;
            }

            string[] rollbackSteps = [];
            var rollbackMarker = ":: rollback ::";
            var rollbackIndex = stepBlock.IndexOf(rollbackMarker, StringComparison.OrdinalIgnoreCase);

            if (rollbackIndex >= 0)
            {
                rollbackSteps = SplitSteps(stepBlock[(rollbackIndex + rollbackMarker.Length)..]);
                stepBlock = stepBlock[..rollbackIndex];
            }

            command = new MacroCommand(
                "save",
                name,
                SplitSteps(stepBlock),
                rollbackSteps);
            return true;
        }

        command = new MacroCommand(string.Empty, Steps: [], RollbackSteps: []);
        return true;
    }

    private static bool TryGetMacroTail(string input, out string tail)
    {
        if (ToolInputHelper.StartsWithCommand(input, "macro"))
        {
            tail = ToolInputHelper.GetCommandTail(input, "macro");
            return true;
        }

        if (ToolInputHelper.StartsWithCommand(input, "workflow"))
        {
            tail = ToolInputHelper.GetCommandTail(input, "workflow");
            return true;
        }

        tail = string.Empty;
        return false;
    }

    private static bool TrySplitNameAndBlock(string payload, out string name, out string block)
    {
        name = string.Empty;
        block = string.Empty;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split(["::"], 2, StringSplitOptions.None | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return false;
        }

        name = parts[0].Trim().Trim('"');
        block = parts[1].Trim();
        return !string.IsNullOrWhiteSpace(name);
    }

    private static string[] SplitSteps(string block)
    {
        return block
            .Split([";;"], StringSplitOptions.None | StringSplitOptions.TrimEntries)
            .Select(step => step.Trim())
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .ToArray();
    }
}
