namespace Jarvis.Core;

internal static class MemoryPolicies
{
    public static string NormalizeRetentionPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "forever" or "permanent" => "forever",
            "long" or "long-term" => "long",
            "short" or "short-term" => "short",
            "standard" or "default" => "standard",
            "episodic" or "medium" => "episodic",
            "rolling" or "rolling-summary" => "rolling",
            "until-complete" or "until complete" => "until-complete",
            var normalized => normalized
        };
    }

    public static string ResolveRetentionPolicy(MemoryWriteRequest request)
    {
        var normalized = NormalizeRetentionPolicy(request.RetentionPolicy);

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        var kind = NormalizeLabel(request.Kind, "note");
        var category = NormalizeLabel(request.Category, string.Empty);

        if (kind == "reminder" || request.ReminderAtUtc is not null)
        {
            return "until-complete";
        }

        if (kind == "summary")
        {
            return "rolling";
        }

        if (kind == "episodic")
        {
            return "episodic";
        }

        if (kind == "preference"
            || kind == "entity"
            || kind == "relationship"
            || category is "routines" or "habits" or "favorites")
        {
            return "long";
        }

        return "standard";
    }

    public static DateTimeOffset? ResolveExpiryUtc(MemoryWriteRequest request, DateTimeOffset createdAtUtc)
    {
        if (request.ExpiresAtUtc is not null)
        {
            return request.ExpiresAtUtc.Value.ToUniversalTime();
        }

        return ResolveExpiryUtc(ResolveRetentionPolicy(request), createdAtUtc, request.ReminderAtUtc, request.ReminderStatus);
    }

    public static DateTimeOffset? ResolveExpiryUtc(
        string retentionPolicy,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? reminderAtUtc = null,
        string? reminderStatus = null)
    {
        var normalized = NormalizeRetentionPolicy(retentionPolicy);

        return normalized switch
        {
            "forever" => null,
            "long" => createdAtUtc.AddDays(365),
            "standard" => createdAtUtc.AddDays(180),
            "episodic" => createdAtUtc.AddDays(90),
            "rolling" => createdAtUtc.AddDays(45),
            "short" => createdAtUtc.AddDays(30),
            "until-complete" when IsReminderInactive(reminderStatus) => (reminderAtUtc ?? createdAtUtc).AddDays(14),
            "until-complete" => null,
            _ => createdAtUtc.AddDays(180)
        };
    }

    public static double ResolveImportanceScore(MemoryWriteRequest request)
    {
        if (request.ImportanceScore is double explicitScore)
        {
            return ClampImportance(explicitScore);
        }

        var kind = NormalizeLabel(request.Kind, "note");
        var category = NormalizeLabel(request.Category, string.Empty);
        var score = kind switch
        {
            "reminder" => 0.82d,
            "relationship" => 0.80d,
            "entity" => 0.72d,
            "preference" => 0.68d,
            "episodic" => 0.58d,
            "summary" => 0.45d,
            _ => 0.36d
        };

        if (request.Source.Equals("manual", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.07d;
        }

        if (request.UserApprovedRetention)
        {
            score += 0.08d;
        }

        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            var content = request.Content.Trim();

            if (content.Length >= 100)
            {
                score += 0.05d;
            }
            else if (content.Length <= 24)
            {
                score -= 0.04d;
            }

            if (ContainsImportanceCue(content))
            {
                score += 0.06d;
            }
        }

        if (request.Entities is { Length: > 0 })
        {
            score += Math.Min(0.16d, request.Entities.Length * 0.05d);
        }

        if (request.ReminderAtUtc is not null)
        {
            score += 0.10d;
        }

        if (category is "routines" or "habits" or "favorites")
        {
            score += 0.05d;
        }

        if (kind == "preference")
        {
            score += request.Category?.Trim().ToLowerInvariant() switch
            {
                "address" => 0.08d,
                "tone" or "response style" or "conversation" or "phrasing" => 0.06d,
                "apps" or "automations" or "locations" => 0.05d,
                _ => 0.02d
            };
        }

        return ClampImportance(score);
    }

    public static double ResolveImportanceScore(MemoryNote note)
    {
        var baseline = ResolveImportanceScore(new MemoryWriteRequest(
            note.Content,
            Kind: note.Kind,
            Category: note.Category,
            Source: note.Source,
            Privacy: note.Privacy,
            Tags: note.Tags,
            Context: note.Context,
            CreatedAtUtc: note.CreatedAtUtc,
            SummaryWindowStartUtc: note.SummaryWindowStartUtc,
            SummaryWindowEndUtc: note.SummaryWindowEndUtc,
            ImportanceScore: null,
            RetentionPolicy: note.RetentionPolicy,
            ExpiresAtUtc: note.ExpiresAtUtc,
            UserApprovedRetention: note.UserApprovedRetention,
            Entities: note.Entities,
            ReminderAtUtc: note.ReminderAtUtc,
            ReminderText: note.ReminderText,
            ReminderStatus: note.ReminderStatus));
        var observationBonus = Math.Min(0.14d, Math.Max(0, note.ObservationCount - 1) * 0.025d);
        var recencyBonus = note.LastObservedAtUtc is DateTimeOffset lastObservedAtUtc
            ? Math.Max(0d, 0.06d - Math.Min(0.06d, (DateTimeOffset.UtcNow - lastObservedAtUtc).TotalDays / 365d * 0.06d))
            : 0d;

        return ClampImportance(baseline + observationBonus + recencyBonus);
    }

    public static double BlendImportance(MemoryNote existing, MemoryWriteRequest incoming)
    {
        var incomingScore = incoming.ImportanceScore ?? ResolveImportanceScore(incoming);
        var observedScore = ResolveImportanceScore(existing);
        var blended = Math.Max(existing.ImportanceScore, (observedScore * 0.7d) + (incomingScore * 0.3d) + 0.03d);
        return ClampImportance(blended);
    }

    public static bool IsActive(MemoryNote note, DateTimeOffset nowUtc)
    {
        if (note.IsForgotten)
        {
            return false;
        }

        return !IsExpired(note, nowUtc);
    }

    public static bool IsExpired(MemoryNote note, DateTimeOffset nowUtc)
    {
        return note.ExpiresAtUtc is DateTimeOffset expiresAtUtc
            && expiresAtUtc <= nowUtc;
    }

    public static bool IsPendingReminder(MemoryNote note)
    {
        return note.ReminderAtUtc is not null
            && !note.IsForgotten
            && !IsReminderInactive(note.ReminderStatus);
    }

    public static bool IsDueReminder(MemoryNote note, DateTimeOffset nowUtc, TimeSpan lookahead)
    {
        return IsPendingReminder(note)
            && note.ReminderAtUtc is DateTimeOffset reminderAtUtc
            && reminderAtUtc <= nowUtc.Add(lookahead);
    }

    public static string NormalizeReminderStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "pending";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "done" => "completed",
            "complete" => "completed",
            "dismiss" => "dismissed",
            "snooze" => "snoozed",
            var normalized => normalized
        };
    }

    public static double ClampImportance(double value)
    {
        return Math.Clamp(value, 0.05d, 1d);
    }

    private static bool IsReminderInactive(string? value)
    {
        var normalized = NormalizeReminderStatus(value);
        return normalized is "completed" or "dismissed" or "canceled" or "cancelled";
    }

    private static bool ContainsImportanceCue(string content)
    {
        return content.Contains("always ", StringComparison.OrdinalIgnoreCase)
            || content.Contains("never ", StringComparison.OrdinalIgnoreCase)
            || content.Contains("important", StringComparison.OrdinalIgnoreCase)
            || content.Contains("favorite", StringComparison.OrdinalIgnoreCase)
            || content.Contains("prefer", StringComparison.OrdinalIgnoreCase)
            || content.Contains("every ", StringComparison.OrdinalIgnoreCase)
            || content.Contains("each ", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLabel(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
    }
}
