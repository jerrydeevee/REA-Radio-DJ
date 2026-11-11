using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Model;
using Xunit;

namespace ReaRadioDj.Tests;

public class TransitionRuleTests
{
    private static LogItem Item(LogItemType type, MediaKind kind, string category = "", long? ad = null) => new()
    {
        Type = type, AdPlayId = ad,
        Media = new MediaItem { Path = "x.wav", Kind = kind, Category = category, Title = "t" },
    };

    [Fact]
    public void The_top_rule_that_fits_wins_and_auto_means_the_measured_mix()
    {
        var rules = TransitionRules.Clean(
        [
            new("cat:Klassiek", "*", "overlap", 6),
            new("jingle", "song", "cut"),
            new("song", "song", "auto"),
            new("*", "*", "fade", 99),               // seconds kept within reason
            new("onzin", "*", "cut"),                // unknown side: dropped
            new("*", "*", "spin"),                   // unknown mode: dropped
            new("ad", "*", "cut", Enabled: false),
        ]);
        Assert.Equal(5, rules.Count);
        Assert.Equal(30, rules[3].Seconds);

        var song = Item(LogItemType.Song, MediaKind.Song, "Pop");
        var classic = Item(LogItemType.Song, MediaKind.Song, "klassiek");
        var jingle = Item(LogItemType.Jingle, MediaKind.Jingle, "Jingles");
        var ad = Item(LogItemType.Jingle, MediaKind.Jingle, "Reclame", ad: 5);

        Assert.Equal("overlap", TransitionRules.Find(rules, classic, jingle)!.Mode);    // category, any case
        Assert.Equal("cut", TransitionRules.Find(rules, jingle, song)!.Mode);
        Assert.Null(TransitionRules.Find(rules, song, song));                          // auto
        Assert.Equal("fade", TransitionRules.Find(rules, ad, song)!.Mode);              // ad rule is off; an ad is no jingle
        Assert.Equal("fade", TransitionRules.Find(rules, song, null)!.Mode);            // nothing next: only "*" fits
    }

    [Fact]
    public void A_rule_sets_the_mix_point_and_the_fade()
    {
        // item plays 0..200, measured mix 195, fade 2.5
        Assert.Equal((195.0, 2.5), TransitionRules.Apply(null, 0, 200, 195, 2.5));
        Assert.Equal((200.0, 0.0), TransitionRules.Apply(new("*", "*", "cut"), 0, 200, 195, 2.5));
        Assert.Equal((194.0, 6.0), TransitionRules.Apply(new("*", "*", "overlap", 6), 0, 200, 195, 2.5));
        Assert.Equal((0.0, 4.0), TransitionRules.Apply(new("*", "*", "overlap", 6), 0, 4, 3, 1));   // short item
        Assert.Equal((195.0, 5.0), TransitionRules.Apply(new("*", "*", "fade", 8), 0, 200, 195, 2.5)); // not past the end
    }
}
