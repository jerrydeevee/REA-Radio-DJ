namespace ReaRadioDj.Core.Model;

public enum MediaKind { Song, Jingle, Voice }

/// <summary>
/// An audio file plus its playout metadata. Cue points are in seconds from the start of the file,
/// following the classic broadcast model: CueIn (first audio), Ramp (end of the instrumental intro, optional),
/// MixPoint (where the next item starts and this one fades out) and CueOut (last audio).
/// </summary>
public sealed record MediaItem
{
    public required string Path { get; init; }
    public required MediaKind Kind { get; init; }
    public string Artist { get; init; } = "";
    public string Title { get; init; } = "";
    public double Duration { get; init; }
    public double CueIn { get; init; }
    public double MixPoint { get; init; }
    public double CueOut { get; init; }
    /// <summary>End of the intro (talk-up time), if known. Read from a "&lt;file&gt;.cue.json" sidecar.</summary>
    public double? Ramp { get; init; }
    /// <summary>Integrated loudness in LUFS (EBU R128), or null when not measured.</summary>
    public double? Lufs { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
}

public enum BreakState { Pending, Rendering, Ready, Failed }

/// <summary>One entry in the on-air log (the playlist that the engine plays from top to bottom).</summary>
public sealed class LogItem
{
    private static long _nextId;

    public long Id { get; } = Interlocked.Increment(ref _nextId);
    public required LogItemType Type { get; init; }

    /// <summary>The audio to play. For an AI break this is filled in once the break has been rendered.</summary>
    public MediaItem? Media { get; set; }

    // AI-break bookkeeping
    public BreakState BreakState { get; set; } = BreakState.Pending;
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
}

public enum LogItemType { Song, Jingle, AiBreak }
