using ReaRadioDj.Core;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;

namespace ReaRadioDj.Tests;

/// <summary>The AI presenter speaks the on-air language (Presenter.Language), apart from the studio's language.</summary>
public sealed class OnAirLanguageTests
{
    [Theory]
    [InlineData("en", 15, 15, "quarter past three")]
    [InlineData("en", 9, 40, "twenty to ten")]
    [InlineData("en", 12, 0, "twelve o'clock")]
    [InlineData("de", 15, 30, "halb vier")]
    [InlineData("de", 13, 0, "ein Uhr")]
    [InlineData("de", 8, 25, "fünf vor halb neun")]
    [InlineData("es", 13, 15, "la una y cuarto")]
    [InlineData("es", 9, 45, "las diez menos cuarto")]
    [InlineData("nl", 15, 20, "tien voor half vier")]
    public void Time_is_said_the_way_radio_says_it(string code, int hour, int minute, string expected) =>
        Assert.Equal(expected, OnAir.SpokenTime(new DateTime(2026, 11, 17, hour, minute, 0), code));

    [Fact]
    public async Task Template_break_is_in_the_on_air_language()
    {
        using var _ = OnAir.Speak("en");
        var song = new MediaItem { Path = "x.mp3", Kind = MediaKind.Song, Title = "Lights On", Artist = "The Cyclists" };
        var c = new BreakContext("Radio Test", "Sam", new DateTime(2026, 11, 17, 16, 15, 0), null, song);
        var text = await new TemplateScriptWriter(new Random(1)).WriteAsync(c, CancellationToken.None);
        Assert.Contains("Lights On by The Cyclists", text);
        Assert.DoesNotContain(" van ", text);
    }

    [Fact]
    public void Prompt_asks_for_the_on_air_language()
    {
        using var _ = OnAir.Speak("de");
        var c = new BreakContext("Radio Test", "Sam", new DateTime(2026, 11, 17, 16, 15, 0), null, null);
        var (system, user) = OpenAiScriptWriter.Prompt(new PresenterOptions(), c);
        Assert.Contains("Duits", system);
        Assert.Contains("Viertel nach vier", user);
        Assert.DoesNotContain("Kustband", system);   // the Dutch examples make way for German ones
    }

    [Fact]
    public void News_and_weather_words_around_the_bulletin_follow_the_language()
    {
        using var _ = OnAir.Speak("es");
        var at = new DateTime(2026, 11, 17, 9, 0, 0);
        Assert.StartsWith("El tiempo, para Radio Test.", NewsWriter.Wrap("weer", at, "Soleado.", "Radio Test"));
        Assert.EndsWith("Esas fueron las noticias.", NewsWriter.Wrap("nieuws", at, "Nada.", "Radio Test"));
        var f = new WeatherForecast("Gieten", 8, "bewolkt", 9, 6, 20, 4, "zuidwesten", "regen", 6, 11, 80);
        var plain = NewsWriter.PlainWeather(f);
        Assert.Contains("8 grados", plain);
        Assert.Contains("nublado", plain);
        Assert.Contains("suroeste", plain);
    }

    [Fact]
    public void Dutch_stays_as_it_was() =>
        Assert.Equal("kwart over vier", TemplateScriptWriter.SpokenTime(new DateTime(2026, 11, 17, 16, 15, 0)));
}
