using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public class CuePointCalculatorTests
{
    private static readonly MixOptions Mix = new() { CueInDb = -40, MixDb = -24, CueOutDb = -50, MaxOverlap = 8 };

    private static List<double> Envelope(params (double Seconds, double Db)[] parts)
    {
        var list = new List<double>();
        foreach (var (seconds, db) in parts)
            for (int i = 0; i < (int)Math.Round(seconds / 0.02); i++) list.Add(db);
        return list;
    }

    [Fact]
    public void Finds_cue_in_after_leading_silence()
    {
        var cues = CuePointCalculator.Compute(Envelope((1.0, -90), (10, -6), (1, -90)), 0.02, Mix);
        Assert.Equal(1.0, cues.CueIn, 2);
        Assert.Equal(11.0, cues.CueOut, 2);
    }

    [Fact]
    public void Mix_point_is_where_the_tail_drops_below_mix_level()
    {
        // 20 s loud, 4 s quiet tail (-30 dB, above cue-out level), then silence.
        var cues = CuePointCalculator.Compute(Envelope((20, -6), (4, -30), (1, -90)), 0.02, Mix);
        Assert.Equal(20.0, cues.MixPoint, 2);
        Assert.Equal(24.0, cues.CueOut, 2);
    }

    [Fact]
    public void Overlap_is_limited_to_max_overlap()
    {
        var cues = CuePointCalculator.Compute(Envelope((20, -6), (15, -30)), 0.02, Mix);
        Assert.Equal(35 - 8, cues.MixPoint, 2);
    }

    [Fact]
    public void Silence_plays_as_is()
    {
        var cues = CuePointCalculator.Compute(Envelope((5, -100)), 0.02, Mix);
        Assert.Equal(0, cues.CueIn);
        Assert.Equal(5, cues.CueOut, 2);
    }
}

public class LogBuilderTests
{
    private static MediaItem Song(string artist, string title) => new()
    {
        Path = $"{artist}-{title}.mp3", Kind = MediaKind.Song, Artist = artist, Title = title, CueOut = 180, MixPoint = 175,
    };

    private static readonly MediaItem Jingle = new() { Path = "id.wav", Kind = MediaKind.Jingle, Title = "ID", CueOut = 5, MixPoint = 5 };

    [Fact]
    public void Inserts_breaks_and_jingles_on_schedule()
    {
        var songs = Enumerable.Range(0, 20).Select(i => Song($"Artist {i}", $"Song {i}")).ToList();
        var log = new PlayoutLog();
        var builder = new LogBuilder(new RotationOptions { BreakEverySongs = 2, JingleEverySongs = 3, LookAhead = 12 }, true, new Random(1));
        builder.Fill(log, songs, [Jingle]);

        var types = log.Upcoming.Select(i => i.Type).ToList();
        Assert.Equal(12, types.Count);
        Assert.Contains(LogItemType.AiBreak, types);
        Assert.Contains(LogItemType.Jingle, types);
        // Never two non-song items in a row.
        for (int i = 1; i < types.Count; i++)
            Assert.False(types[i] != LogItemType.Song && types[i - 1] != LogItemType.Song, $"two specials at {i}");
    }

    [Fact]
    public void Respects_artist_separation()
    {
        var songs = new[] { "A", "B", "C", "D" }.SelectMany(a => Enumerable.Range(0, 3).Select(i => Song(a, $"{a}{i}"))).ToList();
        var log = new PlayoutLog();
        var builder = new LogBuilder(new RotationOptions { BreakEverySongs = 0, JingleEverySongs = 0, ArtistSeparation = 3, LookAhead = 12 }, false, new Random(7));
        builder.Fill(log, songs, []);

        var artists = log.Upcoming.Select(i => i.Media!.Artist).ToList();
        for (int i = 0; i < artists.Count; i++)
            for (int k = Math.Max(0, i - 3); k < i; k++)
                Assert.NotEqual(artists[k], artists[i]);
    }

    [Fact]
    public void Small_library_still_fills_the_log()
    {
        var log = new PlayoutLog();
        var builder = new LogBuilder(new RotationOptions { BreakEverySongs = 0, JingleEverySongs = 0, LookAhead = 5 }, false, new Random(3));
        builder.Fill(log, [Song("A", "1"), Song("A", "2")], []);
        Assert.Equal(5, log.Count);
        var titles = log.Upcoming.Select(i => i.Media!.Title).ToList();
        for (int i = 1; i < titles.Count; i++) Assert.NotEqual(titles[i - 1], titles[i]);
    }

    [Fact]
    public void Neighbour_songs_are_found_for_a_break()
    {
        var log = new PlayoutLog();
        var a = Song("A", "1");
        var b = Song("B", "2");
        log.Append(new LogItem { Type = LogItemType.Song, Media = a });
        var brk = new LogItem { Type = LogItemType.AiBreak };
        log.Append(brk);
        log.Append(new LogItem { Type = LogItemType.Jingle, Media = Jingle });
        log.Append(new LogItem { Type = LogItemType.Song, Media = b });

        var (prev, next) = log.NeighbourSongs(brk.Id);
        Assert.Same(a, prev);
        Assert.Same(b, next);
    }
}

public class PresenterTests
{
    [Theory]
    [InlineData(14, 0, "twee uur")]
    [InlineData(14, 15, "kwart over twee")]
    [InlineData(14, 29, "half drie")]
    [InlineData(14, 41, "tien over half drie")]
    [InlineData(23, 58, "twaalf uur")]
    [InlineData(9, 54, "vijf voor tien")]
    public void Spoken_time_is_natural_dutch(int h, int m, string expected) =>
        Assert.Equal(expected, TemplateScriptWriter.SpokenTime(new DateTime(2026, 11, 8, h, m, 0)));

    [Fact]
    public void Sanitizer_removes_markup_and_stage_directions()
    {
        var text = "<think>plan</think>**Hé!** (lacht) Dat was \"Song\" van *Artiest*. [jingle] Nu iets nieuws.";
        Assert.Equal("Hé! Dat was Song van . Nu iets nieuws.", ScriptSanitizer.Clean(text, 50));
    }

    [Fact]
    public void Sanitizer_limits_length_at_a_sentence_end()
    {
        var text = string.Join(" ", Enumerable.Repeat("Een twee drie vier vijf.", 10));
        var clean = ScriptSanitizer.Clean(text, 12);
        Assert.True(clean.Split(' ').Length <= 12);
        Assert.EndsWith(".", clean);
    }

    [Fact]
    public async Task Template_writer_mentions_both_songs_and_station()
    {
        var prev = new MediaItem { Path = "a", Kind = MediaKind.Song, Artist = "Doe Maar", Title = "Smoorverliefd" };
        var next = new MediaItem { Path = "b", Kind = MediaKind.Song, Artist = "Golden Earring", Title = "Radar Love" };
        var ctx = new BreakContext("Test FM", "Sam", new DateTime(2026, 11, 8, 15, 0, 0), prev, next);
        var text = await new TemplateScriptWriter(new Random(2)).WriteAsync(ctx, CancellationToken.None);
        Assert.Contains("Radar Love", text);
        Assert.DoesNotContain("{", text);
    }
}
