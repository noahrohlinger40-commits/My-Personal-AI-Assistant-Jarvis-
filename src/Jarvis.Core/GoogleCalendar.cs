using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Jarvis.Core;

public sealed record GoogleEventColor(string Id, string Name);

public sealed record CalendarEventRequest(string Title, DateOnly Date, TimeOnly? Start, TimeSpan Duration, GoogleEventColor? Color);

public sealed record CalendarEventInfo(string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay);

public sealed class CalendarException(string message) : Exception(message);

public sealed class CalendarTool : IAssistantTool
{
    private GoogleCalendarClient? _client;

    public string Name => "calendar";

    public string Description =>
        "Google Calendar. `calendar today`, `calendar tomorrow`, or `calendar friday` reads that day's events. "
        + "`calendar add <title> :: <date> :: <time> :: <color>` adds an event (time and color are optional). "
        + "`calendar connect` signs in to Google.";

    public bool CanHandle(string input) => ToolInputHelper.StartsWithCommand(input, "calendar");

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        var tail = ToolInputHelper.GetCommandTail(input, "calendar").Trim();
        var now = DateTime.Now;
        _client ??= new GoogleCalendarClient(ResolvePath(context.WorkspaceRoot, context.Options.GoogleCalendarClientSecretPath));

        try
        {
            if (tail.Equals("connect", StringComparison.OrdinalIgnoreCase))
            {
                await _client.ConnectAsync(cancellationToken);
                return new ToolResult("Connected to your Google Calendar.");
            }

            if (tail.Equals("add", StringComparison.OrdinalIgnoreCase) || tail.StartsWith("add ", StringComparison.OrdinalIgnoreCase))
            {
                return await AddAsync(tail[3..].Trim(), now, cancellationToken);
            }

            var day = DateOnly.FromDateTime(now);

            if (tail.Length > 0 && !CalendarText.TryParseDay(tail, now, out day))
            {
                return new ToolResult("Usage: calendar [today|tomorrow|<day>] or calendar add <event>", Succeeded: false);
            }

            var events = await _client.ListEventsAsync(day, cancellationToken);
            return new ToolResult(CalendarText.DescribeDay(events, day, DateOnly.FromDateTime(now)), SpeakInFull: true);
        }
        catch (CalendarException exception)
        {
            return new ToolResult(exception.Message, Succeeded: false);
        }
        catch (HttpRequestException exception)
        {
            return new ToolResult($"I couldn't reach Google Calendar: {exception.Message}", Succeeded: false);
        }
    }

    private async Task<ToolResult> AddAsync(string text, DateTime now, CancellationToken cancellationToken)
    {
        if (!CalendarText.TryParseEvent(text, now, out var request, out var missing))
        {
            // The answer is appended to this command, so "Dentist" or "tomorrow at 3" completes it.
            return missing == "title"
                ? new ToolResult("What should I call the event?", Succeeded: false, FollowUpCommand: $"calendar add {text}")
                : new ToolResult("When is it?", Succeeded: false, FollowUpCommand: $"calendar add {text}");
        }

        await _client!.AddEventAsync(request, cancellationToken);
        return new ToolResult($"Added {request.Title} {CalendarText.DescribeWhen(request, DateOnly.FromDateTime(now))}"
            + (request.Color is null ? "." : $" in {request.Color.Name}."));
    }

    private static string ResolvePath(string workspaceRoot, string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(workspaceRoot, path);
}

/// <summary>Reads spoken or typed event descriptions: "dentist tomorrow at 3pm in red".</summary>
public static partial class CalendarText
{
    // Google Calendar's eleven event colors, under the words people say for them. Two-word names come
    // first so "light blue" is not read as "blue".
    private static readonly (string Word, GoogleEventColor Color)[] Colors =
    [
        ("light green", new("2", "Sage")), ("light blue", new("7", "Peacock")),
        ("dark blue", new("9", "Blueberry")), ("dark green", new("10", "Basil")),
        ("lavender", new("1", "Lavender")), ("sage", new("2", "Sage")),
        ("grape", new("3", "Grape")), ("purple", new("3", "Grape")), ("violet", new("3", "Grape")),
        ("flamingo", new("4", "Flamingo")), ("pink", new("4", "Flamingo")),
        ("banana", new("5", "Banana")), ("yellow", new("5", "Banana")),
        ("tangerine", new("6", "Tangerine")), ("orange", new("6", "Tangerine")),
        ("peacock", new("7", "Peacock")), ("teal", new("7", "Peacock")), ("turquoise", new("7", "Peacock")), ("cyan", new("7", "Peacock")),
        ("graphite", new("8", "Graphite")), ("gray", new("8", "Graphite")), ("grey", new("8", "Graphite")), ("black", new("8", "Graphite")),
        ("blueberry", new("9", "Blueberry")), ("blue", new("9", "Blueberry")),
        ("basil", new("10", "Basil")), ("green", new("10", "Basil")),
        ("tomato", new("11", "Tomato")), ("red", new("11", "Tomato"))
    ];

    private static readonly string ColorWords = string.Join("|", Colors.Select(color => color.Word.Replace(" ", @"\s+")));

    // A color counts only when it is marked as one ("in red", "red box", "color it blue"), so a title
    // such as "Red Sox game" keeps its name. The structured "::" form accepts a bare color.
    private static readonly Regex MarkedColorRegex = new(
        $@"(?:,\s*)?(?:\b(?:in|as|colou?red|colou?r(?:\s+it)?|make\s+it|with\s+(?:a\s+|the\s+)?colou?r(?:\s+of)?)\s+(?:the\s+colou?r\s+)?(?<c>{ColorWords})\b(?:\s+(?:colou?r|box|block))?|\b(?<c>{ColorWords})\s+(?:colou?r|box|block)\b)",
        RegexOptions.IgnoreCase);

    private static readonly Regex BareColorRegex = new($@"^\s*(?<c>{ColorWords})\s*$", RegexOptions.IgnoreCase);

    private const string TimeToken = @"(?:\d{1,2}(?::\d{2})?\s*(?:[ap]\.?\s?m\.?)?|noon|midnight)";

    // The trailing (?![\w/:]) keeps "at 3rd street" and "at 3/28" from reading as a time.
    private static readonly Regex TimeRangeRegex = new(
        $@"\b(?:from|at|between)\s+(?<s>{TimeToken})\s*(?:-|to|until|till|and)\s*(?<e>{TimeToken})(?![\w/:])",
        RegexOptions.IgnoreCase);

    private static readonly Regex AtTimeRegex = new($@"\bat\s+(?<s>{TimeToken})(?![\w/:])", RegexOptions.IgnoreCase);

    // "3pm" or "3:30 p.m." said without "at".
    private static readonly Regex MeridiemTimeRegex = new(@"\b(?<s>\d{1,2}(?::\d{2})?\s*[ap]\.?\s?m\.?)(?![\w/:])", RegexOptions.IgnoreCase);

    private static readonly Regex BareTimeRegex = new($@"^\s*(?<s>{TimeToken})\s*(?:(?:-|to)\s*(?<e>{TimeToken})\s*)?$", RegexOptions.IgnoreCase);

    private static readonly Regex DurationRegex = new(
        @"\bfor\s+(?:(?<half>half\s+an)|(?<n>an?|one|two|three|four|\d+(?:\.\d+)?))\s*(?<u>hours?|hrs?|minutes?|mins?)\b",
        RegexOptions.IgnoreCase);

    private const string Months = "january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sept|sep|oct|nov|dec";
    private const string Weekdays = "sunday|monday|tuesday|wednesday|thursday|friday|saturday";

    private static readonly Regex DateRegex = new(
        $@"\b(?:on\s+|for\s+)?(?:
            (?<after>(?:the\s+)?day\s+after\s+tomorrow)
          | (?<tomorrow>tomorrow(?:\s+(?:morning|afternoon|evening|night))?)
          | (?<today>today|tonight|this\s+(?:morning|afternoon|evening))
          | (?:(?<which>next|this|coming)\s+)?(?<weekday>{Weekdays})
          | (?:the\s+)?(?<d1>\d{{1,2}})(?:st|nd|rd|th)?\s+(?:of\s+)?(?<m1>{Months})\.?(?:,?\s+(?<y1>\d{{4}}))?
          | (?<m2>{Months})\.?\s+(?:the\s+)?(?<d2>\d{{1,2}})(?:st|nd|rd|th)?(?:,?\s+(?<y2>\d{{4}}))?
          | (?<y3>\d{{4}})-(?<m3>\d{{1,2}})-(?<d3>\d{{1,2}})
          | (?<m4>\d{{1,2}})/(?<d4>\d{{1,2}})(?:/(?<y4>\d{{2,4}}))?
          | the\s+(?<d5>\d{{1,2}})(?:st|nd|rd|th)
        )\b",
        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex CalendarPhraseRegex = new(
        @"\b(?:to|on|in|onto|into)\s+(?:my\s+)?(?:google\s+)?calendar\b",
        RegexOptions.IgnoreCase);

    private static readonly Regex LeadingVerbRegex = new(
        @"^(?:please\s+)?(?:add|schedule|put|create|make|book|set\s+up)\s+",
        RegexOptions.IgnoreCase);

    private static readonly Regex LeadingEventWordsRegex = new(
        @"^(?:(?:an?|the)\s+)?(?:new\s+)?(?:(?:event|appointment|entry)\s+)?(?:called|named|titled|that\s+says|for)\s+|^(?:an?|the)\s+(?:new\s+)?(?:event|appointment|entry)\b\s*|^(?:an?|the)\s+",
        RegexOptions.IgnoreCase);

    public static bool TryParseEvent(string text, DateTime now, out CalendarEventRequest request, out string missing)
    {
        request = null!;
        string title;
        DateOnly? date = null;
        (TimeOnly Start, TimeOnly? End)? time = null;
        TimeSpan? duration = null;
        GoogleEventColor? color = null;

        if (text.Contains("::", StringComparison.Ordinal))
        {
            // Structured form from the planner: the title first, then date, time, color in any order.
            var parts = text.Split("::", StringSplitOptions.TrimEntries);
            title = parts[0];

            // A field may hold a bare value ("3pm", "red") or a phrase ("tomorrow at 3pm", "in red").
            foreach (var part in parts.Skip(1))
            {
                var value = part;
                color ??= ExtractColor(ref value, bare: true) ?? ExtractColor(ref value, bare: false);
                duration ??= ExtractDuration(ref value);
                date ??= ExtractDate(ref value, now);
                time ??= ExtractTime(ref value, bare: true) ?? ExtractTime(ref value, bare: false);
            }
        }
        else
        {
            var value = CalendarPhraseRegex.Replace(text, " ");
            color = ExtractColor(ref value, bare: false);
            duration = ExtractDuration(ref value);
            time = ExtractTime(ref value, bare: false);
            date = ExtractDate(ref value, now);
            title = value;
        }

        title = CleanTitle(title);

        if (title.Length == 0)
        {
            missing = "title";
            return false;
        }

        if (date is null && time is null)
        {
            missing = "when";
            return false;
        }

        // "Gym at 6" said at 8 PM means tomorrow's 6.
        // ponytail: a time without a date is assumed to be the next one; say the date to override.
        if (date is null)
        {
            var today = DateOnly.FromDateTime(now);
            date = time!.Value.Start > TimeOnly.FromDateTime(now) ? today : today.AddDays(1);
        }

        var length = duration
            ?? (time?.End is TimeOnly end && end > time.Value.Start ? end - time.Value.Start : TimeSpan.FromHours(1));

        request = new CalendarEventRequest(title, date.Value, time?.Start, length, color);
        missing = string.Empty;
        return true;
    }

    public static bool TryParseDay(string text, DateTime now, out DateOnly day)
    {
        var value = text;
        var parsed = ExtractDate(ref value, now);
        day = parsed ?? default;
        // Only a date, plus harmless filler ("for", "on"), may be left over.
        return parsed is not null && Regex.IsMatch(value, @"^[\s,.?!]*(?:(?:for|on|my|calendar|schedule)[\s,.?!]*)*$", RegexOptions.IgnoreCase);
    }

    /// <summary>Any date mentioned in the text ("what's on my calendar for friday"), or null.</summary>
    public static DateOnly? FindDate(string text, DateTime now)
    {
        var value = text;
        return ExtractDate(ref value, now);
    }

    public static string DescribeDay(IReadOnlyList<CalendarEventInfo> events, DateOnly day, DateOnly today)
    {
        var dayLabel = DescribeDate(day, today);

        if (events.Count == 0)
        {
            return $"Your calendar is clear {dayLabel}.";
        }

        var items = events.Select(item =>
        {
            if (item.AllDay)
            {
                return $"{item.Title} all day";
            }

            var start = item.Start.ToLocalTime();
            var end = item.End.ToLocalTime();

            // An event that began on an earlier day is described by when it ends.
            return DateOnly.FromDateTime(start.DateTime) < day
                ? $"{item.Title} until {FormatTime(end.DateTime)}"
                : end > start ? $"{item.Title} from {FormatTime(start.DateTime)} to {FormatTime(end.DateTime)}" : $"{item.Title} at {FormatTime(start.DateTime)}";
        }).ToList();

        var list = items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + ", and " + items[^1];
        var count = events.Count == 1 ? "one event" : $"{events.Count} events";
        return $"{char.ToUpperInvariant(dayLabel[0])}{dayLabel[1..]} you have {count}: {list}.";
    }

    public static string DescribeWhen(CalendarEventRequest request, DateOnly today)
    {
        var date = DescribeDate(request.Date, today);
        return request.Start is TimeOnly start
            ? $"{date} at {FormatTime(request.Date.ToDateTime(start))}"
            : $"{date}, all day";
    }

    private static string DescribeDate(DateOnly day, DateOnly today) =>
        day == today ? "today"
        : day == today.AddDays(1) ? "tomorrow"
        : $"on {day.ToString("dddd, MMMM d", CultureInfo.InvariantCulture)}";

    // "3 PM", "3:30 PM": short enough to read aloud naturally.
    private static string FormatTime(DateTime time) =>
        time.ToString(time.Minute == 0 ? "h tt" : "h:mm tt", CultureInfo.InvariantCulture);

    private static GoogleEventColor? ExtractColor(ref string text, bool bare)
    {
        var match = bare ? BareColorRegex.Match(text) : MarkedColorRegex.Matches(text).LastOrDefault();

        if (match is null || !match.Success)
        {
            return null;
        }

        var word = Regex.Replace(match.Groups["c"].Value.ToLowerInvariant(), @"\s+", " ");
        text = text.Remove(match.Index, match.Length).Insert(match.Index, " ");
        return Colors.First(color => color.Word == word).Color;
    }

    private static TimeSpan? ExtractDuration(ref string text)
    {
        var match = DurationRegex.Match(text);

        if (!match.Success)
        {
            return null;
        }

        text = text.Remove(match.Index, match.Length).Insert(match.Index, " ");

        var amount = match.Groups["half"].Success ? 0.5 : match.Groups["n"].Value.ToLowerInvariant() switch
        {
            "a" or "an" or "one" => 1,
            "two" => 2,
            "three" => 3,
            "four" => 4,
            var number => double.Parse(number, CultureInfo.InvariantCulture)
        };

        return match.Groups["u"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromHours(amount)
            : TimeSpan.FromMinutes(amount);
    }

    private static (TimeOnly Start, TimeOnly? End)? ExtractTime(ref string text, bool bare)
    {
        var match = bare ? BareTimeRegex.Match(text) : TimeRangeRegex.Match(text);

        if (!match.Success && !bare)
        {
            match = AtTimeRegex.Match(text);

            if (!match.Success)
            {
                match = MeridiemTimeRegex.Match(text);
            }
        }

        if (!match.Success)
        {
            return null;
        }

        var endToken = match.Groups["e"].Success ? match.Groups["e"].Value : null;
        var end = endToken is null ? null : ParseTime(endToken, null);
        var start = ParseTime(match.Groups["s"].Value, end is null ? null : endToken);

        if (start is null)
        {
            return null;
        }

        text = text.Remove(match.Index, match.Length).Insert(match.Index, " ");
        return (start.Value, end);
    }

    // "from 3 to 4pm": a start without am/pm borrows the end's.
    private static TimeOnly? ParseTime(string token, string? rangeEndToken)
    {
        var value = token.Trim().ToLowerInvariant().Replace(".", string.Empty).Replace(" ", string.Empty);

        if (value == "noon")
        {
            return new TimeOnly(12, 0);
        }

        if (value == "midnight")
        {
            return new TimeOnly(0, 0);
        }

        var match = Regex.Match(value, @"^(?<h>\d{1,2})(?::(?<m>\d{2}))?(?<ampm>am|pm)?$");

        if (!match.Success)
        {
            return null;
        }

        var hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        var meridiem = match.Groups["ampm"].Value;

        if (meridiem.Length == 0 && rangeEndToken is not null)
        {
            var endMeridiem = Regex.Match(rangeEndToken.ToLowerInvariant().Replace(".", string.Empty).Replace(" ", string.Empty), "(am|pm)$").Value;
            var endHour = int.Parse(Regex.Match(rangeEndToken, @"\d{1,2}").Value, CultureInfo.InvariantCulture);
            // "11 to 1pm" crosses noon, so the start is in the morning.
            meridiem = endMeridiem == "pm" && hour > endHour && hour != 12 ? "am" : endMeridiem;
        }

        if (hour > 23 || minute > 59 || (meridiem.Length > 0 && (hour == 0 || hour > 12)))
        {
            return null;
        }

        if (meridiem == "pm" && hour < 12)
        {
            hour += 12;
        }
        else if (meridiem == "am" && hour == 12)
        {
            hour = 0;
        }
        else if (meridiem.Length == 0 && hour is >= 1 and <= 7)
        {
            // ponytail: "at 3" with no am/pm is taken as the afternoon (1-7 PM); say "am" to override.
            hour += 12;
        }

        return new TimeOnly(hour, minute);
    }

    private static DateOnly? ExtractDate(ref string text, DateTime now)
    {
        var match = DateRegex.Match(text);

        if (!match.Success)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(now);
        DateOnly? date = null;
        var groups = match.Groups;

        if (groups["after"].Success)
        {
            date = today.AddDays(2);
        }
        else if (groups["tomorrow"].Success)
        {
            date = today.AddDays(1);
        }
        else if (groups["today"].Success)
        {
            date = today;
        }
        else if (groups["weekday"].Success)
        {
            var target = Enum.Parse<DayOfWeek>(groups["weekday"].Value, ignoreCase: true);
            var ahead = ((int)target - (int)today.DayOfWeek + 7) % 7;

            if (ahead == 0 && groups["which"].Value.Equals("next", StringComparison.OrdinalIgnoreCase))
            {
                ahead = 7;
            }

            date = today.AddDays(ahead);
        }
        else if (groups["m1"].Success || groups["m2"].Success)
        {
            var month = ParseMonth(groups["m1"].Success ? groups["m1"].Value : groups["m2"].Value);
            var day = int.Parse(groups["d1"].Success ? groups["d1"].Value : groups["d2"].Value, CultureInfo.InvariantCulture);
            var year = groups["y1"].Success ? groups["y1"].Value : groups["y2"].Value;
            date = BuildDate(year, month, day, today);
        }
        else if (groups["y3"].Success)
        {
            date = BuildDate(groups["y3"].Value, int.Parse(groups["m3"].Value, CultureInfo.InvariantCulture), int.Parse(groups["d3"].Value, CultureInfo.InvariantCulture), today);
        }
        else if (groups["m4"].Success)
        {
            var year = groups["y4"].Value;
            year = year.Length == 2 ? "20" + year : year;
            date = BuildDate(year, int.Parse(groups["m4"].Value, CultureInfo.InvariantCulture), int.Parse(groups["d4"].Value, CultureInfo.InvariantCulture), today);
        }
        else if (groups["d5"].Success)
        {
            // "on the 28th": this month, or next month once the day has passed.
            var day = int.Parse(groups["d5"].Value, CultureInfo.InvariantCulture);
            var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
            date = BuildDate(today.Year.ToString(CultureInfo.InvariantCulture), today.Month, day, today) is DateOnly thisMonth && thisMonth >= today
                ? thisMonth
                : BuildDate(nextMonth.Year.ToString(CultureInfo.InvariantCulture), nextMonth.Month, day, today);
        }

        if (date is null)
        {
            return null;
        }

        text = text.Remove(match.Index, match.Length).Insert(match.Index, " ");
        return date;
    }

    // Without a year, a date that has already passed means next year's.
    private static DateOnly? BuildDate(string year, int month, int day, DateOnly today)
    {
        var explicitYear = int.TryParse(year, out var parsedYear);
        var candidateYear = explicitYear ? parsedYear : today.Year;

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(candidateYear, month))
        {
            return null;
        }

        var date = new DateOnly(candidateYear, month, day);
        return !explicitYear && date < today ? date.AddYears(1) : date;
    }

    private static int ParseMonth(string value)
    {
        var key = value.ToLowerInvariant().TrimEnd('.');
        key = key == "sept" ? "sep" : key;
        return Array.FindIndex(CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames, name => name.Length > 0 && key.StartsWith(name.ToLowerInvariant(), StringComparison.Ordinal)) + 1;
    }

    private static string CleanTitle(string value)
    {
        var title = Regex.Replace(value, @"\s+", " ").Trim(' ', ',', '.', ';', ':', '-', '"', '\'');
        title = LeadingVerbRegex.Replace(title, string.Empty);
        title = LeadingEventWordsRegex.Replace(title, string.Empty);

        // Words stranded by the removed date and time: "dentist on at" -> "dentist".
        title = Regex.Replace(title, @"(?:\s+(?:on|at|for|from|in|and|to|,))+\s*$", string.Empty, RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"^(?:(?:on|at|for|from|in|and|to)\s+)+", string.Empty, RegexOptions.IgnoreCase);
        title = title.Trim(' ', ',', '.', ';', ':', '-', '"', '\'');

        if (Regex.IsMatch(title, @"^(?:an?\s+)?(?:new\s+)?(?:event|appointment|entry)?$", RegexOptions.IgnoreCase))
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(title[0]) + title[1..];
    }
}

/// <summary>
/// Google Calendar over its REST API. Signs in once through the browser (OAuth for desktop apps, with
/// PKCE and a loopback redirect) and keeps only the refresh token, in Windows Credential Manager.
/// </summary>
internal sealed class GoogleCalendarClient(string clientSecretPath)
{
    private const string CredentialTarget = "Jarvis/GoogleCalendar";
    private const string ApiBase = "https://www.googleapis.com/calendar/v3";
    private const string Scopes = "https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.calendarlist.readonly";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly SemaphoreSlim _authGate = new(1, 1);
    private string _accessToken = string.Empty;
    private DateTimeOffset _accessTokenExpiresUtc;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await GetAccessTokenAsync(forceRefresh: false, cancellationToken);
    }

    public async Task<IReadOnlyList<CalendarEventInfo>> ListEventsAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue));
        var dayEnd = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue));

        using var calendarList = await SendAsync(HttpMethod.Get, $"{ApiBase}/users/me/calendarList", null, cancellationToken);
        // The calendars ticked in Google Calendar's sidebar, which is what the user sees there.
        var calendarIds = calendarList.RootElement.GetProperty("items").EnumerateArray()
            .Where(calendar => ReadBool(calendar, "primary") || ReadBool(calendar, "selected"))
            .Select(calendar => calendar.GetProperty("id").GetString()!)
            .ToList();

        var events = new List<CalendarEventInfo>();

        foreach (var calendarId in calendarIds)
        {
            var url = $"{ApiBase}/calendars/{Uri.EscapeDataString(calendarId)}/events?singleEvents=true&orderBy=startTime"
                + $"&timeMin={Uri.EscapeDataString(ToRfc3339(dayStart))}&timeMax={Uri.EscapeDataString(ToRfc3339(dayEnd))}";
            using var page = await SendAsync(HttpMethod.Get, url, null, cancellationToken);

            foreach (var item in page.RootElement.GetProperty("items").EnumerateArray())
            {
                if (ReadString(item, "status") == "cancelled")
                {
                    continue;
                }

                var start = item.GetProperty("start");
                var end = item.GetProperty("end");
                var title = ReadString(item, "summary").Trim() is { Length: > 0 } summary ? summary : "Untitled event";

                events.Add(start.TryGetProperty("dateTime", out var startTime)
                    ? new CalendarEventInfo(title, DateTimeOffset.Parse(startTime.GetString()!, CultureInfo.InvariantCulture), DateTimeOffset.Parse(end.GetProperty("dateTime").GetString()!, CultureInfo.InvariantCulture), false)
                    : new CalendarEventInfo(title, new DateTimeOffset(DateOnly.Parse(start.GetProperty("date").GetString()!, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue)), dayEnd, true));
            }
        }

        // One event can sit on two calendars (an invite plus a shared calendar); say it once.
        return events
            .GroupBy(item => (Title: item.Title.Trim().ToLowerInvariant(), item.Start, item.AllDay))
            .Select(group => group.First())
            .OrderBy(item => item.AllDay)
            .ThenBy(item => item.Start)
            .ToList();
    }

    public async Task AddEventAsync(CalendarEventRequest request, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object> { ["summary"] = request.Title };

        if (request.Color is not null)
        {
            body["colorId"] = request.Color.Id;
        }

        if (request.Start is TimeOnly start)
        {
            // Built from local time, so the offset (including daylight saving) is the one on that date.
            var startsAt = new DateTimeOffset(request.Date.ToDateTime(start));
            body["start"] = new Dictionary<string, string> { ["dateTime"] = ToRfc3339(startsAt) };
            body["end"] = new Dictionary<string, string> { ["dateTime"] = ToRfc3339(startsAt + request.Duration) };
        }
        else
        {
            // All-day events end on the following day, which Google treats as exclusive.
            body["start"] = new Dictionary<string, string> { ["date"] = request.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            body["end"] = new Dictionary<string, string> { ["date"] = request.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }

        using var _ = await SendAsync(HttpMethod.Post, $"{ApiBase}/calendars/primary/events", JsonSerializer.Serialize(body), cancellationToken);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string url, string? json, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetAccessTokenAsync(forceRefresh: attempt > 0, cancellationToken);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (json is not null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await Http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);

            // An access token can be revoked before it expires; refresh once and retry.
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CalendarException(DescribeApiError(response.StatusCode, text));
            }

            return JsonDocument.Parse(text);
        }
    }

    private async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _authGate.WaitAsync(cancellationToken);

        try
        {
            if (!forceRefresh && _accessToken.Length > 0 && DateTimeOffset.UtcNow < _accessTokenExpiresUtc)
            {
                return _accessToken;
            }

            var (clientId, clientSecret) = ReadClientSecret();

            if (WindowsCredentialStore.TryReadGenericSecret(CredentialTarget, out var refreshToken))
            {
                var refreshed = await RequestTokenAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret
                }, cancellationToken);

                if (refreshed is not null)
                {
                    return refreshed;
                }
            }

            // No saved sign-in, or Google no longer accepts it (revoked, or expired by the 7-day limit
            // on apps left in "Testing"): sign in again.
            return await SignInAsync(clientId, clientSecret, cancellationToken);
        }
        finally
        {
            _authGate.Release();
        }
    }

    private async Task<string> SignInAsync(string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        // Google sends the browser back to this port on this PC; nothing listens beyond 127.0.0.1.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var redirectUri = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var state = Base64Url(RandomNumberGenerator.GetBytes(16));
            var query = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = Scopes,
                ["access_type"] = "offline",
                ["prompt"] = "consent",
                ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                ["code_challenge_method"] = "S256",
                ["state"] = state
            };

            Process.Start(new ProcessStartInfo(
                "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join("&", query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}")))
            {
                UseShellExecute = true
            });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            while (true)
            {
                using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(timeout.Token) ?? string.Empty;
                var path = requestLine.Split(' ') is [_, var target, ..] ? target : "/";
                var parameters = HttpUtility.ParseQueryString(new Uri(new Uri(redirectUri), path).Query);
                var code = parameters["code"];
                var error = parameters["error"];

                // The browser may also ask for a favicon; only the redirect carries a code or an error.
                if (code is null && error is null)
                {
                    await RespondAsync(stream, "404 Not Found", string.Empty, timeout.Token);
                    continue;
                }

                var succeeded = error is null && parameters["state"] == state;
                await RespondAsync(
                    stream,
                    "200 OK",
                    succeeded
                        ? "Jarvis is connected to your Google Calendar. You can close this tab."
                        : "Google sign-in did not finish. You can close this tab and ask Jarvis again.",
                    timeout.Token);

                if (!succeeded)
                {
                    throw new CalendarException(error == "access_denied"
                        ? "Google sign-in was cancelled, so I can't reach your calendar."
                        : $"Google sign-in failed ({error ?? "the reply did not match the request"}).");
                }

                return await RequestTokenAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code!,
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["redirect_uri"] = redirectUri,
                    ["code_verifier"] = verifier
                }, cancellationToken) ?? throw new CalendarException("Google rejected the sign-in. Please try again.");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CalendarException("Google sign-in timed out. Ask me again when you're ready to sign in.");
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Returns the new access token, or null when Google refuses the grant (invalid_grant).</summary>
    private async Task<string?> RequestTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        if (!response.IsSuccessStatusCode)
        {
            if (ReadString(root, "error") == "invalid_grant")
            {
                return null;
            }

            var reason = ReadString(root, "error_description") is { Length: > 0 } description ? description : ReadString(root, "error");
            throw new CalendarException($"Google sign-in failed: {reason}.");
        }

        if (ReadString(root, "refresh_token") is { Length: > 0 } refreshToken
            && !WindowsCredentialStore.TryWriteGenericSecret(CredentialTarget, refreshToken))
        {
            throw new CalendarException("Signed in to Google, but Windows would not store the sign-in, so it would be lost on restart.");
        }

        _accessToken = root.GetProperty("access_token").GetString()!;
        _accessTokenExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32() - 60);
        return _accessToken;
    }

    private (string ClientId, string ClientSecret) ReadClientSecret()
    {
        if (!File.Exists(clientSecretPath))
        {
            throw new CalendarException(
                "Google Calendar isn't set up yet. In Google Cloud Console (console.cloud.google.com): create a project, "
                + "enable the Google Calendar API, set up the OAuth consent screen (External, and add your Gmail as a test user), "
                + "then under Credentials create an OAuth client ID of type Desktop app and download its JSON. "
                + $"Save that file as {clientSecretPath} and ask me again.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(clientSecretPath));
        var root = document.RootElement;
        var client = root.TryGetProperty("installed", out var installed) ? installed
            : root.TryGetProperty("web", out var web) ? web
            : root;

        var clientId = ReadString(client, "client_id");
        var clientSecret = ReadString(client, "client_secret");

        if (clientId.Length == 0 || clientSecret.Length == 0)
        {
            throw new CalendarException($"{clientSecretPath} has no client_id or client_secret. Download the Desktop app OAuth client JSON again.");
        }

        return (clientId, clientSecret);
    }

    private static string DescribeApiError(HttpStatusCode status, string body)
    {
        if (body.Contains("accessNotConfigured", StringComparison.Ordinal) || body.Contains("SERVICE_DISABLED", StringComparison.Ordinal))
        {
            return "The Google Calendar API is not enabled for your Google Cloud project. Enable it at "
                + "console.cloud.google.com/apis/library/calendar-json.googleapis.com and ask me again.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var message = document.RootElement.GetProperty("error").GetProperty("message").GetString();
            return $"Google Calendar returned an error ({(int)status}): {message}";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return $"Google Calendar returned an error ({(int)status}).";
        }
    }

    private static async Task RespondAsync(Stream stream, string status, string message, CancellationToken cancellationToken)
    {
        var html = $"<!doctype html><meta charset=\"utf-8\"><title>Jarvis</title><body style=\"font-family:sans-serif;padding:2em\">{WebUtility.HtmlEncode(message)}</body>";
        var bytes = Encoding.UTF8.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(html)}\r\nConnection: close\r\n\r\n{html}");
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static string ToRfc3339(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
