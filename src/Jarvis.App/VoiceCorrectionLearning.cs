using System.Text.RegularExpressions;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed record VoiceCorrectionDirective(string CorrectedText, string HeardText);

internal static partial class VoiceCorrectionLearning
{
    private const int MaxCorrectionEntries = 48;
    private const int MaxRejectedPhraseEntries = 48;

    public static bool TryParseCorrectionDirective(string input, out VoiceCorrectionDirective directive)
    {
        directive = default!;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = CollapseWhitespace(input);
        var match = HeardSecondCorrectionRegex().Match(trimmed);

        if (!match.Success)
        {
            match = HeardFirstCorrectionRegex().Match(trimmed);
        }

        if (!match.Success)
        {
            return false;
        }

        var correctedText = NormalizePhrase(match.Groups["corrected"].Value);
        var heardText = NormalizePhrase(match.Groups["heard"].Value);

        if (!CanLearnCorrection(heardText, correctedText))
        {
            return false;
        }

        directive = new VoiceCorrectionDirective(correctedText, heardText);
        return true;
    }

    public static bool IsReviewCommand(string input)
    {
        var normalized = NormalizeKey(input);
        return normalized is "voice corrections"
            or "show voice corrections"
            or "speech corrections"
            or "voice correction log"
            or "speech correction log";
    }

    public static bool CanLearnCorrection(string heardText, string correctedText)
    {
        var heard = NormalizePhrase(heardText);
        var corrected = NormalizePhrase(correctedText);

        if (string.IsNullOrWhiteSpace(heard)
            || string.IsNullOrWhiteSpace(corrected)
            || heard.Length > 160
            || corrected.Length > 160)
        {
            return false;
        }

        return !string.Equals(NormalizeKey(heard), NormalizeKey(corrected), StringComparison.Ordinal);
    }

    public static JarvisOptions RecordCorrection(
        JarvisOptions options,
        string heardText,
        string correctedText,
        double? confidence)
    {
        var heard = NormalizePhrase(heardText);
        var corrected = NormalizePhrase(correctedText);

        if (!CanLearnCorrection(heard, corrected))
        {
            return options;
        }

        var now = DateTimeOffset.UtcNow;
        var corrections = options.SpeechRecognitionCorrections
            .Where(entry => !string.IsNullOrWhiteSpace(entry.HeardText) && !string.IsNullOrWhiteSpace(entry.CorrectedText))
            .ToList();
        var existingIndex = corrections.FindIndex(entry =>
            string.Equals(NormalizeKey(entry.HeardText), NormalizeKey(heard), StringComparison.Ordinal));

        if (existingIndex >= 0)
        {
            var existing = corrections[existingIndex];
            corrections[existingIndex] = existing with
            {
                HeardText = heard,
                CorrectedText = corrected,
                CorrectionCount = Math.Max(1, existing.CorrectionCount) + 1,
                LastUpdatedUtc = now,
                LastConfidence = confidence
            };
        }
        else
        {
            corrections.Add(new SpeechRecognitionCorrectionEntry
            {
                HeardText = heard,
                CorrectedText = corrected,
                CorrectionCount = 1,
                LastUpdatedUtc = now,
                LastConfidence = confidence
            });
        }

        var updated = options with
        {
            SpeechRecognitionCorrections = corrections
                .OrderByDescending(entry => entry.CorrectionCount)
                .ThenByDescending(entry => entry.LastUpdatedUtc)
                .Take(MaxCorrectionEntries)
                .ToArray()
        };

        return RecordRejectedPhrase(updated, heard, confidence);
    }

    public static JarvisOptions RecordRejectedPhrase(
        JarvisOptions options,
        string heardText,
        double? confidence)
    {
        var heard = NormalizePhrase(heardText);

        if (string.IsNullOrWhiteSpace(heard) || heard.Length > 160)
        {
            return options;
        }

        var now = DateTimeOffset.UtcNow;
        var rejected = options.SpeechRecognitionRejectedPhrases
            .Where(entry => !string.IsNullOrWhiteSpace(entry.HeardText))
            .ToList();
        var existingIndex = rejected.FindIndex(entry =>
            string.Equals(NormalizeKey(entry.HeardText), NormalizeKey(heard), StringComparison.Ordinal));

        if (existingIndex >= 0)
        {
            var existing = rejected[existingIndex];
            rejected[existingIndex] = existing with
            {
                HeardText = heard,
                RejectionCount = Math.Max(1, existing.RejectionCount) + 1,
                LastRejectedUtc = now,
                LastConfidence = confidence
            };
        }
        else
        {
            rejected.Add(new SpeechRecognitionRejectedPhrase
            {
                HeardText = heard,
                RejectionCount = 1,
                LastRejectedUtc = now,
                LastConfidence = confidence
            });
        }

        return options with
        {
            SpeechRecognitionRejectedPhrases = rejected
                .OrderByDescending(entry => entry.LastRejectedUtc)
                .ThenByDescending(entry => entry.RejectionCount)
                .Take(MaxRejectedPhraseEntries)
                .ToArray()
        };
    }

    public static string BuildReview(JarvisOptions options)
    {
        var corrections = options.SpeechRecognitionCorrections
            .Where(entry => !string.IsNullOrWhiteSpace(entry.HeardText) && !string.IsNullOrWhiteSpace(entry.CorrectedText))
            .OrderByDescending(entry => entry.CorrectionCount)
            .ThenByDescending(entry => entry.LastUpdatedUtc)
            .Take(8)
            .ToArray();
        var rejected = options.SpeechRecognitionRejectedPhrases
            .Where(entry => !string.IsNullOrWhiteSpace(entry.HeardText))
            .OrderByDescending(entry => entry.LastRejectedUtc)
            .ThenByDescending(entry => entry.RejectionCount)
            .Take(8)
            .ToArray();
        var lines = new List<string>();

        lines.Add("Voice correction learning:");

        if (corrections.Length == 0)
        {
            lines.Add("- No learned corrections yet.");
        }
        else
        {
            lines.Add("- Learned corrections:");
            lines.AddRange(corrections.Select(entry =>
                $"  \"{entry.HeardText}\" -> \"{entry.CorrectedText}\" | count {entry.CorrectionCount} | updated {entry.LastUpdatedUtc.LocalDateTime:g}"));
        }

        if (rejected.Length == 0)
        {
            lines.Add("- No rejected transcripts logged yet.");
        }
        else
        {
            lines.Add("- Recent rejected transcripts:");
            lines.AddRange(rejected.Select(entry =>
                $"  \"{entry.HeardText}\" | rejections {entry.RejectionCount} | last {entry.LastRejectedUtc.LocalDateTime:g}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizePhrase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = CollapseWhitespace(value)
            .Trim()
            .Trim('"', '\'', '`', '“', '”', '‘', '’')
            .Trim();

        return trimmed.Trim(',', '.', '!', '?', ':', ';');
    }

    private static string NormalizeKey(string value)
    {
        var normalized = NormalizePhrase(value);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var chars = normalized
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray();

        return CollapseWhitespace(new string(chars));
    }

    private static string CollapseWhitespace(string value) =>
        WhitespaceRegex().Replace(value.Trim(), " ");

    [GeneratedRegex(@"^(?:no[, ]+)?i said (?<corrected>.+?)(?:,?\s+not|,?\s+instead of) (?<heard>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeardSecondCorrectionRegex();

    [GeneratedRegex(@"^(?:you heard|jarvis heard|you wrote|jarvis wrote) (?<heard>.+?)(?:,?\s+(?:but\s+)?i said) (?<corrected>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeardFirstCorrectionRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
