using ReaRadioDj.Contracts.Localization;

namespace ReaRadioDj.Tests;

/// <summary>Texts from the engine (always Dutch, often with values filled in) shown in the studio's language.</summary>
[Collection("Language")]
public sealed class TranslationTests : IDisposable
{
    public TranslationTests() => Tr.Language = "en";

    public void Dispose() => Tr.Language = "nl";

    [Fact]
    public void ExactTextIsTranslated() => Assert.Equal("Automation started", Tr.E("Automatisering gestart"));

    [Fact]
    public void FilledInValuesAreKept()
    {
        Assert.Equal("silence at the start (6 s)", Tr.E("stilte aan het begin (6 s)"));
        Assert.Equal("low bitrate (105 kbps)", Tr.E("lage bitrate (105 kbps)"));
        Assert.Equal("25578 songs, 34 jingles", Tr.E("25578 nummers, 34 jingles"));
    }

    [Fact]
    public void GluedPiecesAreTranslatedOneByOne() =>
        Assert.Equal("25578 songs, 34 jingles, 3 recognised", Tr.E("25578 nummers, 34 jingles, 3 herkend"));

    [Fact]
    public void ValuesThatAreKnownTextsAreTranslatedToo() =>
        Assert.Equal("VoIP 1: call ended (other side hung up)", Tr.E("VoIP 1: gesprek afgelopen (overkant hing op)"));

    [Fact]
    public void PrefixTextTakesTheRest() => Assert.Equal("Recording failed: disk full", Tr.E("Opnemen lukte niet: disk full"));

    [Fact]
    public void UnknownTextStaysAsItIs()
    {
        Assert.Equal("Mijn eigen klok", Tr.E("Mijn eigen klok"));
        Assert.Equal("", Tr.E(null));
    }

    [Fact]
    public void DutchNeedsNoTranslation()
    {
        Tr.Language = "nl";
        Assert.Equal("stilte aan het begin (6 s)", Tr.E("stilte aan het begin (6 s)"));
    }

    [Fact]
    public void EveryTranslationKeepsItsPlaceholders()
    {
        var holes = new System.Text.RegularExpressions.Regex(@"\{(\d+)");
        foreach (var language in new[] { "en", "de", "es" })
            foreach (var (nl, translated) in Tr.Table(language))
            {
                var want = holes.Matches(nl).Select(m => m.Groups[1].Value).Distinct().Order();
                var have = holes.Matches(translated).Select(m => m.Groups[1].Value).Distinct().Order();
                Assert.True(want.SequenceEqual(have), $"{language}: \"{nl}\" → \"{translated}\"");
            }
    }
}
