using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Model;
using Xunit;

namespace ReaRadioDj.Tests;

/// <summary>Longest play time per kind of item, and short items that play out without a fade.</summary>
public class PlayLimitTests
{
    private static LogItem Item(LogItemType type, MediaKind kind, string category = "", long? ad = null) => new()
    {
        Type = type, AdPlayId = ad,
        Media = new MediaItem { Path = "x.wav", Kind = kind, Category = category, Title = "t" },
    };

    [Fact]
    public void Limits_are_cleaned_and_the_top_one_that_fits_counts()
    {
        var limits = TransitionRules.CleanLimits(
        [
            new("ad", NoFadeUnder: 60),
            new("jingle", MaxSeconds: 30),
            new("cat:Pop", MaxSeconds: 9999),       // kept within reason
            new("onzin", MaxSeconds: 10),           // unknown side: dropped
            new("song"),                            // no effect: dropped
            new("*", MaxSeconds: 5, Enabled: false),
        ]);
        Assert.Equal(4, limits.Count);
        Assert.Equal(3600, limits[2].MaxSeconds);

        Assert.Equal(60, TransitionRules.FindLimit(limits, Item(LogItemType.Jingle, MediaKind.Jingle, "Reclame", ad: 3))!.NoFadeUnder);
        Assert.Equal(30, TransitionRules.FindLimit(limits, Item(LogItemType.Jingle, MediaKind.Jingle))!.MaxSeconds);
        Assert.NotNull(TransitionRules.FindLimit(limits, Item(LogItemType.Song, MediaKind.Song, "pop")));
        Assert.Null(TransitionRules.FindLimit(limits, Item(LogItemType.Song, MediaKind.Song, "Rock")));   // "*" is off
    }

    [Fact]
    public void A_long_item_stops_at_its_limit_with_a_fade()
    {
        var limit = new PlayLimit("jingle", MaxSeconds: 30);
        var (mix, fade, drop) = TransitionRules.Limit(limit, from: 1, to: 61, mixAt: 58, fade: 1);
        Assert.Equal(31, mix);
        Assert.Equal(TransitionRules.LimitFade, fade);
        Assert.True(drop);
    }

    [Fact]
    public void An_item_within_its_limit_keeps_its_mix()
    {
        var limit = new PlayLimit("jingle", MaxSeconds: 30);
        Assert.Equal((20.0, 1.5, false), TransitionRules.Limit(limit, 0, 22, 20, 1.5));
        Assert.Equal((20.0, 1.5, false), TransitionRules.Limit(null, 0, 22, 20, 1.5));
    }

    [Fact]
    public void A_short_item_plays_to_its_end_without_a_fade()
    {
        var limit = new PlayLimit("ad", NoFadeUnder: 60);
        Assert.Equal((45.5, 0.0, true), TransitionRules.Limit(limit, 0.5, 45.5, 43, 2));
        // longer than the limit: the normal mix
        Assert.Equal((88.0, 2.0, false), TransitionRules.Limit(limit, 0, 90, 88, 2));
    }

    [Fact]
    public void The_limit_also_holds_after_a_cut_rule()
    {
        var rule = new TransitionRule("jingle", "*", "cut");
        var (mix, fade) = TransitionRules.Apply(rule, 0, 50, 48, 1);
        Assert.Equal((50.0, 0.0), (mix, fade));
        var limited = TransitionRules.Limit(new PlayLimit("jingle", MaxSeconds: 20), 0, 50, mix, fade);
        Assert.Equal(20, limited.MixAt);
        Assert.Equal(TransitionRules.LimitFade, limited.Fade);
    }
}
