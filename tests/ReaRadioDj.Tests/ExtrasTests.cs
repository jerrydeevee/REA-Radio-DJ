using System.Xml.Linq;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Remote;

namespace ReaRadioDj.Tests;

/// <summary>Uitzending gemist (podcast), placeholders and mail settings.</summary>
public sealed class ExtrasTests
{
    private static PodcastProgram Morning => new("p1", "Ochtendshow", true, 0b001_1111, "07:00", "09:00");

    [Fact]
    public void A_broadcast_is_published_once_it_has_ended()
    {
        var monday = new DateTime(2026, 11, 16);
        Assert.Empty(Morning.EndedBetween(monday.AddHours(8), monday.AddHours(8).AddMinutes(59)));
        var ended = Morning.EndedBetween(monday.AddHours(8), monday.AddHours(9)).Single();
        Assert.Equal((monday.AddHours(7), monday.AddHours(9)), ended);
        Assert.Empty(Morning.EndedBetween(monday.AddDays(5), monday.AddDays(6).AddHours(10)));   // weekend
    }

    [Fact]
    public void A_night_programme_runs_past_midnight()
    {
        var p = Morning with { Start = "23:00", End = "01:00", Days = 0b111_1111 };
        Assert.Equal(TimeSpan.FromHours(2), p.Length);
        var night = new DateTime(2026, 11, 16, 23, 0, 0);
        Assert.Equal((night, night.AddHours(2)), p.EndedBetween(night, night.AddHours(2)).Single());
    }

    [Fact]
    public void The_feed_is_valid_rss_with_the_newest_episode_first()
    {
        var p = Morning with { Name = "Ochtend & zo <live>" };
        var rss = PodcastFeed.Rss(p, "Radio Test", [
            new PodcastEpisode("2026-11-16-0700 a.mp3", new DateTime(2026, 11, 16, 7, 0, 0), 7200, 1000),
            new PodcastEpisode("2026-11-17-0700 a.mp3", new DateTime(2026, 11, 17, 7, 0, 0), 7190, 2000),
        ], "https://zender.nl/podcast", DateTime.Now);
        var doc = XDocument.Parse(rss);
        var items = doc.Descendants("item").ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("https://zender.nl/podcast/2026-11-17-0700%20a.mp3", items[0].Element("enclosure")!.Attribute("url")!.Value);
        Assert.Equal("Ochtend & zo <live>", doc.Descendants("channel").Single().Element("title")!.Value);
    }

    [Theory]
    [InlineData("", "07:00", "09:00", "")]
    [InlineData("Show", "7", "09:00", "")]
    [InlineData("Show", "07:00", "07:00", "")]
    [InlineData("Show", "07:00", "12:00", "")]
    [InlineData("Show", "07:00", "09:00", "zender.nl/podcast")]
    public void A_programme_that_cannot_be_published_says_why(string name, string start, string end, string url) =>
        Assert.NotNull(new PodcastProgram("x", name, true, 1, start, end, PublicUrl: url).Problem());

    [Fact]
    public void A_placeholder_warns_a_day_before_and_once_more_when_late()
    {
        var due = new DateTime(2026, 11, 20, 12, 0, 0);
        var p = new Placeholder(1, "Bakker", "Kerst", @"C:\x.mp3", 30, due, "", 0, false, DateTime.Now);
        var day = PlaceholderStore.WarnBefore;
        Assert.Equal(0, p.WarningDue(due.AddDays(-2), day));
        Assert.Equal(1, p.WarningDue(due.AddHours(-3), day));
        Assert.Equal(0, (p with { Warned = 1 }).WarningDue(due.AddHours(-1), day));
        Assert.Equal(2, (p with { Warned = 1 }).WarningDue(due.AddMinutes(1), day));
        Assert.Equal(2, p.WarningDue(due.AddMinutes(1), day));
        Assert.Equal(0, (p with { Warned = 2 }).WarningDue(due.AddDays(1), day));
        Assert.Equal(0, (p with { Arrived = true }).WarningDue(due.AddDays(1), day));
    }

    [Fact]
    public void Placeholder_file_name_is_safe() =>
        Assert.Equal("Bakker_Jansen - Kerst 30_.mp3", PlaceholderStore.FileName("Bakker/Jansen", "Kerst 30\""));

    [Fact]
    public void Mail_settings_say_what_is_missing()
    {
        var ok = new MailSettings { Host = "smtp.example.org", From = "studio@example.org", To = "a@example.org, b@example.org" };
        Assert.Null(ok.Problem());
        Assert.Equal(2, ok.Recipients.Count);
        Assert.NotNull((ok with { Host = "" }).Problem());
        Assert.NotNull((ok with { Port = 465 }).Problem());
        Assert.NotNull((ok with { To = "geen adres" }).Problem());
        Assert.NotNull((ok with { From = "" }).Problem());
    }
}
