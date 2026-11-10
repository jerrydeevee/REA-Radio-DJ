using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using Xunit;

namespace ReaRadioDj.Tests;

public class TrackProfileTests
{
    private static byte[] Curve(int seed, int seconds = 60)
    {
        var r = new Random(seed);
        var c = new byte[seconds];
        for (int i = 0; i < seconds; i++) c[i] = (byte)(150 + r.Next(-40, 40));
        return c;
    }

    [Fact]
    public void Same_curve_is_recognised_also_shifted_and_a_little_louder()
    {
        var a = Curve(1);
        var shifted = new byte[a.Length];
        for (int i = 0; i < a.Length; i++) shifted[i] = (byte)Math.Min(255, a[Math.Max(0, i - 1)] + 3);
        Assert.True(TrackProfile.Difference(a, 60, shifted, 60.5) < TrackProfile.SameTrack);
    }

    [Fact]
    public void Different_or_flat_curves_are_never_the_same_track()
    {
        Assert.Null(TrackProfile.Difference(Curve(1), 60, Curve(2), 60));
        var flat = Enumerable.Repeat((byte)200, 60).ToArray();
        Assert.Null(TrackProfile.Difference(flat, 60, flat, 60));
        Assert.Null(TrackProfile.Difference(Curve(1), 60, Curve(1), 65)); // other length
    }

    [Fact]
    public void Energy_and_markers_follow_the_loudness()
    {
        // 5 s quiet intro (-30 dB), 50 s full, 5 s quiet ending; peaks per 20 ms
        var peaks = new List<ushort>();
        void Add(int seconds, double amp) { for (int i = 0; i < seconds * 50; i++) peaks.Add((ushort)(amp * 65535)); }
        Add(5, 0.03); Add(50, 0.9); Add(5, 0.03);
        var e = TrackProfile.Energy([.. peaks]);
        Assert.Equal(60, e.Length);
        Assert.InRange(e[10], 240, 255);
        Assert.InRange(e[0], 120, 135);
        var (intro, outro) = TrackProfile.Markers(e);
        Assert.Equal(5, intro);
        Assert.Equal(55, outro);
    }
}

[Collection("bass")]
public class LibraryFolderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-libfolders-" + Guid.NewGuid().ToString("N"));

    public LibraryFolderTests()
    {
        Directory.CreateDirectory(_dir);
        if (ReaRadioDj.Core.Audio.BassRuntime.IsAvailable) ReaRadioDj.Core.Audio.BassRuntime.Initialize(0);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>A 16-bit stereo WAV whose loudness changes every second in a pattern set by <paramref name="seed"/>.</summary>
    private static void WritePatterned(string path, int seed, int seconds = 40, int rate = 22050)
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
            short v = (short)(Math.Sin(2 * Math.PI * 330 * i / rate) * levels[i / rate] * 30000);
            w.Write(v); w.Write(v);
        }
    }

    private (MediaLibrary Library, ReaRadioDjOptions Options) Library()
    {
        var options = new ReaRadioDjOptions
        {
            DataFolder = Path.Combine(_dir, "data"), MusicFolder = Path.Combine(_dir, "music"), JingleFolder = Path.Combine(_dir, "jingles"),
            WatchLibrary = false, NightlyScanHour = -1,
        };
        Directory.CreateDirectory(options.MusicFolder);
        Directory.CreateDirectory(options.JingleFolder);
        return (new MediaLibrary(options, new StationDb(Path.Combine(options.DataFolder, "station.db"))), options);
    }

    private static async Task Settle(MediaLibrary library)
    {
        for (int i = 0; i < 100 && library.IsScanning; i++) await Task.Delay(50);
        await library.ScanAsync();
    }

    [BassFact]
    public async Task Added_folder_is_read_removed_folder_keeps_its_tracks_and_a_moved_track_is_recognised()
    {
        var (library, _) = Library();
        string share = Path.Combine(_dir, "share");
        WritePatterned(Path.Combine(share, "Set", "A - Walk.wav"), seed: 1);
        WritePatterned(Path.Combine(share, "Set", "B - Run.wav"), seed: 2);

        var (folder, error) = library.AddFolder(share, MediaKind.Song, "", watch: false, writeTags: false);
        Assert.Null(error);
        await Settle(library);
        Assert.Equal(2, library.Songs.Count);
        var walk = library.Search("walk").Single();
        Assert.Equal("Set", walk.Media.Category);
        Assert.True(library.StateOf(folder!)!.Reachable);
        library.SaveCues(walk.Media.Id, walk.Media.Cues with { Intro = 3 });

        // Overlapping folders are refused.
        Assert.NotNull(library.AddFolder(Path.Combine(share, "Set"), MediaKind.Song, "", false, false).Error);
        Assert.NotNull(library.AddFolder(Path.Combine(_dir, "nope"), MediaKind.Song, "", false, false).Error);

        // Removing the folder hides its tracks but keeps them.
        Assert.Equal(2, library.RemoveFolder(folder!.Id));
        Assert.Empty(library.Songs);
        Assert.NotNull(library.GetDetails(walk.Media.Id));

        // The same recording turns up elsewhere under another name: it gets its old id, cues and history back.
        string other = Path.Combine(_dir, "other");
        Directory.CreateDirectory(other);
        File.Move(Path.Combine(share, "Set", "A - Walk.wav"), Path.Combine(other, "renamed.wav"));
        var (folder2, error2) = library.AddFolder(other, MediaKind.Song, "Nieuw", watch: false, writeTags: false);
        Assert.Null(error2);
        await Settle(library);
        var back = Assert.Single(library.Songs);
        Assert.Equal(walk.Media.Id, back.Id);
        Assert.Equal(3, back.Intro);
        Assert.True(library.GetDetails(back.Id)!.CuesManual);
        Assert.EndsWith("renamed.wav", back.Path);
        Assert.Equal("Nieuw", back.Category); // the new folder has its own category

        // A share that is offline does not make its tracks missing.
        Directory.Move(other, other + "-offline");
        await library.ScanAsync();
        Assert.Single(library.Songs);
        Assert.False(library.StateOf(folder2!)!.Reachable);
    }

    [BassFact]
    public async Task Several_items_change_at_once_and_tags_can_be_corrected()
    {
        var (library, options) = Library();
        for (int i = 0; i < 3; i++) WritePatterned(Path.Combine(options.MusicFolder, "Pop", $"X - Song {i}.wav"), seed: 10 + i, seconds: 12);
        await library.ScanAsync();
        var ids = library.Songs.Select(s => s.Id).ToList();
        var rock = library.AddCategory("Rock", MediaKind.Song, null);
        Assert.Equal(3, library.UpdateMany(ids, rock.Id, enabled: false, clearDates: false, null, new DateTime(2030, 1, 1)));
        Assert.Empty(library.Songs); // switched off
        var d = library.GetDetails(ids[0])!;
        Assert.Equal("Rock", d.Media.Category);
        Assert.Equal(new DateTime(2030, 1, 1), d.ValidUntil);
        library.UpdateMany(ids, null, enabled: true, clearDates: true, null, null);
        Assert.Equal(3, library.Songs.Count);
        Assert.Null(library.GetDetails(ids[0])!.ValidUntil);
        Assert.Equal("Rock", library.GetDetails(ids[0])!.Media.Category); // category untouched when not given

        Assert.True(library.UpdateTags(ids[1], "Nieuwe Artiest", "Andere Titel", "Album", "Nederpop", 1984));
        var t = library.GetDetails(ids[1])!;
        Assert.Equal(("Nieuwe Artiest", "Andere Titel", "Nederpop", 1984), (t.Media.Artist, t.Media.Title, t.Genre, t.Year));
        Assert.Single(library.Search("genre:nederpop jaar:198"));
        await library.ScanAsync(); // an unchanged file keeps the corrected tags
        Assert.Equal("Andere Titel", library.GetDetails(ids[1])!.Media.Title);
    }

    [BassFact]
    public async Task Markers_can_be_written_into_the_file_and_are_read_back_by_another_library()
    {
        var (library, _) = Library();
        string folder = Path.Combine(_dir, "tagged");
        string file = Path.Combine(folder, "C - Song.wav");
        WritePatterned(file, seed: 3);
        var (f, _) = library.AddFolder(folder, MediaKind.Song, "", watch: false, writeTags: true);
        await Settle(library);
        var item = library.Songs.Single();
        library.SaveCues(item.Id, item.Cues with { Intro = 4.5, Mix = 30 });

        var markers = FileMarkers.Read(file);
        Assert.NotNull(markers);
        Assert.True(markers!.Manual);
        Assert.Equal(4.5, markers.Intro);
        Assert.Equal(30, markers.Mix);

        // A fresh station reads the file and starts with these markers.
        var fresh = new MediaLibrary(new ReaRadioDjOptions
        {
            DataFolder = Path.Combine(_dir, "data2"), MusicFolder = folder, JingleFolder = Path.Combine(_dir, "jingles2"), WatchLibrary = false, NightlyScanHour = -1,
        }, new StationDb(Path.Combine(_dir, "data2", "station.db")));
        await fresh.ScanAsync();
        var read = fresh.Songs.Single();
        Assert.Equal(4.5, read.Intro);
        Assert.Equal(30, read.Cues.Mix);
        Assert.True(fresh.GetDetails(read.Id)!.CuesManual);
    }
}
