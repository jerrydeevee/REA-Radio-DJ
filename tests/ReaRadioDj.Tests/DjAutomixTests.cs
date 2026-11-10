using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

public sealed class DjAutomixTests
{
    [Fact]
    public void A_transition_starts_at_the_mix_point_or_early_enough_to_fit()
    {
        Assert.Equal(200, DjAutomix.MixStart(DjAutomix.Fade, 0, 200, 210, 8, 1, 0, 0), 6);
        Assert.Equal(190, DjAutomix.MixStart(DjAutomix.Fade, 0, 200, 210, 20, 1, 0, 0), 6);  // 20 s does not fit after 200
        Assert.Equal(202, DjAutomix.MixStart(DjAutomix.Fade, 1, 0, 210, 8, 1, 0, 0), 6);     // no mix point: before the end
    }

    [Fact]
    public void The_beat_types_start_on_the_next_beat_of_the_outgoing_track()
    {
        Assert.Equal(200.6, DjAutomix.MixStart(DjAutomix.Beat, 0, 200.3, 210, 8, 1, 0.1, 0.5), 6);
        Assert.Equal(200.6, DjAutomix.MixStart(DjAutomix.Cut, 0, 200.3, 210, 0, 1, 0.1, 0.5), 6);
        // Playing 10 % faster: 10 real seconds take 11 seconds of the file.
        Assert.Equal(199.1, DjAutomix.MixStart(DjAutomix.Beat, 0, 205, 210, 10, 1.1, 0.1, 0.5), 6);
    }

    [Fact]
    public void A_transition_lasts_its_beats_at_the_tempo_you_hear()
    {
        Assert.Equal(8, DjAutomix.Seconds(DjAutomix.Beat, 16, 120), 6);
        Assert.Equal(0, DjAutomix.Seconds(DjAutomix.Cut, 16, 120), 6);
        Assert.Equal(8, DjAutomix.Seconds(DjAutomix.Fade, 16, 0), 6);   // no tempo: as at 120 BPM
        Assert.Equal(40, DjAutomix.Seconds(DjAutomix.Fade, 32, 30), 6); // never longer than 40 s
    }

    [Fact]
    public void The_crossfader_moves_away_from_the_outgoing_side_and_the_tempo_glides_back()
    {
        Assert.Equal(0.25, DjAutomix.CrossfaderAt(0.25, outIsLeft: true), 6);
        Assert.Equal(0.75, DjAutomix.CrossfaderAt(0.25, outIsLeft: false), 6);
        Assert.Equal(1, DjAutomix.CrossfaderAt(3, true), 6);
        Assert.Equal(4, DjAutomix.GlideTempo(4, 0), 6);
        Assert.Equal(2, DjAutomix.GlideTempo(4, DjAutomix.GlideSeconds / 2), 6);
        Assert.Equal(0, DjAutomix.GlideTempo(4, 99), 6);
    }

    [Fact]
    public void A_tempo_transition_matches_close_tempos_only()
    {
        Assert.Equal(1.694915, TransitionRules.TempoMatch(120, 118)!.Value, 5);
        Assert.Equal(0, TransitionRules.TempoMatch(128, 64)!.Value, 6);  // double tempo
        Assert.Null(TransitionRules.TempoMatch(140, 120));                // 16.7 %: too far
        Assert.Null(TransitionRules.TempoMatch(null, 120));
        Assert.Equal(10.1, TransitionRules.NextBeat(10.0, 0.1, 0.5), 6);
        Assert.Equal(10.1, TransitionRules.NextBeat(10.1, 0.1, 0.5), 6);
        var rule = new TransitionRule("song", "song", "tempo");
        Assert.Equal((202d, 8d), TransitionRules.Apply(rule, 0, 210, 200, 3));
        Assert.Single(TransitionRules.Clean([rule]));
    }
}
