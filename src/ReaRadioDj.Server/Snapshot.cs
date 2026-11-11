using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Streaming;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Server;

/// <summary>Maps the engine objects to the wire contract.</summary>
public static class Snapshot
{
    public static StateDto Build(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<ReaRadioDjOptions>();
        var engine = sp.GetRequiredService<PlayoutEngine>();
        var log = sp.GetRequiredService<PlayoutLog>();
        var library = sp.GetRequiredService<MediaLibrary>();
        var players = sp.GetRequiredService<StudioPlayers>();
        var router = sp.GetRequiredService<AudioRouter>();
        var automation = sp.GetRequiredService<AutomationService>();
        var streamer = sp.GetRequiredService<StreamGroup>();
        var messages = sp.GetRequiredService<MessageCenter>();

        var now = DateTime.Now;
        var state = engine.GetState();
        var upcoming = new List<LogItemDto>();
        double offset = state.SecondsToNext;
        foreach (var item in log.Upcoming.Take(40))
        {
            upcoming.Add(Item(item, now.AddSeconds(offset)));
            offset += item.PlannedLength;
        }
        var s = streamer.GetStatus();
        return new StateDto(
            options.StationName, now, automation.CurrentClock,
            Engine(state),
            upcoming,
            log.History.Take(20).Select(i => Item(i, default)).ToList(),
            players.Standby.Select(p => Player(p.Info)).ToList(),
            Player(players.Pfl.Info),
            players.Carts.Active.Concat(sp.GetRequiredService<Cartwall>().SamplerActive).Select(Clip).ToList(),
            players.Overlays.Active.Select(Clip).ToList(),
            Mixer(router),
            automation.UpcomingEvents(6).Select(e => new UpcomingEventDto(e.Id, e.Name, e.At, Name(e.Timing), e.Prepared)).ToList(),
            new LibrarySummaryDto(library.Songs.Count, library.Jingles.Count, library.Status, library.IsScanning),
            new StreamDto(s.Enabled, s.Connected, s.Server, s.BytesSent, s.Error),
            messages.LastId,
            DjApi.Dto(sp.GetRequiredService<DjMixer>().Info, sp.GetRequiredService<DjHandover>().Following, sp.GetRequiredService<DjMixer>().Automix.Info));
    }

    public static string Name<T>(T value) where T : struct, Enum
    {
        var n = value.ToString();
        return char.ToLowerInvariant(n[0]) + n[1..];
    }

    public static T Parse<T>(string? text, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(text, ignoreCase: true, out var v) ? v : fallback;

    public static EngineDto Engine(EngineState s) => new(s.Running, s.AutoPlay, s.Current is null ? null : Now(s.Current),
        s.Playing.Select(Now).ToList(), s.LevelLeft, s.LevelRight, s.SecondsToNext);

    private static NowPlayingDto Now(NowPlaying n) => new(n.Id, Lower(n.Type), n.Label, n.Artist, n.Title, n.Category, n.Origin,
        n.Position, n.From, n.To, n.MixAt, n.Intro, n.FadeAt, n.Script, n.MediaId);

    private static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    public static LogItemDto Item(LogItem i, DateTime startsAt) => new(
        i.Id, Name(i.Type), i.Label, i.Media?.Artist, i.Media?.Title, i.Media?.Category, i.Origin,
        Math.Round(i.PlannedLength, 1), startsAt,
        i.Type == LogItemType.AiBreak ? Name(i.BreakState) : null,
        i.Script, i.ScriptSource, i.Voice, i.Error, i.Media is { Id: > 0 } m ? m.Id : null, i.UseHook, i.Extra, i.CheckScore, i.MixIn is not null,
        i.Type == LogItemType.Container ? Children(i, startsAt) : null);

    // The items of a container with the time each would start.
    private static List<LogItemDto> Children(LogItem c, DateTime startsAt)
    {
        var list = new List<LogItemDto>(c.Children.Count);
        foreach (var child in c.Children)
        {
            list.Add(Item(child, startsAt));
            startsAt = startsAt.AddSeconds(child.PlannedLength);
        }
        return list;
    }

    public static PlayerDto Player(PlayerInfo p) => new(p.Id, p.Name, Name(p.State), p.Media is { Id: > 0 } m ? m.Id : null,
        p.Media?.DisplayName, p.Position, p.CueIn, p.CueOut, p.Media?.Intro, p.Stack.Select(x => x.DisplayName).ToList());

    public static ClipDto Clip(ClipInfo c) => new(c.Key, c.Label, c.Media.Id > 0 ? c.Media.Id : null, c.Position, Math.Round(c.Length, 2), c.Loop);

    public static MixerDto Mixer(AudioRouter router)
    {
        if (!router.IsStarted) return new MixerDto([], new MicDto(false, false, router.MicStatus), 0, 0);
        var strips = router.Strips.Select(s =>
        {
            var (l, r) = s.Levels();
            return new StripDto(s.Id, s.Name, s.Bus.Id, s.Volume, s.Muted, s.DuckLevel, l, r, s.OnAir, s.Source, s.Processor.Settings.Effect, s.Processor.Settings.EffectOn,
                s.Recorder.IsRecording, Math.Round(s.Recorder.Seconds, 1));
        }).ToList();
        var (pl, pr) = router.Program.Levels();
        var (st, it, tp) = router.Master.Read();
        return new MixerDto(strips, new MicDto(router.MicAvailable, router.MicOpen, router.MicStatus), pl, pr, st, it, tp, router.Master.LimiterOn);
    }

    public static CueDto Cues(CueSet c) => new(c.CueIn, c.FullLevel, c.Intro, c.Intro2, c.Outro, c.FadeOut, c.Mix, c.CueOut, c.HookIn, c.HookOut);

    public static CueSet Cues(CueDto c) => new()
    {
        CueIn = c.CueIn, FullLevel = c.FullLevel, Intro = c.Intro, Intro2 = c.Intro2, Outro = c.Outro, FadeOut = c.FadeOut,
        Mix = c.Mix, CueOut = c.CueOut, HookIn = c.HookIn, HookOut = c.HookOut,
    };

    public static MediaDto Media(MediaDetails d, string? block = null) => new(d.Media.Id, d.Media.Path, Name(d.Media.Kind), d.Media.CategoryId,
        d.Media.Category, d.Media.Artist, d.Media.Title, d.Album, d.Media.Duration, d.Media.Lufs, Cues(d.Media.Cues), d.Enabled, d.CuesManual,
        d.ValidFrom, d.ValidUntil, d.PlayCount, d.LastPlayed, d.Missing, d.Year, d.Added, block, d.Genre, d.Bpm, d.FirstBeat, d.EstIntro, d.EstOutro,
        d.Problems, d.Language, d.Mood, d.Rating, d.EnergyLevel, LibraryQuality.Bitrate(d),
        d.MusicalKey?.Name, d.MusicalKey?.Camelot, Core.Audio.ReplayGain.TrackGain(d.Media.Lufs));

    public static CategoryDto Category(Category c) => new(c.Id, c.Name, Name(c.Kind), c.Color);

    public static CartDto Cart(CartState c) => new(c.Slot.Wall, c.Slot.Slot, c.Slot.Page, c.Slot.MediaId, c.Slot.Label, c.Slot.Color,
        c.Slot.Loop, c.Slot.Overlay, c.Playing, c.Position, Math.Round(c.Length, 2), c.Media?.DisplayName);

    public static ClockDto Clock(ClockDef c) => new(c.Id, c.Name, c.Color,
        c.Rules.Select(r => new ClockRuleDto(r.Id, Name(r.Kind), r.CategoryId, r.MediaId, Name(r.Mode), r.Note)).ToList());

    public static ClockDef Clock(ClockDto c) => new(c.Id, string.IsNullOrWhiteSpace(c.Name) ? "Klok" : c.Name, c.Color ?? "#3fa7ff",
        (c.Rules ?? []).Select(r => new ClockRule(r.Id, Parse(r.Kind, ClockRuleKind.Category), r.CategoryId, r.MediaId,
            Parse(r.Mode, RuleMode.Random), r.Note ?? "")).ToList());

    public static TimedEventDto Event(TimedEvent e, DateTime now) => new(e.Id, e.Name, e.Enabled, e.Days, e.Hours, e.Minute, e.Second,
        Name(e.Timing), Name(e.Action), e.CategoryId, e.MediaId, e.Describe(), e.Enabled ? e.NextOccurrence(now) : null, e.Text);

    public static TimedEvent Event(TimedEventDto e) => new(e.Id, string.IsNullOrWhiteSpace(e.Name) ? "Vast item" : e.Name, e.Enabled,
        e.Days, e.Hours ?? "*", e.Minute, e.Second, Parse(e.Timing, EventTiming.Hard), Parse(e.Action, EventAction.PlayCategory),
        e.CategoryId, e.MediaId, e.Text ?? "");

    public static ScriptRuleDto Rule(ScriptRule r, Func<long?, string> category) => new(r.Id, r.Name, r.Enabled, Name(r.Kind),
        r.FromCategoryId, r.ToCategoryId, r.Count, Name(r.Action), r.Describe(category));

    public static ScriptRule Rule(ScriptRuleDto r) => new(r.Id, string.IsNullOrWhiteSpace(r.Name) ? "Regel" : r.Name, r.Enabled,
        Parse(r.Kind, RuleKind.MaxInRow), r.FromCategoryId, r.ToCategoryId, r.Count, Parse(r.Action, RuleAction.Suppress));

    public static StreamWatchDto Watch(StreamWatch w, string? state) => new(w.Id, w.Name, w.Enabled, w.Url, w.Days, w.Hours,
        Name(w.Mode), w.CheckSeconds, w.Describe(), state);

    public static StreamWatch Watch(StreamWatchDto w) => new(w.Id, string.IsNullOrWhiteSpace(w.Name) ? "Externe stream" : w.Name,
        w.Enabled, (w.Url ?? "").Trim(), w.Days, w.Hours ?? "*", Parse(w.Mode, WatchMode.Takeover), w.CheckSeconds);

    public static AsRunDto AsRun(AsRunEntry e) => new(e.Id, e.Started, e.Ended, e.Source, e.Type, e.MediaId, e.Path, e.Artist, e.Title,
        e.Category, e.Planned, e.Played, e.Ai, e.Note);
}
