namespace Jarvis.Core;

public static class AssistantPresenceStyle
{
    private static readonly string[] GreetingPhrases =
    [
        "hi",
        "hello",
        "hey",
        "good morning",
        "good afternoon",
        "good evening"
    ];

    private static readonly string[] CheckInPhrases =
    [
        "how are you",
        "how are you doing",
        "hows it going",
        "how's it going",
        "you there"
    ];

    private static readonly string[] GratitudePhrases =
    [
        "thanks",
        "thank you",
        "appreciate it",
        "much appreciated"
    ];

    private static readonly string[] IdentityPhrases =
    [
        "who are you",
        "what are you",
        "whats your name",
        "what's your name"
    ];

    private static readonly string[] ReadinessPhrases =
    [
        "are you ready",
        "you ready",
        "ready",
        "still there"
    ];

    private static readonly string[] ApologyPhrases =
    [
        "sorry",
        "my bad",
        "apologies"
    ];

    private static readonly string[] ComplimentPhrases =
    [
        "nice work",
        "good work",
        "good job",
        "well done"
    ];

    private static readonly string[] ClosingPhrases =
    [
        "bye",
        "goodbye",
        "good night",
        "goodnight",
        "see you",
        "talk later"
    ];

    private static readonly string[] UrgentTerms =
    [
        "asap",
        "urgent",
        "right now",
        "immediately",
        "as soon as possible",
        "quickly"
    ];

    private static readonly string[] FrustratedTerms =
    [
        "not working",
        "broken",
        "stuck",
        "frustrated",
        "annoying",
        "hate this",
        "wtf",
        "damn",
        "issue",
        "problem"
    ];

    public static bool TryBuildSocialReply(
        string input,
        JarvisOptions options,
        string preferredUserAddress,
        out string response)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalized = NormalizeComparable(input);

        if (LooksLikeGreeting(normalized))
        {
            response = BuildGreetingReply(normalized, options, preferredUserAddress);
            return true;
        }

        if (LooksLikeCheckIn(normalized))
        {
            response = BuildCheckInReply(options);
            return true;
        }

        if (LooksLikeIdentity(normalized))
        {
            response = BuildIdentityReply(options);
            return true;
        }

        if (LooksLikeReadiness(normalized))
        {
            response = BuildReadinessReply(options);
            return true;
        }

        if (LooksLikeGratitude(normalized))
        {
            response = BuildGratitudeReply(options, preferredUserAddress);
            return true;
        }

        if (LooksLikeApology(normalized))
        {
            response = BuildApologyReply(options);
            return true;
        }

        if (LooksLikeCompliment(normalized))
        {
            response = BuildComplimentReply(options, preferredUserAddress);
            return true;
        }

        if (LooksLikeClosing(normalized))
        {
            response = BuildClosingReply(options);
            return true;
        }

        response = string.Empty;
        return false;
    }

    public static bool TryBuildAcknowledgement(string input, JarvisOptions options, out string acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalized = NormalizeComparable(input);
        var style = AssistantPersonaConfiguration.ResolveResponseStyle(options);

        if (LooksLikeGreeting(normalized))
        {
            acknowledgement = style == "cinematic" ? "Right here." : "I'm here.";
            return true;
        }

        if (LooksLikeCheckIn(normalized))
        {
            acknowledgement = style == "concise" ? "Steady." : "Steady and ready.";
            return true;
        }

        if (LooksLikeIdentity(normalized))
        {
            acknowledgement = style == "cinematic" ? "Jarvis." : options.AssistantName;
            return true;
        }

        if (LooksLikeReadiness(normalized))
        {
            acknowledgement = style == "concise" ? "Ready." : "Ready when you are.";
            return true;
        }

        if (LooksLikeGratitude(normalized))
        {
            acknowledgement = "Of course.";
            return true;
        }

        if (LooksLikeApology(normalized))
        {
            acknowledgement = "No issue.";
            return true;
        }

        if (LooksLikeCompliment(normalized))
        {
            acknowledgement = options.AssistantSmallTalkEnabled ? "Appreciated." : "Understood.";
            return true;
        }

        if (LooksLikeClosing(normalized))
        {
            acknowledgement = style == "cinematic" ? "Holding on standby." : "Standing by.";
            return true;
        }

        var signal = AnalyzeSignal(normalized);

        acknowledgement = signal switch
        {
            PresenceSignal.UrgentAndFrustrated => "I see the issue. Prioritizing it now.",
            PresenceSignal.Urgent => "Prioritizing it now.",
            PresenceSignal.Frustrated => style == "concise" ? "Working the issue." : "I see the issue. Working it.",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(acknowledgement);
    }

    public static string ApplyResponsePresence(
        string userInput,
        string responseText,
        JarvisOptions options,
        AgentIntentKind intent,
        bool isFollowUpQuestion,
        string preferredUserAddress = "")
    {
        ArgumentNullException.ThrowIfNull(options);

        var trimmed = responseText?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed)
            || ShouldLeaveUntouched(trimmed, intent, isFollowUpQuestion))
        {
            return trimmed;
        }

        var lead = BuildResponseLead(userInput, options, preferredUserAddress);

        if (string.IsNullOrWhiteSpace(lead)
            || trimmed.StartsWith(lead, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{lead} {trimmed}";
    }

    private static string BuildGreetingReply(string normalizedInput, JarvisOptions options, string preferredUserAddress)
    {
        var salutation = ResolveGreeting(normalizedInput, DateTimeOffset.Now);
        var address = BuildOptionalAddress(options, preferredUserAddress);
        var style = AssistantPersonaConfiguration.ResolveResponseStyle(options);

        return style switch
        {
            "concise" => $"{salutation}{address}. Ready.",
            "normal" => $"{salutation}{address}. I'm here and ready.",
            "detailed" => $"{salutation}{address}. I'm here, synced, and ready to help.",
            "cinematic" => $"{salutation}{address}. Systems are up. Ready when you are.",
            _ => $"{salutation}{address}. I'm here and ready."
        };
    }

    private static string BuildCheckInReply(JarvisOptions options)
    {
        var style = AssistantPersonaConfiguration.ResolveResponseStyle(options);

        return style switch
        {
            "concise" => "Operational and ready.",
            "detailed" => "Operational, clear, and ready to help with whatever is next.",
            "cinematic" => "Operational. Clear-headed and ready for the next move.",
            _ => "Steady and ready to help."
        };
    }

    private static string BuildGratitudeReply(JarvisOptions options, string preferredUserAddress)
    {
        if (!options.AssistantSmallTalkEnabled)
        {
            return "You're welcome.";
        }

        var address = BuildOptionalAddress(options, preferredUserAddress);
        var style = AssistantPersonaConfiguration.ResolveResponseStyle(options);

        return style switch
        {
            "concise" => $"Any time{address}.",
            "detailed" => $"Any time{address}. I'm here when you want the next step.",
            "cinematic" => $"Any time{address}. Ready when you want to move again.",
            _ => $"Any time{address}."
        };
    }

    private static string BuildIdentityReply(JarvisOptions options)
    {
        var style = AssistantPersonaConfiguration.ResolveResponseStyle(options);

        return style switch
        {
            "concise" => $"{options.AssistantName}. Your desktop operator assistant.",
            "detailed" => $"{options.AssistantName}. Your desktop-side operator assistant for planning, automation, and live system context.",
            "cinematic" => $"{options.AssistantName}. Your live desktop operator for planning, control, and execution.",
            _ => $"{options.AssistantName}. Your desktop assistant for planning, automation, and live workspace control."
        };
    }

    private static string BuildReadinessReply(JarvisOptions options)
    {
        var style = AssistantPersonaConfiguration.ResolveResponseStyle(options);

        return style switch
        {
            "concise" => "Ready.",
            "detailed" => "Ready. Systems are stable and I'm set for the next directive.",
            "cinematic" => "Ready. Standing by for the next directive.",
            _ => "Ready when you are."
        };
    }

    private static string BuildApologyReply(JarvisOptions options)
    {
        if (!options.AssistantSmallTalkEnabled)
        {
            return "No issue.";
        }

        var style = AssistantPersonaConfiguration.ResolveResponseStyle(options);

        return style switch
        {
            "concise" => "No issue.",
            "detailed" => "No issue. We can keep moving.",
            "cinematic" => "No issue. We stay on track.",
            _ => "No problem."
        };
    }

    private static string BuildComplimentReply(JarvisOptions options, string preferredUserAddress)
    {
        if (!options.AssistantSmallTalkEnabled)
        {
            return "Understood.";
        }

        var address = BuildOptionalAddress(options, preferredUserAddress);
        var style = AssistantPersonaConfiguration.ResolveResponseStyle(options);

        return style switch
        {
            "concise" => $"Appreciated{address}.",
            "detailed" => $"Appreciated{address}. Ready for the next step when you are.",
            "cinematic" => $"Appreciated{address}. Standing by for the next move.",
            _ => $"Appreciated{address}."
        };
    }

    private static string BuildClosingReply(JarvisOptions options)
    {
        var style = AssistantPersonaConfiguration.ResolveResponseStyle(options);

        return style switch
        {
            "concise" => "Understood. I'll stay on standby.",
            "detailed" => "Understood. I'll stay on standby until you need me again.",
            "cinematic" => "Understood. I'll hold on standby until the next directive.",
            _ => "Understood. I'll stay on standby."
        };
    }

    private static string BuildResponseLead(string userInput, JarvisOptions options, string preferredUserAddress)
    {
        var normalized = NormalizeComparable(userInput);
        var signal = AnalyzeSignal(normalized);
        var style = AssistantPersonaConfiguration.ResolveResponseStyle(options);

        return signal switch
        {
            PresenceSignal.UrgentAndFrustrated => BuildUrgentAndFrustratedLead(style),
            PresenceSignal.Urgent => BuildUrgentLead(style),
            PresenceSignal.Frustrated => BuildFrustratedLead(style),
            PresenceSignal.Gratitude when options.AssistantSmallTalkEnabled => BuildGratitudeReply(options, preferredUserAddress),
            _ => string.Empty
        };
    }

    private static string BuildUrgentAndFrustratedLead(string style)
    {
        return style == "concise"
            ? "Prioritizing the issue now."
            : "I see the issue. Prioritizing this now.";
    }

    private static string BuildUrgentLead(string style)
    {
        return style == "concise"
            ? "Prioritizing it now."
            : "Prioritizing this now.";
    }

    private static string BuildFrustratedLead(string style)
    {
        return style switch
        {
            "concise" => "Working the issue.",
            "cinematic" => "I see the issue. Moving on it now.",
            _ => "I see the issue. Working it now."
        };
    }

    private static bool ShouldLeaveUntouched(string responseText, AgentIntentKind intent, bool isFollowUpQuestion)
    {
        if (isFollowUpQuestion
            || responseText.Contains(Environment.NewLine, StringComparison.Ordinal)
            || responseText.Length > 260)
        {
            return true;
        }

        if (intent == AgentIntentKind.Background)
        {
            return true;
        }

        var blockedPrefixes = new[]
        {
            "approval required",
            "blocked for safety",
            "available directives:",
            "usage:",
            "intent:",
            "summary:",
            "planner request failed",
            "agent runtime is active",
            "bootstrap mode is active"
        };

        return blockedPrefixes.Any(prefix => responseText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            || responseText.StartsWith("- ", StringComparison.Ordinal)
            || responseText.Contains("`", StringComparison.Ordinal);
    }

    private static PresenceSignal AnalyzeSignal(string normalizedInput)
    {
        var urgent = ContainsAny(normalizedInput, UrgentTerms);
        var frustrated = ContainsAny(normalizedInput, FrustratedTerms);

        if (urgent && frustrated)
        {
            return PresenceSignal.UrgentAndFrustrated;
        }

        if (urgent)
        {
            return PresenceSignal.Urgent;
        }

        if (frustrated)
        {
            return PresenceSignal.Frustrated;
        }

        if (LooksLikeGratitude(normalizedInput))
        {
            return PresenceSignal.Gratitude;
        }

        return PresenceSignal.Neutral;
    }

    private static bool LooksLikeGreeting(string normalizedInput) =>
        IsExactMatch(normalizedInput, GreetingPhrases, 4);

    private static bool LooksLikeCheckIn(string normalizedInput) =>
        IsExactMatch(normalizedInput, CheckInPhrases, 6);

    private static bool LooksLikeIdentity(string normalizedInput) =>
        IsExactMatch(normalizedInput, IdentityPhrases, 5);

    private static bool LooksLikeReadiness(string normalizedInput) =>
        IsExactMatch(normalizedInput, ReadinessPhrases, 4);

    private static bool LooksLikeGratitude(string normalizedInput) =>
        IsExactMatch(normalizedInput, GratitudePhrases, 4);

    private static bool LooksLikeApology(string normalizedInput) =>
        IsExactMatch(normalizedInput, ApologyPhrases, 3);

    private static bool LooksLikeCompliment(string normalizedInput) =>
        IsExactMatch(normalizedInput, ComplimentPhrases, 3);

    private static bool LooksLikeClosing(string normalizedInput) =>
        IsExactMatch(normalizedInput, ClosingPhrases, 4);

    private static bool IsExactMatch(string normalizedInput, IReadOnlyList<string> phrases, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return false;
        }

        if (normalizedInput.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > maxWords)
        {
            return false;
        }

        return phrases.Any(phrase => string.Equals(normalizedInput, phrase, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string normalizedInput, IReadOnlyList<string> phrases) =>
        phrases.Any(phrase => normalizedInput.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    private static string ResolveGreeting(string normalizedInput, DateTimeOffset now)
    {
        if (normalizedInput.StartsWith("good morning", StringComparison.OrdinalIgnoreCase))
        {
            return "Good morning";
        }

        if (normalizedInput.StartsWith("good afternoon", StringComparison.OrdinalIgnoreCase))
        {
            return "Good afternoon";
        }

        if (normalizedInput.StartsWith("good evening", StringComparison.OrdinalIgnoreCase))
        {
            return "Good evening";
        }

        var hour = now.Hour;

        if (hour < 12)
        {
            return "Good morning";
        }

        if (hour < 18)
        {
            return "Good afternoon";
        }

        return "Good evening";
    }

    private static string BuildOptionalAddress(JarvisOptions options, string preferredUserAddress)
    {
        if (!options.AssistantSmallTalkEnabled || string.IsNullOrWhiteSpace(preferredUserAddress))
        {
            return string.Empty;
        }

        return $", {preferredUserAddress.Trim()}";
    }

    private static string NormalizeComparable(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var buffer = new char[input.Length];
        var index = 0;
        var previousWasSpace = true;

        foreach (var character in input.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[index++] = char.ToLowerInvariant(character);
                previousWasSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(character) && !previousWasSpace)
            {
                buffer[index++] = ' ';
                previousWasSpace = true;
            }
        }

        return new string(buffer, 0, index).Trim();
    }

    private enum PresenceSignal
    {
        Neutral,
        Gratitude,
        Urgent,
        Frustrated,
        UrgentAndFrustrated
    }
}
