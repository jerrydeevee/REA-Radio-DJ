using System.Diagnostics;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace ReaRadioDj.Tests;

public class LibraryQualityTests(ITestOutputHelper output)
{
    private static MediaDetails Item(long id, string artist, string title, double duration = 200, double? lufs = -14,
        double cueIn = 0.2, double? cueOut = null, string path = "", long size = 0, double? clip = 0, string category = "Pop",
        int? year = null, int? rating = null, int? energy = null, string mood = "", string language = "") =>
        new(new MediaItem
        {
            Id = id, Path = path.Length > 0 ? path : $@"C:\m\{artist} - {title}.wav", Kind = MediaKind.Song, Artist = artist, Title = title,
            Category = category, Duration = duration, Lufs = lufs,
            Cues = new CueSet { CueIn = cueIn, Mix = (cueOut ?? duration - 0.5) - 5, CueOut = cueOut ?? duration - 0.5 },
        }, "", true, false, null, null, 0, null, false, year, null, "", null, null, null, null, size, clip, language, mood, rating, energy);

    [Fact]
    public void Soft_loud_clipping_silence_and_low_bitrate_are_reported()
    {
        Assert.Empty(LibraryQuality.Check(Item(1, "A", "Fine")));
        Assert.Contains(LibraryQuality.Check(Item(2, "A", "Soft", lufs: -27)), p => p.StartsWith("te zacht"));
        Assert.Contains(LibraryQuality.Check(Item(3, "A", "Loud", lufs: -4)), p => p.StartsWith("te hard"));
        Assert.Contains(LibraryQuality.Check(Item(4, "A", "Clip", clip: 45)), p => p.StartsWith("clipping"));
        Assert.Empty(LibraryQuality.Check(Item(9, "A", "Limited", clip: 2)));
        Assert.Contains(LibraryQuality.Check(Item(5, "A", "Quiet start", cueIn: 4.5)), p => p == "stilte aan het begin (4,5 s)" || p == "stilte aan het begin (4.5 s)");
        Assert.Contains(LibraryQuality.Check(Item(6, "A", "Quiet end", cueOut: 190)), p => p.StartsWith("stilte aan het eind"));
        // 200 s at 96 kbps = 2.4 MB
        Assert.Contains(LibraryQuality.Check(Item(7, "A", "Low", path: @"C:\m\low.mp3", size: 2_400_000)), p => p == "lage bitrate (96 kbps)");
        Assert.Empty(LibraryQuality.Check(Item(8, "A", "Wav", path: @"C:\m\x.wav", size: 2_400_000))); // lossless: no bitrate check
    }

    [Fact]
    public void A_double_track_is_found_also_as_radio_edit_but_not_a_different_length()
    {
        var all = new List<MediaDetails>
        {
            Item(1, "Beyoncé", "Halo", 261, category: "Pop"),
            Item(2, "Beyonce", "Halo (Radio Edit)", 262.5, category: "Gold"),
            Item(3, "Beyoncé", "Halo", 330, category: "Extended"), // a long version is not a double
            Item(4, "Adele", "Hello", 295),
        };
        var issues = LibraryQuality.All(all);
        Assert.Contains(issues[1], p => p.StartsWith("dubbel") && p.Contains("Gold"));
        Assert.Contains(issues[2], p => p.StartsWith("dubbel"));
        Assert.False(issues.ContainsKey(3));
        Assert.False(issues.ContainsKey(4));
    }

    [Fact]
    public void New_tag_fields_and_quality_flags_can_be_searched()
    {
        var items = new List<MediaDetails>
        {
            Item(1, "Doe Maar", "Pa", rating: 5, energy: 4, mood: "vrolijk", language: "nl") with { Issues = ["stilte aan het begin (4 s)"] },
            Item(2, "Queen", "Bohemian Rhapsody", rating: 3, energy: 2, mood: "episch", language: "en"),
        };
        var now = DateTime.Now;
        Assert.Equal(1, LibrarySearch.Run(items, "taal:nl", now).Single().Media.Id);
        Assert.Equal(1, LibrarySearch.Run(items, "waardering:>=4", now).Single().Media.Id);
        Assert.Equal(2, LibrarySearch.Run(items, "energie:1-2", now).Single().Media.Id);
        Assert.Equal(1, LibrarySearch.Run(items, "stemming:vrolijk", now).Single().Media.Id);
        Assert.Equal(1, LibrarySearch.Run(items, "vrolijk", now).Single().Media.Id);
        Assert.Equal(1, LibrarySearch.Run(items, "is:aandacht", now).Single().Media.Id);
        Assert.Equal(1, LibrarySearch.Run(items, "is:stil", now).Single().Media.Id);
        Assert.Empty(LibrarySearch.Run(items, "is:dubbel", now));
    }

    [Fact]
    public void Search_over_100000_tracks_stays_quick()
    {
        var r = new Random(7);
        string[] words = ["love", "night", "fever", "dance", "heart", "summer", "rain", "fire", "dream", "light", "blue", "road", "star", "time", "girl"];
        string Word() => words[r.Next(words.Length)] + (r.Next(3) == 0 ? r.Next(100).ToString() : "");
        var items = new List<MediaDetails>(100_000);
        for (int i = 0; i < 100_000; i++)
            items.Add(Item(i, $"Artist {Word()} {i % 5000}", $"{Word()} {Word()} {Word()}", 150 + r.Next(200), year: 1960 + r.Next(65)));
        items.Add(Item(100_001, "David Guetta", "Titanium", 245));
        LibrarySearch.Run(items, "warm up", DateTime.Now); // the first search makes the search fields once (normally in the background after a reload)

        foreach (var q in new[] { "david guetta", "Daivd Guetta", "summer love", "jaar:198 duur:<3:30", "-night fire", "titan" })
        {
            var sw = Stopwatch.StartNew();
            var found = LibrarySearch.Run(items, q, DateTime.Now, limit: 1000);
            sw.Stop();
            output.WriteLine($"{q,-24} {sw.ElapsedMilliseconds,4} ms  {found.Count} gevonden");
            Assert.True(sw.ElapsedMilliseconds < 250, $"{q}: {sw.ElapsedMilliseconds} ms");
        }
        Assert.Equal("Titanium", LibrarySearch.Run(items, "Daivd Guetta", DateTime.Now).First().Media.Title);
    }
}

[Collection("bass")]
public class LibraryListsAndUndoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-liblists-" + Guid.NewGuid().ToString("N"));

    public LibraryListsAndUndoTests()
    {
        Directory.CreateDirectory(_dir);
        if (ReaRadioDj.Core.Audio.BassRuntime.IsAvailable) ReaRadioDj.Core.Audio.BassRuntime.Initialize(0);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>A WAV whose loudness changes every second; <paramref name="silence"/> seconds of silence at the start.</summary>
    private static void Write(string path, int seed, int seconds = 40, double silence = 0, int rate = 22050, double gain = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var r = new Random(seed);
        var levels = Enumerable.Range(0, seconds).Select(_ => 0.05 + r.NextDouble() * 0.85).ToArray();
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        int frames = seconds * rate, data = frames * 4;
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        for (int i = 0; i < frames; i++)
        {
            short v = i < silence * rate ? (short)0
                : (short)Math.Clamp(Math.Sin(2 * Math.PI * 330 * i / rate) * levels[i / rate] * 30000 * gain, -32768, 32767);
            w.Write(v); w.Write(v);
        }
    }

    private MediaLibrary Library()
    {
        var options = new ReaRadioDjOptions
        {
            DataFolder = Path.Combine(_dir, "data"), MusicFolder = Path.Combine(_dir, "music"), JingleFolder = Path.Combine(_dir, "jingles"),
            WatchLibrary = false, NightlyScanHour = -1,
        };
        Directory.CreateDirectory(options.MusicFolder);
        Directory.CreateDirectory(options.JingleFolder);
        return new MediaLibrary(options, new StationDb(Path.Combine(options.DataFolder, "station.db")));
    }

    [BassFact]
    public async Task Quality_check_reports_a_silent_start_and_a_double_track()
    {
        var library = Library();
        string music = Path.Combine(_dir, "music");
        Write(Path.Combine(music, "Pop", "A - Fine.wav"), seed: 1);
        Write(Path.Combine(music, "Pop", "B - Late start.wav"), seed: 2, silence: 5);
        Write(Path.Combine(music, "Pop", "C - Twice.wav"), seed: 3);
        Write(Path.Combine(music, "Gold", "C - Twice.wav"), seed: 4);
        Write(Path.Combine(music, "Pop", "D - Crushed.wav"), seed: 5, gain: 3); // driven into the ceiling: flat tops
        await library.ScanAsync();

        var attention = library.Search("is:aandacht").Select(d => d.Media.Title).Order().ToList();
        Assert.Equal(["Crushed", "Late start", "Twice", "Twice"], attention);
        Assert.Contains(library.Search("crushed").Single().Problems, p => p.StartsWith("clipping"));
        Assert.Single(library.Search("is:clipping"));
        Assert.Contains(library.Search("late start").Single().Problems, p => p.StartsWith("stilte aan het begin"));
        Assert.Equal(2, library.Search("is:dubbel").Count);
        Assert.Empty(library.Search("fine").Single().Problems);
    }

    [BassFact]
    public async Task A_saved_list_shows_the_new_state_after_a_restart()
    {
        var library = Library();
        string music = Path.Combine(_dir, "music");
        Write(Path.Combine(music, "Pop", "A - One.wav"), seed: 1);
        await library.ScanAsync();
        Assert.Contains(library.SmartLists(), l => l.Name == "Nieuw deze maand"); // ready-made lists
        var mine = library.SaveSmartList("Pop zonder cues", "is:zondercues", null, "artist")!;
        Assert.Equal(1, library.Count(mine));

        // A new track arrives; a fresh library (= after a restart) still has the list, and it counts the new state.
        Write(Path.Combine(music, "Pop", "B - Two.wav"), seed: 2);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var again = Library();
        await again.ScanAsync();
        var list = again.SmartLists().Single(l => l.Name == "Pop zonder cues");
        Assert.Equal("artist", list.Sort);
        Assert.Equal(2, again.Count(list));
        Assert.True(again.DeleteSmartList(list.Id));
        Assert.DoesNotContain(again.SmartLists(), l => l.Name == "Pop zonder cues");
    }

    [BassFact]
    public async Task A_wrong_category_change_and_a_tag_edit_are_undone()
    {
        var library = Library();
        string music = Path.Combine(_dir, "music");
        Write(Path.Combine(music, "Pop", "A - One.wav"), seed: 1);
        Write(Path.Combine(music, "Pop", "B - Two.wav"), seed: 2);
        Write(Path.Combine(music, "Gold", "C - Three.wav"), seed: 3);
        await library.ScanAsync();
        long pop = library.Categories.Single(c => c.Name == "Pop").Id, gold = library.Categories.Single(c => c.Name == "Gold").Id;
        var ids = library.Search("cat:pop").Select(d => d.Media.Id).ToList();
        Assert.Null(library.LastChange());

        Assert.Equal(2, library.UpdateMany(ids, gold, null, false, null, null));
        Assert.Equal(3, library.Search("cat:gold").Count);
        Assert.Equal("verplaatsen van 2 nummers", library.LastChange()!.Label);

        var one = ids[0];
        Assert.True(library.UpdateTags(one, "Somebody", "Else", "", "Rock", 1999, "nl", "vrolijk", 4, 5));
        var edited = library.GetDetails(one)!;
        Assert.Equal(("Somebody", "nl", "vrolijk", 4, 5), (edited.Media.Artist, edited.Language, edited.Mood, edited.Rating, edited.EnergyLevel));

        Assert.StartsWith("tags van", library.Undo()!.Label);
        var back = library.GetDetails(one)!;
        Assert.Equal(("", "", (int?)null, 1999 == back.Year), (back.Language, back.Mood, back.Rating, false));
        Assert.NotEqual("Somebody", back.Media.Artist);

        Assert.Equal("verplaatsen van 2 nummers", library.Undo()!.Label);
        Assert.Equal(2, library.Search("cat:pop").Count);
        Assert.All(ids, id => Assert.Equal(pop, library.GetDetails(id)!.Media.CategoryId));
        Assert.Null(library.Undo());
    }
}
