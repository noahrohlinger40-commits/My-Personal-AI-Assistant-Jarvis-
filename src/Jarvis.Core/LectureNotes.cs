using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Core;

/// <param name="At">Time from the start of the recording.</param>
public sealed record TranscriptLine(TimeSpan At, string Text);

/// <param name="AudioPath">The saved recording: an MP3, or the WAV if it could not be converted.</param>
public sealed record LectureRecording(IReadOnlyList<TranscriptLine> Lines, TimeSpan Duration, string AudioPath);

/// <summary>Records the microphone and the computer's own audio, transcribing as it goes.</summary>
public interface ILectureRecorder
{
    bool IsRecording { get; }

    /// <summary>Starts recording to <paramref name="wavPath"/>. Throws if there is no microphone.</summary>
    void Start(string wavPath);

    /// <summary>Stops, waits for the last of the transcription, and saves the audio as MP3.</summary>
    Task<LectureRecording> StopAsync(CancellationToken cancellationToken);
}

public sealed class LectureNotesTool(ILectureRecorder? recorder, IModelGateway? modelGateway) : IAssistantTool
{
    private string _title = string.Empty;
    private string _fileName = string.Empty;
    private DateTime _startedAt;

    public string Name => "lecture notes";

    public string Description =>
        "Record a lecture or meeting (microphone and computer audio) and turn it into notes in the Lecture Notes vault. "
        + "`lecture notes start <course or meeting name>` begins; `lecture notes stop` ends it and writes the notes.";

    public bool CanHandle(string input) => ToolInputHelper.StartsWithCommand(input, "lecture notes");

    public async Task<ToolResult> ExecuteAsync(string input, ToolContext context, CancellationToken cancellationToken)
    {
        if (recorder is null)
        {
            return new ToolResult("Recording isn't available here.", Succeeded: false);
        }

        var tail = ToolInputHelper.GetCommandTail(input, "lecture notes").Trim();
        var vault = Environment.ExpandEnvironmentVariables(context.Options.LectureNotesFolder);

        if (tail.StartsWith("stop", StringComparison.OrdinalIgnoreCase))
        {
            return await StopAsync(vault, cancellationToken);
        }

        if (tail.StartsWith("start", StringComparison.OrdinalIgnoreCase))
        {
            return Start(vault, tail[5..].Trim());
        }

        return new ToolResult(
            recorder.IsRecording ? $"I'm recording {_title}, started at {_startedAt:h:mm tt}." : "I'm not recording anything.");
    }

    private ToolResult Start(string vault, string name)
    {
        if (recorder!.IsRecording)
        {
            return new ToolResult($"I'm already recording {_title}. Say \"stop lecture notes\" to finish.");
        }

        _title = LectureNoteText.CleanTitle(name);
        _startedAt = DateTime.Now;
        var recordings = Path.Combine(vault, "Recordings");
        Directory.CreateDirectory(recordings);

        // "2026-09-29 IT Professions", then "... 2" for a second recording that day.
        _fileName = $"{_startedAt:yyyy-MM-dd} {_title}";

        for (var copy = 2; File.Exists(NotePath(vault)) || File.Exists(Path.Combine(recordings, _fileName + ".mp3")); copy++)
        {
            _fileName = $"{_startedAt:yyyy-MM-dd} {_title} {copy}";
        }

        try
        {
            recorder.Start(Path.Combine(recordings, _fileName + ".wav"));
        }
        catch (Exception exception)
        {
            // No microphone, a device error, or the vault folder can't be written.
            return new ToolResult($"I couldn't start recording: {exception.Message}", Succeeded: false);
        }

        // Kept short: the computer's audio is being recorded, so this reply ends up at the start of it.
        return new ToolResult($"Recording {_title}.");
    }

    private async Task<ToolResult> StopAsync(string vault, CancellationToken cancellationToken)
    {
        if (!recorder!.IsRecording)
        {
            return new ToolResult("I'm not recording anything.", Succeeded: false);
        }

        var recording = await recorder.StopAsync(CancellationToken.None);
        recording = recording with { Lines = LectureNoteText.JoinIntoSentences(recording.Lines) };
        var notePath = NotePath(vault);
        Directory.CreateDirectory(Path.GetDirectoryName(notePath)!);

        // The transcript is saved before the model runs, so a crash or a cancelled request loses nothing.
        var audioFile = Path.GetFileName(recording.AudioPath);
        await File.WriteAllTextAsync(notePath, LectureNoteText.Render(_title, _startedAt, recording, audioFile, null), CancellationToken.None);

        if (recording.Lines.Count == 0)
        {
            return new ToolResult($"I saved the {_title} recording, but I couldn't make out any speech in it.");
        }

        var notes = await LectureNoteText.WriteNotesAsync(modelGateway, _title, recording.Lines, cancellationToken);

        if (notes is null)
        {
            return new ToolResult(
                $"I saved the {_title} recording and transcript, but the local model is off, so there are no notes yet.",
                Succeeded: false);
        }

        await File.WriteAllTextAsync(notePath, LectureNoteText.Render(_title, _startedAt, recording, audioFile, notes), cancellationToken);
        var dueItems = notes.DueItems.Count switch
        {
            0 => string.Empty,
            1 => " There's one due date or to-do in them.",
            var count => $" There are {count} due dates or to-dos in them."
        };

        return new ToolResult(
            $"Your {_title} notes are saved. {notes.Summary}{dueItems}",
            SummaryText: $"Saved lecture notes to {notePath}.",
            SpeakInFull: true);
    }

    private string NotePath(string vault) => Path.Combine(vault, _title, _fileName + ".md");
}

/// <param name="Sections">Key points per stretch of the recording, with where each stretch starts and ends.</param>
public sealed record LectureNotes(string Summary, IReadOnlyList<string> DueItems, IReadOnlyList<(TimeSpan Start, TimeSpan End, IReadOnlyList<string> Points)> Sections);

public static partial class LectureNoteText
{
    // About 3,000 tokens (roughly 15 minutes of speech), inside the local model's 8K context with room to answer.
    private const int SectionCharacters = 12_000;

    private const string KeyPointsInstructions =
        "You take notes from part of a lecture or meeting transcript. Write the key points as 3 to 8 short bullet points, "
        + "each on its own line starting with \"- \". Keep names, terms, numbers, and examples exactly as said. "
        + "Use only what the transcript says. Write nothing else: no heading, no introduction.";

    private const string DueItemsInstructions =
        "From this part of a lecture or meeting transcript, list every assignment, due date, exam, quiz, deadline, and task "
        + "someone must do, and anything the speaker says will be on a test. One per line starting with \"- \", with dates "
        + "and times exactly as said. If there are none, reply with only the word: none";

    private const string SummaryInstructions =
        "These are notes from a lecture or meeting. Summarize it in 2 or 3 plain sentences, under 60 words, for someone "
        + "who missed it. The summary is read aloud: no lists, headings, markdown, or emoji.";

    /// <summary>"it professions" stays as said; characters that files or Obsidian links can't hold are dropped.</summary>
    public static string CleanTitle(string name)
    {
        var cleaned = Regex.Replace(name, @"[\\/:*?""<>|#^\[\]]", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim(' ', '.');
        return cleaned.Length == 0 ? "Lecture" : cleaned;
    }

    /// <summary>"05:07", or "1:05:07" past an hour: the formats the Audio Timestamp Player plugin reads.</summary>
    public static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>A clickable time: with the plugin it plays the recording from there, without it it's a plain link.</summary>
    private static string TimeLink(TimeSpan time) => $"[{FormatTime(time)}](#audio-time={FormatTime(time)})";

    /// <summary>The whole note. Without <paramref name="notes"/> it holds the recording and transcript only.</summary>
    public static string Render(string title, DateTime startedAt, LectureRecording recording, string audioFile, LectureNotes? notes)
    {
        var kind = title.Contains("meeting", StringComparison.OrdinalIgnoreCase) ? "meeting" : "lecture";
        var tag = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        var note = new StringBuilder();

        note.AppendLine("---");
        note.AppendLine($"date: {startedAt:yyyy-MM-dd}");
        note.AppendLine($"time: \"{startedAt:HH:mm}\"");
        note.AppendLine($"duration: \"{FormatTime(recording.Duration)}\"");
        note.AppendLine("tags:");
        note.AppendLine($"  - {kind}");

        if (tag.Length > 0 && tag != kind)
        {
            note.AppendLine($"  - {tag}");
        }

        note.AppendLine("---");
        note.AppendLine($"# {title}, {startedAt.ToString("ddd MMM d", CultureInfo.InvariantCulture)}");
        note.AppendLine();
        note.AppendLine($"![[{audioFile}]]");
        note.AppendLine();

        if (notes is null)
        {
            note.AppendLine("*No notes yet: the local model was off or still working when this was saved. The transcript is below.*");
            note.AppendLine();
        }
        else
        {
            note.AppendLine("## Summary");
            note.AppendLine(notes.Summary);
            note.AppendLine();
            note.AppendLine("## Due dates and to-dos");
            note.AppendLine(notes.DueItems.Count == 0 ? "None mentioned." : string.Join(Environment.NewLine, notes.DueItems.Select(item => $"- [ ] {item}")));
            note.AppendLine();
            note.AppendLine("## Notes");

            foreach (var (start, end, points) in notes.Sections)
            {
                note.AppendLine($"### {TimeLink(start)} to {FormatTime(end)}");
                note.AppendLine(string.Join(Environment.NewLine, points.Select(point => $"- {point}")));
                note.AppendLine();
            }
        }

        note.AppendLine("## Transcript");

        foreach (var line in recording.Lines)
        {
            note.AppendLine($"{TimeLink(line.At)} {line.Text}");
        }

        return note.ToString();
    }

    /// <summary>Notes from the local model, or null when it is off or fails.</summary>
    public static async Task<LectureNotes?> WriteNotesAsync(
        IModelGateway? gateway,
        string title,
        IReadOnlyList<TranscriptLine> lines,
        CancellationToken cancellationToken)
    {
        if (gateway is not { IsAvailable: true })
        {
            return null;
        }

        try
        {
            var sections = new List<(TimeSpan Start, TimeSpan End, IReadOnlyList<string> Points)>();
            var dueItems = new List<string>();

            foreach (var (start, end, text) in SplitIntoSections(lines))
            {
                var input = $"Title: {title}\n\nTranscript:\n{text}";
                var points = Bullets(await CompleteAsync(gateway, KeyPointsInstructions, input, cancellationToken));
                sections.Add((start, end, points.Select(point => FlagUnheardNumbers(point, text)).ToList()));
                var due = Bullets(await CompleteAsync(gateway, DueItemsInstructions, input, cancellationToken));
                dueItems.AddRange(due.Select(item => FlagUnheardNumbers(item, text)));
            }

            var allPoints = string.Join("\n", sections.SelectMany(section => section.Points).Select(point => $"- {point}"));
            var summary = Regex.Replace(await CompleteAsync(gateway, SummaryInstructions, $"Title: {title}\n\nNotes:\n{allPoints}", cancellationToken), @"[*#_`]+|\s+", " ").Trim();
            return new LectureNotes(summary, dueItems.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), sections);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Whisper's segments break mid-sentence ("...in seven" / "layers."), so a segment that doesn't end a
    /// sentence is joined to the next, keeping the first one's time. Capped so unpunctuated speech still breaks.
    /// </summary>
    public static List<TranscriptLine> JoinIntoSentences(IReadOnlyList<TranscriptLine> lines)
    {
        var joined = new List<TranscriptLine>();

        foreach (var line in lines)
        {
            if (joined.Count > 0 && joined[^1] is var last && !Regex.IsMatch(last.Text, @"[.?!)]$") && last.Text.Length < 250)
            {
                joined[^1] = last with { Text = $"{last.Text} {line.Text}" };
            }
            else
            {
                joined.Add(line);
            }
        }

        return joined;
    }

    /// <summary>Consecutive transcript lines grouped into sections the model can take in one pass.</summary>
    public static List<(TimeSpan Start, TimeSpan End, string Text)> SplitIntoSections(IReadOnlyList<TranscriptLine> lines)
    {
        var sections = new List<(TimeSpan, TimeSpan, string)>();
        var text = new StringBuilder();
        var start = TimeSpan.Zero;

        for (var index = 0; index < lines.Count; index++)
        {
            if (text.Length == 0)
            {
                start = lines[index].At;
            }

            text.Append(lines[index].Text).Append(' ');

            if (index == lines.Count - 1 || text.Length + lines[index + 1].Text.Length > SectionCharacters)
            {
                var end = index == lines.Count - 1 ? lines[index].At : lines[index + 1].At;
                sections.Add((start, end, text.ToString().Trim()));
                text.Clear();
            }
        }

        return sections;
    }

    /// <summary>
    /// The small local model sometimes changes a number (it once wrote port 447 for 443). These notes are
    /// studied from, so a point with a number the transcript doesn't have is flagged rather than trusted.
    /// </summary>
    public static string FlagUnheardNumbers(string point, string transcript) =>
        Regex.Matches(point, @"\d+").Any(number => !Regex.IsMatch(transcript, $@"(?<!\d){number.Value}(?!\d)"))
            ? $"{point} ⚠️ *check the recording: a number here isn't in the transcript*"
            : point;

    /// <summary>The model's list items, without markers, headings, or "none".</summary>
    public static List<string> Bullets(string reply) =>
        reply.Split('\n')
            .Select(line => Regex.Replace(line, @"^\s*(?:[-*•]|\d+[.)])\s*|\*\*", string.Empty).Trim())
            .Where(line => line.Length > 0 && !line.EndsWith(':') && !NothingFoundRegex().IsMatch(line))
            .ToList();

    private static async Task<string> CompleteAsync(IModelGateway gateway, string instructions, string input, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var response = await gateway.CompleteAsync(
            new ChatCompletionRequest(
                [
                    new ChatMessage("system", [ChatMessageContentPart.FromText(instructions)]),
                    new ChatMessage("user", [ChatMessageContentPart.FromText(input)])
                ],
                // 0, not the usual 0.2: notes should repeat the facts, not vary them.
                Temperature: 0),
            // Reasoning allows 384 tokens on the local model; Fast's 128 would cut the notes off.
            ModelRoute.Reasoning,
            timeout.Token);

        return ThinkBlockRegex().Replace(response.Content ?? string.Empty, string.Empty);
    }

    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlockRegex();

    // "none", "There are no deadlines.", "No assignments were mentioned."
    [GeneratedRegex(@"^(?:none|n/a)\b|^there (?:are|were|is) no\b|^no\s+(?:\w+\s+){0,3}(?:mentioned|found|given|listed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NothingFoundRegex();
}
