using System.Text.Json;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Tests;

public class NewsWriterTests
{
    private const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0"><channel><title>NOS Nieuws - Algemeen</title>
          <item><title>Kabinet presenteert plan voor 40.000 woningen</title>
            <description>&lt;p&gt;Het kabinet wil &lt;b&gt;40.000&lt;/b&gt; extra woningen bouwen in 2027.&lt;/p&gt;</description>
            <pubDate>Wed, 11 Nov 2026 18:00:00 +0100</pubDate></item>
          <item><title>Oud nieuws</title><pubDate>Mon, 09 Nov 2026 08:00:00 +0100</pubDate></item>
          <item><title>Storm op komst</title></item>
        </channel></rss>
        """;

    private sealed class FakeLlm(string answer) : ITextWriter
    {
        public string Name => "llm:test";
        public bool Configured => true;
        public string? LastUser { get; private set; }
        public Task<string> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct, int minSeconds = 0)
        {
            LastUser = user;
            return answer == "!fail" ? throw new HttpRequestException("weg") : Task.FromResult(answer);
        }
    }

    [Fact]
    public void Feed_items_have_plain_text_source_and_date()
    {
        var items = NewsWriter.ParseItems(Rss, regional: false).ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal("Het kabinet wil 40.000 extra woningen bouwen in 2027.", items[0].Summary);
        Assert.Equal("NOS Nieuws - Algemeen", items[0].Source);
        Assert.Equal(new DateTime(2026, 11, 11, 18, 0, 0), items[0].Published!.Value.ToUniversalTime().AddHours(1));
        Assert.Null(items[2].Published);
    }

    [Fact]
    public void Interleave_takes_turns_and_skips_doubles()
    {
        FeedItem I(string t, bool r = false) => new(t, "", "", r, null);
        var mixed = NewsWriter.Interleave([[I("a"), I("b"), I("c")], [I("x", true), I("A", true)]], 4);
        Assert.Equal(["a", "x", "b", "c"], mixed.Select(i => i.Title));
    }

    [Fact]
    public void Invented_numbers_are_found()
    {
        var items = NewsWriter.ParseItems(Rss, false).Take(1).ToList();
        var at = new DateTime(2026, 11, 11, 19, 0, 0);
        var source = NewsWriter.SourceText(items, at);
        Assert.Empty(NewsWriter.UnsupportedNumbers("Het kabinet wil in 2027 40.000 woningen bouwen.", source));
        Assert.Equal(["12"], NewsWriter.UnsupportedNumbers("Daarbij vielen 12 gewonden, in 2027.", source));
    }

    [Fact]
    public async Task Bulletin_is_rewritten_or_falls_back_to_the_headlines()
    {
        var items = NewsWriter.ParseItems(Rss, false).Take(1).ToList();
        var at = new DateTime(2026, 11, 11, 19, 0, 0);

        var good = new FakeLlm("Het kabinet wil volgend jaar, in 2027, 40.000 extra woningen laten bouwen, meldt de NOS.");
        var (script, source, note) = await NewsWriter.NewsScriptAsync(good, "Radio Aa en Hunze", at, items, 120, true, default);
        Assert.Equal("Het nieuws van 19 uur. Het kabinet wil volgend jaar, in 2027, 40.000 extra woningen laten bouwen, meldt de NOS. Dat was het nieuws.", script);
        Assert.Equal("llm:test, herschreven", source);
        Assert.Null(note);
        Assert.Contains("bron: NOS Nieuws - Algemeen", good.LastUser);

        var invents = new FakeLlm("Het kabinet wil 55.000 woningen bouwen, en dat kost heel veel geld volgens de NOS vanavond.");
        (script, source, note) = await NewsWriter.NewsScriptAsync(invents, "Radio Aa en Hunze", at, items, 120, true, default);
        Assert.Equal("Het nieuws van 19 uur. Kabinet presenteert plan voor 40.000 woningen, meldt de NOS. Dat was het nieuws.", script);
        Assert.Equal("koppen letterlijk", source);
        Assert.Contains("55.000", note);

        (_, _, note) = await NewsWriter.NewsScriptAsync(new FakeLlm("!fail"), "Radio Aa en Hunze", at, items, 120, true, default);
        Assert.Contains("niet bereikbaar", note);
    }

    private const string Forecast = """
        {"current":{"temperature_2m":7.6,"weather_code":3,"wind_speed_10m":6.1,"wind_direction_10m":225},
         "daily":{"weather_code":[3,61],"temperature_2m_max":[9.4,11.2],"temperature_2m_min":[4.1,5.8],"precipitation_probability_max":[20,80]}}
        """;

    [Fact]
    public async Task Weather_report_from_the_forecast()
    {
        using var doc = JsonDocument.Parse(Forecast);
        var f = NewsWriter.ParseForecast(doc.RootElement, "Gieten");
        Assert.Equal(new WeatherForecast("Gieten", 8, "bewolkt", 9, 6, 20, 4, "zuidwesten", "regen", 6, 11, 80), f);
        var at = new DateTime(2026, 11, 11, 7, 30, 0);

        var (plain, _, _) = await NewsWriter.WeatherScriptAsync(null, "Radio Aa en Hunze", at, f, 80, default);
        Assert.StartsWith("Het weer, voor Radio Aa en Hunze. In Gieten is het nu 8 graden en bewolkt.", plain);
        Assert.Contains("Morgen regen met 6 graden tot 11 graden en 80 procent kans op regen.", plain);

        var (_, source, note) = await NewsWriter.WeatherScriptAsync(new FakeLlm("Het is nu 8 graden in Gieten, morgen tot 14 graden met veel regen."),
            "Radio Aa en Hunze", at, f, 80, default);
        Assert.Equal("weer uit vaste zinnen", source);
        Assert.Contains("14", note);
    }

    [Theory]
    [InlineData(22, 0, "22 uur")]
    [InlineData(22, 30, "half elf")]
    [InlineData(11, 30, "half twaalf")]
    [InlineData(12, 30, "half een")]
    [InlineData(23, 30, "half twaalf")]
    [InlineData(0, 15, "kwart over twaalf")]
    [InlineData(6, 45, "kwart voor zeven")]
    [InlineData(22, 12, "22:12")]
    public void Time_as_said_on_the_radio(int hour, int minute, string expected) =>
        Assert.Equal(expected, NewsWriter.SpokenHour(new DateTime(2026, 11, 11, hour, minute, 0)));

    [Theory]
    [InlineData(0, "noorden")]
    [InlineData(225, "zuidwesten")]
    [InlineData(350, "noorden")]
    [InlineData(100, "oosten")]
    public void Wind_direction_in_words(double degrees, string expected) => Assert.Equal(expected, NewsWriter.WindFrom(degrees));

    [Fact]
    public void Feed_lines_carry_an_optional_name()
    {
        var lines = NewsBlock.FeedLines("https://feeds.nos.nl/nosnieuwsalgemeen\r\nhttps://www.rtvdrenthe.nl/rss/index.xml | RTV Drenthe\n" +
                                        "https://a.example/x, https://b.example/y\nzomaar tekst\nhttps://feeds.nos.nl/nosnieuwsalgemeen | dubbel");
        Assert.Equal([("https://feeds.nos.nl/nosnieuwsalgemeen", ""), ("https://www.rtvdrenthe.nl/rss/index.xml", "RTV Drenthe"),
            ("https://a.example/x", ""), ("https://b.example/y", "")], lines);
        Assert.Equal("https://www.rtvdrenthe.nl/rss/index.xml | RTV Drenthe", NewsBlock.FeedText(lines.Skip(1).Take(1)));
        Assert.Equal("de NOS", NewsWriter.SourceName("NOS Nieuws"));
    }

    [Fact]
    public void Bulletin_is_spoken_in_parts_of_whole_sentences()
    {
        var text = "Het nieuws van 19 uur. " + string.Join(" ", Enumerable.Range(1, 6).Select(i => $"Bericht {i} heeft hier precies tien woorden in de zin staan."))
                   + " Dat was het nieuws.";
        var parts = BreakRenderer.BulletinParts(text, 35);
        Assert.Equal(text, string.Join(" ", parts));
        Assert.All(parts, p => Assert.True(p.Split(' ').Length <= 35));
        Assert.All(parts, p => Assert.EndsWith(".", p));
        Assert.Equal(3, parts.Count);
        Assert.Single(BreakRenderer.BulletinParts("Eén korte zin. En nog een.", 35));
        Assert.Equal(["Een zin van tien woorden die langer is dan het maximum.", "Kort."],
            BreakRenderer.BulletinParts("Een zin van tien woorden die langer is dan het maximum. Kort.", 5));
    }

    [Fact]
    public void Block_options_survive_storage_shape()
    {
        var o = new NewsBlockOptions { RegionalFeeds = "https://a.example/rss nonsense https://a.example/rss", Words = 5000, BedId = 4, Place = " Gieten " }.Checked();
        Assert.Equal("https://a.example/rss | nonsense", o.RegionalFeeds);   // words after an address are its name
        Assert.Equal(400, o.Words);
        Assert.Equal("Gieten", o.Place);
        Assert.Equal(o, NewsBlockOptions.FromJson(o.ToJson()));
        Assert.Equal(new NewsBlockOptions(), NewsBlockOptions.FromJson("{kapot"));
    }
}
