using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Jarvis.Core;

namespace Jarvis.App;

/// <summary>
/// Reads the page in the frontmost Chromium browser through Windows UI Automation, the interface screen
/// readers use. It sees exactly what is on screen, including pages behind a sign-in, and needs no browser
/// extension or debugging port.
/// </summary>
internal sealed class UiaBrowserPageReader : IBrowserPageReader
{
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "vivaldi", "opera"
    };

    // UIA_HeadingLevelPropertyId. The managed UIA wrapper predates it, so it is looked up by id; values run
    // from HeadingLevel_None (80050) through HeadingLevel9 (80059).
    private static readonly AutomationProperty? HeadingLevelProperty = AutomationProperty.LookupById(30173);

    public Task<BrowserPage?> ReadActivePageAsync(CancellationToken cancellationToken) =>
        // Off the UI thread: UI Automation calls into another process and must not block the form.
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private static BrowserPage? Read(CancellationToken cancellationToken)
    {
        var documentCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
        BrowserPage? page = null;

        // Chrome builds its accessibility tree only once something asks for it, so the first look at a page
        // can come back empty or partial. Retry briefly until the page has real content.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Front to back. Pop-ups (a first-run prompt, a download bubble) are browser windows too, but hold
            // no page, so the first window that contains one is the page being looked at.
            foreach (var handle in FindBrowserWindows())
            {
                var window = AutomationElement.FromHandle(handle);
                var document = window.FindFirst(TreeScope.Descendants, documentCondition);

                if (document is null)
                {
                    continue;
                }

                page = Capture(document, window);

                if (CountNodes(page.Document) >= 20)
                {
                    return page;
                }

                // That page is still loading: wait for it rather than read a window behind it.
                break;
            }

            Thread.Sleep(400);
        }

        return page;
    }

    private static BrowserPage Capture(AutomationElement document, AutomationElement window)
    {
        // One cross-process request for the whole tree instead of one per element: about 0.1 to 0.3 s even
        // for a long article.
        var cache = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            AutomationElementMode = AutomationElementMode.None
        };
        cache.Add(AutomationElement.LocalizedControlTypeProperty);
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.ClassNameProperty);

        if (HeadingLevelProperty is not null)
        {
            cache.Add(HeadingLevelProperty);
        }

        AutomationElement root;

        using (cache.Activate())
        {
            root = document.GetUpdatedCache(cache);
        }

        return new BrowserPage(root.Cached.Name, ReadAddress(window), ToNode(root));
    }

    private static PageNode ToNode(AutomationElement element)
    {
        var children = new List<PageNode>();

        foreach (AutomationElement child in element.CachedChildren)
        {
            children.Add(ToNode(child));
        }

        var headingLevel = 0;

        if (HeadingLevelProperty is not null
            && element.GetCachedPropertyValue(HeadingLevelProperty, ignoreDefaultValue: true) is int level
            && level is > 80050 and <= 80059)
        {
            headingLevel = level - 80050;
        }

        var cached = element.Cached;
        return new PageNode(cached.LocalizedControlType ?? string.Empty, cached.Name ?? string.Empty, cached.ClassName ?? string.Empty, headingLevel, children);
    }

    private static int CountNodes(PageNode node) => 1 + node.Children.Sum(CountNodes);

    // The address bar is Chromium's omnibox; its class name is the same in Chrome, Edge, and Brave.
    private static string ReadAddress(AutomationElement window)
    {
        try
        {
            var omnibox = window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, "OmniboxViewViews"));

            return omnibox?.GetCurrentPattern(ValuePattern.Pattern) is ValuePattern value ? value.Current.Value : string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ElementNotAvailableException)
        {
            return string.Empty;
        }
    }

    // Top-level windows come back front to back, so the order is the one on screen. Minimized windows go
    // last: they are used only if no browser window is showing.
    private static List<IntPtr> FindBrowserWindows()
    {
        var visible = new List<IntPtr>();
        var minimized = new List<IntPtr>();

        EnumWindows((handle, parameter) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0 || !IsChromiumWindow(handle))
            {
                return true;
            }

            _ = GetWindowThreadProcessId(handle, out var processId);

            try
            {
                using var process = Process.GetProcessById((int)processId);

                if (!BrowserProcesses.Contains(process.ProcessName))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            (IsIconic(handle) ? minimized : visible).Add(handle);
            return true;
        }, IntPtr.Zero);

        return [.. visible, .. minimized];
    }

    private static bool IsChromiumWindow(IntPtr handle)
    {
        var className = new StringBuilder(64);
        _ = GetClassName(handle, className, className.Capacity);
        return className.ToString() == "Chrome_WidgetWin_1";
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
}
