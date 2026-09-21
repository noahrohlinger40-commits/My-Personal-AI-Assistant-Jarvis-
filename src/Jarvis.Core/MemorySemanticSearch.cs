namespace Jarvis.Core;

internal static class MemorySemanticSearch
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "about",
        "and",
        "are",
        "as",
        "at",
        "be",
        "but",
        "by",
        "did",
        "do",
        "for",
        "from",
        "had",
        "have",
        "how",
        "i",
        "if",
        "in",
        "into",
        "is",
        "it",
        "me",
        "my",
        "of",
        "on",
        "or",
        "our",
        "so",
        "that",
        "the",
        "their",
        "them",
        "there",
        "they",
        "this",
        "to",
        "up",
        "us",
        "was",
        "we",
        "what",
        "when",
        "where",
        "who",
        "why",
        "with",
        "you",
        "your"
    };

    private static readonly Dictionary<string, string> CanonicalTerms = BuildCanonicalTerms();

    public static IReadOnlyList<MemoryNote> Search(
        IReadOnlyList<MemoryNote> notes,
        string query,
        int maxResults)
    {
        return Search(notes, query, maxResults, new MemoryQueryContext(query));
    }

    public static IReadOnlyList<MemoryNote> Search(
        IReadOnlyList<MemoryNote> notes,
        string query,
        int maxResults,
        MemoryQueryContext queryContext)
    {
        if (notes.Count == 0
            || maxResults <= 0
            || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<MemoryNote>();
        }

        var queryDocument = SearchDocument.Create(BuildQueryText(query, queryContext));

        if (queryDocument.TermCounts.Count == 0 && queryDocument.Bigrams.Count == 0)
        {
            return Array.Empty<MemoryNote>();
        }

        var documents = notes
            .Select(note => new ScoredDocument(note, SearchDocument.Create(BuildDocumentText(note))))
            .ToArray();
        var documentFrequency = BuildDocumentFrequency(documents.Select(item => item.Document));
        var queryVector = BuildVector(queryDocument, documentFrequency, documents.Length);
        var queryVectorNorm = ComputeVectorNorm(queryVector);

        if (queryVectorNorm <= 0)
        {
            return Array.Empty<MemoryNote>();
        }

        var minimumScore = GetMinimumScore(queryDocument);
        var now = DateTimeOffset.UtcNow;

        return documents
            .Select(item => new
            {
                item.Note,
                Score = Score(
                    item.Document,
                    queryDocument,
                    queryVector,
                    queryVectorNorm,
                    documentFrequency,
                    documents.Length,
                    item.Note.CreatedAtUtc,
                    now,
                    item.Note,
                    queryContext)
            })
            .Where(item => item.Score >= minimumScore)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Note.CreatedAtUtc)
            .Take(maxResults)
            .Select(item => item.Note)
            .ToArray();
    }

    private static Dictionary<string, int> BuildDocumentFrequency(IEnumerable<SearchDocument> documents)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            foreach (var term in document.TermCounts.Keys)
            {
                Increment(counts, term);
            }

            foreach (var bigram in document.Bigrams)
            {
                Increment(counts, BigramKey(bigram));
            }
        }

        return counts;
    }

    private static Dictionary<string, double> BuildVector(
        SearchDocument document,
        IReadOnlyDictionary<string, int> documentFrequency,
        int totalDocuments)
    {
        var vector = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var (term, frequency) in document.TermCounts)
        {
            var weight = (1d + Math.Log(frequency)) * ComputeInverseDocumentFrequency(documentFrequency, term, totalDocuments);

            if (ContainsDigit(term))
            {
                weight *= 1.1d;
            }

            vector[term] = weight;
        }

        foreach (var bigram in document.Bigrams)
        {
            var key = BigramKey(bigram);
            vector[key] = 1.35d * ComputeInverseDocumentFrequency(documentFrequency, key, totalDocuments);
        }

        return vector;
    }

    private static double Score(
        SearchDocument document,
        SearchDocument query,
        IReadOnlyDictionary<string, double> queryVector,
        double queryVectorNorm,
        IReadOnlyDictionary<string, int> documentFrequency,
        int totalDocuments,
        DateTimeOffset createdAtUtc,
        DateTimeOffset now,
        MemoryNote note,
        MemoryQueryContext queryContext)
    {
        var documentVector = BuildVector(document, documentFrequency, totalDocuments);
        var documentVectorNorm = ComputeVectorNorm(documentVector);

        if (documentVectorNorm <= 0)
        {
            return 0;
        }

        var cosineSimilarity = ComputeDotProduct(queryVector, documentVector) / (queryVectorNorm * documentVectorNorm);
        var matchedTerms = query.TermCounts.Keys.Count(document.TermCounts.ContainsKey);
        var matchedBigrams = query.Bigrams.Count(document.Bigrams.Contains);
        var phraseMatch = !string.IsNullOrWhiteSpace(query.SearchText)
            && document.SearchText.Contains(query.SearchText, StringComparison.Ordinal);
        var trigramDice = ComputeDiceCoefficient(query.CharacterTrigrams, document.CharacterTrigrams);

        if (cosineSimilarity <= 0
            && matchedTerms == 0
            && matchedBigrams == 0
            && !phraseMatch
            && trigramDice < 0.34d)
        {
            return 0;
        }

        var termCoverageBonus = query.TermCounts.Count == 0
            ? 0
            : 0.24d * matchedTerms / query.TermCounts.Count;
        var bigramBonus = query.Bigrams.Count == 0
            ? 0
            : 0.18d * matchedBigrams / query.Bigrams.Count;
        var phraseBonus = phraseMatch ? 0.35d : 0;
        var fuzzyBonus = 0.16d * trigramDice;
        var ageInDays = Math.Max(0, (now - createdAtUtc).TotalDays);
        var recencyBonus = 0.08d / (1d + ageInDays / 30d);
        var contextBonus = ComputeContextBonus(note.Context, queryContext);
        var categoryBonus = ComputeCategoryBonus(note, queryContext);
        var importanceBonus = 0.22d * Math.Clamp(note.ImportanceScore, 0d, 1d);
        var reminderBonus = ComputeReminderBonus(note, now, query);

        return cosineSimilarity + termCoverageBonus + bigramBonus + phraseBonus + fuzzyBonus + recencyBonus + contextBonus + categoryBonus + importanceBonus + reminderBonus;
    }

    private static double ComputeDotProduct(
        IReadOnlyDictionary<string, double> left,
        IReadOnlyDictionary<string, double> right)
    {
        if (left.Count > right.Count)
        {
            return ComputeDotProduct(right, left);
        }

        var sum = 0d;

        foreach (var (term, weight) in left)
        {
            if (right.TryGetValue(term, out var otherWeight))
            {
                sum += weight * otherWeight;
            }
        }

        return sum;
    }

    private static double ComputeVectorNorm(IReadOnlyDictionary<string, double> vector)
    {
        var sum = vector.Values.Sum(weight => weight * weight);
        return Math.Sqrt(sum);
    }

    private static double ComputeInverseDocumentFrequency(
        IReadOnlyDictionary<string, int> documentFrequency,
        string term,
        int totalDocuments)
    {
        var frequency = documentFrequency.TryGetValue(term, out var value) ? value : 0;
        return Math.Log(1d + (totalDocuments + 1d) / (frequency + 0.5d)) + 1d;
    }

    private static double ComputeDiceCoefficient(
        IReadOnlySet<string> left,
        IReadOnlySet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var intersection = left.Count <= right.Count
            ? left.Count(right.Contains)
            : right.Count(left.Contains);

        return 2d * intersection / (left.Count + right.Count);
    }

    private static double GetMinimumScore(SearchDocument query)
    {
        var signalCount = query.TermCounts.Count + query.Bigrams.Count;

        return signalCount switch
        {
            <= 1 => 0.18d,
            2 => 0.16d,
            _ => 0.14d
        };
    }

    private static void Increment(IDictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out var value) ? value + 1 : 1;
    }

    private static string BigramKey(string value) => "bigram:" + value;

    private static bool ContainsDigit(string value) => value.Any(char.IsDigit);

    private static string BuildDocumentText(MemoryNote note)
    {
        var parts = new List<string>
        {
            note.Content
        };

        AddIfPresent(parts, note.Kind);
        AddIfPresent(parts, note.Category);
        AddRange(parts, note.Tags);

        if (note.Context is not null)
        {
            AddIfPresent(parts, note.Context.TimeOfDay);
            AddIfPresent(parts, note.Context.Location);
            AddIfPresent(parts, note.Context.ActiveApp);
            AddIfPresent(parts, note.Context.ActiveWindow);
            AddRange(parts, note.Context.VisibleApps);
            AddRange(parts, note.Context.RecentActivity);
        }

        if (note.Entities is { Length: > 0 })
        {
            foreach (var entity in note.Entities)
            {
                AddIfPresent(parts, entity.Kind);
                AddIfPresent(parts, entity.Name);
                AddIfPresent(parts, entity.Summary);
                AddRange(parts, entity.SafeAliases);

                foreach (var attribute in entity.SafeAttributes)
                {
                    AddIfPresent(parts, attribute.Key);
                    AddIfPresent(parts, attribute.Value);
                }
            }
        }

        AddIfPresent(parts, note.ReminderText);
        AddIfPresent(parts, note.ReminderStatus);

        return string.Join(' ', parts);
    }

    private static string BuildQueryText(string query, MemoryQueryContext context)
    {
        var parts = new List<string>
        {
            query
        };

        AddIfPresent(parts, context.TimeOfDay);
        AddIfPresent(parts, context.Location);
        AddIfPresent(parts, context.ActiveApp);
        AddIfPresent(parts, context.ActiveWindow);
        AddRange(parts, context.RecentActivity);

        return string.Join(' ', parts);
    }

    private static void AddIfPresent(ICollection<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value.Trim());
        }
    }

    private static void AddRange(ICollection<string> parts, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            AddIfPresent(parts, value);
        }
    }

    private static double ComputeContextBonus(InteractionContextSnapshot? noteContext, MemoryQueryContext queryContext)
    {
        if (noteContext is null)
        {
            return 0;
        }

        var bonus = 0d;
        bonus += ComputeStringMatchBonus(noteContext.TimeOfDay, queryContext.TimeOfDay, 0.08d, 0.03d);
        bonus += ComputeStringMatchBonus(noteContext.Location, queryContext.Location, 0.12d, 0.05d);
        bonus += ComputeStringMatchBonus(noteContext.ActiveApp, queryContext.ActiveApp, 0.14d, 0.06d);
        bonus += ComputeStringMatchBonus(noteContext.ActiveWindow, queryContext.ActiveWindow, 0.08d, 0.03d);

        if (noteContext.SafeRecentActivity.Length > 0 && queryContext.SafeRecentActivity.Length > 0)
        {
            var overlap = noteContext.SafeRecentActivity.Count(noteActivity =>
                queryContext.SafeRecentActivity.Any(queryActivity =>
                    IsLooseMatch(noteActivity, queryActivity)));

            bonus += Math.Min(0.12d, overlap * 0.04d);
        }

        return bonus;
    }

    private static double ComputeCategoryBonus(MemoryNote note, MemoryQueryContext queryContext)
    {
        if (!string.Equals(note.Kind, "preference", StringComparison.OrdinalIgnoreCase))
        {
            return note.Kind switch
            {
                "relationship" => 0.08d,
                "entity" => 0.06d,
                "reminder" => 0.05d,
                _ => 0
            };
        }

        var query = queryContext.Query;

        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(note.Category))
        {
            return 0.03d;
        }

        return query.Contains(note.Category, StringComparison.OrdinalIgnoreCase) ? 0.08d : 0.03d;
    }

    private static double ComputeReminderBonus(MemoryNote note, DateTimeOffset now, SearchDocument query)
    {
        if (note.ReminderAtUtc is not DateTimeOffset reminderAtUtc
            || !MemoryPolicies.IsPendingReminder(note))
        {
            return 0;
        }

        var bonus = 0d;
        var hoursUntilDue = (reminderAtUtc - now).TotalHours;

        if (hoursUntilDue <= 24)
        {
            bonus += 0.08d;
        }

        if (hoursUntilDue <= 0)
        {
            bonus += 0.08d;
        }

        if (query.SearchText.Contains("today", StringComparison.Ordinal)
            || query.SearchText.Contains("tomorrow", StringComparison.Ordinal)
            || query.SearchText.Contains("reminder", StringComparison.Ordinal)
            || query.SearchText.Contains("due", StringComparison.Ordinal))
        {
            bonus += 0.06d;
        }

        return bonus;
    }

    private static double ComputeStringMatchBonus(string left, string right, double exactBonus, double partialBonus)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        if (IsLooseMatch(left, right))
        {
            return exactBonus;
        }

        var normalizedLeft = NormalizeLooseText(left);
        var normalizedRight = NormalizeLooseText(right);
        return normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal)
            || normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal)
            ? partialBonus
            : 0;
    }

    private static bool IsLooseMatch(string left, string right)
    {
        return string.Equals(NormalizeLooseText(left), NormalizeLooseText(right), StringComparison.Ordinal);
    }

    private static string NormalizeLooseText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var characters = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ')
            .ToArray();

        return string.Join(' ', new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static Dictionary<string, string> BuildCanonicalTerms()
    {
        var groups = new[]
        {
            new[] { "appointment", "meeting", "session", "consultation", "visit" },
            new[] { "meeting", "sync", "standup", "checkin", "1on1", "oneonone" },
            new[] { "todo", "task", "chore", "errand", "assignment", "homework" },
            new[] { "buy", "purchase", "order", "pickup", "pick", "grab" },
            new[] { "fix", "repair", "resolve", "patch" },
            new[] { "bug", "issue", "problem", "error", "fault" },
            new[] { "study", "review", "revise", "practice", "learn" },
            new[] { "doctor", "physician", "medical", "clinic" },
            new[] { "dentist", "dental" },
            new[] { "email", "mail", "inbox", "message", "msg", "dm", "text", "ping" },
            new[] { "deadline", "due", "cutoff", "expiry", "expiration" },
            new[] { "birthday", "bday" },
            new[] { "bill", "invoice", "payment", "rent", "fee" },
            new[] { "car", "vehicle", "auto" },
            new[] { "class", "course", "lecture", "seminar", "lab" },
            new[] { "travel", "trip", "flight", "hotel", "booking" },
            new[] { "workout", "exercise", "gym", "training", "run" },
            new[] { "idea", "concept", "thought" },
            new[] { "calibrate", "calibration", "tune", "tuning", "adjust", "setup", "configure", "config" },
            new[] { "note", "memo", "remember" },
            new[] { "mom", "mother", "mum" },
            new[] { "dad", "father" },
            new[] { "spouse", "wife", "husband" }
        };

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var canonical = group[0];

            foreach (var value in group)
            {
                map[value] = canonical;
            }
        }

        return map;
    }

    private sealed class SearchDocument
    {
        private SearchDocument(
            Dictionary<string, int> termCounts,
            HashSet<string> bigrams,
            HashSet<string> characterTrigrams,
            string searchText)
        {
            TermCounts = termCounts;
            Bigrams = bigrams;
            CharacterTrigrams = characterTrigrams;
            SearchText = searchText;
        }

        public Dictionary<string, int> TermCounts { get; }

        public HashSet<string> Bigrams { get; }

        public HashSet<string> CharacterTrigrams { get; }

        public string SearchText { get; }

        public static SearchDocument Create(string text)
        {
            var tokens = ExtractTokens(text);
            var termCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var token in tokens)
            {
                Increment(termCounts, token);
            }

            var searchText = string.Join(' ', tokens);
            var bigrams = BuildBigrams(tokens);
            var characterTrigrams = BuildCharacterTrigrams(searchText);
            return new SearchDocument(termCounts, bigrams, characterTrigrams, searchText);
        }

        private static List<string> ExtractTokens(string text)
        {
            var tokens = new List<string>();
            var buffer = new char[text.Length];
            var length = 0;

            foreach (var character in text)
            {
                if (char.IsLetterOrDigit(character))
                {
                    buffer[length++] = char.ToLowerInvariant(character);
                    continue;
                }

                FlushBuffer();
            }

            FlushBuffer();
            return tokens;

            void FlushBuffer()
            {
                if (length == 0)
                {
                    return;
                }

                var rawToken = new string(buffer, 0, length);
                length = 0;
                var normalized = NormalizeToken(rawToken);

                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    tokens.Add(normalized);
                }
            }
        }

        private static string NormalizeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            var value = token.Trim();

            if (value.Length == 0)
            {
                return string.Empty;
            }

            value = Canonicalize(value);
            value = Stem(value);
            value = Canonicalize(value);

            if (!ContainsDigit(value))
            {
                if (value.Length <= 1 || StopWords.Contains(value))
                {
                    return string.Empty;
                }
            }

            return value;
        }

        private static string Canonicalize(string token)
        {
            return CanonicalTerms.TryGetValue(token, out var canonical)
                ? canonical
                : token;
        }

        private static string Stem(string token)
        {
            if (token.Length <= 3)
            {
                return token;
            }

            if (token.EndsWith("ization", StringComparison.Ordinal))
            {
                return token[..^7] + "ize";
            }

            if (token.EndsWith("ation", StringComparison.Ordinal))
            {
                return token[..^5] + "ate";
            }

            if (token.EndsWith("ator", StringComparison.Ordinal))
            {
                return token[..^4] + "ate";
            }

            if (token.EndsWith("ingly", StringComparison.Ordinal) && token.Length > 7)
            {
                token = token[..^5];
            }
            else if (token.EndsWith("edly", StringComparison.Ordinal) && token.Length > 6)
            {
                token = token[..^4];
            }
            else if (token.EndsWith("ing", StringComparison.Ordinal) && token.Length > 5)
            {
                token = TrimDoubleEnding(token[..^3]);
            }
            else if (token.EndsWith("ed", StringComparison.Ordinal) && token.Length > 4)
            {
                token = TrimDoubleEnding(token[..^2]);
            }

            if (token.EndsWith("ment", StringComparison.Ordinal) && token.Length > 6)
            {
                token = token[..^4];
            }

            if (token.EndsWith("ies", StringComparison.Ordinal) && token.Length > 4)
            {
                token = token[..^3] + "y";
            }
            else if (token.EndsWith("ers", StringComparison.Ordinal) && token.Length > 5)
            {
                token = token[..^3];
            }
            else if (token.EndsWith("er", StringComparison.Ordinal) && token.Length > 4)
            {
                token = token[..^2];
            }
            else if (token.EndsWith("ly", StringComparison.Ordinal) && token.Length > 4)
            {
                token = token[..^2];
            }
            else if ((token.EndsWith("ses", StringComparison.Ordinal)
                    || token.EndsWith("xes", StringComparison.Ordinal)
                    || token.EndsWith("zes", StringComparison.Ordinal)
                    || token.EndsWith("ches", StringComparison.Ordinal)
                    || token.EndsWith("shes", StringComparison.Ordinal))
                && token.Length > 4)
            {
                token = token[..^2];
            }
            else if (token.EndsWith('s') && token.Length > 3 && !token.EndsWith("ss", StringComparison.Ordinal))
            {
                token = token[..^1];
            }

            return token;
        }

        private static string TrimDoubleEnding(string token)
        {
            if (token.Length >= 2
                && token[^1] == token[^2]
                && !"aeiou".Contains(token[^1]))
            {
                return token[..^1];
            }

            return token;
        }

        private static HashSet<string> BuildBigrams(IReadOnlyList<string> tokens)
        {
            var bigrams = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 1; index < tokens.Count; index++)
            {
                bigrams.Add(tokens[index - 1] + "|" + tokens[index]);
            }

            return bigrams;
        }

        private static HashSet<string> BuildCharacterTrigrams(string text)
        {
            var source = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
            var trigrams = new HashSet<string>(StringComparer.Ordinal);

            if (source.Length < 3)
            {
                return trigrams;
            }

            for (var index = 0; index <= source.Length - 3; index++)
            {
                trigrams.Add(source.Substring(index, 3));
            }

            return trigrams;
        }
    }

    private sealed record ScoredDocument(MemoryNote Note, SearchDocument Document);
}
