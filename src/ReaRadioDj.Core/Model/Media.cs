namespace ReaRadioDj.Core.Model;

public enum MediaKind { Song, Jingle, Voice }

/// <summary>
/// The cue points of one audio file, in seconds from the start of the file. The classic broadcast model:
/// <list type="bullet">
/// <item>CueIn: first audio. FullLevel: end of the fade-in (optional).</item>
/// <item>Intro / Intro2: end of the instrumental intro, the talk-up time (optional).</item>
/// <item>Outro: start of the instrumental end (optional). FadeOut: where the fade-out starts (optional).</item>
/// <item>Mix: where the next item starts. CueOut: last audio.</item>
/// <item>HookIn / HookOut: a short recognisable fragment for teasers and promos (optional).</item>
/// </list>
/// </summary>
public sealed record CueSet
{
    public double CueIn { get; init; }
    public double? FullLevel { get; init; }
    public double? Intro { get; init; }
    public double? Intro2 { get; init; }
    public double? Outro { get; init; }
    public double? FadeOut { get; init; }
    public double Mix { get; init; }
    public double CueOut { get; init; }
    public double? HookIn { get; init; }
    public double? HookOut { get; init; }

    /// <summary>
    /// Returns a consistent copy: everything inside [0, duration], CueIn before CueOut, Mix between them and the
    /// optional points inside the playable range (dropped when they fall outside it).
    /// </summary>
    public CueSet Normalize(double duration)
    {
        duration = Math.Max(0, duration);
        double Clamp(double v) => Math.Clamp(double.IsFinite(v) ? v : 0, 0, duration);
        double cueOut = CueOut > 0 ? Clamp(CueOut) : duration;
        double cueIn = Math.Min(Clamp(CueIn), Math.Max(0, cueOut - 0.1));
        double mix = Mix > 0 ? Math.Clamp(Clamp(Mix), cueIn, cueOut) : cueOut;
        double? Inside(double? v) => v is double x && double.IsFinite(x) && x > cueIn && x < cueOut ? Math.Round(x, 3) : null;

        double? intro = Inside(Intro), intro2 = Inside(Intro2);
        if (intro is double a && intro2 is double b && b <= a) intro2 = null;
        double? hookIn = Inside(HookIn) ?? (HookIn is double hi && Math.Abs(hi - cueIn) < 0.0005 ? cueIn : null);
        double? hookOut = Inside(HookOut);
        if (hookIn is null || hookOut is null || hookOut <= hookIn) { hookIn = null; hookOut = null; }

        return new CueSet
        {
            CueIn = Math.Round(cueIn, 3),
            FullLevel = Inside(FullLevel),
            Intro = intro,
            Intro2 = intro2,
            Outro = Inside(Outro),
            FadeOut = Inside(FadeOut),
            Mix = Math.Round(mix, 3),
            CueOut = Math.Round(cueOut, 3),
            HookIn = hookIn,
            HookOut = hookOut,
        };
    }
}

/// <summary>An audio file plus its playout metadata.</summary>
public sealed record MediaItem
{
    /// <summary>Database id; 0 for audio that is not in the library (rendered voice breaks).</summary>
    public long Id { get; init; }
    public required string Path { get; init; }
    public required MediaKind Kind { get; init; }
    public long? CategoryId { get; init; }
    public string Category { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Title { get; init; } = "";
    public double Duration { get; init; }
    public CueSet Cues { get; init; } = new();
    /// <summary>Integrated loudness in LUFS (EBU R128), or null when not measured.</summary>
    public double? Lufs { get; init; }

    public double CueIn => Cues.CueIn;
    public double MixPoint => Cues.Mix;
    public double CueOut => Cues.CueOut;
    /// <summary>End of the intro (talk-up time), if known.</summary>
    public double? Intro => Cues.Intro;

    public string DisplayName => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
}

/// <summary>Pending → Rendering → (AwaitingApproval →) Ready, or Failed. Anything but Ready is skipped on air.</summary>
public enum BreakState { Pending, Rendering, Ready, Failed, AwaitingApproval }

public enum LogItemType { Song, Jingle, AiBreak }

/// <summary>One entry in the on-air log (the playlist that the engine plays from top to bottom).</summary>
public sealed class LogItem
{
    private static long _nextId;

    public long Id { get; } = Interlocked.Increment(ref _nextId);
    public required LogItemType Type { get; init; }

    /// <summary>The audio to play. For an AI break this is filled in once the break has been rendered.</summary>
    public MediaItem? Media { get; set; }

    /// <summary>Where the item came from: "klok: Standaard", "vast: Nieuws", "handmatig" ...</summary>
    public string Origin { get; set; } = "";
    /// <summary>Play only the hook (HookIn to HookOut) instead of the whole track.</summary>
    public bool UseHook { get; init; }

    // AI-break bookkeeping
    public BreakState BreakState { get; set; } = BreakState.Pending;
    /// <summary>Name of the persona chosen for the break's air time (null = general settings).</summary>
    public string? Persona { get; set; }
    /// <summary>What the break mentions besides the music: "weather", "news", "note" or null.</summary>
    public string? Extra { get; set; }
    public bool OutroAdded { get; set; }
    /// <summary>Speech check: how well the rendered voice matches the script (0..1), or null when not checked.</summary>
    public double? CheckScore { get; set; }
    public string? Script { get; set; }
    public string? ScriptSource { get; set; }
    public string? Voice { get; set; }
    public string? Error { get; set; }

    public string Label => Type switch
    {
        LogItemType.AiBreak when Media is null => $"AI-break ({BreakState})",
        LogItemType.AiBreak => "AI-break",
        _ => Media?.DisplayName ?? Type.ToString(),
    };

    /// <summary>Playing length used for planning: from CueIn to the mix point (or the hook length).</summary>
    public double PlannedLength => Media is not { } m ? 12
        : UseHook && m.Cues is { HookIn: double hi, HookOut: double ho } ? ho - hi
        : Math.Max(0.5, m.MixPoint - m.CueIn);
}
