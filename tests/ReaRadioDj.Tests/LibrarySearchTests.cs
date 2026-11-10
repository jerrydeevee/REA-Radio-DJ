using ReaRadioDj.Core;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using Xunit;

namespace ReaRadioDj.Tests;

public class LibrarySearchTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 14, 0, 0);

    private static MediaDetails D(long id, string artist, string title, string album = "", int? year = null, double dur = 200,
        int plays = 0, DateTime? added = null, DateTime? last = null, bool enabled = true, bool missing = false, bool manual = false,
        DateTime? until = null, MediaKind kind = MediaKind.Song, string cat = "Pop") =>
        new(new MediaItem { Id = id, Path = $@"C:\m\{id}.mp3", Artist = artist, Title = title, Duration = dur, Kind = kind, Category = cat },
            album, enabled, manual, null, until, plays, last, missing, year, added);

    private static readonly MediaDetails[] Lib =
    [
        D(1, "Beyoncé", "Halo", "I Am... Sasha Fierce", 2008, 261, plays: 12, last: Now.AddDays(-2)),
        D(2, "ABBA", "Dancing Queen", "Arrival", 1976, 231, plays: 40, last: Now.AddDays(-60)),
        D(3, "Queen", "Bohemian Rhapsody", "A Night at the Opera", 1975, 355, plays: 3),
        D(4, "Adele", "Hello", "25", 2015, 295, added: Now.AddDays(-3)),
        D(5, "AC/DC", "Back in Black", "Back in Black", 1980, 255, missing: true),
        D(6, "Bee Gees", "Night Fever", "Saturday Night Fever", 1977, 213, enabled: false),
        D(7, "Station", "Jingle kort", kind: MediaKind.Jingle, dur: 5, cat: "Jingles"),
        D(8, "Doe Maar", "De bom", "Lijf aan lijf", 1983, 199, until: Now.AddDays(-1), manual: true),
    ];

    private static long[] Ids(string q, Func<MediaDetails, string?>? block = null) =>
        LibrarySearch.Run(Lib, q, Now, block).Select(d => d.Media.Id).ToArray();

    [Theory]
    [InlineData("beyonce", 1)]          // without the accent
    [InlineData("BEYONCÉ halo", 1)]
    [InlineData("acdc", 5)]             // AC/DC without the slash
    [InlineData("ac dc", 5)]
    [InlineData("adelle", 4)]           // one typo
    [InlineData("bohemain", 3)]         // swapped letters
    [InlineData("rapsody", 3)]          // missing letter
    public void Finds_without_accents_and_with_small_typos(string q, long expected)
    {
        var ids = Ids(q);
        if (expected == 0) Assert.Empty(ids); else Assert.Equal(expected, ids[0]);
    }

    [Fact]
    public void Words_may_come_from_different_fields_and_best_match_is_first()
    {
        Assert.Equal([4], Ids("adele 2015"));
        Assert.Equal([3, 2], Ids("queen"));               // artist "Queen" before the title "Dancing Queen"
        Assert.Equal([6], Ids("\"night fever\""));
        Assert.DoesNotContain(6L, Ids("night -fever"));
    }

    [Fact]
    public void Short_filters_narrow_the_list()
    {
        Assert.Equal([2, 3, 6], Ids("jaar:197").Order().ToArray());
        Assert.Equal([5, 8], Ids("jaar:1980-1989").Order().ToArray());
        Assert.Equal([1, 4], Ids("jaar:>2000").Order().ToArray());
        Assert.Equal([2, 6, 7, 8], Ids("duur:<3:55").Order().ToArray());
        Assert.Equal([3], Ids("duur:>5:00"));
        Assert.Equal([2], Ids("artiest:abba"));
        Assert.Empty(Ids("titel:abba"));
        Assert.Equal([7], Ids("is:jingle"));
        Assert.Equal([2], Ids("gespeeld:>20"));
    }

    [Fact]
    public void Status_filters()
    {
        Assert.Equal([4], Ids("is:nieuw"));
        Assert.Equal([4, 5, 6, 7, 8], Ids("is:nooit").Order().ToArray());
        Assert.DoesNotContain(1L, Ids("is:lang"));
        Assert.Contains(2L, Ids("is:lang"));
        Assert.Equal([5], Ids("is:ontbreekt"));
        Assert.Equal([6], Ids("is:uit"));
        Assert.Equal([8], Ids("is:verlopen"));
        Assert.DoesNotContain(8L, Ids("is:zondercues"));
        Assert.Equal([5, 6, 8], Ids("is:geblokkeerd").Order().ToArray());
        Assert.Equal([1, 2, 3, 4, 7], Ids("is:mag").Order().ToArray());
        // combined with words
        Assert.Equal([3], Ids("is:mag jaar:1975 queen"));
    }

    [Fact]
    public void Unknown_filter_is_searched_as_a_word() => Assert.Empty(Ids("kleur:rood"));

    [Fact]
    public void Normalize_removes_accents_and_punctuation()
    {
        Assert.Equal("beyonce", LibrarySearch.Normalize("Beyoncé"));
        Assert.Equal("ac dc", LibrarySearch.Normalize("AC/DC"));
        Assert.Equal("dont stop me now", LibrarySearch.Normalize("Don't Stop  Me Now!"));
        Assert.Equal("strasse", LibrarySearch.Normalize("Straße"));
        Assert.Equal(1, LibrarySearch.Distance("bohemain", "bohemian"));
    }

    [Fact]
    public void Rotation_check_explains_why_a_song_may_not_play()
    {
        var recent = new[] { Lib[1].Media, Lib[0].Media }; // ABBA, then Beyoncé (newest)
        var check = new RotationCheck(recent, new RotationOptions { SongSeparation = 10, ArtistSeparation = 3 }, Now);
        Assert.StartsWith("zit in de laatste 10 nummers", check.Reason(Lib[0]));
        var otherAbba = D(9, "ABBA", "Waterloo");
        Assert.Equal("artiest zit in de laatste 3 nummers", check.Reason(otherAbba));
        Assert.Null(check.Reason(Lib[3]));
        Assert.Equal("bestand ontbreekt", check.Reason(Lib[4]));
        Assert.Equal("uitgezet", check.Reason(Lib[5]));
        Assert.StartsWith("verlopen op", check.Reason(Lib[7]));
        Assert.Null(check.Reason(Lib[6])); // jingles have no song separation
        // the separation window counts back from the newest song
        var narrow = new RotationCheck(recent, new RotationOptions { SongSeparation = 1, ArtistSeparation = 1 }, Now);
        Assert.Null(narrow.Reason(Lib[1]));
    }
}
