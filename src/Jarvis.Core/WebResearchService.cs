using System.Net;
using System.Text.RegularExpressions;

namespace Jarvis.Core;

public sealed record WebResearchResult(
    string Query,
    AssistantCitation[] Citations,
    bool IsOffline,
    string SummaryText);

public interface IWebResearchService
{
    Task<WebResearchResult> ResearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}

public sealed class DuckDuckGoWebResearchService : IWebResearchService
{
    private static readonly Regex ResultRegex = new(
        "<a[^>]*class=\"result__a\"[^>]*href=\"(?<href>[^\"]+)\"[^>]*>(?<title>.*?)</a>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SnippetRegex = new(
        "<a[^>]*class=\"result__snippet\"[^>]*>(?<snippet>.*?)</a>|<div[^>]*class=\"result__snippet\"[^>]*>(?<snippet2>.*?)</div>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _httpClient;

    public DuckDuckGoWebResearchService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Jarvis/2.0 (+https://localhost)");
    }

    public async Task<WebResearchResult> ResearchAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new WebResearchResult(string.Empty, Array.Empty<AssistantCitation>(), false, "No query was provided.");
        }

        try
        {
            var citations = await SearchDuckDuckGoAsync(query.Trim(), Math.Clamp(maxResults, 1, 8), cancellationToken);

            if (citations.Length > 0)
            {
                return new WebResearchResult(
                    query.Trim(),
                    citations,
                    IsOffline: false,
                    $"Found {citations.Length} web source{(citations.Length == 1 ? string.Empty : "s")}.");
            }

            return new WebResearchResult(
                query.Trim(),
                Array.Empty<AssistantCitation>(),
                IsOffline: false,
                "No public web results were found.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new WebResearchResult(
                query.Trim(),
                Array.Empty<AssistantCitation>(),
                IsOffline: true,
                $"Web research is unavailable right now: {TrimError(exception.Message)}");
        }
    }

    private async Task<AssistantCitation[]> SearchDuckDuckGoAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Search HTTP {(int)response.StatusCode}");
        }

        var titleMatches = ResultRegex.Matches(body);
        var snippetMatches = SnippetRegex.Matches(body);
        var citations = new List<AssistantCitation>();

        for (var index = 0; index < titleMatches.Count && citations.Count < maxResults; index++)
        {
            var match = titleMatches[index];
            var rawUrl = HtmlDecode(match.Groups["href"].Value);
            var resolvedUrl = ResolveDuckDuckGoRedirect(rawUrl);

            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                continue;
            }

            var title = StripHtml(HtmlDecode(match.Groups["title"].Value));

            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var snippet = string.Empty;

            if (index < snippetMatches.Count)
            {
                var snippetGroup = snippetMatches[index].Groups["snippet"];
                var alternateGroup = snippetMatches[index].Groups["snippet2"];
                snippet = StripHtml(HtmlDecode(snippetGroup.Success ? snippetGroup.Value : alternateGroup.Value));
            }

            citations.Add(new AssistantCitation(title, resolvedUrl, snippet));
        }

        return citations.ToArray();
    }

    private static string ResolveDuckDuckGoRedirect(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absolute)
            && !absolute.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            return absolute.ToString();
        }

        if (!Uri.TryCreate("https://duckduckgo.com" + rawUrl, UriKind.Absolute, out var redirectUri))
        {
            return rawUrl;
        }

        var query = redirectUri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in query)
        {
            var segments = part.Split('=', 2);

            if (segments.Length != 2 || !string.Equals(segments[0], "uddg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(segments[1]);
        }

        return rawUrl;
    }

    private static string HtmlDecode(string value) => WebUtility.HtmlDecode(value).Trim();

    private static string StripHtml(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var noTags = Regex.Replace(value, "<.*?>", string.Empty);
        return WebUtility.HtmlDecode(noTags).Trim();
    }

    private static string TrimError(string text)
    {
        const int maxLength = 240;
        var value = text.Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
