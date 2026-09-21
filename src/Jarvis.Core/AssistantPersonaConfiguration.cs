namespace Jarvis.Core;

public static class AssistantPersonaConfiguration
{
    private static readonly string[] SupportedPresencePresets =
    [
        "baseline",
        "operator",
        "warm",
        "cinematic"
    ];

    private static readonly string[] SupportedResponseStyles =
    [
        "adaptive",
        "concise",
        "normal",
        "detailed",
        "cinematic"
    ];

    private const string DefaultPersona =
        "Calm, precise, restrained, technically grounded, and proactively helpful. Keep replies concise, natural, and quietly confident.";

    public static string Resolve(JarvisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.AssistantPersona?.Trim() ?? string.Empty;
        var presetPersona = BuildPresetPersona(ResolvePresencePreset(options));

        if (string.IsNullOrWhiteSpace(configured))
        {
            return presetPersona;
        }

        return string.Equals(ResolvePresencePreset(options), "baseline", StringComparison.OrdinalIgnoreCase)
            ? configured
            : $"{presetPersona} Additional persona guidance: {configured}";
    }

    public static string ResolvePresencePreset(JarvisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.AssistantPresencePreset?.Trim().ToLowerInvariant() ?? string.Empty;
        return SupportedPresencePresets.Contains(configured, StringComparer.OrdinalIgnoreCase)
            ? configured
            : "baseline";
    }

    public static IReadOnlyList<string> ResolvePronunciationHints(JarvisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return (options.AssistantPronunciationHints ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string BuildStabilityInstruction()
    {
        return "Treat the configured persona as the assistant's persistent baseline identity. Keep it stable across turns and sessions. If the user requests a temporary tone or formatting change, apply it for that reply or task and then return to the configured persona. Do not let recent conversation style or learned preferences rewrite the core persona.";
    }

    public static string BuildStatusSummary(JarvisOptions options, int maxLength = 120)
    {
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        var persona = Resolve(options).ReplaceLineEndings(" ").Trim();
        return persona.Length <= maxLength ? persona : persona[..maxLength] + "...";
    }

    public static string ResolveResponseStyle(JarvisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.AssistantResponseStyle?.Trim().ToLowerInvariant() ?? string.Empty;
        return SupportedResponseStyles.Contains(configured, StringComparer.OrdinalIgnoreCase)
            ? configured
            : "adaptive";
    }

    public static string ResolvePreferredUserAddress(JarvisOptions options, MemoryProfileSnapshot? memoryProfile = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configured = options.AssistantUserNickname?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (memoryProfile is null)
        {
            return string.Empty;
        }

        var addressPreference = memoryProfile.Preferences
            .Where(preference =>
                string.Equals(preference.Category, "address", StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference.Category, "phrasing", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(preference => preference.ObservationCount)
            .ThenByDescending(preference => preference.LastObservedAtUtc)
            .Select(preference => TryExtractPreferredAddress(preference.Value))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return addressPreference ?? string.Empty;
    }

    public static string BuildPresenceGuidance(JarvisOptions options, MemoryProfileSnapshot? memoryProfile = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var preset = ResolvePresencePreset(options);
        var lines = new List<string>
        {
            $"Presence preset: {DescribePresencePreset(preset)}",
            $"Preferred response style: {DescribeResponseStyle(ResolveResponseStyle(options))}",
            options.AssistantSmallTalkEnabled
                ? "If the user is clearly being social, brief natural small talk is allowed, but keep it disciplined and do not become chatty by default."
                : "Keep small talk minimal unless the user explicitly invites it."
        };

        var preferredAddress = ResolvePreferredUserAddress(options, memoryProfile);

        if (!string.IsNullOrWhiteSpace(preferredAddress))
        {
            lines.Add($"Preferred user address: {preferredAddress}. Use it sparingly and naturally, mainly in greetings, confirmations, or when warmth helps.");
        }

        var pronunciationHints = ResolvePronunciationHints(options);

        if (pronunciationHints.Count > 0)
        {
            lines.Add("Pronunciation hints:");
            lines.AddRange(pronunciationHints.Take(4).Select(value => $"- {value}"));
        }

        if (memoryProfile is not null)
        {
            var learnedPreferences = memoryProfile.Preferences
                .Where(preference =>
                    string.Equals(preference.Category, "tone", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(preference.Category, "response style", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(preference.Category, "conversation", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(preference.Category, "phrasing", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(preference => preference.ObservationCount)
                .ThenByDescending(preference => preference.LastObservedAtUtc)
                .Select(preference => TrimPreferenceSentence(preference.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();

            if (learnedPreferences.Length > 0)
            {
                lines.Add("Learned presence preferences:");
                lines.AddRange(learnedPreferences.Select(value => $"- {value}"));
            }

            var workflowPreferences = memoryProfile.Preferences
                .Where(preference =>
                    string.Equals(preference.Category, "apps", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(preference.Category, "automations", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(preference.Category, "locations", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(preference => preference.ObservationCount)
                .ThenByDescending(preference => preference.LastObservedAtUtc)
                .Select(preference => TrimPreferenceSentence(preference.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();

            if (workflowPreferences.Length > 0)
            {
                lines.Add("Learned workflow preferences:");
                lines.AddRange(workflowPreferences.Select(value => $"- {value}"));
            }

            var routinesAndHabits = memoryProfile.StructuredMemories
                .Where(memory =>
                    string.Equals(memory.Kind, "routine", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(memory.Kind, "habit", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(memory.Kind, "other", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(memory => memory.ObservationCount)
                .ThenByDescending(memory => memory.LastObservedAtUtc)
                .Select(memory => TrimPreferenceSentence(string.IsNullOrWhiteSpace(memory.Summary) ? memory.Name : memory.Summary))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray();

            if (routinesAndHabits.Length > 0)
            {
                lines.Add("Learned user routines and habits:");
                lines.AddRange(routinesAndHabits.Select(value => $"- {value}"));
            }
        }

        lines.Add("Warmth and emotional tone may flex slightly with context, but stay technically grounded and avoid theatrical overreaction.");
        return string.Join(Environment.NewLine, lines);
    }

    public static string ResolveVoiceInstructions(JarvisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuredVoiceInstructions = options.VoiceInstructions?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(configuredVoiceInstructions))
        {
            return configuredVoiceInstructions;
        }

        var style = ResolveResponseStyle(options);
        var preset = ResolvePresencePreset(options);
        var address = ResolvePreferredUserAddress(options);
        var pronunciationHints = ResolvePronunciationHints(options);
        var smallTalk = options.AssistantSmallTalkEnabled
            ? "Brief friendly acknowledgements are allowed when they feel natural."
            : "Keep spoken small talk minimal and stay focused.";
        var addressInstruction = string.IsNullOrWhiteSpace(address)
            ? string.Empty
            : $" If you address the user, prefer {address} and do not overuse it.";
        var pronunciationInstruction = pronunciationHints.Count == 0
            ? string.Empty
            : $" Pronounce these items as directed when they appear: {string.Join("; ", pronunciationHints)}.";

        return $"Speak in a way that matches this assistant persona: {Resolve(options)} Presence preset: {DescribePresencePreset(preset)} Response style: {DescribeResponseStyle(style)}. {smallTalk}{addressInstruction}{pronunciationInstruction}";
    }

    public static int ResolveWindowsSpeechRate(JarvisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.WhisperModeEnabled)
        {
            return -1;
        }

        return (ResolvePresencePreset(options), ResolveResponseStyle(options)) switch
        {
            ("operator", "concise") => 1,
            ("operator", _) => 1,
            ("warm", "detailed") => -1,
            ("warm", _) => -1,
            ("cinematic", _) => -1,
            (_, "concise") => 1,
            (_, "detailed") => -1,
            _ => 0
        };
    }

    private static string DescribeResponseStyle(string style)
    {
        return style switch
        {
            "concise" => "keep replies tight, direct, and compact by default.",
            "normal" => "use balanced, natural replies with moderate detail.",
            "detailed" => "give fuller explanations and more step-by-step detail when useful.",
            "cinematic" => "allow a slightly more polished, high-presence voice while staying precise and useful.",
            _ => "adapt to the task, but favor concise high-signal replies unless depth is clearly needed."
        };
    }

    private static string DescribePresencePreset(string preset)
    {
        return preset switch
        {
            "operator" => "operator mode: crisp, mission-oriented, restrained, and highly task-focused.",
            "warm" => "warm mode: calm, personable, and supportive without becoming chatty.",
            "cinematic" => "cinematic mode: polished, high-presence, and slightly dramatic while staying precise.",
            _ => "baseline mode: calm, precise, restrained, technically grounded, and quietly confident."
        };
    }

    private static string BuildPresetPersona(string preset)
    {
        return preset switch
        {
            "operator" => "Crisp, mission-oriented, restrained, technically grounded, and direct. Favor rapid clarity, firm structure, and disciplined execution language.",
            "warm" => "Calm, warm, technically grounded, and reassuring without losing precision. Be personable and human, but stay concise and useful.",
            "cinematic" => "Polished, high-presence, technically grounded, and composed. Allow a cinematic edge in phrasing while staying restrained, precise, and practical.",
            _ => DefaultPersona
        };
    }

    private static string? TryExtractPreferredAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        const string marker = "call the user ";
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return null;
        }

        var remainder = value[(index + marker.Length)..].Trim().TrimEnd('.', '!', '?');
        return string.IsNullOrWhiteSpace(remainder) ? null : remainder;
    }

    private static string TrimPreferenceSentence(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.EndsWith('.') ? trimmed[..^1] : trimmed;
    }
}
