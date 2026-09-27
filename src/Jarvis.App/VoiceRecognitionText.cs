using System.Text.RegularExpressions;
using Jarvis.Core;

namespace Jarvis.App;

internal static partial class VoiceRecognitionText
{
    private static readonly string[] WakeLeadIns =
    [
        "hey",
        "okay",
        "ok",
        "hello",
        "hi",
        "please",
        "yo"
    ];

    private static readonly HashSet<string> WakeSkippableTokens = new(StringComparer.Ordinal)
    {
        "uh",
        "um",
        "erm",
        "ah",
        "well",
        "please",
        "hey",
        "okay",
        "ok",
        "hello",
        "hi",
        "yo"
    };

    private static readonly HashSet<string> CanonicalWakeVariants = new(StringComparer.Ordinal)
    {
        "jarvis",
        "jervis",
        "jarviss",
        "jarvice",
        "jarvus",
        "jarvess",
        // Spellings Whisper produced for a real "Jarvis" heard through a laptop microphone.
        "garbus",
        "gargus"
    };

    private static readonly HashSet<string> ConfirmationApprovals = new(StringComparer.Ordinal)
    {
        "yes",
        "yeah",
        "yep",
        "correct",
        "thats right",
        "that is right",
        "right",
        "do that",
        "continue",
        "go ahead"
    };

    private static readonly HashSet<string> ConfirmationDenials = new(StringComparer.Ordinal)
    {
        "no",
        "nope",
        "negative",
        "wrong",
        "cancel",
        "stop",
        "say that again",
        "repeat that",
        "try again"
    };

    public static string NormalizeTranscript(string text, JarvisOptions options, bool applyPersonalCorrections = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var tokens = Tokenize(text);

        for (var index = 0; index < Math.Min(tokens.Count, 3); index++)
        {
            var corrected = CorrectWakeToken(tokens[index].Match, options);

            if (!string.Equals(corrected, tokens[index].Match, StringComparison.Ordinal))
            {
                tokens[index] = tokens[index] with { Original = corrected, Match = corrected };
            }
        }

        var normalized = string.Join(' ', tokens.Select(token => token.Original));
        normalized = WhitespaceRegex().Replace(normalized.Trim(), " ");
        normalized = Regex.Replace(normalized, "\\s+([.,!?;:])", "$1");
        normalized = Regex.Replace(normalized, "([.,!?;:])([^\\s])", "$1 $2");

        if (applyPersonalCorrections)
        {
            normalized = ApplyPersonalCorrections(normalized, options);
        }

        if (options.SpeechRecognitionPunctuationRestorationEnabled && normalized.Length > 1)
        {
            normalized = char.ToUpperInvariant(normalized[0]) + normalized[1..];
        }

        return normalized;
    }

    public static string ApplyPersonalCorrections(string text, JarvisOptions options)
    {
        if (string.IsNullOrWhiteSpace(text) || options.SpeechRecognitionCorrections.Length == 0)
        {
            return text.Trim();
        }

        var corrected = text;
        var entries = options.SpeechRecognitionCorrections
            .Where(entry => !string.IsNullOrWhiteSpace(entry.HeardText) && !string.IsNullOrWhiteSpace(entry.CorrectedText))
            .OrderByDescending(entry => entry.HeardText.Length)
            .ThenByDescending(entry => entry.CorrectionCount);

        foreach (var entry in entries)
        {
            var heard = WhitespaceRegex().Replace(entry.HeardText.Trim(), " ");
            var replacement = entry.CorrectedText.Trim();

            if (string.IsNullOrWhiteSpace(heard) || string.IsNullOrWhiteSpace(replacement))
            {
                continue;
            }

            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(heard)}(?![\p{{L}}\p{{N}}])";
            corrected = Regex.Replace(
                corrected,
                pattern,
                replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        corrected = WhitespaceRegex().Replace(corrected.Trim(), " ");
        corrected = Regex.Replace(corrected, "\\s+([.,!?;:])", "$1");
        corrected = Regex.Replace(corrected, "([.,!?;:])([^\\s])", "$1 $2");
        return corrected;
    }

    public static bool TryExtractWakeCommand(string recognizedText, JarvisOptions options, out string commandText)
    {
        var tokens = Tokenize(recognizedText);

        foreach (var prefix in BuildWakePrefixTokens(options))
        {
            for (var startIndex = 0; startIndex <= Math.Min(2, tokens.Count - prefix.Length); startIndex++)
            {
                if (startIndex > 0
                    && tokens.Take(startIndex).Any(token => !WakeSkippableTokens.Contains(token.Match)))
                {
                    continue;
                }

                var matches = true;

                for (var index = 0; index < prefix.Length; index++)
                {
                    if (!IsWakeTokenMatch(tokens[startIndex + index].Match, prefix[index]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                commandText = NormalizeCommandText(
                    string.Join(' ', tokens.Skip(startIndex + prefix.Length).Select(token => token.Original)));
                return true;
            }
        }

        // Pauses get merged into one utterance, so "so if I say Jarvis open Spotify" is common.
        // Mid-sentence only an exact wake word counts; fuzzy matching there would catch "Travis" or "Marvin".
        for (var startIndex = 0; startIndex < tokens.Count; startIndex++)
        {
            if (tokens[startIndex].Match != CorrectWakeToken(CanonicalizeForMatching(options.WakePhrase), null))
            {
                continue;
            }

            commandText = NormalizeCommandText(
                string.Join(' ', tokens.Skip(startIndex + 1).Select(token => token.Original)));
            return true;
        }

        commandText = string.Empty;
        return false;
    }

    public static IReadOnlyList<string> BuildWakeGrammarChoices(JarvisOptions options)
    {
        var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in BuildWakeAliases(options))
        {
            phrases.Add(alias);

            foreach (var leadIn in WakeLeadIns)
            {
                phrases.Add($"{leadIn} {alias}");
            }
        }

        return phrases.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string BuildTranscriptionPrompt(JarvisOptions options)
    {
        var wakeAsset = WakeWordAssetCatalog.ResolveConfiguredAsset(options);
        var wakeAliases = BuildWakeAliases(options).Take(8).ToArray();
        var appAliasHints = options.ApplicationAliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias.Key) && !string.IsNullOrWhiteSpace(alias.Value))
            .Take(6)
            .Select(alias => $"{alias.Key.Trim()} means {DescribeAppAliasTarget(alias.Value)}")
            .ToArray();
        var instructions = new List<string>
        {
            "This is live speech for a Windows desktop assistant.",
            $"Assistant name: {options.AssistantName}.",
            $"Primary wake phrase: {options.WakePhrase}.",
            $"Treat these as equivalent wake words: {string.Join(", ", wakeAliases)}.",
            "Preserve command phrases and app names accurately.",
            "Common commands include: help, status, time, notes, weather, system, computer, apps, open app, focus app, close app, window state, send hotkey, open path, list files, read file, find files, browse, search web, screen, analyze document, analyze image, click element, type into, get element text, scroll, wait, run.",
            "If you hear Jervis, Jarviss, or Jarvice, transcribe it as Jarvis."
        };

        if (wakeAsset is not null && !wakeAsset.IsRecognizerDefault)
        {
            instructions.Add($"Loaded wake asset: {wakeAsset.BuildShortLabel()}.");
        }

        if (appAliasHints.Length > 0)
        {
            instructions.Add($"Known spoken app aliases: {string.Join(", ", appAliasHints)}.");
        }

        var correctionHints = options.SpeechRecognitionCorrections
            .Where(entry => !string.IsNullOrWhiteSpace(entry.HeardText) && !string.IsNullOrWhiteSpace(entry.CorrectedText))
            .OrderByDescending(entry => entry.CorrectionCount)
            .ThenByDescending(entry => entry.LastUpdatedUtc)
            .Take(6)
            .Select(entry => $"\"{entry.HeardText.Trim()}\" should be \"{entry.CorrectedText.Trim()}\"")
            .ToArray();

        if (correctionHints.Length > 0)
        {
            instructions.Add($"Personal correction hints: {string.Join("; ", correctionHints)}.");
        }

        if (!string.IsNullOrWhiteSpace(options.SpeechRecognitionPrompt))
        {
            instructions.Add(options.SpeechRecognitionPrompt.Trim());
        }

        return string.Join(' ', instructions);
    }

    public static bool LooksLikeConfirmationApproval(string input) =>
        ConfirmationApprovals.Contains(CanonicalizeForMatching(input));

    public static bool LooksLikeConfirmationDenial(string input) =>
        ConfirmationDenials.Contains(CanonicalizeForMatching(input));

    private static IReadOnlyList<string[]> BuildWakePrefixTokens(JarvisOptions options)
    {
        var prefixes = new List<string[]>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var alias in BuildWakeAliases(options))
        {
            AddPrefix(alias);

            foreach (var leadIn in WakeLeadIns)
            {
                AddPrefix($"{leadIn} {alias}");
            }
        }

        return prefixes;

        void AddPrefix(string phrase)
        {
            var tokens = Tokenize(phrase).Select(token => token.Match).ToArray();

            if (tokens.Length == 0)
            {
                return;
            }

            var key = string.Join('|', tokens);

            if (!seen.Add(key))
            {
                return;
            }

            prefixes.Add(tokens);
        }
    }

    public static IReadOnlyList<string> BuildWakeAliases(JarvisOptions options)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wakeAsset = WakeWordAssetCatalog.ResolveConfiguredAsset(options);

        AddAlias(options.WakePhrase);
        AddAlias(options.AssistantName);

        foreach (var alias in options.SpeechRecognitionWakeAliases)
        {
            AddAlias(alias);
        }

        if (CanonicalizeForMatching(options.WakePhrase) == "jarvis")
        {
            AddAlias("jervis");
            AddAlias("jarviss");
            AddAlias("jarvice");
            AddAlias("jarvus");
            AddAlias("jarvess");
            AddAlias("garbus");
            AddAlias("gargus");
        }

        if (wakeAsset is not null)
        {
            foreach (var alias in wakeAsset.BuildWakeAliases())
            {
                AddAlias(alias);
            }
        }

        return aliases.ToArray();

        void AddAlias(string? alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            var cleaned = WhitespaceRegex().Replace(alias.Trim(), " ");

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return;
            }

            aliases.Add(cleaned);
        }
    }

    private static string DescribeAppAliasTarget(string target)
    {
        var trimmed = target.Trim();

        if (trimmed.EndsWith(':'))
        {
            return trimmed[..^1];
        }

        var leaf = Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? trimmed : leaf;
    }

    private static List<SpokenToken> Tokenize(string text)
    {
        return text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token =>
            {
                var original = token.Trim();
                var match = NormalizeTokenForMatching(original);
                return string.IsNullOrWhiteSpace(match)
                    ? null
                    : new SpokenToken(original, match);
            })
            .Where(token => token is not null)
            .Cast<SpokenToken>()
            .ToList();
    }

    private static string NormalizeTokenForMatching(string token)
    {
        var trimmed = token
            .Trim()
            .Trim('"', '\'', ',', '.', '!', '?', ';', ':', '(', ')', '[', ']', '{', '}')
            .Replace("’", "'", StringComparison.Ordinal)
            .Replace("“", "\"", StringComparison.Ordinal)
            .Replace("”", "\"", StringComparison.Ordinal);

        return CorrectWakeToken(trimmed.ToLowerInvariant(), null);
    }

    private static string CorrectWakeToken(string token, JarvisOptions? options)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var canonical = CanonicalizeForMatching(token);

        if (CanonicalWakeVariants.Contains(canonical))
        {
            return options is null
                ? "jarvis"
                : options.WakePhrase.Trim().ToLowerInvariant();
        }

        return token;
    }

    private static string NormalizeCommandText(string text)
    {
        return text
            .Trim()
            .Trim(',', '.', '!', '?', ':', ';', '-', '"', '\'')
            .Trim();
    }

    private static bool IsWakeTokenMatch(string actual, string expected)
    {
        if (string.Equals(actual, expected, StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedActual = CorrectWakeToken(actual, null);
        var normalizedExpected = CorrectWakeToken(expected, null);

        if (string.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal))
        {
            return true;
        }

        var actualCanonical = CanonicalizeForMatching(actual);
        var expectedCanonical = CanonicalizeForMatching(expected);

        if (string.IsNullOrWhiteSpace(actualCanonical) || string.IsNullOrWhiteSpace(expectedCanonical))
        {
            return false;
        }

        var allowedDistance = expectedCanonical.Length >= 6 ? 2 : 1;
        return ComputeLevenshteinDistance(actualCanonical, expectedCanonical) <= allowedDistance;
    }

    private static int ComputeLevenshteinDistance(string left, string right)
    {
        var costs = new int[right.Length + 1];

        for (var index = 0; index <= right.Length; index++)
        {
            costs[index] = index;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            var previousDiagonal = costs[0];
            costs[0] = leftIndex;

            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var previous = costs[rightIndex];
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                costs[rightIndex] = Math.Min(
                    Math.Min(costs[rightIndex] + 1, costs[rightIndex - 1] + 1),
                    previousDiagonal + substitutionCost);
                previousDiagonal = previous;
            }
        }

        return costs[right.Length];
    }

    private static string CanonicalizeForMatching(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character == '.' || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray();

        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^\s*(?<head>[\p{L}'\-]{2,14}(?:\s+[\p{L}'\-]{2,14})?)\s*[,.]\s*(?<rest>\S.*)$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingVocativeRegex();

    // A sentence that starts with one of these is a real command, never a misheard wake word.
    private static readonly HashSet<string> CommandLeadWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "close", "play", "pause", "stop", "search", "set", "take", "turn", "what", "what's", "whats",
        "help", "status", "weather", "time", "notes", "note", "focus", "click", "type", "scroll", "wait",
        "run", "browse", "read", "list", "find", "show", "send", "get", "how", "where", "when", "who", "why",
        "can", "could", "please", "tell", "remind", "call", "start", "launch", "switch", "go"
    };

    /// <summary>
    /// Removes a leading name such as "Jokes, what time is it?" or "Join us. What time is it?". Use it
    /// only when the wake word has already been confirmed some other way: transcription often mishears
    /// "Jarvis" as a different word, and that word would otherwise stay glued to the front of the command.
    /// </summary>
    public static string StripLeadingVocative(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var match = LeadingVocativeRegex().Match(text);

        if (!match.Success)
        {
            return text;
        }

        var firstWord = match.Groups["head"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return CommandLeadWords.Contains(firstWord) ? text : match.Groups["rest"].Value.Trim();
    }

    private sealed record SpokenToken(string Original, string Match);
}
