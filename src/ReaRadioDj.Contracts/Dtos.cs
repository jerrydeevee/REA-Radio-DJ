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
    long LastMessageId,
    DjDto? Dj = null);

// ---------------------------------------------------------------- DJ desk

/// <summary>One DJ deck. Tempo in percent, equaliser and filter -1..1, fader 0..1, positions in seconds.</summary>
public sealed record DjDeckDto(string Id, long? MediaId, string Title, string Artist, bool Loaded, bool Playing, double Position,
    double Duration, double Bpm, double FirstBeat, bool Analysing, double Tempo, double TempoRange, bool KeyLock, bool Synced,
    double CuePoint, IReadOnlyList<double?> HotCues, double? LoopIn, double? LoopOut, bool LoopActive, double LoopBeats, double Gain,
    double EqLow, double EqMid, double EqHigh, bool KillLow, bool KillMid, bool KillHigh, double Filter, double Fader, bool Pfl,
    float LevelLeft, float LevelRight);

/// <summary>Crossfader 0 (decks A and C) .. 1 (B and D); curve "smooth" or "sharp"; OnAir = the desk is heard on the radio.</summary>
/// Following = deck A copies what the automation plays, ready to take over.
public sealed record DjDto(double Crossfader, string Curve, bool OnAir, IReadOnlyList<DjDeckDto> Decks, bool Following = false);

public sealed record DjCommand(long? MediaId = null, double? Value = null, int? Index = null, bool? On = null);

public sealed record DjMixerRequest(double? Crossfader = null, string? Curve = null, bool? OnAir = null);

public sealed record EngineDto(bool Running, bool AutoPlay, NowPlayingDto? Current, IReadOnlyList<NowPlayingDto> Playing,
    float LevelLeft, float LevelRight, double SecondsToNext);

public sealed record NowPlayingDto(long Id, string Type, string Label, string Artist, string Title, string Category, string Origin,
    double Position, double From, double To, double MixAt, double? Intro, double? FadeAt, string? Script, long MediaId);

public sealed record LogItemDto(long Id, string Type, string Label, string? Artist, string? Title, string? Category, string Origin,
    double Length, DateTime StartsAt, string? BreakState, string? Script, string? ScriptSource, string? Voice, string? Error,
    long? MediaId, bool UseHook, string? Extra = null, double? CheckScore = null, bool HandMadeMix = false);

public sealed record PlayerDto(string Id, string Name, string State, long? MediaId, string? Label, double Position, double CueIn,
    double CueOut, double? Intro, IReadOnlyList<string> Stack);

public sealed record ClipDto(string Key, string Label, long? MediaId, double Position, double Length, bool Loop);

public sealed record StripDto(string Id, string Name, string Bus, float Volume, bool Muted, float Duck, float LevelLeft, float LevelRight,
    bool OnAir = true, string? Source = null, string Effect = "", bool EffectOn = false, bool Recording = false, double RecordSeconds = 0);

public sealed record MicDto(bool Available, bool Open, string Status);

public sealed record MixerDto(IReadOnlyList<StripDto> Strips, MicDto Mic, float ProgramLeft, float ProgramRight,
    double? Lufs = null, double? LufsIntegrated = null, double? TruePeakDb = null, bool Limiter = false);

public sealed record UpcomingEventDto(long Id, string Name, DateTime At, string Timing, bool Prepared);

public sealed record LibrarySummaryDto(int Songs, int Jingles, string Status, bool Scanning);

public sealed record StreamDto(bool Enabled, bool Connected, string Server, long BytesSent, string? Error);

// ---------------------------------------------------------------- library

public sealed record CueDto(double CueIn, double? FullLevel, double? Intro, double? Intro2, double? Outro, double? FadeOut,
    double Mix, double CueOut, double? HookIn, double? HookOut);

public sealed record MediaDto(long Id, string Path, string Kind, long? CategoryId, string Category, string Artist, string Title,
    string Album, double Duration, double? Lufs, CueDto Cues, bool Enabled, bool CuesManual, DateTime? ValidFrom,
    DateTime? ValidUntil, int PlayCount, DateTime? LastPlayed, bool Missing, int? Year = null, DateTime? Added = null,
    string? Block = null, string Genre = "", double? Bpm = null, double? FirstBeat = null, double? EstIntro = null, double? EstOutro = null,
    IReadOnlyList<string>? Issues = null, string Language = "", string Mood = "", int? Rating = null, int? EnergyLevel = null, int? Bitrate = null,
    string? Key = null, string? Camelot = null, double? ReplayGain = null)
{
    /// <summary>True when the quality check found something (te zacht, clipping, stilte, dubbel, ...).</summary>
    public bool HasIssues => Issues is { Count: > 0 };
    public string IssueText => Issues is { Count: > 0 } ? string.Join(" · ", Issues) : "";
    /// <summary>True when the item may be scheduled now (<see cref="Block"/> says why not).</summary>
    public bool Allowed => Block is null;
    public string DisplayName => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
}

/// <summary>One line of the search help: an example and what it does.</summary>
public sealed record SearchHelpDto(string Example, string Meaning);

/// <summary>A folder of the library (Fixed = music or jingle folder from the settings). Files/Reachable/Error: last scan.</summary>
public sealed record LibraryFolderDto(long Id, string Path, string Kind, string Category, bool Watch, bool WriteTags, bool Enabled,
    bool Fixed, int? Files, bool? Reachable, string Error, DateTime? Scanned);

/// <summary>A selection of library items.</summary>
public sealed record MediaIdsRequest(IReadOnlyList<long> Ids);

public sealed record MediaBulkRequest(IReadOnlyList<long> Ids, long? CategoryId = null, bool? Enabled = null, bool ClearDates = false,
    DateTime? ValidFrom = null, DateTime? ValidUntil = null);

public sealed record MediaTagsRequest(string Artist, string Title, string Album = "", string Genre = "", int? Year = null,
    string? Language = null, string? Mood = null, int? Rating = null, int? EnergyLevel = null);

/// <summary>A saved search ("slimme lijst"); Count = how many items it shows now.</summary>
public sealed record SmartListDto(long Id, string Name, string Query, long? CategoryId, string Sort, int Count);

public sealed record SmartListRequest(string Name, string Query, long? CategoryId = null, string Sort = "");

/// <summary>The last library change that can be undone.</summary>
public sealed record UndoDto(string Label, DateTime Time, int Items);

/// <summary>One time an item went on air.</summary>
public sealed record PlayedDto(DateTime Started, double? Played, string Source);

public sealed record LibraryFolderRequest(string Path, string Kind = "song", string Category = "", bool Watch = true, bool WriteTags = false,
    bool Enabled = true);

/// <summary>A replacement location: a file missing under From is looked up under To. Reachable: the To folder is there now.</summary>
public sealed record PathSubstituteDto(string From, string To, bool Enabled = true, bool Reachable = false);

public sealed record RelocateRequest(string From, string To);

public sealed record RelocateResultDto(int Changed, int Found);

/// <summary>The station's own recordings: settings plus what is being recorded now.</summary>
public sealed record RecordingSettingsDto(bool Program, bool Mic, bool MicToLibrary, int Bitrate, int KeepDays, string Folder,
    string FolderInUse = "", string? ProgramFile = null, string? MicFile = null, string Error = "");

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

/// <summary>
/// The station settings of the engine. Passwords are never sent to the studio: the "...Set" flags say whether one is
/// stored; when saving, an empty password keeps the stored one.
/// </summary>
public sealed record SettingsDto(
    string StationName, string MusicFolder, string JingleFolder, bool StartAutomation, int StandbyPlayers,
    double TargetLufs, double CueInDb, double MixDb, double CueOutDb, double MaxOverlap, double FadeOut, double DuckLevel,
    int ArtistSeparation, int SongSeparation, int LookAhead,
    bool StreamEnabled, string StreamServer, string StreamPassword, bool StreamPasswordSet, int StreamBitrate, string StreamGenre, string StreamUrl,
    string ControlBindAddress, int ControlTcpPort, int ControlOscPort, string ControlPassword, bool ControlPasswordSet, bool ClearControlPassword,
    bool SilenceEnabled = true, double SilenceDb = -45, double SilenceSeconds = 20, string SilenceAction = "autoplay",
    long? SilenceCategoryId = null);

/// <summary>The answer to saving the settings: what was stored, and what needs a restart or a rescan.</summary>
public sealed record SettingsSavedDto(SettingsDto Settings, IReadOnlyList<string> NeedsRestart, bool Rescanning);

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
    double Temperature, string? SapiVoice, bool Consent, string? ConsentNote, bool IsDefault, string? Problem = null, long? FallbackId = null);

/// <summary>Online voice keys (masked when read) and the speech check settings.</summary>
public sealed record VoiceServicesDto(string ElevenLabsKey, string ElevenLabsModel, string AzureKey, string AzureRegion, bool CheckEnabled,
    string CheckUrl, string CheckModel, double CheckMinScore, int CheckRetries, bool CheckReject);

public sealed record CheckTestRequest(string Text);

/// <summary>Voicetracking: Step is "idle", "outro" (previous item on PFL, recording) or "intro" (next item on PFL).</summary>
public sealed record VoiceTrackDto(string Step, double Elapsed, double Outro, long? SlotBefore, VoiceTakeDto? Take, string? Error);

public sealed record VoiceTakeDto(string Id, double Length, double StartEarly, double? NextAt, long BeforeItemId, string Previous, string Next,
    double PeakDb);

public sealed record VoiceTrackStartRequest(long BeforeId, double Preroll, int? Device);

/// <summary>
/// A news block. Timing "hard" or "soft"; Source "folder" (newest file in Folder, at most MaxAgeMinutes old),
/// "category" or "headlines" (Feeds, one URL per line, read out by the AI voice). When and Next are filled in by the engine.
/// </summary>
public sealed record NewsBlockDto(long Id, string Name, bool Enabled, int Days, string Hours, int Minute, int Second, string Timing,
    long? OpenerId, long? CloserId, string Source, string Folder, int MaxAgeMinutes, long? CategoryId, string Feeds, int MaxHeadlines,
    string? When = null, DateTime? Next = null);

/// <summary>A hardware device. Kind "midi", "serial", "livewire" or "ember"; Port is the MIDI name, COM port or host[:port].</summary>
public sealed record HwDeviceDto(long Id, string Name, bool Enabled, string Kind, string Port, int Baud, string Password,
    bool Connected = false, string Status = "");

/// <summary>A trigger ("note 60", "cc 7", "gpi 3.1", "ember 1.2.3 > -40" or a serial line) and the commands for on and off.</summary>
public sealed record HwInputDto(long Id, long DeviceId, string Trigger, string OnCommand, string OffCommand);

/// <summary>A tally: State "mic", "onair" or "auto"; Target "note 60", "cc 20", "gpo 4.2" or "ember 1.2.3"; texts (serial) or values (Ember+) for on and off.</summary>
public sealed record HwOutputDto(long Id, long DeviceId, string State, string Target, string OnText, string OffText);

/// <summary>Something a device reported, and what it started.</summary>
public sealed record HwEventDto(DateTime Time, long DeviceId, string Device, string Key, bool On, string Result);

/// <summary>A node or parameter of an Ember+ device, for browsing its tree.</summary>
public sealed record HwEmberItemDto(string Path, string Identifier, string Description, bool IsNode, string Value, string Type, bool Writable);

public sealed record HwPortsDto(List<string> Midi, List<string> Serial);

/// <summary>Everything the hardware window shows.</summary>
public sealed record HwOverviewDto(List<HwDeviceDto> Devices, List<HwInputDto> Inputs, List<HwOutputDto> Outputs, Dictionary<string, bool> States);

/// <summary>A music catalogue in another database (ODBC). Kind "song" or "jingle"; SyncMinutes 0 = only on request.</summary>
public sealed record DbSourceDto(long Id, string Name, bool Enabled, string Connection, string Query, string Kind, int SyncMinutes,
    string PathFrom, string PathTo, DateTime? LastSync = null, string LastResult = "", bool Busy = false);

/// <summary>What a test query returned: the columns, which of them are used, and the first rows.</summary>
public sealed record DbTestDto(bool Ok, List<string> Columns, List<string> Used, List<string> Sample, string? Problem);

/// <summary>What a metadata lookup found for a library item; only fields that are empty now are filled in.</summary>
public sealed record MetaProposalDto(long MediaId, string Song, string Source, string Found, string? Album, int? Year, string? Isrc,
    string? Label, string Link);

/// <summary>The state of the metadata lookup.</summary>
public sealed record MetaStatusDto(bool Running, int Done, int Total, string Current, int NotFound, int NothingNew, bool Spotify,
    List<MetaProposalDto> Proposals);

public sealed record MetaStartDto(int Limit);
public sealed record IdListDto(List<long> Ids);

/// <summary>Where a field is: Column for separated files (1 = first), Start and Length for fixed width. Zeros = not used.</summary>
public sealed record ImportColumnDto(int Column = 0, int Start = 0, int Length = 0);

/// <summary>How the logs of an external music scheduler look. Layout "delimited" or "fixed"; Separator ";", ",", "\t" or "|".</summary>
public sealed record ImportProfileDto(long Id, string Name, string Layout, string Separator, bool HasHeader, string SkipPrefix,
    ImportColumnDto Time, string TimeFormat, ImportColumnDto File, ImportColumnDto MediaId, ImportColumnDto Artist, ImportColumnDto Title,
    string PathFrom, string PathTo);

/// <summary>A log file to read with a profile. Date "yyyy-MM-dd"; StartHour is used when the profile has no time field.</summary>
public sealed record ImportRequestDto(long ProfileId, string Text, string Date, int StartHour, string? FileName);

/// <summary>One line of an imported log: the hour it goes in, what it was matched to, or the problem.</summary>
public sealed record ImportLineDto(int Number, string? Hour, string Text, string? Match, string? Problem);

/// <summary>The outcome of reading (and, when Applied, storing) a log file.</summary>
public sealed record ImportResultDto(bool Applied, int Lines, int Matched, List<ImportLineDto> Items, List<string> Hours, string? Error);

/// <summary>An hour that plays an imported log instead of its clock.</summary>
public sealed record ImportedHourDto(string Date, int Hour, int Items, string Source);

/// <summary>A spot of a campaign (a library item) with its playing length.</summary>
public sealed record AdSpotDto(long MediaId, string Name, double Length);

/// <summary>An ad campaign. Dates "yyyy-MM-dd"; Position "any", "first" or "last". Today and Status are filled in by the engine.</summary>
public sealed record AdCampaignDto(long Id, string Advertiser, string Name, bool Enabled, string Start, string End, int Days, string Hours,
    int PerDay, int PerHour, string Position, List<AdSpotDto> Spots, string Notes, int Today = 0, string? Status = null);

/// <summary>An ad break on a fixed time. Timing "hard" or "soft".</summary>
public sealed record AdBreakDto(long Id, string Name, bool Enabled, int Days, string Hours, int Minute, int Second, string Timing,
    long? OpenerId, long? CloserId, int MaxSeconds, string? When = null, DateTime? Next = null);

/// <summary>One spot in the proof of broadcast.</summary>
public sealed record AdPlayDto(long Id, string Advertiser, string Campaign, string Spot, string Break, DateTime Scheduled, DateTime? Started,
    double Length, double? Played, bool Complete);

/// <summary>What an ad break would hold now.</summary>
public sealed record AdPreviewDto(List<string> Lines, double Seconds, string? Problem);

/// <summary>What a news block would play now: the parts in order, or the problem that would make it skip.</summary>
public sealed record NewsCheckDto(bool Ok, List<string> Parts, string? Problem, string? Script);

/// <summary>Transition editor: a point on a volume line (seconds in the item's own time, linear gain 0..1.5).</summary>
public sealed record EnvelopePointDto(double Seconds, double Gain);

/// <summary>How an item comes in: starts at <c>StartAt</c> in the outgoing item, from <c>From</c> in itself.</summary>
public sealed record TransitionSettingsDto(double StartAt, double From, List<EnvelopePointDto> Out, List<EnvelopePointDto> In,
    long? InsertMediaId, double InsertAt);

/// <summary>One side of a transition with its waveform (linear peaks, UInt16 little-endian, base64).</summary>
public sealed record TransitionSideDto(long LogId, string Label, long MediaId, double Duration, double CueIn, double CueOut,
    double Mix, string Peaks);

public sealed record TransitionDto(long ItemId, TransitionSideDto Out, TransitionSideDto In, TransitionSettingsDto Settings,
    TransitionSettingsDto Automatic, bool HandMade, int BlockMs, string? InsertLabel, double InsertLength);

public sealed record CheckTestDto(bool Ok, double Score, string Heard, string Voice, string? Error);

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

/// <summary>AI presenter settings changed in the studio. ApiKey comes back masked; sending the mask keeps the stored key.</summary>
public sealed record PresenterSettingsDto(bool Enabled, string PresenterName, string Persona, int MaxWords, string LlmBaseUrl,
    string LlmModel, string LlmApiKey, double Temperature, bool RequireApproval, bool PostProcess, long? BedCategoryId = null);

/// <summary>A presenter for part of the day or week. Empty fields use the general presenter settings.</summary>
public sealed record PersonaDto(long Id, string Name, bool Enabled, int Days, string Hours, string PresenterName, string Character,
    long? VoiceId, int? MaxWords, long? BedCategoryId, int Sort, string? Describe = null, bool OnAirNow = false);

public sealed record LlmModelsDto(bool Ok, IReadOnlyList<string> Models, string? Error);

public sealed record ScriptTryDto(string Script, string Source, double Seconds, int Words, string? Persona = null);

public sealed record ScriptTryRequest(long? PersonaId);

public sealed record ApproveBreakRequest(string? Script);

/// <summary>What the presenter may mention besides the music, and the jingle after a break.</summary>
public sealed record PresenterExtrasDto(long? OutroCategoryId, bool WeatherEnabled, string Place, double Latitude, double Longitude,
    int WeatherEveryMinutes, bool NewsEnabled, string NewsFeeds, int NewsEveryMinutes);

public sealed record ExtrasPreviewDto(string? Weather, string? Headline, string? Error);

/// <summary>
/// The creative presenter. Tone = "random" or serieus/grappig/leuk/randje; the four weights (0..10) decide how often each
/// tone comes up at random. Themes and Feeds: one per line.
/// </summary>
public sealed record CreativeSettingsDto(bool Enabled, int Chance, string Tone, int Serious, int Funny, int Light, int Edgy,
    bool UseOwnThemes, string Themes, bool UseBuiltIn, bool UseFeeds, string Feeds, int MaxWords, long? VoiceId, bool ToneVoice,
    int RepeatHours, bool LlmReady = false);

/// <summary>A creative trial: tone and theme are picked when empty; Speak renders it with the voice, Play also plays it on PFL.</summary>
public sealed record CreativeTryRequest(string? Tone, string? Theme, bool Speak, bool Play);

public sealed record CreativeTryDto(string Theme, string Tone, string ThemeSource, string Script, string Source, int Words,
    double Seconds, string? Voice, double? Length, string? File, string? Error);

/// <summary>The themes the presenter can choose from now, with where each comes from (eigen, ingebouwd or the feed's host).</summary>
public sealed record CreativeThemeDto(string Theme, string Source, string? Tone);

public sealed record PresenterNoteDto(long Id, string Text, DateTime? Until, int MaxUses, int Used);

public sealed record AddNoteRequest(string Text, DateTime? Until, int MaxUses);

/// <summary>Keeping the station running: watchdog, start at logon, system log and backups.</summary>
public sealed record SystemDto(bool Watched, bool AutoStart, string? AutoStartCommand, bool DetailedLog, bool RestorePending,
    string DataFolder, string WatchdogLogPath, IReadOnlyList<string> WatchdogLog);

public sealed record SystemSettingsRequest(bool AutoStart, bool DetailedLog);

public sealed record SystemLogEntryDto(long Id, DateTime Time, string Level, string Source, string Text);

public sealed record BackupStagedDto(string Station, DateTime Created, string Machine, bool Restarting);

/// <summary>An output a mixer channel can go to ("" = its normal bus).</summary>
public sealed record OutputChoiceDto(string Id, string Name);

public sealed record OutputRouteDto(string Strip, string Name, string Target);

public sealed record OutputsDto(IReadOnlyList<OutputChoiceDto> Targets, IReadOnlyList<OutputRouteDto> Strips);

public sealed record OutputRouteRequest(string? Target);

/// <summary>Sound engine settings that apply after a restart: ASIO for the buses, sample rate and buffers.</summary>
public sealed record AudioSettingsDto(string ProgramAsio, string PreviewAsio, int SampleRate, int BufferMs, int AsioBuffer,
    IReadOnlyList<OutputChoiceDto> AsioChoices, bool AsioAvailable, string ProgramNow, string PreviewNow, bool NeedsRestart);

public sealed record AudioSettingsRequest(string ProgramAsio, string PreviewAsio, int SampleRate, int BufferMs, int AsioBuffer);

public sealed record PresetsAddedDto(int Added);

/// <summary>Limiter on the on-air bus.</summary>
public sealed record MasterDto(bool LimiterOn, double CeilingDb);

/// <summary>Channel strip of one mixer channel.</summary>
public sealed record ChannelSettingsDto(double GainDb, double Pan, double EqLowDb, double EqMidDb, double EqHighDb,
    bool CompressorOn, double CompressorThresholdDb, double CompressorRatio, bool AgcOn, bool OnAir,
    string Effect = "", bool EffectOn = false, double EffectMix = 0.35);

/// <summary>A channel recording: whether it runs, its file and how long it is so far.</summary>
public sealed record RecordingDto(string Channel, bool Recording, string? File, double Seconds);

/// <summary>A recorded file in the recordings folder.</summary>
public sealed record RecordingFileDto(string Name, long Bytes, double Seconds, DateTime Modified);

public sealed record ChannelDto(string Id, string Name, string? Source, ChannelSettingsDto Settings, IReadOnlyList<VstSlotDto>? Vst = null);

/// <summary>A VST 2 plugin file found in the plugin folders; Error is set when it cannot be used.</summary>
public sealed record VstPluginDto(string Path, string Name, string Vendor, int Params, string? Error);

/// <summary>The plugins found, and the folders that were searched.</summary>
public sealed record VstPluginsDto(IReadOnlyList<VstPluginDto> Plugins, IReadOnlyList<string> Folders, DateTime? Scanned);

/// <summary>One VST slot of a channel: the plugin (empty path = empty slot), on or bypassed, and its parameters.</summary>
public sealed record VstSlotDto(int Slot, string Path, string Name, string Vendor, bool On, string? Error, IReadOnlyList<VstParamDto>? Params = null);

public sealed record VstParamDto(int Index, string Name, string Unit, string Display, float Value);

public sealed record VstSlotRequest(string? Path, bool On);

public sealed record VstParamRequest(float Value);

public sealed record VstFoldersRequest(IReadOnlyList<string> Folders);

/// <summary>All mixer channels with their settings, and the sources an input can use.</summary>
public sealed record ChannelsDto(IReadOnlyList<ChannelDto> Channels, IReadOnlyList<OutputChoiceDto> Sources, IReadOnlyList<OutputChoiceDto>? Effects = null);

public sealed record InputRequest(string Name, string Source);

/// <summary>Title updates (stream, file, web address) and the program log as a text file per day.</summary>
public sealed record TitleLogDto(string TitleFormat, List<string> TitleKinds, string TitleOther, string TitleFile, string TitleUrl,
    string LogFolder, string LogFormat, int LogKeepDays, string? LastTitle = null, string? TodayFile = null);

public sealed record TitleLogTestDto(string Title, string LogLine, bool? UrlOk);

/// <summary>Playout report: one campaign of one advertiser over a period.</summary>
public sealed record AdReportLineDto(string Advertiser, string Campaign, int Planned, int Aired, int Complete, int Missed,
    double SecondsOnAir, DateTime? First, DateTime? Last)
{
    public string Text => $"{Aired}× uitgezonden ({Complete} volledig){(Missed > 0 ? $", {Missed} gemist" : "")} · {SecondsOnAir / 60:0.0} min";
}
