using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;
using ReaRadioDj.Core.Presenter;

namespace ReaRadioDj.Tests;

public sealed class VoiceProcessorTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "rea-voice-" + Guid.NewGuid().ToString("N") + ".wav");

    public void Dispose() { try { File.Delete(_file); } catch (IOException) { } }

    /// <summary>A "voice": 0.5 s silence, 1 s of a quiet tone with a loud burst and DC rumble, 0.5 s silence; 24 kHz mono.</summary>
    internal static float[] Voice(int rate = 24000)
    {
        var x = new float[rate * 2];
        for (int i = rate / 2; i < rate * 3 / 2; i++)
        {
            double t = i / (double)rate;
            double amp = i is > 30000 and < 32000 ? 0.9 : 0.1;
            x[i] = (float)(amp * Math.Sin(2 * Math.PI * 300 * t) + 0.05 * Math.Sin(2 * Math.PI * 20 * t));
        }
        return x;
    }

    [Fact]
    public void Wav_round_trips_as_16_bit()
    {
        var x = new float[] { 0, 0.5f, -0.5f, 0.25f };
        var (rate, channels, y) = VoiceProcessor.Read(VoiceProcessor.Write(24000, 1, x));
        Assert.Equal(24000, rate);
        Assert.Equal(1, channels);
        for (int i = 0; i < x.Length; i++) Assert.Equal(x[i], y[i], 3);
    }

    [Fact]
    public void Process_trims_silence_and_sets_the_peak()
    {
        File.WriteAllBytes(_file, VoiceProcessor.Write(24000, 1, Voice()));
        var r = VoiceProcessor.Process(_file);

        Assert.Equal(2.0, r.SecondsIn, 2);
        Assert.InRange(r.SecondsOut, 1.0, 1.2);   // 1 s of sound plus a little room at both ends
        var (_, _, y) = VoiceProcessor.Read(File.ReadAllBytes(_file));
        double peakDb = 20 * Math.Log10(y.Max(Math.Abs));
        Assert.InRange(peakDb, -1.2, -0.8);
        Assert.Equal(0, y[0], 3);                 // faded in, no click
    }

    [Fact]
    public void Compressor_narrows_the_difference_between_loud_and_quiet()
    {
        var x = Voice();
        static double Ratio(float[] v) => v.Skip(30500).Take(1000).Max(Math.Abs) / v.Skip(20000).Take(1000).Max(Math.Abs);
        double before = Ratio(x);
        VoiceProcessor.Compress(x, 24000, 1, new VoiceProcessor.Settings());
        Assert.True(Ratio(x) < before * 0.75, $"loud/quiet {before:0.0} should shrink, is {Ratio(x):0.0}");
    }

    [Fact]
    public void High_pass_removes_rumble()
    {
        int rate = 24000;
        var x = Enumerable.Range(0, rate).Select(i => (float)Math.Sin(2 * Math.PI * 20 * i / rate)).ToArray();
        VoiceProcessor.HighPass(x, rate, 1, 80);
        Assert.True(x.Skip(rate / 2).Max(Math.Abs) < 0.1);
    }

    [Fact]
    public void Server_root_accepts_both_forms() =>
        Assert.Equal(OpenAiScriptWriter.Root("http://localhost:11434/v1/"), OpenAiScriptWriter.Root("http://localhost:11434"));
}

public sealed class PersonaStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-radio-dj-persona-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public PersonaStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void First_matching_persona_wins_by_day_and_hour()
    {
        var st = new PersonaStore(_db);
        int weekdays = 0b001_1111;
        st.Save(new Persona(0, "Ochtend", true, weekdays, "6-9", "Joke", "Rustig", null, 30, null, 0));
        st.Save(new Persona(0, "Weekend", true, TimedEvent.AllDays & ~weekdays, "*", "Bram", "", null, 400, null, 1));
        st.Save(new Persona(0, "Uit", false, TimedEvent.AllDays, "*", "", "", null, null, null, 2));

        Assert.Equal("Ochtend", st.For(new DateTime(2026, 11, 9, 7, 30, 0))?.Name);   // Monday 07:30
        Assert.Null(st.For(new DateTime(2026, 11, 9, 12, 0, 0)));                      // Monday noon: general settings
        Assert.Equal("Weekend", st.For(new DateTime(2026, 11, 14, 7, 30, 0))?.Name);   // Saturday
        Assert.Equal(150, st.All().Single(p => p.Name == "Weekend").MaxWords);         // clamped

        var o = st.All().Single(p => p.Name == "Ochtend");
        st.Save(o with { Hours = "6-10" });
        Assert.Equal("Ochtend", st.For(new DateTime(2026, 11, 9, 10, 15, 0))?.Name);
        Assert.True(st.Delete(o.Id));
        Assert.Equal(2, st.All().Count);
    }
}

public sealed class BreakExtrasTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-radio-dj-extras-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public BreakExtrasTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Weather_from_open_meteo_reads_as_plain_dutch()
    {
        using var doc = JsonDocument.Parse("""
            {"current":{"temperature_2m":7.6,"weather_code":61,"wind_speed_10m":6.1},
             "daily":{"temperature_2m_max":[10.2],"precipitation_probability_max":[80]}}
            """);
        var w = BreakExtras.WeatherExtra(doc.RootElement, "Gieten");
        Assert.Equal("Het weer in Gieten: regen en 8 graden, vandaag tot 10 graden.", w.Spoken);
        Assert.Contains("wind kracht 4", w.Fact);
        Assert.Contains("kans op regen 80 procent", w.Fact);
        Assert.Equal(0, BreakExtras.Beaufort(0.1));
        Assert.Equal(6, BreakExtras.Beaufort(12));
    }

    [Fact]
    public void Rss_and_atom_feeds_give_titles()
    {
        var rss = BreakExtras.ParseFeed("""
            <rss version="2.0"><channel><title>Dorpsnieuws</title>
              <item><title>Nieuwe brug in Annen open</title></item><item><title> </title></item></channel></rss>
            """).ToList();
        Assert.Equal([("Nieuwe brug in Annen open", "Dorpsnieuws")], rss);
        var atom = BreakExtras.ParseFeed("""
            <feed xmlns="http://www.w3.org/2005/Atom"><title>Gemeente</title><entry><title>Raad vergadert</title></entry></feed>
            """).Single();
        Assert.Equal(("Raad vergadert", "Gemeente"), atom);
    }

    [Fact]
    public async Task A_note_comes_first_and_is_read_only_as_often_as_allowed()
    {
        var notes = new NoteStore(_db);
        notes.Add("Groeten aan Jan uit Gieten, 80 jaar!", null, 2);
        notes.Add("Verlopen bericht", DateTime.Now.AddHours(-1), 1);
        var extras = new BreakExtras(new HttpClient(), new PresenterOptions(), notes);   // weather and news off: no internet

        for (int i = 0; i < 2; i++)
        {
            var e = await extras.PickAsync(DateTime.Now, CancellationToken.None);
            Assert.Equal("note", e?.Kind);
            Assert.Equal("Groeten aan Jan uit Gieten, 80 jaar!", e!.Spoken);
            extras.Used(e, DateTime.Now);
        }
        Assert.Null(await extras.PickAsync(DateTime.Now, CancellationToken.None));
        Assert.Equal(2, notes.CleanUp(DateTime.Now.AddDays(2)));
    }

    [Fact]
    public async Task Template_puts_the_extra_before_the_next_song()
    {
        var next = new MediaItem { Path = "y", Kind = MediaKind.Song, Artist = "Golden Earring", Title = "Radar Love" };
        var c = new BreakContext("Radio Aa en Hunze", "Sam", new DateTime(2026, 11, 9, 8, 0, 0), null, next,
            Extra: new BreakExtra("weather", "", "Het weer: zonnig en 12 graden."));
        var text = await new TemplateScriptWriter(new Random(1)).WriteAsync(c, CancellationToken.None);
        Assert.Contains("Het weer: zonnig en 12 graden.", text);
        Assert.True(text.IndexOf("zonnig", StringComparison.Ordinal) < text.IndexOf("Radar Love", StringComparison.Ordinal), text);
    }
}
