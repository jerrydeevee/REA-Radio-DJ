using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

/// <summary>Which pieces of which hour recordings make up a stretch of air time (no audio, no BASS).</summary>
public class RecordingCutterTests
{
    private static readonly DateTime Day = new(2026, 11, 14);

    private static RecordedFile Hour(int hour, double startOffset = 0, double seconds = 3600) =>
        new($"uitzending-2026-11-14-{hour:00}.mp3", Day.AddHours(hour).AddSeconds(startOffset), seconds);

    [Fact]
    public void A_stretch_inside_one_hour_is_one_piece()
    {
        var p = Assert.Single(RecordingCutter.Plan([Hour(9), Hour(10)], Day.AddHours(10).AddMinutes(12), Day.AddHours(10).AddMinutes(22)));
        Assert.Equal("uitzending-2026-11-14-10.mp3", p.Path);
        Assert.Equal(720, p.Offset);
        Assert.Equal(600, p.Seconds);
    }

    [Fact]
    public void A_stretch_over_the_hour_is_joined_and_the_overlap_taken_once()
    {
        // the 10 o'clock file runs on a second into the next hour; the 11 o'clock one starts on the hour
        var pieces = RecordingCutter.Plan([Hour(10, 0, 3601), Hour(11)], Day.AddHours(10).AddMinutes(55), Day.AddHours(11).AddMinutes(5));
        Assert.Equal(2, pieces.Count);
        Assert.Equal(3300, pieces[0].Offset);
        Assert.Equal(301, pieces[0].Seconds);
        Assert.Equal(1, pieces[1].Offset);
        Assert.Equal(299, pieces[1].Seconds);
        Assert.Equal(600, pieces.Sum(p => p.Seconds));
    }

    [Fact]
    public void Time_without_a_recording_is_left_out()
    {
        // the engine started at 10:20
        var p = Assert.Single(RecordingCutter.Plan([Hour(10, 1200, 2400)], Day.AddHours(10).AddMinutes(15), Day.AddHours(10).AddMinutes(25)));
        Assert.Equal(0, p.Offset);
        Assert.Equal(300, p.Seconds);
        Assert.Empty(RecordingCutter.Plan([Hour(10)], Day.AddHours(12), Day.AddHours(12).AddMinutes(5)));
    }

    [Theory]
    [InlineData("uitzending-2026-11-14-09.mp3", 9)]
    [InlineData("uitzending-2026-11-14-23-2.mp3", 23)]
    [InlineData("microfoon-2026-11-14-090000.mp3", null)]
    [InlineData("spraak-2026-11-14-09.mp3", null)]
    public void The_hour_comes_from_the_name_of_a_program_recording(string name, int? hour)
    {
        Assert.Equal(hour is int h ? Day.AddHours(h) : null, RecordingCutter.HourOf(name));
    }
}
