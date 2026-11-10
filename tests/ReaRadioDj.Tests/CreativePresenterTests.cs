using ReaRadioDj.Core;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;
using Xunit;

namespace ReaRadioDj.Tests;

public class CreativePresenterTests
{
    private static PresenterOptions Options(Action<CreativeOptions>? set = null)
    {
        var o = new PresenterOptions { Creative = { Enabled = true, Chance = 100, UseBuiltIn = false, UseOwnThemes = true } };
        set?.Invoke(o.Creative);
        return o;
    }

    private static CreativePicker Picker(PresenterOptions o, int seed = 1) => new(new HttpClient(), o, random: new Random(seed));

    [Fact]
    public void A_theme_line_can_fix_its_tone()
    {
        Assert.Equal(("grappig", "fietsen met tegenwind"), CreativePicker.ParseLine("Grappig: fietsen met tegenwind"));
        Assert.Equal(("randje", "de buurtapp"), CreativePicker.ParseLine("op het randje: de buurtapp"));
        Assert.Equal((null, "tijd: een uur extra"), CreativePicker.ParseLine("tijd: een uur extra"));   // not a tone
        Assert.Equal((null, "zomaar een onderwerp"), CreativePicker.ParseLine("  zomaar een onderwerp "));
    }

    [Fact]
    public void Random_tone_follows_the_weights_and_zero_means_never()
    {
        var o = Options(c => { c.Serious = 0; c.Funny = 3; c.Light = 1; c.Edgy = 0; });
        var picker = Picker(o);
        var counts = Enumerable.Range(0, 4000).Select(_ => picker.ToneFor(null)).GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        Assert.False(counts.ContainsKey(CreativeTones.Serious));
        Assert.False(counts.ContainsKey(CreativeTones.Edgy));
        Assert.InRange(counts[CreativeTones.Funny] / (double)counts[CreativeTones.Light], 2.5, 3.5);

        o.Creative.Tone = CreativeTones.Serious;   // a fixed tone wins over the weights
        Assert.Equal(CreativeTones.Serious, picker.ToneFor(null));
        Assert.Equal(CreativeTones.Edgy, picker.ToneFor(CreativeTones.Edgy));   // and a trial's own choice wins over that
    }

    [Fact]
    public async Task Off_or_zero_chance_gives_normal_breaks()
    {
        var o = Options(c => c.Themes = ["iets"]);
        o.Creative.Enabled = false;
        Assert.Null(await Picker(o).PickAsync(DateTime.Now, CancellationToken.None));
        o.Creative.Enabled = true;
        o.Creative.Chance = 0;
        Assert.Null(await Picker(o).PickAsync(DateTime.Now, CancellationToken.None));
        Assert.NotNull(await Picker(o).PickAsync(DateTime.Now, CancellationToken.None, force: true));   // a trial always works
    }

    [Fact]
    public async Task A_theme_is_not_repeated_until_all_others_had_a_turn()
    {
        var o = Options(c => { c.Themes = ["een", "twee", "grappig: drie"]; c.RepeatHours = 12; });
        var picker = Picker(o);
        var t = new DateTime(2026, 11, 10, 9, 0, 0);
        var seen = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var idea = (await picker.PickAsync(t.AddMinutes(i * 20), CancellationToken.None))!;
            seen.Add(idea.Theme);
            picker.Used(idea, t.AddMinutes(i * 20));
            if (idea.Theme == "drie") Assert.Equal(CreativeTones.Funny, idea.Tone);
            Assert.Equal("eigen", idea.Source);
        }
        Assert.Equal(["drie", "een", "twee"], seen.Order());
        Assert.NotNull(await picker.PickAsync(t.AddHours(1), CancellationToken.None));   // all used: starts again
    }

    [Fact]
    public void Feed_themes_come_from_rss_titles_or_plain_lines()
    {
        var rss = CreativePicker.ParseThemes("""
            <rss><channel><title>Zender</title>
              <item><title>Week van de vrijwilliger</title></item>
              <item><title>Open dag bij de molen</title></item>
            </channel></rss>
            """, "www.zender.nl").ToList();
        Assert.Equal(["Week van de vrijwilliger", "Open dag bij de molen"], rss.Select(r => r.Theme));

        var text = CreativePicker.ParseThemes("# thema's van deze week\nde eerste nachtvorst\n\nkoken met pompoen\nx\n", "zender.nl").ToList();
        Assert.Equal(["de eerste nachtvorst", "koken met pompoen"], text.Select(t => t.Theme));
        Assert.All(text, t => Assert.Equal("zender.nl", t.Source));
    }

    [Fact]
    public async Task Feed_themes_are_fetched_and_mixed_with_own_themes()
    {
        var o = Options(c => { c.Themes = ["eigen onderwerp"]; c.UseFeeds = true; c.Feeds = ["https://zender.example/themas.txt"]; });
        string? agent = null;
        var http = new HttpClient(new FakeHandler(r =>
        {
            agent = r.Headers.UserAgent.ToString();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("sinterklaas in het dorp\nde kerstmarkt") };
        }));
        var picker = new CreativePicker(http, o, random: new Random(3));
        var pool = await picker.PoolAsync(DateTime.Now, CancellationToken.None);
        Assert.Contains(pool, p => p.Theme == "eigen onderwerp" && p.Source == "eigen");
        Assert.Contains(pool, p => p.Theme == "de kerstmarkt" && p.Source == "zender.example");
        Assert.StartsWith("REA-Radio-DJ/0.1", agent);
    }

    [Fact]
    public async Task Headlines_from_a_feed_are_never_joked_about()
    {
        var o = Options(c => { c.UseOwnThemes = false; c.UseFeeds = true; c.Feeds = ["https://zender.example/feed.xml"]; c.Funny = 5; c.Edgy = 5; });
        var http = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""
                <rss><channel><title>Zender - nieuws</title>
                  <item><title>Ravage in Assen na enorme brand</title></item>
                  <item><title>Kroketten zijn het medicijn tegen polarisatie</title></item>
                </channel></rss>
                """),
        }));
        var picker = new CreativePicker(http, o, random: new Random(5));
        for (int i = 0; i < 40; i++)
        {
            var idea = (await picker.PickAsync(DateTime.Now, CancellationToken.None, tone: CreativeTones.Edgy))!;
            Assert.True(idea.Headline);
            if (idea.Theme.Contains("brand")) Assert.Equal(CreativeTones.Serious, idea.Tone);
            else Assert.Contains(idea.Tone, new[] { CreativeTones.Serious, CreativeTones.Light });
        }
        Assert.True(CreativeTones.IsSad("Auto slaat over de kop bij Tynaarlo"));
        Assert.False(CreativeTones.IsSad("Konijn van Olland krijgt nieuw verblijf"));

        var c = new BreakContext("Radio Aa en Hunze", "Sam", DateTime.Now, null, null,
            Creative: new CreativeIdea("Konijn van Olland krijgt nieuw verblijf", CreativeTones.Light, "zender", Headline: true));
        Assert.Contains("echte nieuwskop", OpenAiScriptWriter.Prompt(new PresenterOptions(), c).System);
        Assert.DoesNotContain("echte nieuwskop", OpenAiScriptWriter.Prompt(new PresenterOptions(), c with { Creative = c.Creative! with { Headline = false } }).System);
    }

    [Fact]
    public void Built_in_themes_fit_the_moment()
    {
        var winterMonday = CreativePicker.BuiltIn(new DateTime(2026, 1, 5, 7, 30, 0));
        Assert.Contains(winterMonday, t => t.Contains("maandag"));
        Assert.Contains(winterMonday, t => t.Contains("winter"));
        Assert.Contains(winterMonday, t => t.Contains("ontbijt"));
        var summerNight = CreativePicker.BuiltIn(new DateTime(2026, 7, 4, 23, 0, 0));
        Assert.Contains(summerNight, t => t.Contains("zomer"));
        Assert.DoesNotContain(summerNight, t => t.Contains("winter"));
    }

    [Fact]
    public void Creative_prompt_carries_theme_tone_and_the_fixed_limits()
    {
        var o = new PresenterOptions();
        var next = new MediaItem { Path = "y", Kind = MediaKind.Song, Artist = "De Fietsers", Title = "Licht Aan" };
        var c = new BreakContext("Radio Aa en Hunze", "Sam", new DateTime(2026, 11, 10, 8, 15, 0), null, next,
            Creative: new CreativeIdea("de verloren sok", CreativeTones.Edgy, "ingebouwd"));
        var (system, user) = OpenAiScriptWriter.Prompt(o, c);
        Assert.Contains("tikkie op het randje", system);
        Assert.Contains("Niets kwetsends", system);
        Assert.Contains("Verzin geen nieuws", system);
        Assert.Contains("Hoogstens 70 woorden", system);
        Assert.Contains("Onderwerp: de verloren sok", user);
        Assert.Contains("Licht Aan van De Fietsers", user);
        Assert.Contains("dinsdag", user);

        var (plain, _) = OpenAiScriptWriter.Prompt(o, c with { Creative = null });
        Assert.DoesNotContain("Onderwerp", plain);
    }

    [Fact]
    public void Prompt_spells_out_title_and_artist_for_titles_with_brackets()
    {
        var riva = new MediaItem { Path = "r", Kind = MediaKind.Song, Artist = "Klingande", Title = "Riva (Restart The Game)" };
        Assert.Equal("Riva (Restart The Game) van Klingande [titel: Riva (Restart The Game) | artiest: Klingande]", OpenAiScriptWriter.Song(riva));
        var (_, user) = OpenAiScriptWriter.Prompt(new PresenterOptions(), new BreakContext("Zender", "Sam", DateTime.Now, null, riva));
        Assert.Contains("artiest: Klingande", user);
        Assert.Equal("Hier is Riva van Klingande.", ScriptSanitizer.Clean("Hier is Riva van Klingande [titel: Riva | artiest: Klingande].", 50));
    }

    [Fact]
    public void Higgs_tags_differ_per_tone()
    {
        var tags = CreativeTones.Keys.Select(CreativeTones.HiggsTags).ToList();
        Assert.All(tags, t => Assert.StartsWith("<|emotion:", t));
        Assert.Equal(tags.Count, tags.Distinct().Count());
    }
}
