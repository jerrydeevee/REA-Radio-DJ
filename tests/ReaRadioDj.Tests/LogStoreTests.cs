using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public sealed class LogStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-logstore-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _now = new(2026, 11, 9, 14, 0, 0);

    public LogStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private MediaItem Media(string title, MediaKind kind = MediaKind.Song)
    {
        var path = Path.Combine(_dir, title + ".wav");
        File.WriteAllBytes(path, [0]);
        return new MediaItem { Path = path, Kind = kind, Title = title, Duration = 60, Cues = new CueSet { CueIn = 0.5, Mix = 55, CueOut = 58, Intro = 9 } };
    }

    private PlayoutLog Log()
    {
        var log = new PlayoutLog();
        log.Append(new LogItem { Type = LogItemType.Song, Media = Media("Gespeeld") });
        log.TakeNext(); // into the history
        log.Append(new LogItem { Type = LogItemType.Song, Media = Media("Een"), Origin = "vast voor voicetrack · klok: Standaard" });
        log.Append(new LogItem { Type = LogItemType.VoiceTrack, Media = Media("Voicetrack", MediaKind.Voice), Origin = "voicetrack", StartEarly = 3.2 });
        log.Append(new LogItem { Type = LogItemType.Song, Media = Media("Twee"), Origin = "klok: Standaard", UseHook = true });
        log.Append(new LogItem { Type = LogItemType.AiBreak, Origin = "klok: Standaard", Persona = "Ochtend", Extra = "weather" });
        log.Append(new LogItem { Type = LogItemType.Jingle, Media = Media("Jingle", MediaKind.Jingle), Origin = "handmatig" });
        return log;
    }

    [Fact]
    public void A_restart_brings_back_the_whole_log_with_voicetracks_and_history()
    {
        var store = new LogStore(Path.Combine(_dir, "log.json"));
        Assert.True(store.Save(Log(), _now));

        var restored = new PlayoutLog();
        var r = new LogStore(Path.Combine(_dir, "log.json")).Restore(restored, _now.AddMinutes(5));

        Assert.Equal(new LogStore.Restored(5, 1, 0, 0), r);
        var up = restored.Upcoming;
        Assert.Equal(new[] { LogItemType.Song, LogItemType.VoiceTrack, LogItemType.Song, LogItemType.AiBreak, LogItemType.Jingle }, up.Select(i => i.Type));
        Assert.Equal(3.2, up[1].StartEarly);
        Assert.Equal(new CueSet { CueIn = 0.5, Mix = 55, CueOut = 58, Intro = 9 }, up[1].Media!.Cues);
        Assert.True(up[2].UseHook);
        Assert.Equal(("Ochtend", "weather", BreakState.Pending), (up[3].Persona, up[3].Extra, up[3].BreakState));
        Assert.Equal("Gespeeld", restored.History[0].Media!.Title);
        Assert.Equal(2, restored.ClearPlanned()); // "Twee" and the break; the pinned song stays with its voicetrack
    }

    [Fact]
    public void An_old_log_keeps_voicetracks_and_manual_items_and_lets_the_clocks_plan_again()
    {
        var store = new LogStore(Path.Combine(_dir, "log.json"));
        store.Save(Log(), _now);

        var restored = new PlayoutLog();
        var r = store.Restore(restored, _now.AddHours(3));

        Assert.Equal(2, r!.Replanned);
        Assert.Equal(new[] { "Een", "Voicetrack", "Jingle" }, restored.Upcoming.Select(i => i.Media!.Title));
    }

    [Fact]
    public void Missing_audio_is_skipped_and_a_rendered_break_whose_file_is_gone_is_rendered_again()
    {
        var log = new PlayoutLog();
        var song = Media("Weg");
        var spoken = Media("break-1", MediaKind.Voice);
        log.Append(new LogItem { Type = LogItemType.Song, Media = song, Origin = "handmatig" });
        log.Append(new LogItem { Type = LogItemType.AiBreak, Media = spoken, Origin = "klok: Standaard", BreakState = BreakState.Ready, Script = "Hallo", OutroAdded = true });
        var store = new LogStore(Path.Combine(_dir, "log.json"));
        store.Save(log, _now);
        File.Delete(song.Path);
        File.Delete(spoken.Path);

        var restored = new PlayoutLog();
        var r = store.Restore(restored, _now);

        Assert.Equal(1, r!.Missing);
        var brk = Assert.Single(restored.Upcoming);
        Assert.Equal((BreakState.Pending, (MediaItem?)null, (string?)null, false), (brk.BreakState, brk.Media, brk.Script, brk.OutroAdded));
    }

    [Fact]
    public void Nothing_saved_or_a_damaged_file_leaves_the_log_alone()
    {
        var path = Path.Combine(_dir, "log.json");
        var log = new PlayoutLog();
        Assert.Null(new LogStore(path).Restore(log));
        File.WriteAllText(path, "{ kapot");
        Assert.Null(new LogStore(path).Restore(log));
        Assert.Equal(0, log.Count);
    }

    [Fact]
    public void An_unchanged_log_is_not_written_again()
    {
        var store = new LogStore(Path.Combine(_dir, "log.json"));
        var log = Log();
        Assert.True(store.Save(log, _now));
        Assert.False(store.Save(log, _now.AddSeconds(1)));
        log.TakeNext();
        Assert.True(store.Save(log, _now.AddSeconds(2)));
    }
}
