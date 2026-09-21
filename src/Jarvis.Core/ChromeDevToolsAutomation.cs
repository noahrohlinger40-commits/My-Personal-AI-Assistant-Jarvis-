using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jarvis.Core;

internal static class ChromeDevToolsAutomation
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2.5)
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly TimeSpan SessionStartupTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan PageReadyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultSelectorTimeout = TimeSpan.FromSeconds(8);

    public static bool TryParseSelectorAndText(string payload, out string selector, out string text)
    {
        selector = string.Empty;
        text = string.Empty;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split(["::"], 2, StringSplitOptions.None | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return false;
        }

        selector = parts[0].Trim();
        text = parts[1];
        return !string.IsNullOrWhiteSpace(selector) && !string.IsNullOrWhiteSpace(text);
    }

    public static async Task<ToolResult> GetStatusAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var acquisition = await EnsureSessionAsync(context, launchIfMissing: false, cancellationToken);

        if (acquisition.Session is null)
        {
            var detail = "Chrome DOM automation is idle. The first `chrome dom ...` command will launch a DevTools-backed Chrome window using `data\\automation\\chrome-devtools-profile`.";
            return new ToolResult(
                detail,
                VerificationText: acquisition.Detail,
                SummaryText: "Chrome DOM automation is idle.");
        }

        var session = acquisition.Session;
        var currentTab = SelectCurrentTab(session);
        var lines = new List<string>
        {
            $"Chrome DOM automation is online on port {session.Port}.",
            $"- Browser: {session.BrowserName}",
            $"- Automation profile: {GetProfileDirectory(context.WorkspaceRoot)}",
            $"- DOM tabs: {session.Tabs.Count}"
        };

        if (currentTab is not null)
        {
            lines.Add($"- Current tab: {FormatTabLabel(currentTab)}");
        }

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: $"Attached to a DevTools-backed Chrome session on port {session.Port} with {session.Tabs.Count} page tab(s).",
            SummaryText: "Chrome DOM automation is online.");
    }

    public static async Task<ToolResult> ListTabsAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var acquisition = await EnsureSessionAsync(context, launchIfMissing: true, cancellationToken);

        if (acquisition.Session is null)
        {
            return Failure(acquisition.Detail, "Chrome DOM tab listing failed.");
        }

        var session = acquisition.Session;
        var currentTargetId = session.State.LastTargetId?.Trim() ?? string.Empty;

        if (session.Tabs.Count == 0)
        {
            return new ToolResult(
                "Chrome DOM automation is running, but no page tabs are open yet.",
                VerificationText: $"The DevTools-backed Chrome session on port {session.Port} reported zero page tabs.",
                SummaryText: "No Chrome DOM tabs are open.");
        }

        var lines = new List<string> { $"Chrome DOM tabs ({session.Tabs.Count}):" };

        for (var index = 0; index < session.Tabs.Count; index++)
        {
            var tab = session.Tabs[index];
            var marker = string.Equals(tab.Id, currentTargetId, StringComparison.OrdinalIgnoreCase) ? " [selected]" : string.Empty;
            lines.Add($"- {index + 1}. {FormatTabLabel(tab)}{marker}");
        }

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: $"Loaded {session.Tabs.Count} DevTools tab record(s) from Chrome on port {session.Port}.",
            SummaryText: "Listed Chrome DOM tabs.");
    }

    public static async Task<ToolResult> SelectTabAsync(string payload, ToolContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Failure("Usage: chrome dom select <tab-index|tab-id|title-or-url-fragment>", "Missing Chrome DOM tab selector.");
        }

        var acquisition = await EnsureSessionAsync(context, launchIfMissing: true, cancellationToken);

        if (acquisition.Session is null)
        {
            return Failure(acquisition.Detail, "Chrome DOM tab selection failed.");
        }

        var session = acquisition.Session;
        var match = FindTabMatch(session.Tabs, payload.Trim());

        if (match.Error is not null)
        {
            return Failure(match.Error, "Chrome DOM tab selection failed.");
        }

        var target = match.Tab!;
        await ActivateTargetAsync(session, target.Id, cancellationToken);
        await SaveStateAsync(context.WorkspaceRoot, new ChromeAutomationState(session.Port, target.Id, DateTimeOffset.UtcNow), cancellationToken);

        return new ToolResult(
            $"Selected Chrome DOM tab: {FormatTabLabel(target)}",
            VerificationText: $"Activated DevTools tab `{ShortTargetId(target.Id)}` with title `{TrimForInline(target.Title, 80)}` and URL `{TrimForInline(target.Url, 120)}`.",
            SummaryText: "Selected a Chrome DOM tab.");
    }

    public static async Task<ToolResult> OpenAsync(
        string target,
        bool newTab,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var normalizedTarget = string.IsNullOrWhiteSpace(target)
            ? "about:blank"
            : AppIntegrationHelpers.NormalizeBrowserTarget(target);

        var acquisition = await EnsureSessionAsync(context, launchIfMissing: true, cancellationToken);

        if (acquisition.Session is null)
        {
            return Failure(acquisition.Detail, "Chrome DOM navigation failed.");
        }

        var session = acquisition.Session;
        ChromeTab tab;

        if (newTab)
        {
            tab = await CreateTargetAsync(session, normalizedTarget, cancellationToken);
        }
        else
        {
            var selection = await ResolveCurrentTabAsync(context, session, createIfMissing: true, cancellationToken);

            if (selection.Tab is null)
            {
                return Failure("Chrome DOM automation could not find or create a browser tab.", "Chrome DOM navigation failed.");
            }

            tab = selection.Tab;
            await NavigateTabAsync(tab, normalizedTarget, cancellationToken);
        }

        await ActivateTargetAsync(session, tab.Id, cancellationToken);
        await SaveStateAsync(context.WorkspaceRoot, new ChromeAutomationState(session.Port, tab.Id, DateTimeOffset.UtcNow), cancellationToken);
        var snapshot = await WaitForPageReadyAsync(tab, cancellationToken);

        var response = newTab
            ? $"Opened a new Chrome DOM tab: {FormatPageSnapshot(snapshot)}"
            : $"Navigated the current Chrome DOM tab: {FormatPageSnapshot(snapshot)}";

        var verification = $"DOM verification observed readyState `{GetString(snapshot, "readyState", "unknown")}` on `{TrimForInline(GetString(snapshot, "title", "Untitled"), 80)}` at `{TrimForInline(GetString(snapshot, "url", normalizedTarget), 120)}`.";

        return new ToolResult(
            response,
            VerificationText: verification,
            SummaryText: newTab ? "Opened a Chrome DOM tab." : "Navigated a Chrome DOM tab.");
    }

    public static async Task<ToolResult> GetPageAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var acquisition = await EnsureSessionAsync(context, launchIfMissing: true, cancellationToken);

        if (acquisition.Session is null)
        {
            return Failure(acquisition.Detail, "Chrome DOM page inspection failed.");
        }

        var selection = await ResolveCurrentTabAsync(context, acquisition.Session, createIfMissing: true, cancellationToken);

        if (selection.Tab is null)
        {
            return Failure("Chrome DOM automation could not find a current page tab.", "Chrome DOM page inspection failed.");
        }

        var snapshot = await EvaluatePageSnapshotAsync(selection.Tab, cancellationToken);
        var lines = new[]
        {
            "Current Chrome DOM page:",
            $"- Title: {GetString(snapshot, "title", "Untitled")}",
            $"- URL: {GetString(snapshot, "url", string.Empty)}",
            $"- Ready state: {GetString(snapshot, "readyState", "unknown")}",
            $"- Interactive elements: {GetInt(snapshot, "interactiveCount")}",
            $"- Selected tab: {ShortTargetId(selection.Tab.Id)}"
        };

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: $"DOM inspection read the current page title, URL, readyState, and interactive-element count from tab `{ShortTargetId(selection.Tab.Id)}`.",
            SummaryText: "Inspected the current Chrome DOM page.");
    }

    public static async Task<ToolResult> ReadAsync(string payload, ToolContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Failure("Usage: chrome dom read <css-selector>", "Missing Chrome DOM selector.");
        }

        var selection = await ResolveCurrentTabAsync(context, launchIfMissing: true, createIfMissing: true, cancellationToken);

        if (selection.Tab is null)
        {
            return Failure(selection.Error, "Chrome DOM read failed.");
        }

        var selector = payload.Trim();
        var snapshot = await EvaluateSelectorSnapshotAsync(selection.Tab, selector, cancellationToken);

        if (!GetBoolean(snapshot, "found"))
        {
            return Failure(
                $"Selector not found in the current Chrome DOM tab: {selector}",
                $"Chrome DOM verification did not find selector `{TrimForInline(selector, 80)}` on the current page.");
        }

        var lines = new List<string>
        {
            $"DOM read for `{selector}`:",
            $"- Tag: {GetString(snapshot, "tag", "unknown")}",
            $"- Text: {GetString(snapshot, "text", string.Empty)}",
            $"- Value: {GetString(snapshot, "value", string.Empty)}"
        };

        var href = GetString(snapshot, "href", string.Empty);

        if (!string.IsNullOrWhiteSpace(href))
        {
            lines.Add($"- Href: {href}");
        }

        lines.Add($"- Visible: {GetBoolean(snapshot, "visible")}");
        lines.Add($"- Page: {GetString(snapshot, "title", "Untitled")} | {GetString(snapshot, "url", string.Empty)}");

        return new ToolResult(
            string.Join(Environment.NewLine, lines),
            VerificationText: $"DOM verification read selector `{TrimForInline(selector, 80)}` as tag `{TrimForInline(GetString(snapshot, "tag", "unknown"), 32)}` on `{TrimForInline(GetString(snapshot, "title", "Untitled"), 80)}`.",
            SummaryText: "Read a DOM element in Chrome.");
    }

    public static async Task<ToolResult> ClickAsync(string payload, ToolContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Failure("Usage: chrome dom click <css-selector>", "Missing Chrome DOM selector.");
        }

        var selection = await ResolveCurrentTabAsync(context, launchIfMissing: true, createIfMissing: true, cancellationToken);

        if (selection.Tab is null)
        {
            return Failure(selection.Error, "Chrome DOM click failed.");
        }

        var selector = payload.Trim();
        var result = await EvaluateOnTabAsync(selection.Tab, BuildClickExpression(selector), cancellationToken);

        if (!GetBoolean(result, "found"))
        {
            return Failure(
                $"Selector not found in the current Chrome DOM tab: {selector}",
                $"Chrome DOM verification did not find selector `{TrimForInline(selector, 80)}` before the click attempt.");
        }

        var response = $"Clicked `{selector}` in Chrome DOM automation on {TrimForInline(GetString(result, "title", "Untitled"), 80)}.";
        var verification = $"DOM verification clicked selector `{TrimForInline(selector, 80)}` and then observed URL `{TrimForInline(GetString(result, "afterUrl", string.Empty), 120)}` with active element `{TrimForInline(GetString(result, "activeTag", string.Empty), 32)}`.";

        return new ToolResult(
            response,
            VerificationText: verification,
            SummaryText: "Clicked a DOM element in Chrome.");
    }

    public static async Task<ToolResult> TypeAsync(string payload, ToolContext context, CancellationToken cancellationToken)
    {
        if (!TryParseSelectorAndText(payload, out var selector, out var text))
        {
            return Failure("Usage: chrome dom type <css-selector> :: <text>", "Missing Chrome DOM selector or text.");
        }

        var selection = await ResolveCurrentTabAsync(context, launchIfMissing: true, createIfMissing: true, cancellationToken);

        if (selection.Tab is null)
        {
            return Failure(selection.Error, "Chrome DOM text entry failed.");
        }

        var result = await EvaluateOnTabAsync(selection.Tab, BuildTypeExpression(selector, text), cancellationToken);

        if (!GetBoolean(result, "found"))
        {
            return Failure(
                $"Selector not found in the current Chrome DOM tab: {selector}",
                $"Chrome DOM verification did not find selector `{TrimForInline(selector, 80)}` before the type attempt.");
        }

        if (!GetBoolean(result, "verified"))
        {
            return Failure(
                $"Chrome DOM found `{selector}`, but the typed value did not verify cleanly.",
                $"DOM verification saw selector `{TrimForInline(selector, 80)}` but the post-entry value check did not match the requested content.");
        }

        var response = $"Typed {text.Length} character(s) into `{selector}` in Chrome DOM automation.";
        var verification = $"DOM verification wrote to selector `{TrimForInline(selector, 80)}` and confirmed a matching value length of {GetInt(result, "valueLength")} on `{TrimForInline(GetString(result, "title", "Untitled"), 80)}`.";

        return new ToolResult(
            response,
            VerificationText: verification,
            SummaryText: "Typed into a DOM element in Chrome.");
    }

    public static async Task<ToolResult> WaitForAsync(string payload, ToolContext context, CancellationToken cancellationToken)
    {
        if (!TryParseSelectorAndTimeout(payload, out var selector, out var timeout, out var parseError))
        {
            return Failure(parseError, "Missing Chrome DOM wait target.");
        }

        var selection = await ResolveCurrentTabAsync(context, launchIfMissing: true, createIfMissing: true, cancellationToken);

        if (selection.Tab is null)
        {
            return Failure(selection.Error, "Chrome DOM wait failed.");
        }

        var result = await EvaluateOnTabAsync(selection.Tab, BuildWaitExpression(selector, timeout), cancellationToken);

        if (!GetBoolean(result, "found"))
        {
            return Failure(
                $"Selector `{selector}` did not appear within {timeout.TotalSeconds:0.#} seconds.",
                $"DOM verification waited {timeout.TotalSeconds:0.#} second(s) and never observed selector `{TrimForInline(selector, 80)}` on the current page.");
        }

        return new ToolResult(
            $"Selector `{selector}` appeared in Chrome DOM automation after {GetDouble(result, "elapsedMs") / 1000d:0.##} seconds.",
            VerificationText: $"DOM verification observed selector `{TrimForInline(selector, 80)}` as visible={GetBoolean(result, "visible")} on `{TrimForInline(GetString(result, "title", "Untitled"), 80)}`.",
            SummaryText: "Waited for a DOM element in Chrome.");
    }

    public static async Task<ToolResult> VerifyAsync(string payload, ToolContext context, CancellationToken cancellationToken)
    {
        if (!TryParseSelectorAndExpectation(payload, out var selector, out var expected))
        {
            return Failure("Usage: chrome dom verify <css-selector> [:: <expected-text>]", "Missing Chrome DOM verification target.");
        }

        var selection = await ResolveCurrentTabAsync(context, launchIfMissing: true, createIfMissing: true, cancellationToken);

        if (selection.Tab is null)
        {
            return Failure(selection.Error, "Chrome DOM verification failed.");
        }

        var result = await EvaluateOnTabAsync(selection.Tab, BuildVerifyExpression(selector, expected), cancellationToken);

        if (!GetBoolean(result, "found"))
        {
            return Failure(
                $"Selector not found in the current Chrome DOM tab: {selector}",
                $"DOM verification did not find selector `{TrimForInline(selector, 80)}` on the current page.");
        }

        if (!GetBoolean(result, "matches"))
        {
            var actual = GetString(result, "actual", string.Empty);
            return Failure(
                $"Selector `{selector}` was found, but the expected text did not match. Actual: {actual}",
                $"DOM verification found selector `{TrimForInline(selector, 80)}` but the expected text was not present. Actual preview: `{TrimForInline(actual, 120)}`.");
        }

        var response = string.IsNullOrWhiteSpace(expected)
            ? $"Verified that `{selector}` exists in the current Chrome DOM tab."
            : $"Verified that `{selector}` contains the expected text in the current Chrome DOM tab.";

        var verification = string.IsNullOrWhiteSpace(expected)
            ? $"DOM verification confirmed selector `{TrimForInline(selector, 80)}` exists on `{TrimForInline(GetString(result, "title", "Untitled"), 80)}`."
            : $"DOM verification confirmed selector `{TrimForInline(selector, 80)}` contains `{TrimForInline(expected, 80)}` on `{TrimForInline(GetString(result, "title", "Untitled"), 80)}`.";

        return new ToolResult(
            response,
            VerificationText: verification,
            SummaryText: "Verified a DOM condition in Chrome.");
    }

    private static ToolResult Failure(string response, string verificationOrSummary)
    {
        return new ToolResult(
            response,
            Succeeded: false,
            VerificationText: verificationOrSummary,
            SummaryText: verificationOrSummary);
    }

    private static async Task<(ChromeSession? Session, string Detail)> EnsureSessionAsync(
        ToolContext context,
        bool launchIfMissing,
        CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(context.WorkspaceRoot, cancellationToken);

        if (state.Port > 0)
        {
            var existingSession = await TryGetSessionAsync(state.Port, state, cancellationToken);

            if (existingSession is not null)
            {
                return (existingSession, $"Attached to the existing DevTools-backed Chrome session on port {state.Port}.");
            }
        }

        if (!launchIfMissing)
        {
            return (null, "No DevTools-backed Chrome session is currently running.");
        }

        var port = GetFreeTcpPort();
        Directory.CreateDirectory(GetProfileDirectory(context.WorkspaceRoot));
        var arguments = BuildLaunchArguments(context.WorkspaceRoot, port);
        var launchResult = await context.DesktopAutomation.LaunchApplicationAsync("chrome", arguments, cancellationToken);

        if (UiCommandParsing.LooksLikeFailure(launchResult))
        {
            return (null, launchResult);
        }

        var startedSession = await WaitForSessionAsync(context.WorkspaceRoot, port, SessionStartupTimeout, cancellationToken);

        if (startedSession is null)
        {
            return (null, $"Chrome launched, but the DevTools endpoint on port {port} never became ready.");
        }

        return (startedSession, launchResult);
    }

    private static async Task<ChromePageSelection> ResolveCurrentTabAsync(
        ToolContext context,
        bool launchIfMissing,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var acquisition = await EnsureSessionAsync(context, launchIfMissing, cancellationToken);
        return await ResolveCurrentTabAsync(context, acquisition.Session, createIfMissing, cancellationToken, acquisition.Detail);
    }

    private static async Task<ChromePageSelection> ResolveCurrentTabAsync(
        ToolContext context,
        ChromeSession? session,
        bool createIfMissing,
        CancellationToken cancellationToken,
        string error = "")
    {
        if (session is null)
        {
            return new ChromePageSelection(null, error);
        }

        var selected = SelectCurrentTab(session);

        if (selected is not null)
        {
            await SaveStateAsync(context.WorkspaceRoot, new ChromeAutomationState(session.Port, selected.Id, DateTimeOffset.UtcNow), cancellationToken);
            return new ChromePageSelection(selected, string.Empty);
        }

        if (!createIfMissing)
        {
            return new ChromePageSelection(null, "Chrome DOM automation has no page tabs open.");
        }

        var tab = await CreateTargetAsync(session, "about:blank", cancellationToken);
        await SaveStateAsync(context.WorkspaceRoot, new ChromeAutomationState(session.Port, tab.Id, DateTimeOffset.UtcNow), cancellationToken);
        return new ChromePageSelection(tab, string.Empty);
    }

    private static async Task<ChromeSession?> WaitForSessionAsync(
        string workspaceRoot,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = new ChromeAutomationState(port, string.Empty, DateTimeOffset.UtcNow);
            var session = await TryGetSessionAsync(port, state, cancellationToken);

            if (session is not null)
            {
                await SaveStateAsync(workspaceRoot, session.State, cancellationToken);
                return session;
            }

            await Task.Delay(250, cancellationToken);
        }

        return null;
    }

    private static async Task<ChromeSession?> TryGetSessionAsync(
        int port,
        ChromeAutomationState state,
        CancellationToken cancellationToken)
    {
        var version = await GetVersionAsync(port, cancellationToken);

        if (version is null || string.IsNullOrWhiteSpace(version.WebSocketDebuggerUrl))
        {
            return null;
        }

        var tabs = await GetTabsAsync(port, cancellationToken);
        return new ChromeSession(
            port,
            string.IsNullOrWhiteSpace(version.Browser) ? "Chrome" : version.Browser,
            version.WebSocketDebuggerUrl,
            state with { Port = port },
            tabs);
    }

    private static async Task<ChromeVersionResponse?> GetVersionAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync($"http://127.0.0.1:{port}/json/version", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<ChromeVersionResponse>(stream, JsonOptions, cancellationToken);
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<ChromeTab>> GetTabsAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync($"http://127.0.0.1:{port}/json/list", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<ChromeTab>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var descriptors = await JsonSerializer.DeserializeAsync<ChromeTargetDescriptor[]>(stream, JsonOptions, cancellationToken)
                ?? Array.Empty<ChromeTargetDescriptor>();

            return descriptors
                .Where(item => string.Equals(item.Type, "page", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.Id)
                    && !string.IsNullOrWhiteSpace(item.WebSocketDebuggerUrl))
                .Select(item => new ChromeTab(
                    item.Id ?? string.Empty,
                    item.Title ?? string.Empty,
                    item.Url ?? string.Empty,
                    item.WebSocketDebuggerUrl ?? string.Empty))
                .ToArray();
        }
        catch
        {
            return Array.Empty<ChromeTab>();
        }
    }

    private static ChromeTab? SelectCurrentTab(ChromeSession session)
    {
        var selectedId = session.State.LastTargetId?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var selected = session.Tabs.FirstOrDefault(tab => string.Equals(tab.Id, selectedId, StringComparison.OrdinalIgnoreCase));

            if (selected is not null)
            {
                return selected;
            }
        }

        return session.Tabs
            .FirstOrDefault(tab => !IsPlaceholderUrl(tab.Url))
            ?? session.Tabs.LastOrDefault();
    }

    private static async Task<ChromeTab> CreateTargetAsync(
        ChromeSession session,
        string url,
        CancellationToken cancellationToken)
    {
        await using var browserClient = await DevToolsClient.ConnectAsync(session.BrowserWebSocketUrl, cancellationToken);
        var result = await browserClient.SendCommandAsync("Target.createTarget", new { url }, cancellationToken);
        var targetId = GetString(result, "targetId", string.Empty);

        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new InvalidOperationException("Chrome DevTools did not return a target id.");
        }

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var tabs = await GetTabsAsync(session.Port, cancellationToken);
            var match = tabs.FirstOrDefault(tab => string.Equals(tab.Id, targetId, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(150, cancellationToken);
        }

        throw new InvalidOperationException("Chrome created a new tab, but the DevTools target never became addressable.");
    }

    private static async Task ActivateTargetAsync(
        ChromeSession session,
        string targetId,
        CancellationToken cancellationToken)
    {
        await using var browserClient = await DevToolsClient.ConnectAsync(session.BrowserWebSocketUrl, cancellationToken);
        await browserClient.SendCommandAsync("Target.activateTarget", new { targetId }, cancellationToken);
    }

    private static async Task NavigateTabAsync(
        ChromeTab tab,
        string url,
        CancellationToken cancellationToken)
    {
        await using var client = await DevToolsClient.ConnectAsync(tab.WebSocketDebuggerUrl, cancellationToken);
        await client.SendCommandAsync("Page.enable", null, cancellationToken);
        await client.SendCommandAsync("Page.navigate", new { url }, cancellationToken);
    }

    private static async Task<JsonElement> WaitForPageReadyAsync(
        ChromeTab tab,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + PageReadyTimeout;
        JsonElement? lastSnapshot = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var snapshot = await EvaluatePageSnapshotAsync(tab, cancellationToken);
                lastSnapshot = snapshot;
                var readyState = GetString(snapshot, "readyState", string.Empty);

                if (readyState is "interactive" or "complete")
                {
                    return snapshot;
                }
            }
            catch
            {
                // Ignore transient navigation/context-reset failures while the page is loading.
            }

            await Task.Delay(250, cancellationToken);
        }

        return lastSnapshot ?? await EvaluatePageSnapshotAsync(tab, cancellationToken);
    }

    private static async Task<JsonElement> EvaluatePageSnapshotAsync(ChromeTab tab, CancellationToken cancellationToken)
    {
        return await EvaluateOnTabAsync(
            tab,
            """
            (() => ({
              title: document.title || '',
              url: location.href,
              readyState: document.readyState,
              interactiveCount: document.querySelectorAll('a, button, input, select, textarea').length
            }))()
            """,
            cancellationToken);
    }

    private static async Task<JsonElement> EvaluateSelectorSnapshotAsync(
        ChromeTab tab,
        string selector,
        CancellationToken cancellationToken)
    {
        return await EvaluateOnTabAsync(tab, BuildReadExpression(selector), cancellationToken);
    }

    private static async Task<JsonElement> EvaluateOnTabAsync(
        ChromeTab tab,
        string expression,
        CancellationToken cancellationToken)
    {
        await using var client = await DevToolsClient.ConnectAsync(tab.WebSocketDebuggerUrl, cancellationToken);
        await client.SendCommandAsync("Runtime.enable", null, cancellationToken);
        var result = await client.SendCommandAsync(
            "Runtime.evaluate",
            new
            {
                expression,
                returnByValue = true,
                awaitPromise = true,
                userGesture = true
            },
            cancellationToken);

        return ExtractEvaluateValue(result);
    }

    private static JsonElement ExtractEvaluateValue(JsonElement evaluateResult)
    {
        if (evaluateResult.TryGetProperty("exceptionDetails", out var exceptionDetails))
        {
            var text = GetString(exceptionDetails, "text", "Runtime evaluation failed.");
            throw new InvalidOperationException(text);
        }

        if (!evaluateResult.TryGetProperty("result", out var remoteObject))
        {
            throw new InvalidOperationException("Chrome DevTools returned no evaluation object.");
        }

        if (remoteObject.TryGetProperty("value", out var value))
        {
            return value.Clone();
        }

        var description = GetString(remoteObject, "description", "Chrome DevTools returned a non-value result.");
        throw new InvalidOperationException(description);
    }

    private static string BuildReadExpression(string selector)
    {
        var selectorJson = JsonSerializer.Serialize(selector);

        return $$"""
        (() => {
          const selector = {{selectorJson}};
          const element = document.querySelector(selector);
          if (!element) {
            return { found: false, selector, title: document.title || '', url: location.href };
          }

          const rect = element.getBoundingClientRect();
          const style = window.getComputedStyle(element);
          const text = (element.innerText || element.textContent || '').trim();
          const value = 'value' in element ? String(element.value ?? '') : '';
          const href = 'href' in element ? String(element.href ?? '') : '';

          return {
            found: true,
            selector,
            tag: element.tagName.toLowerCase(),
            text: text.slice(0, 240),
            value: value.slice(0, 240),
            href: href.slice(0, 240),
            visible: rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none',
            title: document.title || '',
            url: location.href
          };
        })()
        """;
    }

    private static string BuildClickExpression(string selector)
    {
        var selectorJson = JsonSerializer.Serialize(selector);

        return $$"""
        (async () => {
          const selector = {{selectorJson}};
          const element = document.querySelector(selector);
          if (!element) {
            return { found: false, selector, title: document.title || '', url: location.href };
          }

          element.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
          element.focus?.();
          const beforeUrl = location.href;
          element.click();
          await new Promise(resolve => setTimeout(resolve, 250));

          return {
            found: true,
            selector,
            tag: element.tagName.toLowerCase(),
            text: (element.innerText || element.textContent || '').trim().slice(0, 160),
            beforeUrl,
            afterUrl: location.href,
            title: document.title || '',
            activeTag: document.activeElement?.tagName?.toLowerCase?.() || ''
          };
        })()
        """;
    }

    private static string BuildTypeExpression(string selector, string text)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        var textJson = JsonSerializer.Serialize(text);

        return $$"""
        (() => {
          const selector = {{selectorJson}};
          const nextValue = {{textJson}};
          const element = document.querySelector(selector);
          if (!element) {
            return { found: false, selector, title: document.title || '', url: location.href };
          }

          element.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
          element.focus?.();

          const tag = element.tagName.toLowerCase();
          let actual = '';

          if (element.isContentEditable) {
            element.textContent = nextValue;
            actual = element.innerText || element.textContent || '';
          } else if ('value' in element) {
            element.value = nextValue;
            actual = String(element.value ?? '');
          } else {
            element.textContent = nextValue;
            actual = element.textContent || '';
          }

          element.dispatchEvent(new Event('input', { bubbles: true }));
          element.dispatchEvent(new Event('change', { bubbles: true }));

          return {
            found: true,
            selector,
            tag,
            verified: actual === nextValue,
            valueLength: actual.length,
            title: document.title || '',
            url: location.href
          };
        })()
        """;
    }

    private static string BuildWaitExpression(string selector, TimeSpan timeout)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        var timeoutMs = Math.Max(250, (int)timeout.TotalMilliseconds);

        return $$"""
        (async () => {
          const selector = {{selectorJson}};
          const timeoutMs = {{timeoutMs}};
          const startedAt = performance.now();

          const isVisible = element => {
            const rect = element.getBoundingClientRect();
            const style = window.getComputedStyle(element);
            return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
          };

          while ((performance.now() - startedAt) <= timeoutMs) {
            const element = document.querySelector(selector);
            if (element) {
              return {
                found: true,
                selector,
                visible: isVisible(element),
                title: document.title || '',
                url: location.href,
                elapsedMs: performance.now() - startedAt
              };
            }

            await new Promise(resolve => setTimeout(resolve, 200));
          }

          return {
            found: false,
            selector,
            title: document.title || '',
            url: location.href,
            elapsedMs: performance.now() - startedAt
          };
        })()
        """;
    }

    private static string BuildVerifyExpression(string selector, string expected)
    {
        var selectorJson = JsonSerializer.Serialize(selector);
        var expectedJson = string.IsNullOrWhiteSpace(expected) ? "null" : JsonSerializer.Serialize(expected);

        return $$"""
        (() => {
          const selector = {{selectorJson}};
          const expected = {{expectedJson}};
          const element = document.querySelector(selector);
          if (!element) {
            return { found: false, matches: false, selector, title: document.title || '', url: location.href };
          }

          const text = (element.innerText || element.textContent || '').trim();
          const value = 'value' in element ? String(element.value ?? '') : '';
          const actual = [text, value].filter(Boolean).join(' | ').trim();
          const matches = expected === null
            ? true
            : actual.toLowerCase().includes(String(expected).toLowerCase());

          return {
            found: true,
            matches,
            selector,
            actual: actual.slice(0, 240),
            title: document.title || '',
            url: location.href
          };
        })()
        """;
    }

    private static bool TryParseSelectorAndExpectation(string payload, out string selector, out string expected)
    {
        selector = string.Empty;
        expected = string.Empty;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split(["::"], 2, StringSplitOptions.None | StringSplitOptions.TrimEntries);
        selector = parts[0].Trim();

        if (parts.Length > 1)
        {
            expected = parts[1];
        }

        return !string.IsNullOrWhiteSpace(selector);
    }

    private static bool TryParseSelectorAndTimeout(
        string payload,
        out string selector,
        out TimeSpan timeout,
        out string error)
    {
        selector = string.Empty;
        timeout = DefaultSelectorTimeout;
        error = "Usage: chrome dom wait <css-selector> [:: <timeout-seconds>]";

        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var parts = payload.Split(["::"], 2, StringSplitOptions.None | StringSplitOptions.TrimEntries);
        selector = parts[0].Trim();

        if (string.IsNullOrWhiteSpace(selector))
        {
            return false;
        }

        if (parts.Length == 1)
        {
            error = string.Empty;
            return true;
        }

        if (!double.TryParse(parts[1], out var seconds) || seconds <= 0)
        {
            error = "Usage: chrome dom wait <css-selector> [:: <timeout-seconds>]";
            return false;
        }

        timeout = TimeSpan.FromSeconds(Math.Min(seconds, 60));
        error = string.Empty;
        return true;
    }

    private static (ChromeTab? Tab, string? Error) FindTabMatch(IReadOnlyList<ChromeTab> tabs, string selector)
    {
        if (tabs.Count == 0)
        {
            return (null, "Chrome DOM automation has no tabs to select.");
        }

        if (int.TryParse(selector, out var index))
        {
            if (index < 1 || index > tabs.Count)
            {
                return (null, $"Tab index must be between 1 and {tabs.Count}.");
            }

            return (tabs[index - 1], null);
        }

        var exact = tabs.FirstOrDefault(tab => string.Equals(tab.Id, selector, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return (exact, null);
        }

        var matches = tabs
            .Where(tab => tab.Title.Contains(selector, StringComparison.OrdinalIgnoreCase)
                || tab.Url.Contains(selector, StringComparison.OrdinalIgnoreCase)
                || ShortTargetId(tab.Id).Equals(selector, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            0 => (null, $"No Chrome DOM tab matched `{selector}`."),
            1 => (matches[0], null),
            _ => (null, $"Multiple Chrome DOM tabs matched `{selector}`. Use `chrome dom tabs` and select by index.")
        };
    }

    private static async Task<ChromeAutomationState> LoadStateAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var statePath = GetStatePath(workspaceRoot);

        if (!File.Exists(statePath))
        {
            return new ChromeAutomationState(0, string.Empty, DateTimeOffset.MinValue);
        }

        await using var stream = File.OpenRead(statePath);
        return await JsonSerializer.DeserializeAsync<ChromeAutomationState>(stream, JsonOptions, cancellationToken)
            ?? new ChromeAutomationState(0, string.Empty, DateTimeOffset.MinValue);
    }

    private static async Task SaveStateAsync(string workspaceRoot, ChromeAutomationState state, CancellationToken cancellationToken)
    {
        var statePath = GetStatePath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        await using var stream = File.Create(statePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
    }

    private static string GetStatePath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, "data", "automation", "chrome-devtools-session.json");

    private static string GetProfileDirectory(string workspaceRoot) =>
        Path.Combine(workspaceRoot, "data", "automation", "chrome-devtools-profile");

    private static string BuildLaunchArguments(string workspaceRoot, int port)
    {
        var profileDir = GetProfileDirectory(workspaceRoot);
        return $"--remote-debugging-port={port} --remote-allow-origins=* --user-data-dir=\"{profileDir}\" --no-first-run --no-default-browser-check --new-window about:blank";
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool IsPlaceholderUrl(string url)
    {
        return url.Equals("about:blank", StringComparison.OrdinalIgnoreCase)
            || url.Equals("chrome://newtab/", StringComparison.OrdinalIgnoreCase)
            || url.Equals("chrome://new-tab-page/", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPageSnapshot(JsonElement snapshot)
    {
        return $"{GetString(snapshot, "title", "Untitled")} | {GetString(snapshot, "url", string.Empty)} | readyState={GetString(snapshot, "readyState", "unknown")}";
    }

    private static string FormatTabLabel(ChromeTab tab)
    {
        return $"{ShortTargetId(tab.Id)} | {TrimForInline(tab.Title, 80)} | {TrimForInline(tab.Url, 120)}";
    }

    private static string ShortTargetId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 8 ? value : value[..8];
    }

    private static string TrimForInline(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var flattened = value.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..maxLength] + "...";
    }

    private static string GetString(JsonElement element, string propertyName, string fallback)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
    }

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out var value)
            ? value
            : 0;
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.TryGetDouble(out var value)
            ? value
            : 0d;
    }

    private sealed record ChromeAutomationState(int Port, string LastTargetId, DateTimeOffset UpdatedAtUtc);

    private sealed record ChromeSession(
        int Port,
        string BrowserName,
        string BrowserWebSocketUrl,
        ChromeAutomationState State,
        IReadOnlyList<ChromeTab> Tabs);

    private sealed record ChromePageSelection(ChromeTab? Tab, string Error);

    private sealed record ChromeTab(
        string Id,
        string Title,
        string Url,
        string WebSocketDebuggerUrl);

    private sealed record ChromeVersionResponse(
        [property: JsonPropertyName("Browser")] string Browser,
        [property: JsonPropertyName("webSocketDebuggerUrl")] string WebSocketDebuggerUrl);

    private sealed record ChromeTargetDescriptor(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("webSocketDebuggerUrl")] string WebSocketDebuggerUrl);

    private sealed class DevToolsClient : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket;
        private int _nextId;

        private DevToolsClient(ClientWebSocket socket)
        {
            _socket = socket;
        }

        public static async Task<DevToolsClient> ConnectAsync(string webSocketUrl, CancellationToken cancellationToken)
        {
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken);
            return new DevToolsClient(socket);
        }

        public async Task<JsonElement> SendCommandAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextId);
            var payload = parameters is null
                ? JsonSerializer.Serialize(new { id, method }, JsonOptions)
                : JsonSerializer.Serialize(new { id, method, @params = parameters }, JsonOptions);

            var bytes = Encoding.UTF8.GetBytes(payload);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

            while (true)
            {
                using var document = await ReceiveDocumentAsync(cancellationToken);
                var root = document.RootElement;

                if (!root.TryGetProperty("id", out var messageId) || messageId.GetInt32() != id)
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    var errorMessage = GetString(error, "message", "Chrome DevTools reported an error.");
                    throw new InvalidOperationException(errorMessage);
                }

                return root.TryGetProperty("result", out var result)
                    ? result.Clone()
                    : JsonDocument.Parse("{}").RootElement.Clone();
            }
        }

        private async Task<JsonDocument> ReceiveDocumentAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using var stream = new MemoryStream();

            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("Chrome DevTools closed the WebSocket connection.");
                }

                stream.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    stream.Position = 0;
                    return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                }
                catch
                {
                    // Ignore best-effort WebSocket shutdown failures.
                }
            }

            _socket.Dispose();
        }
    }
}
