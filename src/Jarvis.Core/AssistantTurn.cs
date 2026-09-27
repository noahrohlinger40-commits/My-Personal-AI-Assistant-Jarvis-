using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Core;

public static class SpokenReply
{
    private const int Budget = 300; // about 20 seconds of speech

    /// <summary>
    /// The part of a reply to say aloud. Every reply is spoken; a long one is cut to its opening
    /// sentences, since the full text is on screen. <paramref name="speakInFull"/> replies (page
    /// reading, calendar) are read whole.
    /// </summary>
    public static string Build(string reply, bool speakInFull)
    {
        if (speakInFull)
        {
            return reply.Trim();
        }

        var spoken = new StringBuilder();

        foreach (var line in reply.Split('\n'))
        {
            // Emoji and markdown bullets are for the eye. The Windows fallback voice reads emoji aloud
            // ("smiling face"), and "**Sources:**" or "- milk" should sound like plain words.
            var text = Regex.Replace(line, @"[\p{So}\p{Cs}\p{Cf}️*`]", "").Trim().Trim('-', '•', '#').Trim();

            if (text.Equals("Sources:", StringComparison.OrdinalIgnoreCase))
            {
                break; // a citation list is for reading, not listening
            }

            foreach (var sentence in Regex.Split(text, @"(?<=[.!?])\s+"))
            {
                if (sentence.Length == 0)
                {
                    continue;
                }

                if (spoken.Length > 0 && spoken.Length + sentence.Length > Budget)
                {
                    return spoken.Append(" The rest is on screen.").ToString();
                }

                spoken.Append(spoken.Length > 0 ? " " : "").Append(sentence);

                // A line break is a pause. Without end punctuation the voice runs the lines together.
                if (!".!?:;,".Contains(sentence[^1]))
                {
                    spoken.Append('.');
                }
            }
        }

        return spoken.ToString();
    }
}

public sealed record AssistantTurn(
    DateTimeOffset TimestampUtc,
    string UserInput,
    string ResponseText,
    bool ShouldExit = false,
    string Intent = "",
    string SessionId = "",
    bool IsFollowUpQuestion = false,
    AssistantToolExecution[]? ToolResults = null,
    AssistantCitation[]? Citations = null,
    string Privacy = "persistent",
    InteractionContextSnapshot? Context = null,
    bool SpeakInFull = false);

public sealed record MemoryNote(
    string Id,
    string Content,
    DateTimeOffset CreatedAtUtc,
    string Kind = "note",
    string Category = "",
    string Source = "manual",
    string Privacy = "persistent",
    string[]? Tags = null,
    InteractionContextSnapshot? Context = null,
    int ObservationCount = 1,
    DateTimeOffset? LastObservedAtUtc = null,
    DateTimeOffset? SummaryWindowStartUtc = null,
    DateTimeOffset? SummaryWindowEndUtc = null,
    double ImportanceScore = 0.35d,
    string RetentionPolicy = "",
    DateTimeOffset? ExpiresAtUtc = null,
    bool UserApprovedRetention = false,
    StructuredMemoryReference[]? Entities = null,
    DateTimeOffset? ReminderAtUtc = null,
    string ReminderText = "",
    string ReminderStatus = "",
    bool IsForgotten = false,
    DateTimeOffset? ForgottenAtUtc = null,
    string ForgottenReason = "");
