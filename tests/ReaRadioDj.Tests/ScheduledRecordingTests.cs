using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

/// <summary>When a scheduled recording runs, and under which name it lands in the library.</summary>
public sealed class ScheduledRecordingTests
{
    private const int Weekdays = 0b001_1111;

    private static ScheduledRecording News(int days = Weekdays, string start = "12:00", int minutes = 5, bool replace = true) =>
        new("n1", "Regionaal nieuws", true, days, start, minutes, "strip:line1", replace);

    [Fact]
    public void Runs_from_its_start_for_its_length_on_its_days()
    {
        var monday = new DateTime(2026, 11, 16);
        var r = News();
        Assert.Null(r.Running(monday.AddHours(11).AddMinutes(59)));
        Assert.Equal(monday.AddHours(12), r.Running(monday.AddHours(12)));
        Assert.Equal(monday.AddHours(12), r.Running(monday.AddHours(12).AddMinutes(4)));
        Assert.Null(r.Running(monday.AddHours(12).AddMinutes(5)));
        Assert.Null(r.Running(monday.AddDays(5).AddHours(12).AddMinutes(1)));   // saturday
    }

    [Fact]
    public void A_recording_over_midnight_belongs_to_the_day_it_began()
    {
        var friday = new DateTime(2026, 11, 20);
        var r = News(days: 1 << 4, start: "23:30", minutes: 60);
        Assert.Equal(friday.AddHours(23).AddMinutes(30), r.Running(friday.AddDays(1).AddMinutes(10)));
        Assert.Null(r.Running(friday.AddDays(1).AddMinutes(31)));
    }

    [Fact]
    public void Next_start_skips_days_it_does_not_run()
    {
        var friday = new DateTime(2026, 11, 20, 13, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 23, 12, 0, 0), News().Next(friday));
        Assert.Null((News() with { Enabled = false }).Next(friday));
        Assert.Null(News(days: 0).Next(friday));
    }

    [Fact]
    public void Replace_keeps_one_file_name_otherwise_the_date_is_in_front()
    {
        var at = new DateTime(2026, 11, 16, 12, 0, 0);
        Assert.Equal("Regionaal nieuws.mp3", News().FileName(at));
        Assert.Equal("2026-11-16-1200 Regionaal nieuws.mp3", News(replace: false).FileName(at));
        Assert.Equal("a_b.mp3", (News() with { Name = "a/b" }).FileName(at));
    }

    [Fact]
    public void Checked_fills_an_id_and_tidies_the_time()
    {
        var r = new ScheduledRecording("", " Nieuws ", true, 0xFF, "9:05", 999, " https://x.nl/s.mp3 ").Checked();
        Assert.False(string.IsNullOrEmpty(r.Id));
        Assert.Equal("Nieuws", r.Name);
        Assert.Equal("09:05", r.Start);
        Assert.Equal(ScheduledRecording.MaxMinutes, r.Minutes);
        Assert.Equal(0b111_1111, r.Days);
        Assert.True(r.IsStream);
        Assert.Null(r.Problem());
    }

    [Theory]
    [InlineData("", "12:00", "strip:mic")]
    [InlineData("Nieuws", "12h", "strip:mic")]
    [InlineData("Nieuws", "12:00", "")]
    public void A_recording_that_cannot_run_says_why(string name, string start, string source) =>
        Assert.NotNull(new ScheduledRecording("x", name, true, Weekdays, start, 5, source).Problem());
}
