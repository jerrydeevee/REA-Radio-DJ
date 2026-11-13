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
    DjDto? Dj = null,
    IReadOnlyList<VoipLineDto>? Voip = null,
    SipStatusDto? Sip = null);

// ---------------------------------------------------------------- VoIP / codec lines

/// <summary>
/// Open an RTP line on a VoIP channel. RemoteHost empty = answer whoever sends to LocalPort (symmetric RTP);
/// Codec "pcma", "pcmu" or "l16"; JitterMs = what the jitter buffer holds before it plays.
/// </summary>
public sealed record VoipConnectRequest(int LocalPort, string RemoteHost, int RemotePort, string Codec, double JitterMs);

/// <summary>What the jitter buffer measured: packets in, played, lost, late, twice, times it ran dry, frames left out.</summary>
public sealed record JitterDto(long Received, long Played, long Lost, long Late, long Duplicates, long Underruns, long Skipped,
    double JitterMs, double BufferMs, double TargetMs, double LossPercent);

/// <summary>One VoIP channel and its line. Level = the channel's own level (what comes in), 0..1.</summary>
public sealed record VoipLineDto(string Id, string Name, bool Connected, string State, string Codec, int? LocalPort, string? Remote,
    long Sent, JitterDto Jitter, double SecondsSinceAudio, DateTime? OpenedUtc, string? Error, bool Pfl, bool OnAir, float Level,
    VoipConnectRequest Settings, bool Recording = false, double RecordSeconds = 0);

/// <summary>Auto = every call or line is recorded (what comes in) from the moment it has audio until it goes quiet.</summary>
public sealed record VoipRecordDto(bool Auto);

public sealed record CodecDto(string Name, string Description);

/// <summary>
/// The SIP account of the VoIP channels. The password never comes back: HasPassword says whether one is stored.
/// Registrar/Proxy/OutboundProxy/Stun are host or host:port; Codecs in order of preference; IncomingChannel
/// "auto", "voip1", "voip2" or "geen".
/// </summary>
public sealed record SipAccountDto(bool Enabled, string DisplayName, string User, string AuthUser, bool HasPassword, string Domain,
    string Registrar, string Proxy, string OutboundProxy, string Transport, int LocalPort, string Stun, IReadOnlyList<string> Codecs,
    bool Register, int RegisterSeconds, int KeepAliveSeconds, string IncomingChannel);

/// <summary>Saves the SIP account. Password null = keep the stored one, "" = remove it.</summary>
public sealed record SipAccountRequest(bool Enabled, string DisplayName, string User, string AuthUser, string? Password, string Domain,
    string Registrar, string Proxy, string OutboundProxy, string Transport, int LocalPort, string Stun, IReadOnlyList<string> Codecs,
    bool Register, int RegisterSeconds, int KeepAliveSeconds, string IncomingChannel);

public sealed record SipCallDto(string Channel, string State, bool Incoming, string Remote, string RemoteName, DateTime StartedUtc,
    DateTime? AnsweredUtc, string? Reason, string? Codec, bool Ringing, bool Active);

/// <summary>Registration "uit", "niet aangemeld", "aanmelden…", "aangemeld" or "fout" (Error says why).</summary>
public sealed record SipStatusDto(bool Enabled, string Registration, DateTime? RegisteredUntilUtc, string? Error, string? PublicAddress,
    string Account, IReadOnlyList<SipCallDto> Calls);

public sealed record SipDialRequest(string Target);

/// <summary>
/// A saved station in the Verbindingen address book: a SIP call (Kind "sip", Target = number or sip address) or a
/// fixed RTP line (Kind "rtp", with ports, address and codec). Id is made by the engine when empty.
/// </summary>
public sealed record VoipStationDto(string Id, string Name, string Kind, string Target, int LocalPort, string RemoteHost, int RemotePort,
    string Codec, double JitterMs, string Note);

/// <summary>Commands (command bus) to run when a VoIP channel starts ringing, gets a call or line, and goes quiet again; empty = nothing.</summary>
public sealed record VoipSignalsDto(string Ringing, string Connected, string Ended);

// ---------------------------------------------------------------- DJ desk

/// <summary>One DJ deck. Tempo in percent, equaliser and filter -1..1, fader 0..1, positions in seconds.</summary>
public sealed record DjDeckDto(string Id, long? MediaId, string Title, string Artist, bool Loaded, bool Playing, double Position,
    double Duration, double Bpm, double FirstBeat, bool Analysing, double Tempo, double TempoRange, bool KeyLock, bool Synced,
    double CuePoint, IReadOnlyList<double?> HotCues, double? LoopIn, double? LoopOut, bool LoopActive, double LoopBeats, double Gain,
    double EqLow, double EqMid, double EqHigh, bool KillLow, bool KillMid, bool KillHigh, double Filter, double Fader, bool Pfl,
    float LevelLeft, float LevelRight, bool Rolling = false, string FxType = "echo", bool FxOn = false, double FxMix = 0.5, double FxBeats = 1,
    bool Censoring = false, string? Spinning = null);

/// <summary>Crossfader 0 (decks A and C) .. 1 (B and D); curve "smooth" or "sharp"; OnAir = the desk is heard on the radio.</summary>
/// Following = deck A copies what the automation plays, ready to take over.
/// Recording = the file the mix is being recorded to (null when not recording), for RecordingSeconds.
/// Quantize = hot cues, loops and rolls on the beat; CueMix = headphones 0 (cue) .. 1 (master); Limiter on the DJ output.
public sealed record DjDto(double Crossfader, string Curve, bool OnAir, IReadOnlyList<DjDeckDto> Decks, bool Following = false,
    string? Recording = null, double RecordingSeconds = 0, bool Quantize = false, double CueMix = 0, bool Limiter = true, double LimiterDb = -1,
    DjAutomixDto? Automix = null);

/// <summary>
/// Automix on decks A and B. Transition: fade, cut, beat (tempo and beat matched), echo or filter; Beats = its length.
/// State: uit, leeg, klaar, speelt or mixen; Progress 0..1 while mixing; SecondsToMix until the next transition.
/// </summary>
public sealed record DjAutomixDto(bool On, string Transition, double Beats, IReadOnlyList<DjAutomixItemDto> Queue, string State,
    double Progress, double? SecondsToMix, string? OutDeck);

public sealed record DjAutomixItemDto(long Id, string Title, string Artist, double Duration);

/// <summary>Automix settings and list changes; any field left out stays as it is. MediaIds are added (at Index when given).</summary>
public sealed record DjAutomixRequest(bool? On = null, string? Transition = null, double? Beats = null, IReadOnlyList<long>? MediaIds = null,
    int? Index = null, int? Remove = null, int? MoveFrom = null, int? MoveTo = null, bool? Clear = null, bool? MixNow = null);

public sealed record DjCommand(long? MediaId = null, double? Value = null, int? Index = null, bool? On = null, string? Name = null);

public sealed record DjMixerRequest(double? Crossfader = null, string? Curve = null, bool? OnAir = null, bool? Record = null,
    bool? Quantize = null, double? CueMix = null, bool? Limiter = null, double? LimiterDb = null);

public sealed record EngineDto(bool Running, bool AutoPlay, NowPlayingDto? Current, IReadOnlyList<NowPlayingDto> Playing,
    float LevelLeft, float LevelRight, double SecondsToNext);

public sealed record NowPlayingDto(long Id, string Type, string Label, string Artist, string Title, string Category, string Origin,
    double Position, double From, double To, double MixAt, double? Intro, double? FadeAt, string? Script, long MediaId);

public sealed record LogItemDto(long Id, string Type, string Label, string? Artist, string? Title, string? Category, string Origin,
    double Length, DateTime StartsAt, string? BreakState, string? Script, string? ScriptSource, string? Voice, string? Error,
    long? MediaId, bool UseHook, string? Extra = null, double? CheckScore = null, bool HandMadeMix = false,
    List<LogItemDto>? Children = null);

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

/// <summary>
/// An event at a place in a track: Kind "volume" (Gain in dB for Length seconds, with Ramp seconds to go there and back)
/// or "command" (Text is a command-bus line). At is in seconds from the start of the file.
/// </summary>
public sealed record TrackEventDto(string Kind, double At, string? Text = null, double Gain = -6, double Length = 5, double Ramp = 0.5,
    string Label = "");

public sealed record MediaDto(long Id, string Path, string Kind, long? CategoryId, string Category, string Artist, string Title,
    string Album, double Duration, double? Lufs, CueDto Cues, bool Enabled, bool CuesManual, DateTime? ValidFrom,
    DateTime? ValidUntil, int PlayCount, DateTime? LastPlayed, bool Missing, int? Year = null, DateTime? Added = null,
    string? Block = null, string Genre = "", double? Bpm = null, double? FirstBeat = null, double? EstIntro = null, double? EstOutro = null,
    IReadOnlyList<string>? Issues = null, string Language = "", string Mood = "", int? Rating = null, int? EnergyLevel = null, int? Bitrate = null,
    string? Key = null, string? Camelot = null, double? ReplayGain = null, IReadOnlyList<TrackEventDto>? Events = null)
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
    string FolderInUse = "", string? ProgramFile = null, string? MicFile = null, string Error = "",
    bool Speech = false, double SpeechThresholdDb = -40, double SpeechHangSeconds = 2,
    string? SpeechFile = null, bool SpeechRecording = false, long SpeechTakes = 0, string SpeechSource = "mic");

/// <summary>The sound card watch: settings and per Windows card in use its volume (percent, null when it cannot be read).</summary>
public sealed record DeviceWatchDto(bool Enabled, bool Restore, IReadOnlyList<DeviceWatchCardDto> Cards);
public sealed record DeviceWatchCardDto(string Name, string Uses, double? Volume, bool Muted, string Note);

public sealed record CategoryDto(long Id, string Name, string Kind, string Color);

/// <summary>Waveform: one linear peak (0..65535) per block, little-endian UInt16, base64.</summary>
public sealed record PeaksDto(int BlockMs, string Data);

/// <summary>Makes an item music ("song") or a jingle by hand; CategoryId = where it goes (null = a fitting one).</summary>
public sealed record MediaKindRequest(string Kind, long? CategoryId = null);

public sealed record MediaUpdateRequest(long? CategoryId, bool Enabled, DateTime? ValidFrom, DateTime? ValidUntil);

public sealed record CategoryRequest(string Name, string Kind, string? Color);

// ---------------------------------------------------------------- log, players, carts, mixer

/// <summary>Position: null = end of the log, 0 = next.</summary>
public sealed record AddToLogRequest(long MediaId, int? Position);

/// <summary>A container of library items as one line in the log; Position null = at the end, 0 = next.</summary>
public sealed record AddContainerRequest(string Name, List<long> MediaIds, int? Position);
/// <summary>A folder or audio file in the Verkenner; <c>MediaId</c> is set when the file is in the library.</summary>
public sealed record FileEntryDto(string Name, string Path, bool IsFolder, long Size, DateTime Modified, long? MediaId);
public sealed record FolderDto(string Path, string? Parent, IReadOnlyList<FileEntryDto> Entries, string? Error);
/// <summary>Puts an audio file on the log (or in PFL) by its path on the engine machine.</summary>
public sealed record FileRequest(string Path, int? Position);

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
    string? Artist, string? Title, string? Category, double? Planned, double? Played, bool Ai, string? Note, string Presenter = "");

public sealed record MessageDto(long Id, DateTime Time, string Level, string Text);

// ---------------------------------------------------------------- presenter voice

/// <summary>Engine: higgs or sapi. Problem: why the profile cannot be used (e.g. a clone without consent).</summary>
public sealed record VoiceProfileDto(long Id, string? Name, string? Engine, string? ReferenceAudio, string? ReferenceText, string? Tags,
    double Temperature, string? SapiVoice, bool Consent, string? ConsentNote, bool IsDefault, string? Problem = null, long? FallbackId = null,
    string? Owner = null, string? Uses = null)
{
    /// <summary>Short line for lists: engine, whose own voice, the roles it may speak in.</summary>
    [JsonIgnore] public string Summary => string.Join(" · ", new[]
    {
        Engine switch { "higgs" => string.IsNullOrWhiteSpace(ReferenceAudio) ? "Higgs" : "Higgs, gekloond", "sapi" => "Windows", "elevenlabs" => "ElevenLabs", "azure" => "Azure", _ => Engine },
        string.IsNullOrWhiteSpace(Owner) ? null : $"eigen stem van {Owner}",
        string.IsNullOrWhiteSpace(Uses) ? null : "alleen: " + Uses.Replace("presenter", "presentator").Replace("news", "nieuws").Replace("weather", "weer").Replace(",", ", "),
    }.Where(x => !string.IsNullOrEmpty(x)));
}

/// <summary>Which voice speaks as AI presenter (the default profile), as news reader and as weather man or woman (null = the presenter's voice).</summary>
public sealed record VoiceRolesDto(long? Presenter, long? News, long? Weather);

/// <summary>
/// The own voice of a presenter: who (the signed-in user, or a name when the station has no users), the voice when there
/// is one, the text to read for a recording, and the recording waiting to be kept.
/// </summary>
public sealed record OwnVoiceDto(string? Owner, bool SignInNeeded, VoiceProfileDto? Profile, string ReadText, bool Recording,
    double RecordingSeconds, VoiceClipDto? Take);

/// <summary>A voice recording: length, peak and why it cannot be used (null = fine).</summary>
public sealed record VoiceClipDto(double Seconds, double PeakDb, string? Problem);

/// <summary>Start recording the own voice (Device: input device, null = the studio microphone). Owner only without users.</summary>
public sealed record OwnVoiceRecordRequest(string? Owner, int? Device);

/// <summary>Keep the recording as the own voice: the text read, the permission and the roles it may speak in (presenter, news, weather).</summary>
public sealed record OwnVoiceSaveRequest(string? Owner, string? Text, bool Consent, string? Uses);

/// <summary>How the Higgs voice server is started. The command can only be changed on the studio computer itself.</summary>
public sealed record HiggsServerSettingsDto(bool AutoStart, string BaseUrl, string Command, string Arguments, string WorkingDirectory,
    bool CommandEditable = true);

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
/// "category", "headlines" (Feeds, one URL per line, read out by the AI voice), "ainews" (national Feeds and regional
/// feeds rewritten by the language model) or "weather" (a weather report for Ai.Place). When and Next are filled in by the engine.
/// </summary>
public sealed record NewsBlockDto(long Id, string Name, bool Enabled, int Days, string Hours, int Minute, int Second, string Timing,
    long? OpenerId, long? CloserId, string Source, string Folder, int MaxAgeMinutes, long? CategoryId, string Feeds, int MaxHeadlines,
    string? When = null, DateTime? Next = null, NewsAiOptionsDto? Ai = null);

/// <summary>A hardware device. Kind "midi", "serial", "livewire", "ember" or "mackie" (Mackie Control surface); Port is the MIDI name, COM port or host[:port].</summary>
public sealed record HwDeviceDto(long Id, string Name, bool Enabled, string Kind, string Port, int Baud, string Password,
    bool Connected = false, string Status = "");

/// <summary>A trigger ("note 60", "cc 7", "gpi 3.1", "ember 1.2.3 > -40" or a serial line) and the commands for on and off.</summary>
public sealed record HwInputDto(long Id, long DeviceId, string Trigger, string OnCommand, string OffCommand);

/// <summary>A tally: State "mic", "onair", "auto", "voip1-ringing", "voip1-connected", "voip2-ringing" or "voip2-connected"; Target "note 60", "cc 20", "gpo 4.2" or "ember 1.2.3"; texts (serial) or values (Ember+) for on and off.</summary>
public sealed record HwOutputDto(long Id, long DeviceId, string State, string Target, string OnText, string OffText);

/// <summary>Something a device reported, and what it started.</summary>
public sealed record HwEventDto(DateTime Time, long DeviceId, string Device, string Key, bool On, string Result);

/// <summary>A node or parameter of an Ember+ device, for browsing its tree.</summary>
public sealed record HwEmberItemDto(string Path, string Identifier, string Description, bool IsNode, string Value, string Type, bool Writable);

public sealed record HwPortsDto(List<string> Midi, List<string> Serial);

/// <summary>
/// Choices for Mackie Control surfaces: the channel on the master fader ("" = none), transport buttons, meters, which
/// devices are extenders (eight strips more, e.g. iCON V1-X) and whether those stand left of the main panel.
/// </summary>
public sealed record MackieSettingsDto(string MasterStrip, bool Transport, bool Meters, List<long>? Extenders = null, bool ExtendersLeft = false);

/// <summary>A mixer channel a Mackie surface can show, in strip order.</summary>
public sealed record MackieChannelDto(string Id, string Name);

/// <summary>A Mackie Control surface: whether it is connected and which channels are on its strips (Bank = first, from 0).</summary>
public sealed record MackieDeviceDto(long Id, string Name, string Port, bool Connected, string Status, int Bank, bool Extender = false);

/// <summary>The Mackie Control set-up and what happened lately (newest first).</summary>
public sealed record MackieDto(MackieSettingsDto Settings, IReadOnlyList<MackieChannelDto> Channels, IReadOnlyList<MackieDeviceDto> Devices,
    IReadOnlyList<string> Recent);

/// <summary>Messages as if a surface sent them: hex bytes, several separated by commas ("90 10 7F, E0 00 40").</summary>
public sealed record MackieSimulateRequest(long DeviceId, string Messages);

/// <summary>A DJ function a controller can operate. Kind: button, absolute (fader, knob) or relative (jog wheel).</summary>
public sealed record DjMidiControlDto(string Id, string Name, string Group, string Kind);

/// <summary>A learned control: the function, the controller and what it sends ("ch1 cc 7"); Invert turns a fader around.</summary>
public sealed record DjMidiBindingDto(string Control, long DeviceId, string Trigger, bool Invert = false);

public sealed record DjMidiPresetInfoDto(string Name, int Count);

public sealed record DjMidiDeviceDto(long Id, string Name, string Port, bool Enabled);

/// <summary>The DJ controller set-up: functions, learned controls, saved presets, MIDI devices and the learn state.</summary>
public sealed record DjMidiDto(IReadOnlyList<DjMidiControlDto> Controls, IReadOnlyList<DjMidiBindingDto> Bindings,
    IReadOnlyList<DjMidiPresetInfoDto> Presets, IReadOnlyList<DjMidiDeviceDto> Devices, string? Learning, string LearnResult, string LastSignal);

/// <summary>Learn a function (Control null = stop learning), or with Invert: turn a learned fader around.</summary>
public sealed record DjMidiLearnRequest(string? Control, bool? Invert = null);

/// <summary>Save the current controls as a preset (Name only), or import one (Name and Bindings, as exported).</summary>
public sealed record DjMidiPresetRequest(string? Name, IReadOnlyList<DjMidiBindingDto>? Bindings = null);

/// <summary>Everything the hardware window shows.</summary>
public sealed record HwOverviewDto(List<HwDeviceDto> Devices, List<HwInputDto> Inputs, List<HwOutputDto> Outputs, Dictionary<string, bool> States);

/// <summary>A music catalogue in another database (ODBC). Kind "song" or "jingle"; SyncMinutes 0 = only on request.</summary>
public sealed record DbSourceDto(long Id, string Name, bool Enabled, string Connection, string Query, string Kind, int SyncMinutes,
    string PathFrom, string PathTo, DateTime? LastSync = null, string LastResult = "", bool Busy = false);

/// <summary>What a test query returned: the columns, which of them are used, and the first rows.</summary>
public sealed record DbTestDto(bool Ok, List<string> Columns, List<string> Used, List<string> Sample, string? Problem);

/// <summary>What a metadata lookup found for a library item; only fields that are empty now are filled in.</summary>
public sealed record MetaProposalDto(long MediaId, string Song, string Source, string Found, string? Album, int? Year, string? Isrc,
    string? Label, string Link, string? Genre = null, string? Composer = null, string? CoverUrl = null, string? NewArtist = null,
    string? NewTitle = null);

/// <summary>The state of the metadata lookup; AcoustId = recognition by sound is set up, BySound = songs recognised that way.</summary>
public sealed record MetaStatusDto(bool Running, int Done, int Total, string Current, int NotFound, int NothingNew, bool Spotify,
    List<MetaProposalDto> Proposals, bool AcoustId = false, int BySound = 0, bool AutoOnPlay = false, int AutoDone = 0, string AutoLast = "");

/// <summary>Whether songs are completed automatically the first time they go on air.</summary>
public sealed record MetaAutoDto(bool Enabled);

/// <summary>AcoustID: whether a key is set (never the key itself), whether fpcalc is there, and where to register an application.</summary>
public sealed record AcoustIdSettingsDto(bool KeySet, string KeyHint, bool FpcalcFound, bool Usable, string RegisterUrl);

public sealed record AcoustIdSettingsRequest(string? ApiKey, bool Clear = false);

public sealed record AcoustIdTestDto(bool Ok, string Message);

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
public sealed record NewsCheckDto(bool Ok, List<string> Parts, string? Problem, string? Script, string? Note = null, double? Seconds = null);

/// <summary>
/// AI news and weather settings of a news block. RegionalFeeds: one URL per line (national ones are in Feeds);
/// Words: length of the bulletin; BedId: a library item under the voice; VoiceId: a voice profile (null = default).
/// </summary>
public sealed record NewsAiOptionsDto(string RegionalFeeds = "", int RegionalCount = 3, bool RegionalFirst = false, int Words = 120,
    bool Attribution = true, int MaxAgeHours = 6, long? BedId = null, long? VoiceId = null, string Place = "", double Latitude = 0,
    double Longitude = 0);

/// <summary>A place found by name (for the weather report).</summary>
public sealed record PlaceDto(string Name, string Region, double Latitude, double Longitude);

/// <summary>Try a news block: Speak renders it with the voice and loads it into PFL (Play: and starts it).</summary>
public sealed record NewsTryRequest(NewsBlockDto Block, bool Speak = false, bool Play = false);

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

/// <summary>The Spotify keys as the studio sees them: the Client ID in full, of the secret only whether it is set and its last four characters.</summary>
public sealed record SpotifySettingsDto(string ClientId, bool SecretSet, string SecretHint, string Market, bool Configured);

/// <summary>Change the Spotify keys. A null or empty secret keeps the stored one; Clear removes the keys.</summary>
public sealed record SpotifySettingsRequest(string? ClientId, string? ClientSecret, string? Market, bool Clear = false);

public sealed record SpotifyTestDto(bool Ok, string Message);

public sealed record MusicReportSummaryDto(int Tracks, double Seconds, int WithoutIsrc, int WithoutComposer);

/// <summary>Broadcast statistics of a period (from the as-run log). Seconds are rounded.</summary>
public sealed record AirStatsDto(DateOnly From, DateOnly To, int Items, int Songs, double MusicSeconds, double SpeechSeconds,
    double JingleSeconds, double OtherSeconds, List<AirStatsCountDto> TopTracks, List<AirStatsCountDto> TopArtists,
    List<AirStatsRotationDto> Rotation, List<int> SongsPerHour, int IdleDays, List<AirStatsIdleDto> Idle);

public sealed record AirStatsCountDto(string Name, string Detail, int Plays, double Seconds);

public sealed record AirStatsRotationDto(string Category, int Plays, int Tracks, double Seconds);

public sealed record AirStatsIdleDto(long MediaId, string Artist, string Title, string Category, DateTime? LastPlayed);

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
/// <summary>Regiekamer: weather now (null when off or not fetched, with the reason in WeatherNote) and the news headlines.</summary>
public sealed record ControlRoomDto(WeatherDto? Weather, string? WeatherNote, DateTime? WeatherAt,
    IReadOnlyList<HeadlineDto> Headlines, IReadOnlyList<string> NewsProblems, DateTime? NewsAt);
/// <summary>Temperature in degrees Celsius, WMO code, sky in Dutch, wind in Beaufort, today's max; RainChance -1 = unknown.</summary>
public sealed record WeatherDto(string Place, int Temperature, int Code, string Sky, int Beaufort, int Max, int RainChance);
public sealed record HeadlineDto(string Title, string Source);

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

// ---------------------------------------------------------------- main and backup studio

/// <summary>What an engine tells the other studio: healthy, something playing, silence on air, and whether it took the air over.</summary>
public sealed record FailoverBeatDto(string Station, string Role, bool Healthy, bool Playing, bool Silent, bool HasAir, DateTime TimeUtc);

/// <summary>Role "uit", "hoofd" or "reserve"; State in words; Partner = what the other engine said last (null = no answer).</summary>
public sealed record FailoverStatusDto(string Role, string PartnerUrl, string State, bool HasAir, DateTime? LastAnswerUtc, FailoverBeatDto? Partner, string? Error);

public sealed record FailoverSettingsDto(string Role, string Partner, int IntervalSeconds, int LostSeconds, bool TakeOverOnSilence,
    bool GiveBackAutomatically, int GiveBackSeconds, bool StartStream, string OnTakeOver, string OnGiveBack);
