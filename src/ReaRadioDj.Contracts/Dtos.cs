using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReaRadioDj.Contracts;

// The JSON contract of the engine service. Property names are camelCase on the wire; enums travel as camelCase
// strings ("song", "hard", "playing"). Times are local times.

public static class Wire
{
    public static readonly JsonSerializerOptions Json = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }
}

// ---------------------------------------------------------------- live state (WebSocket /api/ws, SSE /api/events)

public sealed record StateDto(
    string Station,
    DateTime Time,
    string? Clock,
    EngineDto Engine,
    IReadOnlyList<LogItemDto> Upcoming,
    IReadOnlyList<LogItemDto> History,
    IReadOnlyList<PlayerDto> Players,
    PlayerDto Pfl,
    IReadOnlyList<ClipDto> Carts,
    IReadOnlyList<ClipDto> Overlays,
    MixerDto Mixer,
    IReadOnlyList<UpcomingEventDto> Events,
    LibrarySummaryDto Library,
    StreamDto Stream,
    long LastMessageId);

public sealed record EngineDto(bool Running, bool AutoPlay, NowPlayingDto? Current, IReadOnlyList<NowPlayingDto> Playing,
    float LevelLeft, float LevelRight, double SecondsToNext);

public sealed record NowPlayingDto(long Id, string Type, string Label, string Artist, string Title, string Category, string Origin,
    double Position, double From, double To, double MixAt, double? Intro, double? FadeAt, string? Script, long MediaId);

public sealed record LogItemDto(long Id, string Type, string Label, string? Artist, string? Title, string? Category, string Origin,
    double Length, DateTime StartsAt, string? BreakState, string? Script, string? ScriptSource, string? Voice, string? Error,
    long? MediaId, bool UseHook);

public sealed record PlayerDto(string Id, string Name, string State, long? MediaId, string? Label, double Position, double CueIn,
    double CueOut, double? Intro, IReadOnlyList<string> Stack);

public sealed record ClipDto(string Key, string Label, long? MediaId, double Position, double Length, bool Loop);

public sealed record StripDto(string Id, string Name, string Bus, float Volume, bool Muted, float Duck, float LevelLeft, float LevelRight);

public sealed record MicDto(bool Available, bool Open, string Status);

public sealed record MixerDto(IReadOnlyList<StripDto> Strips, MicDto Mic, float ProgramLeft, float ProgramRight);

public sealed record UpcomingEventDto(long Id, string Name, DateTime At, string Timing, bool Prepared);

public sealed record LibrarySummaryDto(int Songs, int Jingles, string Status, bool Scanning);

public sealed record StreamDto(bool Enabled, bool Connected, string Server, long BytesSent, string? Error);

// ---------------------------------------------------------------- library

public sealed record CueDto(double CueIn, double? FullLevel, double? Intro, double? Intro2, double? Outro, double? FadeOut,
    double Mix, double CueOut, double? HookIn, double? HookOut);

public sealed record MediaDto(long Id, string Path, string Kind, long? CategoryId, string Category, string Artist, string Title,
    string Album, double Duration, double? Lufs, CueDto Cues, bool Enabled, bool CuesManual, DateTime? ValidFrom,
    DateTime? ValidUntil, int PlayCount, DateTime? LastPlayed, bool Missing)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
}

public sealed record CategoryDto(long Id, string Name, string Kind, string Color);

/// <summary>Waveform: one linear peak (0..65535) per block, little-endian UInt16, base64.</summary>
public sealed record PeaksDto(int BlockMs, string Data);

public sealed record MediaUpdateRequest(long? CategoryId, bool Enabled, DateTime? ValidFrom, DateTime? ValidUntil);

public sealed record CategoryRequest(string Name, string Kind, string? Color);

// ---------------------------------------------------------------- log, players, carts, mixer

/// <summary>Position: null = end of the log, 0 = next.</summary>
public sealed record AddToLogRequest(long MediaId, int? Position);

public sealed record AddBreakRequest(int? Position);

public sealed record MoveRequest(int? Delta, int? Index);

public sealed record MediaRequest(long MediaId);

public sealed record SeekRequest(double Seconds);

public sealed record SwitchRequest(bool On);

public sealed record VolumeRequest(float Volume, bool? Muted);

public sealed record CartDto(int Wall, int Slot, int Page, long? MediaId, string Label, string Color, bool Loop, bool Overlay,
    bool Playing, double Position, double Length, string? MediaLabel);

public sealed record CartRequest(long? MediaId, string? Label, string? Color, bool Loop, bool Overlay);

public sealed record DeviceDto(int Index, string Name, bool Enabled, bool IsDefault);

public sealed record RoutingDto(int OutputDevice, int PreviewDevice, bool MicEnabled, int MicDevice, double MicDuckLevel,
    double OverlayDuckLevel);

// ---------------------------------------------------------------- planner

/// <summary>Kind: category, media or aiBreak. Mode: random, sequential or hook.</summary>
public sealed record ClockRuleDto(long Id, string Kind, long? CategoryId, long? MediaId, string Mode, string Note);

public sealed record ClockDto(long Id, string Name, string Color, IReadOnlyList<ClockRuleDto> Rules);

/// <summary>Day 0 = Monday.</summary>
public sealed record GridCellDto(int Day, int Hour, long ClockId);

public sealed record GridUpdateRequest(IReadOnlyList<GridCellDto> Cells, long? ClockId);

/// <summary>Days: bit 0 = Monday ... bit 6 = Sunday. Hours: "*" or "7-9,12". Timing: hard, soft, overlay.
/// Action: playMedia, playCategory, aiBreak, command, reminder, alert. Text: the command(s) (one per line or split by
/// ';'), or the text of the reminder/alert.</summary>
public sealed record TimedEventDto(long Id, string Name, bool Enabled, int Days, string Hours, int Minute, int Second,
    string Timing, string Action, long? CategoryId, long? MediaId, string? Description = null, DateTime? Next = null,
    string? Text = null);

/// <summary>Kind: maxInRow, notAfter. Action: suppress, defer. Empty category = any.</summary>
public sealed record ScriptRuleDto(long Id, string Name, bool Enabled, string Kind, long? FromCategoryId, long? ToCategoryId,
    int Count, string Action, string? Description = null);

/// <summary>Mode: takeover, overlay. Live: the stream is on air right now.</summary>
public sealed record StreamWatchDto(long Id, string Name, bool Enabled, string Url, int Days, string Hours, string Mode,
    int CheckSeconds, string? Description = null, string? State = null);

public sealed record CommandInfoDto(string Syntax, string Description, string Group);

public sealed record CommandResultDto(bool Ok, string Text);

// ---------------------------------------------------------------- reporting

public sealed record AsRunDto(long Id, DateTime Started, DateTime? Ended, string Source, string Type, long? MediaId, string? Path,
    string? Artist, string? Title, string? Category, double? Planned, double? Played, bool Ai, string? Note);

public sealed record MessageDto(long Id, DateTime Time, string Level, string Text);

// ---------------------------------------------------------------- presenter voice

/// <summary>Engine: higgs or sapi. Problem: why the profile cannot be used (e.g. a clone without consent).</summary>
public sealed record VoiceProfileDto(long Id, string? Name, string? Engine, string? ReferenceAudio, string? ReferenceText, string? Tags,
    double Temperature, string? SapiVoice, bool Consent, string? ConsentNote, bool IsDefault, string? Problem = null);

public sealed record GpuDto(string Name, double MemoryTotalMb, double MemoryUsedMb, double UtilizationPercent, double TemperatureC, string Driver);

public sealed record VoiceStatusDto(GpuDto? Gpu, bool ServerOnline, IReadOnlyList<string> Models, double LatencyMs, string? ServerError,
    string BaseUrl, string Model, bool ServerManaged, bool ServerRunning, string? DefaultVoice, string Engine, bool FallbackToSapi,
    IReadOnlyList<string> SapiVoices);

/// <summary>ProfileId null = the default voice. Tags: try other Higgs tags without saving them. Play: listen on PFL.</summary>
public sealed record VoiceTestRequest(string? Text, long? ProfileId, string? Tags, bool Play);

public sealed record VoiceTestResultDto(bool Ok, string Engine, double RenderSeconds, double AudioSeconds, string? File, string? Error);

// ---------------------------------------------------------------- wish list (Spotify metadata) and music report

public sealed record WishItemDto(long Id, string SourceId, string Playlist, string Title, string Artist, string Album, string Isrc,
    string Release, double Duration, string CoverUrl, string Link, long? MediaId)
{
    public bool InLibrary => MediaId is not null;
}

public sealed record WishStatusDto(bool Configured, IReadOnlyList<string> Playlists, int Total, int InLibrary);

public sealed record WishImportRequest(string? Link);

public sealed record WishImportResultDto(bool Ok, string Playlist, int Total, int Added, int InLibrary, string? Error);

public sealed record MusicReportSummaryDto(int Tracks, double Seconds, int WithoutIsrc, int WithoutComposer);
