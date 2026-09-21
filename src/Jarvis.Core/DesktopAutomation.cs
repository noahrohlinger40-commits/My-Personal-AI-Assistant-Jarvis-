using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Jarvis.Core;

public interface IDesktopAutomationService
{
    Task<string> GetSystemStatusAsync(CancellationToken cancellationToken);

    Task<string> GetComputerContextAsync(CancellationToken cancellationToken);

    Task<DesktopContextSnapshot> GetContextSnapshotAsync(CancellationToken cancellationToken);

    Task<string> GetLocalNetworkStatusAsync(string target, CancellationToken cancellationToken);

    Task<string> ListApplicationsAsync(string target, CancellationToken cancellationToken);

    Task<string> OpenApplicationAsync(string target, CancellationToken cancellationToken);

    Task<string> LaunchApplicationAsync(string target, string arguments, CancellationToken cancellationToken);

    Task<string> OpenPathAsync(string target, CancellationToken cancellationToken);

    Task<string> ListFilesAsync(string target, CancellationToken cancellationToken);

    Task<string> ReadFilePreviewAsync(string target, CancellationToken cancellationToken);

    Task<string> FindFilesAsync(string target, CancellationToken cancellationToken);

    Task<string> WriteFileAsync(string target, string content, bool append, CancellationToken cancellationToken);

    Task<string> ReplaceInFileAsync(string target, string findText, string replaceText, CancellationToken cancellationToken);

    Task<string> MovePathAsync(string source, string destination, CancellationToken cancellationToken);

    Task<string> CopyPathAsync(string source, string destination, CancellationToken cancellationToken);

    Task<string> DeletePathAsync(string target, CancellationToken cancellationToken);

    Task<string> BrowseAsync(string target, CancellationToken cancellationToken);

    Task<string> SearchWebAsync(string query, CancellationToken cancellationToken);

    Task<string> GetApplicationStateAsync(string target, CancellationToken cancellationToken);

    Task<string> ListOpenWindowsAsync(string target, CancellationToken cancellationToken);

    Task<string> FocusApplicationAsync(string targetApp, CancellationToken cancellationToken);

    Task<string> CloseApplicationAsync(string targetApp, CancellationToken cancellationToken);

    Task<string> SetWindowStateAsync(string targetApp, string state, CancellationToken cancellationToken);

    Task<string> MoveWindowAsync(string targetApp, int x, int y, CancellationToken cancellationToken);

    Task<string> ResizeWindowAsync(string targetApp, int width, int height, CancellationToken cancellationToken);

    Task<string> SnapWindowAsync(string targetApp, string position, CancellationToken cancellationToken);

    Task<string> SendHotkeyAsync(string targetApp, string hotkey, CancellationToken cancellationToken);

    Task<string> ClickElementAsync(string targetApp, string elementName, CancellationToken cancellationToken);

    Task<string> TypeIntoElementAsync(string targetApp, string elementName, string text, CancellationToken cancellationToken);

    Task<string> SendTextAsync(string targetApp, string text, CancellationToken cancellationToken);

    Task<string> GetElementTextAsync(string targetApp, string elementName, CancellationToken cancellationToken);

    Task<string> ScrollWindowAsync(string targetApp, string direction, CancellationToken cancellationToken);

    Task<string> ControlMediaAsync(string command, CancellationToken cancellationToken);

    Task<string> GetClipboardAsync(CancellationToken cancellationToken);

    Task<string> SetClipboardAsync(string text, CancellationToken cancellationToken);

    Task<string> ClearClipboardAsync(CancellationToken cancellationToken);

    Task<string> PasteClipboardAsync(string targetApp, CancellationToken cancellationToken);

    Task<string> GetClipboardHistoryAsync(CancellationToken cancellationToken);

    Task<string> RestoreClipboardHistoryAsync(int index, CancellationToken cancellationToken);

    Task<string> SendNotificationAsync(string title, string message, CancellationToken cancellationToken);

    Task<string> ListNotificationsAsync(CancellationToken cancellationToken);

    Task<string> ClearNotificationsAsync(CancellationToken cancellationToken);

    Task<string> WaitAsync(double seconds, CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed partial class DesktopAutomationService : IDesktopAutomationService
{
    private const int SwRestore = 9;
    private static readonly string[] DesktopLaunchableExtensions =
    [
        ".lnk",
        ".url",
        ".appref-ms",
        ".exe",
        ".bat",
        ".cmd",
        ".msc"
    ];

    private static readonly IReadOnlyDictionary<string, string> BuiltInAppAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["notepad"] = "notepad.exe",
            ["calculator"] = "calc.exe",
            ["calc"] = "calc.exe",
            ["paint"] = "mspaint.exe",
            ["file explorer"] = "explorer.exe",
            ["explorer"] = "explorer.exe",
            ["task manager"] = "taskmgr.exe",
            ["powershell"] = "powershell.exe",
            ["command prompt"] = "cmd.exe",
            ["cmd"] = "cmd.exe",
            ["terminal"] = "wt.exe",
            ["windows terminal"] = "wt.exe",
            ["settings"] = "ms-settings:",
            ["spots"] = "spotify:",
            ["spotify"] = "spotify:",
            ["spotify app"] = "spotify:",
            ["music"] = "spotify:",
            ["music app"] = "spotify:",
            ["visual studio code"] = "code",
            ["vs code"] = "code",
            ["vscode"] = "code",
            ["code"] = "code",
            ["chrome"] = "chrome",
            ["google chrome"] = "chrome",
            ["edge"] = "msedge",
            ["microsoft edge"] = "msedge",
            ["firefox"] = "firefox",
            ["brave"] = "brave",
            ["discord"] = "discord",
            ["slack"] = "slack",
            ["steam"] = "steam",
            ["obsidian"] = "obsidian",
            ["word"] = "winword",
            ["excel"] = "excel",
            ["powerpoint"] = "powerpnt",
            ["outlook"] = "outlook",
            ["teams"] = "ms-teams",
            ["pycharm"] = "pycharm64.exe",
            ["rider"] = "rider64.exe"
        };

    private readonly IReadOnlyDictionary<string, string> _appAliases;
    private readonly string _workspaceRoot;

    public Task<string> ClickElementAsync(string targetApp, string elementName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.ClickElementAsync(targetApp, elementName);
    }

    public Task<string> FocusApplicationAsync(string targetApp, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.FocusWindowAsync(targetApp);
    }

    public Task<string> CloseApplicationAsync(string targetApp, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.CloseWindowAsync(targetApp);
    }

    public Task<string> SetWindowStateAsync(string targetApp, string state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.SetWindowStateAsync(targetApp, state);
    }

    public Task<string> ListOpenWindowsAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.ListWindowsAsync(target);
    }

    public Task<string> MoveWindowAsync(string targetApp, int x, int y, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.MoveWindowAsync(targetApp, x, y);
    }

    public Task<string> ResizeWindowAsync(string targetApp, int width, int height, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.ResizeWindowAsync(targetApp, width, height);
    }

    public Task<string> SnapWindowAsync(string targetApp, string position, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.SnapWindowAsync(targetApp, position);
    }

    public Task<string> SendHotkeyAsync(string targetApp, string hotkey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.SendHotkeyAsync(targetApp, hotkey);
    }

    public Task<string> TypeIntoElementAsync(string targetApp, string elementName, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.TypeIntoElementAsync(targetApp, elementName, text);
    }

    public Task<string> SendTextAsync(string targetApp, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.SendTextAsync(targetApp, text);
    }

    public Task<string> GetElementTextAsync(string targetApp, string elementName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.GetElementTextAsync(targetApp, elementName);
    }

    public Task<string> ScrollWindowAsync(string targetApp, string direction, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.ScrollWindowAsync(targetApp, direction);
    }

    public Task<string> ControlMediaAsync(string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.ControlMediaAsync(command);
    }

    public async Task<string> GetClipboardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Win32UiAutomation.GetClipboardTextAsync();

        if (Win32UiAutomation.TryGetClipboardText(out var text, out _))
        {
            await CaptureClipboardHistoryEntryAsync(text, cancellationToken);
        }

        return result;
    }

    public async Task<string> SetClipboardAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await Win32UiAutomation.SetClipboardTextAsync(text);

        if (!UiCommandParsing.LooksLikeFailure(result))
        {
            await CaptureClipboardHistoryEntryAsync(text, cancellationToken);
        }

        return result;
    }

    public Task<string> ClearClipboardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.ClearClipboardAsync();
    }

    public Task<string> PasteClipboardAsync(string targetApp, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Win32UiAutomation.PasteClipboardAsync(targetApp);
    }

    public Task<string> WaitAsync(double seconds, CancellationToken cancellationToken)
    {
        return Win32UiAutomation.WaitAsync(seconds, cancellationToken);
    }

    public DesktopAutomationService(
        string workspaceRoot,
        IReadOnlyDictionary<string, string>? applicationAliases = null)
    {
        _workspaceRoot = workspaceRoot;
        _appAliases = BuildAppAliases(applicationAliases);
    }

    public Task<string> GetSystemStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var drives = GetDriveSummary();

        var response = string.Join(
            Environment.NewLine,
            "System state:",
            $"- OS: {RuntimeInformation.OSDescription}",
            $"- Machine: {Environment.MachineName}",
            $"- User: {Environment.UserName}",
            $"- Architecture: {RuntimeInformation.OSArchitecture}",
            $"- Local time: {DateTimeOffset.Now:dddd, MMM d yyyy h:mm tt}",
            $"- Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m",
            $"- Running processes: {Process.GetProcesses().Length}",
            $"- Workspace: {_workspaceRoot}",
            $"- Drives: {drives}");

        return Task.FromResult(response);
    }

    public Task<string> GetComputerContextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = BuildContextSnapshot();

        var lines = new List<string>
        {
            "Computer context:",
            $"- Local time: {snapshot.LocalTime:dddd, MMMM d, yyyy h:mm tt zzz}",
            $"- Machine: {Environment.MachineName}",
            $"- User: {Environment.UserName}",
            $"- Workspace: {_workspaceRoot}",
            $"- Active application: {DescribeActiveApplication(snapshot)}",
            $"- Active window: {OrUnavailable(snapshot.ActiveWindow)}",
            $"- Visible desktop apps: {(snapshot.VisibleApplications.Length == 0 ? "None detected." : string.Join(" | ", snapshot.VisibleApplications))}",
            $"- Workspace root entries: {(snapshot.WorkspaceEntries.Length == 0 ? "None detected." : string.Join(" | ", snapshot.WorkspaceEntries))}",
            $"- Running processes: {snapshot.RunningProcesses}",
            $"- Drives: {snapshot.DriveSummary}"
        };

        return Task.FromResult(string.Join(Environment.NewLine, lines));
    }

    public Task<DesktopContextSnapshot> GetContextSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildContextSnapshot());
    }

    public Task<string> GetLocalNetworkStatusAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return LocalNetworkMonitoring.GetStatusAsync(target, cancellationToken);
    }

    public Task<string> ListApplicationsAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = BuildApplicationCatalog();
        var filtered = string.IsNullOrWhiteSpace(target)
            ? candidates
                .OrderByDescending(candidate => candidate.IsRunning)
                .ThenByDescending(GetCandidatePriority)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            : candidates
                .Select(candidate => new { Candidate = candidate, Score = ScoreApplicationCandidate(target, candidate) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Candidate.IsRunning)
                .ThenByDescending(item => GetCandidatePriority(item.Candidate))
                .ThenBy(item => item.Candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Candidate);

        var matches = filtered
            .Take(15)
            .Select(candidate =>
            {
                var running = candidate.IsRunning ? "running" : "available";
                return $"- {candidate.DisplayName} ({candidate.Source}, {running})";
            })
            .ToArray();

        if (matches.Length == 0)
        {
            return Task.FromResult(
                string.IsNullOrWhiteSpace(target)
                    ? "No application entries were discovered."
                    : $"No matching application candidates were found for \"{target.Trim()}\".");
        }

        var heading = string.IsNullOrWhiteSpace(target)
            ? "Application catalog:"
            : $"Application matches for \"{target.Trim()}\":";

        return Task.FromResult(heading + Environment.NewLine + string.Join(Environment.NewLine, matches));
    }

    public Task<string> OpenApplicationAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult("Usage: open app <application>");
        }

        var normalizedTarget = target.Trim();
        var matchingCandidates = FindApplicationCandidates(normalizedTarget).ToArray();

        foreach (var candidate in matchingCandidates)
        {
            if (candidate.IsRunning
                && candidate.WindowHandle != IntPtr.Zero
                && TryFocusWindow(candidate.WindowHandle))
            {
                return Task.FromResult($"Focused application: {candidate.DisplayName} ({candidate.Source})");
            }

            if (TryLaunchShellTarget(candidate.LaunchTarget, candidate.LaunchArguments, out var launchedTarget, out var launchError))
            {
                return Task.FromResult($"Opened application target: {candidate.DisplayName} ({candidate.Source}) -> {launchedTarget}");
            }
        }

        var fallbackTarget = _appAliases.TryGetValue(normalizedTarget, out var aliasTarget)
            ? aliasTarget
            : normalizedTarget;

        return Task.FromResult(
            TryLaunchShellTarget(fallbackTarget, out var launchedFallback, out var error)
                ? $"Opened application target: {launchedFallback}"
                : $"Unable to open application target \"{normalizedTarget}\": {error}");
    }

    public Task<string> LaunchApplicationAsync(string target, string arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult("Usage: launch application <application> <arguments>");
        }

        if (string.IsNullOrWhiteSpace(arguments))
        {
            return OpenApplicationAsync(target, cancellationToken);
        }

        var normalizedTarget = target.Trim();
        var matchingCandidates = FindApplicationCandidates(normalizedTarget)
            .OrderByDescending(GetArgumentLaunchPriority)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var candidate in matchingCandidates)
        {
            if (TryLaunchShellTarget(candidate.LaunchTarget, arguments.Trim(), out var launchedTarget, out var launchError))
            {
                return Task.FromResult($"Opened application target: {candidate.DisplayName} ({candidate.Source}) -> {launchedTarget}");
            }
        }

        var fallbackTarget = _appAliases.TryGetValue(normalizedTarget, out var aliasTarget)
            ? aliasTarget
            : normalizedTarget;

        return Task.FromResult(
            TryLaunchShellTarget(fallbackTarget, arguments.Trim(), out var launchedFallback, out var error)
                ? $"Opened application target: {launchedFallback}"
                : $"Unable to launch application target \"{normalizedTarget}\" with arguments: {error}");
    }

    public Task<string> OpenPathAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult("Usage: open path <file-or-folder>");
        }

        var resolvedPath = ResolvePath(target);

        if (!File.Exists(resolvedPath) && !Directory.Exists(resolvedPath))
        {
            return Task.FromResult($"Path not found: {resolvedPath}");
        }

        return Task.FromResult(
            TryLaunchShellTarget(resolvedPath, out _, out var error)
                ? $"Opened path: {resolvedPath}"
                : $"Unable to open path \"{resolvedPath}\": {error}");
    }

    public Task<string> ListFilesAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedPath = ResolvePath(string.IsNullOrWhiteSpace(target) ? _workspaceRoot : target);

        if (!Directory.Exists(resolvedPath))
        {
            return Task.FromResult($"Directory not found: {resolvedPath}");
        }

        var entries = Directory.GetDirectories(resolvedPath)
            .Select(path => ("DIR", Path.GetFileName(path), string.Empty))
            .Concat(
                Directory.GetFiles(resolvedPath)
                    .Select(path =>
                    {
                        var fileInfo = new FileInfo(path);
                        return ("FILE", fileInfo.Name, FormatBytes(fileInfo.Length));
                    }))
            .OrderBy(entry => entry.Item1)
            .ThenBy(entry => entry.Item2, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(entry => entry.Item1 == "DIR"
                ? $"- [DIR] {entry.Item2}"
                : $"- [FILE] {entry.Item2} ({entry.Item3})")
            .ToArray();

        if (entries.Length == 0)
        {
            return Task.FromResult($"No files or folders found in {resolvedPath}");
        }

        return Task.FromResult(
            $"Items in {resolvedPath}:{Environment.NewLine}{string.Join(Environment.NewLine, entries)}");
    }

    public async Task<string> ReadFilePreviewAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(target))
        {
            return "Usage: read file <path>";
        }

        var resolvedPath = ResolvePath(target);

        if (!File.Exists(resolvedPath))
        {
            return $"File not found: {resolvedPath}";
        }

        if (await IsLikelyBinaryAsync(resolvedPath, cancellationToken))
        {
            return $"File appears to be binary and cannot be previewed safely: {resolvedPath}";
        }

        var lines = new List<string>();

        using var stream = File.OpenRead(resolvedPath);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && lines.Count < 40)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines.Add((await reader.ReadLineAsync(cancellationToken)) ?? string.Empty);
        }

        var content = string.Join(Environment.NewLine, lines);

        if (content.Length > 4000)
        {
            content = content[..4000] + Environment.NewLine + "[preview truncated]";
        }

        return string.Join(
            Environment.NewLine,
            $"Preview of {resolvedPath}:",
            string.IsNullOrWhiteSpace(content) ? "[empty file]" : content);
    }

    public Task<string> FindFilesAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult("Usage: find files <name-fragment>");
        }

        var matches = Directory.EnumerateFileSystemEntries(_workspaceRoot, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Contains(target, StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .Select(path => $"- {Path.GetRelativePath(_workspaceRoot, path)}")
            .ToArray();

        if (matches.Length == 0)
        {
            return Task.FromResult($"No matching files or folders found in the workspace for \"{target}\".");
        }

        return Task.FromResult(
            $"Workspace matches for \"{target}\":{Environment.NewLine}{string.Join(Environment.NewLine, matches)}");
    }

    public Task<string> BrowseAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(target))
        {
            return Task.FromResult("Usage: browse <url>");
        }

        var launchTarget = NormalizeBrowseTarget(target.Trim());

        return Task.FromResult(
            TryLaunchShellTarget(launchTarget, out _, out var error)
                ? $"Opened browser target: {launchTarget}"
                : $"Unable to open browser target \"{launchTarget}\": {error}");
    }

    public Task<string> SearchWebAsync(string query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult("Usage: search web <query>");
        }

        var searchUrl = $"https://www.bing.com/search?q={Uri.EscapeDataString(query.Trim())}";

        return Task.FromResult(
            TryLaunchShellTarget(searchUrl, out _, out var error)
                ? $"Opened search results for: {query.Trim()}"
                : $"Unable to open search results: {error}");
    }

    private static bool TryLaunchShellTarget(string target, out string launchedTarget, out string error)
    {
        return TryLaunchShellTarget(target, null, out launchedTarget, out error);
    }

    private static bool TryLaunchShellTarget(string target, string? arguments, out string launchedTarget, out string error)
    {
        try
        {
            var startInfo = CreateLaunchStartInfo(target, arguments);

            Process.Start(startInfo);

            launchedTarget = string.IsNullOrWhiteSpace(arguments)
                ? target
                : $"{target} {arguments}";
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            launchedTarget = string.IsNullOrWhiteSpace(arguments)
                ? target
                : $"{target} {arguments}";
            error = exception.Message;
            return false;
        }
    }

    private static ProcessStartInfo CreateLaunchStartInfo(string target, string? arguments)
    {
        // Store apps and other Start-menu entries are launched through the Apps folder. The raw text is
        // handed to Explorer as-is, because parsing it as a URI would rewrite its backslash.
        if (target.StartsWith(ShellAppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{target}\"",
                UseShellExecute = true
            };
        }

        if (string.IsNullOrWhiteSpace(arguments)
            && Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri)
            && !absoluteUri.IsFile)
        {
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{absoluteUri}\"",
                UseShellExecute = true
            };
        }

        return new ProcessStartInfo
        {
            FileName = target,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true
        };
    }

    private static bool TryFocusWindow(IntPtr windowHandle)
    {
        try
        {
            ShowWindowAsync(windowHandle, SwRestore);
            return SetForegroundWindow(windowHandle);
        }
        catch
        {
            return false;
        }
    }

    private string ResolvePath(string target)
    {
        var trimmed = target.Trim().Trim('"');

        if (TryResolveSpecialFolder(trimmed, out var specialFolderPath))
        {
            return specialFolderPath;
        }

        var expanded = Environment.ExpandEnvironmentVariables(trimmed);

        if (expanded.StartsWith('~'))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = Path.Combine(userProfile, expanded[1..].TrimStart('\\', '/'));
        }

        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        return Path.GetFullPath(Path.Combine(_workspaceRoot, expanded));
    }

    private static string NormalizeBrowseTarget(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        if (target.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{target}";
        }

        if (target.Contains('.') && !target.Contains(' '))
        {
            return $"https://{target}";
        }

        return $"https://www.bing.com/search?q={Uri.EscapeDataString(target)}";
    }

    private static async Task<bool> IsLikelyBinaryAsync(string path, CancellationToken cancellationToken)
    {
        var buffer = new byte[512];

        await using var stream = File.OpenRead(path);
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);

        return buffer.Take(bytesRead).Any(value => value == 0);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private bool TryResolveSpecialFolder(string target, out string path)
    {
        if (target.Equals("workspace", StringComparison.OrdinalIgnoreCase))
        {
            path = _workspaceRoot;
            return true;
        }

        if (target.Equals("desktop", StringComparison.OrdinalIgnoreCase))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return true;
        }

        if (target.Equals("documents", StringComparison.OrdinalIgnoreCase))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return true;
        }

        if (target.Equals("downloads", StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return true;
        }

        if (target.Equals("pictures", StringComparison.OrdinalIgnoreCase))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return true;
        }

        if (target.Equals("music", StringComparison.OrdinalIgnoreCase))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            return true;
        }

        if (target.Equals("videos", StringComparison.OrdinalIgnoreCase))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            return true;
        }

        path = string.Empty;
        return false;
    }

    private IReadOnlyList<ApplicationCandidate> BuildApplicationCatalog(bool forceRefresh = false)
    {
        var candidates = new List<ApplicationCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Windows that are open right now are always read fresh, so a running app wins over its shortcut.
        foreach (var candidate in GetVisibleApplications())
        {
            AddCandidate(candidates, seen, candidate);
        }

        // Everything installed comes from the cached index (see DesktopAutomation.AppIndex.cs).
        foreach (var candidate in GetStaticCatalog(forceRefresh))
        {
            AddCandidate(candidates, seen, candidate);
        }

        return PruneAliasCandidates(candidates);
    }

    private static IReadOnlyDictionary<string, string> BuildAppAliases(
        IReadOnlyDictionary<string, string>? applicationAliases)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in BuiltInAppAliases)
        {
            merged[alias.Key] = alias.Value;
        }

        if (applicationAliases is null)
        {
            return merged;
        }

        foreach (var alias in applicationAliases)
        {
            var key = alias.Key?.Trim();
            var value = alias.Value?.Trim();

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            merged[key] = value;
        }

        return merged;
    }

    private static void AddCandidate(
        ICollection<ApplicationCandidate> candidates,
        ISet<string> seen,
        ApplicationCandidate candidate)
    {
        var key =
            $"{NormalizeForMatching(candidate.DisplayName)}|{NormalizeForMatching(candidate.LaunchTarget)}|{NormalizeForMatching(candidate.LaunchArguments)}";

        if (!seen.Add(key))
        {
            return;
        }

        candidates.Add(candidate);
    }

    private static IReadOnlyList<ApplicationCandidate> PruneAliasCandidates(IReadOnlyList<ApplicationCandidate> candidates)
    {
        var strongerDisplays = candidates
            .Where(candidate => !string.Equals(candidate.Source, "alias", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => NormalizeForMatching(candidate.DisplayName))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidates
            .Where(candidate =>
                !string.Equals(candidate.Source, "alias", StringComparison.OrdinalIgnoreCase)
                || !strongerDisplays.Contains(NormalizeForMatching(candidate.DisplayName)))
            .ToArray();
    }

    private IEnumerable<ApplicationCandidate> FindApplicationCandidates(string target)
    {
        var matches = RankApplicationCandidates(target, BuildApplicationCatalog());

        if (matches.Length > 0)
        {
            return matches;
        }

        // Nothing matched. The app may have been installed since the index was built, so rescan once
        // (rate limited) before giving up.
        lock (_catalogSync)
        {
            if (DateTime.UtcNow - _lastMissRefreshUtc < MissRefreshInterval)
            {
                return matches;
            }

            _lastMissRefreshUtc = DateTime.UtcNow;
        }

        return RankApplicationCandidates(target, BuildApplicationCatalog(forceRefresh: true));
    }

    private static ApplicationCandidate[] RankApplicationCandidates(
        string target,
        IReadOnlyList<ApplicationCandidate> catalog)
    {
        return catalog
            .Select(candidate => new { Candidate = candidate, Score = ScoreApplicationCandidate(target, candidate) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Candidate.IsRunning)
            .ThenByDescending(item => GetCandidatePriority(item.Candidate))
            .ThenBy(item => item.Candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(item => item.Score >= 60)
            .Select(item => item.Candidate)
            .ToArray();
    }

    private static int ScoreApplicationCandidate(string query, ApplicationCandidate candidate)
    {
        var normalizedQuery = StripQueryNoise(NormalizeForMatching(query));

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return 0;
        }

        var display = NormalizeForMatching(candidate.DisplayName);
        var process = NormalizeForMatching(candidate.ProcessName);
        var window = NormalizeForMatching(candidate.WindowTitle);
        var launch = NormalizeForMatching(Path.GetFileNameWithoutExtension(candidate.LaunchTarget));
        var haystacks = new[] { display, process, window, launch }
            .Concat(GetSpokenNames(candidate.DisplayName))
            .ToArray();
        var score = 0;

        if (haystacks.Any(haystack => haystack == normalizedQuery))
        {
            score += 120;
        }

        if (haystacks.Any(haystack => haystack.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
        {
            score += 80;
        }

        if (haystacks.Any(haystack => haystack.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
        {
            score += 45;
        }

        if (display == normalizedQuery)
        {
            score += 50;
        }

        if (process == normalizedQuery || window == normalizedQuery)
        {
            score += 35;
        }

        if (launch == normalizedQuery)
        {
            score += 20;
        }

        foreach (var token in normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (haystacks.Any(haystack => haystack.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 18;
            }
        }

        // Speech recognition often gets one or two letters of a name wrong ("Discourd", "Spotifi").
        if (score < 60)
        {
            score = Math.Max(score, ScoreFuzzyMatch(normalizedQuery, candidate.DisplayName));
        }

        if (candidate.IsRunning)
        {
            score += 12;
        }

        return score;
    }

    private static int GetCandidatePriority(ApplicationCandidate candidate)
    {
        return candidate.Source switch
        {
            "running app" => 7,
            "start menu" => 6,
            "desktop shortcut" => 6,
            "desktop executable" => 6,
            "app paths" => 6,
            "installed app" => 5,
            "store app" => 5,
            "start app" => 5,
            "local app data" => 5,
            "steam library" => 5,
            "epic library" => 5,
            "riot library" => 5,
            "riot client" => 4,
            "xbox library" => 5,
            "battle.net library" => 4,
            "battle.net launcher" => 3,
            "alias" => 1,
            _ => 2
        };
    }

    private static int GetArgumentLaunchPriority(ApplicationCandidate candidate)
    {
        return candidate.Source switch
        {
            "app paths" => 8,
            "local app data" => 8,
            "installed app" => 7,
            "start menu" => 7,
            "desktop executable" => 7,
            "desktop shortcut" => 6,
            "running app" => 5,
            "alias" => 4,
            _ => GetCandidatePriority(candidate)
        };
    }

    private IEnumerable<ApplicationCandidate> GetVisibleApplications()
    {
        var foregroundHandle = GetForegroundWindow();
        var visibleApps = new List<ApplicationCandidate>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.HasExited)
                {
                    continue;
                }

                var handle = process.MainWindowHandle;
                var title = process.MainWindowTitle;

                if (handle == IntPtr.Zero || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                visibleApps.Add(new ApplicationCandidate(
                    process.ProcessName,
                    process.ProcessName,
                    "running app",
                    true,
                    handle,
                    process.Id,
                    process.ProcessName,
                    title));
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return visibleApps
            .OrderByDescending(candidate => candidate.WindowHandle == foregroundHandle)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<ApplicationCandidate> GetStartMenuApplications()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
                 })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(path =>
                        path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                continue;
            }

            foreach (var path in files)
            {
                var name = Path.GetFileNameWithoutExtension(path);

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                yield return new ApplicationCandidate(name, path, "start menu", false, IntPtr.Zero, 0, name, string.Empty);
            }
        }
    }

    private IEnumerable<ApplicationCandidate> GetDesktopApplications()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
                 })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                    .Where(IsLaunchableDesktopEntry);
            }
            catch
            {
                continue;
            }

            foreach (var path in files)
            {
                var name = Path.GetFileNameWithoutExtension(path);

                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var source = Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                    ? "desktop executable"
                    : "desktop shortcut";

                yield return new ApplicationCandidate(name, path, source, false, IntPtr.Zero, 0, name, string.Empty);
            }
        }
    }

    private static bool IsLaunchableDesktopEntry(string path)
    {
        var extension = Path.GetExtension(path);

        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return DesktopLaunchableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<ApplicationCandidate> GetAppPathApplications()
    {
        foreach (var candidate in ReadAppPathKey(Registry.CurrentUser))
        {
            yield return candidate;
        }

        foreach (var candidate in ReadAppPathKey(Registry.LocalMachine))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<ApplicationCandidate> GetInstalledApplications()
    {
        foreach (var candidate in ReadInstalledApplications(RegistryHive.CurrentUser, RegistryView.Registry64))
        {
            yield return candidate;
        }

        foreach (var candidate in ReadInstalledApplications(RegistryHive.CurrentUser, RegistryView.Registry32))
        {
            yield return candidate;
        }

        foreach (var candidate in ReadInstalledApplications(RegistryHive.LocalMachine, RegistryView.Registry64))
        {
            yield return candidate;
        }

        foreach (var candidate in ReadInstalledApplications(RegistryHive.LocalMachine, RegistryView.Registry32))
        {
            yield return candidate;
        }
    }

    private IEnumerable<ApplicationCandidate> GetLocalUserApplications()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localAppData) || !Directory.Exists(localAppData))
        {
            yield break;
        }

        IEnumerable<string> directories;

        try
        {
            directories = Directory.EnumerateDirectories(localAppData, "*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            if (ShouldSkipLocalApplicationDirectory(directory))
            {
                continue;
            }

            var launchDirectory = ResolveLatestSquirrelApplicationDirectory(directory);

            if (string.IsNullOrWhiteSpace(launchDirectory))
            {
                continue;
            }

            var displayName = Path.GetFileName(directory);
            var launchTarget = FindBestExecutableInInstallLocation(launchDirectory, displayName, 1);

            if (string.IsNullOrWhiteSpace(launchTarget))
            {
                continue;
            }

            yield return new ApplicationCandidate(
                displayName,
                launchTarget,
                "local app data",
                false,
                IntPtr.Zero,
                0,
                displayName,
                string.Empty);
        }
    }

    private static IReadOnlyList<ApplicationCandidate> ReadInstalledApplications(RegistryHive hive, RegistryView view)
    {
        var candidates = new List<ApplicationCandidate>();
        RegistryKey? baseKey = null;
        RegistryKey? uninstallKey = null;

        try
        {
            baseKey = RegistryKey.OpenBaseKey(hive, view);
            uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

            if (uninstallKey is null)
            {
                return candidates;
            }

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                using var subKey = uninstallKey.OpenSubKey(subKeyName);

                if (subKey is null)
                {
                    continue;
                }

                var displayName = (subKey.GetValue("DisplayName") as string)?.Trim();

                if (string.IsNullOrWhiteSpace(displayName) || ShouldSkipInstalledApplication(displayName))
                {
                    continue;
                }

                var launchTarget = ResolveInstalledApplicationLaunchTarget(subKey, displayName);

                if (string.IsNullOrWhiteSpace(launchTarget))
                {
                    continue;
                }

                candidates.Add(new ApplicationCandidate(
                    displayName,
                    launchTarget,
                    "installed app",
                    false,
                    IntPtr.Zero,
                    0,
                    displayName,
                    string.Empty));
            }
        }
        catch
        {
        }
        finally
        {
            uninstallKey?.Dispose();
            baseKey?.Dispose();
        }

        return candidates;
    }

    private IEnumerable<ApplicationCandidate> GetSteamLibraryApplications()
    {
        foreach (var steamRoot in GetSteamInstallRoots())
        {
            var steamAppsDirectory = Path.Combine(steamRoot, "steamapps");

            if (!Directory.Exists(steamAppsDirectory))
            {
                continue;
            }

            IEnumerable<string> manifestPaths;

            try
            {
                manifestPaths = Directory.EnumerateFiles(steamAppsDirectory, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var manifestPath in manifestPaths)
            {
                SteamAppManifest? manifest;

                try
                {
                    manifest = ReadSteamAppManifest(manifestPath);
                }
                catch
                {
                    manifest = null;
                }

                if (manifest is null
                    || string.IsNullOrWhiteSpace(manifest.AppId)
                    || string.IsNullOrWhiteSpace(manifest.Name))
                {
                    continue;
                }

                yield return new ApplicationCandidate(
                    manifest.Name,
                    $"steam://rungameid/{manifest.AppId}",
                    "steam library",
                    false,
                    IntPtr.Zero,
                    0,
                    manifest.Name,
                    string.Empty);
            }
        }
    }

    private IEnumerable<ApplicationCandidate> GetEpicLibraryApplications()
    {
        var manifestsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests");

        if (!Directory.Exists(manifestsRoot))
        {
            yield break;
        }

        IEnumerable<string> manifestPaths;

        try
        {
            manifestPaths = Directory.EnumerateFiles(manifestsRoot, "*.item", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var manifestPath in manifestPaths)
        {
            EpicAppManifest? manifest;

            try
            {
                manifest = ReadEpicAppManifest(manifestPath);
            }
            catch
            {
                manifest = null;
            }

            if (manifest is null
                || string.IsNullOrWhiteSpace(manifest.DisplayName)
                || string.IsNullOrWhiteSpace(manifest.InstallLocation))
            {
                continue;
            }

            var launchTarget = ResolveLaunchableExecutablePath(
                manifest.InstallLocation,
                manifest.LaunchExecutable,
                manifest.DisplayName,
                6);

            if (string.IsNullOrWhiteSpace(launchTarget))
            {
                continue;
            }

            yield return new ApplicationCandidate(
                manifest.DisplayName,
                launchTarget,
                "epic library",
                false,
                IntPtr.Zero,
                0,
                manifest.DisplayName,
                string.Empty);
        }
    }

    private IEnumerable<ApplicationCandidate> GetRiotLibraryApplications()
    {
        var riotClientPath = ReadRiotClientPath();

        if (string.IsNullOrWhiteSpace(riotClientPath))
        {
            yield break;
        }

        yield return new ApplicationCandidate(
            "Riot Client",
            riotClientPath,
            "riot client",
            false,
            IntPtr.Zero,
            0,
            "Riot Client",
            string.Empty);

        var metadataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Riot Games",
            "Metadata");

        if (!Directory.Exists(metadataRoot))
        {
            yield break;
        }

        IEnumerable<string> directories;

        try
        {
            directories = Directory.EnumerateDirectories(metadataRoot, "*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            if (!TryParseRiotMetadataDirectoryName(Path.GetFileName(directory), out var product, out var patchline))
            {
                continue;
            }

            var displayName = GetRiotDisplayName(product);
            var key = $"{displayName}|{patchline}";

            if (!seen.Add(key))
            {
                continue;
            }

            yield return new ApplicationCandidate(
                displayName,
                riotClientPath,
                "riot library",
                false,
                IntPtr.Zero,
                0,
                displayName,
                string.Empty,
                $"--launch-product={product} --launch-patchline={patchline}");
        }
    }

    private IEnumerable<ApplicationCandidate> GetXboxLibraryApplications()
    {
        foreach (var root in GetXboxLibraryRoots())
        {
            IEnumerable<string> gameDirectories;

            try
            {
                gameDirectories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var gameDirectory in gameDirectories)
            {
                var contentDirectory = Directory.Exists(Path.Combine(gameDirectory, "Content"))
                    ? Path.Combine(gameDirectory, "Content")
                    : gameDirectory;
                var configPath = Path.Combine(contentDirectory, "MicrosoftGame.Config");
                XboxGameManifest? manifest = null;

                if (File.Exists(configPath))
                {
                    try
                    {
                        manifest = ReadXboxGameManifest(configPath);
                    }
                    catch
                    {
                        manifest = null;
                    }
                }

                var displayName = string.IsNullOrWhiteSpace(manifest?.DisplayName)
                    ? Path.GetFileName(gameDirectory)
                    : manifest.DisplayName;
                var launchTarget = ResolveLaunchableExecutablePath(
                    contentDirectory,
                    manifest?.ExecutableName,
                    displayName,
                    4);

                if (string.IsNullOrWhiteSpace(launchTarget))
                {
                    continue;
                }

                yield return new ApplicationCandidate(
                    displayName,
                    launchTarget,
                    "xbox library",
                    false,
                    IntPtr.Zero,
                    0,
                    displayName,
                    string.Empty);
            }
        }
    }

    private IEnumerable<ApplicationCandidate> GetBattleNetLibraryApplications()
    {
        var battleNetLauncher = ResolveBattleNetLauncherPath();

        if (!string.IsNullOrWhiteSpace(battleNetLauncher))
        {
            yield return new ApplicationCandidate(
                "Battle.net",
                battleNetLauncher,
                "battle.net launcher",
                false,
                IntPtr.Zero,
                0,
                "Battle.net",
                string.Empty);
        }

        foreach (var installDirectory in GetBattleNetGameInstallDirectories())
        {
            var displayName = Path.GetFileName(installDirectory);
            var directLaunchTarget = ResolveLaunchableExecutablePath(installDirectory, null, displayName, 4);
            var launchTarget = !string.IsNullOrWhiteSpace(directLaunchTarget)
                ? directLaunchTarget
                : battleNetLauncher;

            if (string.IsNullOrWhiteSpace(launchTarget))
            {
                continue;
            }

            yield return new ApplicationCandidate(
                displayName,
                launchTarget,
                "battle.net library",
                false,
                IntPtr.Zero,
                0,
                displayName,
                string.Empty);
        }
    }

    private static IEnumerable<ApplicationCandidate> ReadAppPathKey(RegistryKey root)
    {
        using var appPaths = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");

        if (appPaths is null)
        {
            yield break;
        }

        foreach (var subKeyName in appPaths.GetSubKeyNames())
        {
            using var subKey = appPaths.OpenSubKey(subKeyName);
            var target = subKey?.GetValue(null) as string;

            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(subKeyName);

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            yield return new ApplicationCandidate(name, target, "app paths", false, IntPtr.Zero, 0, name, string.Empty);
        }
    }

    private static bool ShouldSkipInstalledApplication(string displayName)
    {
        var normalized = displayName.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        var noisyPrefixes = new[]
        {
            "Microsoft Visual C++",
            "Security Update for",
            "Update for",
            "Hotfix for",
            "NVIDIA PhysX",
            "Microsoft Edge Update",
            "Microsoft OneDrive"
        };

        return noisyPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveInstalledApplicationLaunchTarget(RegistryKey subKey, string displayName)
    {
        var displayIcon = (subKey.GetValue("DisplayIcon") as string)?.Trim();
        var installLocation = (subKey.GetValue("InstallLocation") as string)?.Trim();

        if (TryExtractLaunchablePath(displayIcon, out var launchTarget))
        {
            return launchTarget;
        }

        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            var candidate = FindBestExecutableInInstallLocation(installLocation, displayName);

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static bool TryExtractLaunchablePath(string? rawValue, out string launchTarget)
    {
        launchTarget = string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = Environment.ExpandEnvironmentVariables(rawValue.Trim());
        string candidate;

        if (trimmed.StartsWith('"'))
        {
            var closingQuoteIndex = trimmed.IndexOf('"', 1);

            if (closingQuoteIndex <= 1)
            {
                return false;
            }

            candidate = trimmed[1..closingQuoteIndex];
        }
        else
        {
            var matchedPath = TrySlicePathToKnownExtension(trimmed);

            if (string.IsNullOrWhiteSpace(matchedPath))
            {
                return false;
            }

            candidate = matchedPath;
        }

        if (!Path.IsPathRooted(candidate))
        {
            return false;
        }

        candidate = Path.GetFullPath(candidate);

        if (!File.Exists(candidate))
        {
            return false;
        }

        launchTarget = candidate;
        return true;
    }

    private static string TrySlicePathToKnownExtension(string value)
    {
        var extensions = new[] { ".exe", ".bat", ".cmd", ".lnk", ".appref-ms", ".msc", ".url" };
        var bestEndIndex = -1;

        foreach (var extension in extensions)
        {
            var index = value.IndexOf(extension, StringComparison.OrdinalIgnoreCase);

            if (index < 0)
            {
                continue;
            }

            var candidateEnd = index + extension.Length;

            if (candidateEnd > bestEndIndex)
            {
                bestEndIndex = candidateEnd;
            }
        }

        return bestEndIndex <= 0 ? string.Empty : value[..bestEndIndex];
    }

    private static string FindBestExecutableInInstallLocation(string installLocation, string displayName, int maxDepth = 2)
    {
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return string.Empty;
        }

        var expandedLocation = Environment.ExpandEnvironmentVariables(installLocation);

        if (!Directory.Exists(expandedLocation))
        {
            return string.Empty;
        }

        var displayTokens = NormalizeForMatching(displayName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var queue = new Queue<(string Directory, int Depth)>();
        var scoredCandidates = new List<(string Path, int Score)>();
        queue.Enqueue((expandedLocation, 0));

        while (queue.Count > 0)
        {
            var (directory, depth) = queue.Dequeue();

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);

                    if (ShouldSkipExecutableCandidate(fileName))
                    {
                        continue;
                    }

                    var normalizedFileName = NormalizeForMatching(fileName);
                    var score = 0;

                    if (normalizedFileName == NormalizeForMatching(displayName))
                    {
                        score += 100;
                    }

                    if (normalizedFileName.Contains(NormalizeForMatching(displayName), StringComparison.OrdinalIgnoreCase))
                    {
                        score += 45;
                    }

                    foreach (var token in displayTokens)
                    {
                        if (normalizedFileName.Contains(token, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 18;
                        }
                    }

                    if (directory.Equals(expandedLocation, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 10;
                    }

                    scoredCandidates.Add((filePath, score));
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                foreach (var childDirectory in Directory.EnumerateDirectories(directory))
                {
                    queue.Enqueue((childDirectory, depth + 1));
                }
            }
            catch
            {
            }
        }

        return scoredCandidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path)
            .FirstOrDefault() ?? string.Empty;
    }

    private static bool ShouldSkipExecutableCandidate(string fileName)
    {
        var normalized = fileName.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        var noisyNames = new[]
        {
            "uninstall",
            "unins000",
            "unins001",
            "gamelaunchhelper",
            "setup",
            "update",
            "updater",
            "crashpad_handler"
        };

        return noisyNames.Any(noisy =>
            normalized.Contains(noisy, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveLaunchableExecutablePath(
        string installLocation,
        string? launchExecutable,
        string displayName,
        int fallbackSearchDepth)
    {
        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return string.Empty;
        }

        var normalizedInstallLocation = Environment
            .ExpandEnvironmentVariables(installLocation.Trim())
            .Replace('/', Path.DirectorySeparatorChar);

        if (!Directory.Exists(normalizedInstallLocation))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(launchExecutable))
        {
            var normalizedExecutable = launchExecutable.Trim().Replace('/', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(normalizedExecutable) && File.Exists(normalizedExecutable))
            {
                return Path.GetFullPath(normalizedExecutable);
            }

            var combinedPath = Path.Combine(normalizedInstallLocation, normalizedExecutable.TrimStart(Path.DirectorySeparatorChar));

            if (File.Exists(combinedPath))
            {
                return Path.GetFullPath(combinedPath);
            }

            var exactMatch = FindExecutableByName(
                normalizedInstallLocation,
                Path.GetFileName(normalizedExecutable),
                fallbackSearchDepth);

            if (!string.IsNullOrWhiteSpace(exactMatch))
            {
                return exactMatch;
            }
        }

        return FindBestExecutableInInstallLocation(normalizedInstallLocation, displayName, fallbackSearchDepth);
    }

    private static string FindExecutableByName(string rootDirectory, string? fileName, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory)
            || string.IsNullOrWhiteSpace(fileName)
            || !Directory.Exists(rootDirectory))
        {
            return string.Empty;
        }

        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((rootDirectory, 0));

        while (queue.Count > 0)
        {
            var (directory, depth) = queue.Dequeue();

            try
            {
                foreach (var candidate in Directory.EnumerateFiles(directory, fileName, SearchOption.TopDirectoryOnly))
                {
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                foreach (var childDirectory in Directory.EnumerateDirectories(directory))
                {
                    queue.Enqueue((childDirectory, depth + 1));
                }
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static EpicAppManifest? ReadEpicAppManifest(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var displayName = GetJsonStringProperty(root, "DisplayName");
        var installLocation = GetJsonStringProperty(root, "InstallLocation");
        var launchExecutable = GetJsonStringProperty(root, "LaunchExecutable");
        var appName = GetJsonStringProperty(root, "AppName");

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = appName;
        }

        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installLocation))
        {
            return null;
        }

        return new EpicAppManifest(displayName, installLocation, launchExecutable);
    }

    private static string ReadRiotClientPath()
    {
        var installsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Riot Games",
            "RiotClientInstalls.json");

        if (File.Exists(installsPath))
        {
            try
            {
                using var stream = File.OpenRead(installsPath);
                using var document = JsonDocument.Parse(stream);

                foreach (var propertyName in new[] { "rc_default", "rc_live" })
                {
                    var candidate = GetJsonStringProperty(document.RootElement, propertyName);

                    if (TryExtractLaunchablePath(candidate, out var launchTarget))
                    {
                        return launchTarget;
                    }
                }

                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && TryGetJsonProperty(document.RootElement, "patchlines", out var patchlines)
                    && patchlines.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in patchlines.EnumerateObject())
                    {
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        if (TryExtractLaunchablePath(property.Value.GetString(), out var launchTarget))
                        {
                            return launchTarget;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        var fallbackPath = Path.Combine("C:\\Riot Games", "Riot Client", "RiotClientServices.exe");
        return File.Exists(fallbackPath) ? fallbackPath : string.Empty;
    }

    private static bool TryParseRiotMetadataDirectoryName(
        string? directoryName,
        out string product,
        out string patchline)
    {
        product = string.Empty;
        patchline = string.Empty;

        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        var segments = directoryName
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0)
        {
            return false;
        }

        product = segments[0];

        if (product.StartsWith("keystone", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        patchline = segments.Length > 1 ? segments[1] : "live";
        return !string.IsNullOrWhiteSpace(product) && !string.IsNullOrWhiteSpace(patchline);
    }

    private static string GetRiotDisplayName(string product)
    {
        if (string.IsNullOrWhiteSpace(product))
        {
            return string.Empty;
        }

        if (RiotDisplayNames.TryGetValue(product, out var displayName))
        {
            return displayName;
        }

        var words = product
            .Split(new[] { '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Length == 0
                ? token
                : char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant());

        return string.Join(' ', words);
    }

    private static IEnumerable<string> GetXboxLibraryRoots()
    {
        var candidates = new[]
        {
            "C:\\XboxGames",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ModifiableWindowsApps")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static XboxGameManifest? ReadXboxGameManifest(string configPath)
    {
        var document = XDocument.Load(configPath);
        var shellVisuals = document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "ShellVisuals", StringComparison.OrdinalIgnoreCase));
        var executable = document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "Executable", StringComparison.OrdinalIgnoreCase));
        var displayName = shellVisuals?.Attribute("DefaultDisplayName")?.Value?.Trim() ?? string.Empty;
        var executableName = executable?.Attribute("Name")?.Value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        return new XboxGameManifest(displayName, executableName);
    }

    private static string ResolveBattleNetLauncherPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net Launcher.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Battle.net", "Battle.net Launcher.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Battle.net", "Battle.net.exe")
        };

        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static IEnumerable<string> GetBattleNetGameInstallDirectories()
    {
        var knownDirectoryNames = new[]
        {
            "Call of Duty",
            "Call of Duty HQ",
            "Diablo III",
            "Diablo IV",
            "Hearthstone",
            "Heroes of the Storm",
            "Overwatch",
            "Overwatch 2",
            "StarCraft",
            "StarCraft II",
            "Warcraft III",
            "World of Warcraft",
            "World of Warcraft Classic"
        };

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var directoryName in knownDirectoryNames)
            {
                var candidate = Path.Combine(root, directoryName);

                if (Directory.Exists(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string GetJsonStringProperty(JsonElement element, string propertyName)
    {
        if (!TryGetJsonProperty(element, propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static bool ShouldSkipLocalApplicationDirectory(string directory)
    {
        var name = Path.GetFileName(directory);

        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var noisyNames = new[]
        {
            "Temp",
            "Packages",
            "Package Cache",
            "CrashDumps",
            "ConnectedDevicesPlatform",
            "D3DSCache",
            "Microsoft",
            "NVIDIA",
            "PlaceholderTileLogoFolder",
            "Publishers",
            "SquirrelTemp"
        };

        return noisyNames.Any(noisy => string.Equals(name, noisy, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveLatestSquirrelApplicationDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return string.Empty;
        }

        IEnumerable<string> appDirectories;

        try
        {
            appDirectories = Directory.EnumerateDirectories(directory, "app-*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return string.Empty;
        }

        return appDirectories
            .Select(path => new
            {
                Path = path,
                Version = ParseSquirrelVersion(Path.GetFileName(path))
            })
            .OrderByDescending(item => item.Version is not null)
            .ThenByDescending(item => item.Version)
            .ThenByDescending(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path)
            .FirstOrDefault() ?? string.Empty;
    }

    private static Version? ParseSquirrelVersion(string? directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName)
            || !directoryName.StartsWith("app-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Version.TryParse(directoryName[4..], out var version)
            ? version
            : null;
    }

    private IEnumerable<string> GetSteamInstallRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseRoots = new List<string>();

        foreach (var candidate in ReadSteamInstallRootsFromRegistry())
        {
            if (seen.Add(candidate))
            {
                baseRoots.Add(candidate);
            }
        }

        foreach (var fallback in GetSteamFallbackRoots())
        {
            if (seen.Add(fallback))
            {
                baseRoots.Add(fallback);
            }
        }

        foreach (var root in baseRoots)
        {
            yield return root;

            var libraryFoldersFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");

            if (!File.Exists(libraryFoldersFile))
            {
                continue;
            }

            foreach (var library in ReadSteamLibraryFolders(libraryFoldersFile))
            {
                if (seen.Add(library))
                {
                    yield return library;
                }
            }
        }
    }

    private static IEnumerable<string> ReadSteamInstallRootsFromRegistry()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var steamKey = root.OpenSubKey(@"SOFTWARE\Valve\Steam");

            if (steamKey is null)
            {
                continue;
            }

            var installPath = (steamKey.GetValue("SteamPath") as string)?.Trim();

            if (string.IsNullOrWhiteSpace(installPath))
            {
                installPath = (steamKey.GetValue("InstallPath") as string)?.Trim();
            }

            if (string.IsNullOrWhiteSpace(installPath))
            {
                var steamExe = (steamKey.GetValue("SteamExe") as string)?.Trim();

                if (TryExtractLaunchablePath(steamExe, out var steamExePath))
                {
                    installPath = Path.GetDirectoryName(steamExePath);
                }
            }

            if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
            {
                yield return Path.GetFullPath(installPath);
            }
        }
    }

    private static IEnumerable<string> GetSteamFallbackRoots()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> ReadSteamLibraryFolders(string libraryFoldersFile)
    {
        string[] lines;

        try
        {
            lines = File.ReadAllLines(libraryFoldersFile);
        }
        catch
        {
            yield break;
        }

        foreach (var line in lines)
        {
            var values = ExtractQuotedValues(line);

            if (values.Count < 2 || !string.Equals(values[0], "path", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = values[1].Replace(@"\\", @"\");

            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                yield return Path.GetFullPath(path);
            }
        }
    }

    private static SteamAppManifest? ReadSteamAppManifest(string manifestPath)
    {
        string[] lines;

        try
        {
            lines = File.ReadAllLines(manifestPath);
        }
        catch
        {
            return null;
        }

        string appId = string.Empty;
        string name = string.Empty;

        foreach (var line in lines)
        {
            var values = ExtractQuotedValues(line);

            if (values.Count < 2)
            {
                continue;
            }

            if (string.Equals(values[0], "appid", StringComparison.OrdinalIgnoreCase))
            {
                appId = values[1].Trim();
                continue;
            }

            if (string.Equals(values[0], "name", StringComparison.OrdinalIgnoreCase))
            {
                name = values[1].Trim();
            }
        }

        return string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name)
            ? null
            : new SteamAppManifest(appId, name);
    }

    private static List<string> ExtractQuotedValues(string line)
    {
        var values = new List<string>();

        if (string.IsNullOrWhiteSpace(line))
        {
            return values;
        }

        var index = 0;

        while (index < line.Length)
        {
            var startQuote = line.IndexOf('"', index);

            if (startQuote < 0)
            {
                break;
            }

            var endQuote = line.IndexOf('"', startQuote + 1);

            if (endQuote < 0)
            {
                break;
            }

            values.Add(line[(startQuote + 1)..endQuote]);
            index = endQuote + 1;
        }

        return values;
    }

    private ForegroundWindowInfo? TryGetForegroundWindowInfo()
    {
        try
        {
            var handle = GetForegroundWindow();

            if (handle == IntPtr.Zero)
            {
                return null;
            }

            _ = GetWindowThreadProcessId(handle, out var processId);

            if (processId == 0)
            {
                return null;
            }

            using var process = Process.GetProcessById((int)processId);
            return new ForegroundWindowInfo(process.ProcessName, (int)processId, ReadWindowTitle(handle));
        }
        catch
        {
            return null;
        }
    }

    private static string ReadWindowTitle(IntPtr handle)
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

    private string[] GetWorkspaceEntries()
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(_workspaceRoot)
                .Select(path =>
                {
                    var name = Path.GetFileName(path);
                    return Directory.Exists(path) ? $"[DIR] {name}" : $"[FILE] {name}";
                })
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string GetDriveSummary()
    {
        var drives = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Take(3)
            .Select(drive => $"{drive.Name} {FormatBytes(drive.AvailableFreeSpace)} free of {FormatBytes(drive.TotalSize)}")
            .ToArray();

        return drives.Length == 0 ? "No ready drives found." : string.Join(" | ", drives);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    private DesktopContextSnapshot BuildContextSnapshot()
    {
        var foregroundWindow = TryGetForegroundWindowInfo();
        var visibleApps = GetVisibleApplications()
            .Take(8)
            .Select(app => $"{app.DisplayName} [{app.ProcessName}]")
            .ToArray();
        var workspaceEntries = GetWorkspaceEntries()
            .Take(8)
            .ToArray();

        return new DesktopContextSnapshot(
            DateTimeOffset.Now,
            foregroundWindow?.ProcessName ?? string.Empty,
            foregroundWindow?.WindowTitle ?? string.Empty,
            visibleApps,
            workspaceEntries,
            Process.GetProcesses().Length,
            GetDriveSummary());
    }

    private static string DescribeActiveApplication(DesktopContextSnapshot snapshot)
    {
        return string.IsNullOrWhiteSpace(snapshot.ActiveApplication)
            ? "Unavailable"
            : snapshot.ActiveApplication.Trim();
    }

    private static string OrUnavailable(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unavailable" : value.Trim();
    }

    private sealed record ApplicationCandidate(
        string DisplayName,
        string LaunchTarget,
        string Source,
        bool IsRunning,
        IntPtr WindowHandle,
        int ProcessId,
        string ProcessName,
        string WindowTitle,
        string LaunchArguments = "");

    private sealed record ForegroundWindowInfo(string ProcessName, int ProcessId, string WindowTitle);

    private sealed record EpicAppManifest(string DisplayName, string InstallLocation, string LaunchExecutable);

    private sealed record SteamAppManifest(string AppId, string Name);

    private sealed record XboxGameManifest(string DisplayName, string ExecutableName);

    private static readonly IReadOnlyDictionary<string, string> RiotDisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bacon"] = "VALORANT",
            ["league_of_legends"] = "League of Legends",
            ["league_of_legends_pbe"] = "League of Legends PBE",
            ["lor"] = "Legends of Runeterra",
            ["riot_client"] = "Riot Client",
            ["valorant"] = "VALORANT"
        };
}

public sealed record DesktopContextSnapshot(
    DateTimeOffset LocalTime,
    string ActiveApplication,
    string ActiveWindow,
    string[] VisibleApplications,
    string[] WorkspaceEntries,
    int RunningProcesses,
    string DriveSummary);
