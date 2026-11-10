using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using Xunit;

namespace ReaRadioDj.Tests;

public class MediaPathsTests
{
    [Fact]
    public void A_missing_file_is_found_under_the_first_replacement_that_has_it()
    {
        var rules = MediaPaths.Clean(
        [
            new(@"Y:\Muziek", @"\\nas\muziek\"),
            new(@"y:/muziek/", @"D:\Kopie"),            // same From, second choice
            new(@"Y:\Muziek", @"Y:\Muziek"),            // to itself: dropped
            new("", @"C:\x"),                          // empty: dropped
            new(@"Z:\", @"E:\", Enabled: false),
        ]);
        Assert.Equal(3, rules.Count);
        Assert.Equal(@"Y:\Muziek\", rules[0].From);

        var there = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\Kopie\Pop\A - B.mp3", @"Y:\Muziek\Rock\C - D.mp3" };
        Assert.Equal(@"D:\Kopie\Pop\A - B.mp3", MediaPaths.Resolve(@"Y:\Muziek\Pop\A - B.mp3", rules, there.Contains));
        Assert.Equal(@"Y:\Muziek\Rock\C - D.mp3", MediaPaths.Resolve(@"Y:\Muziek\Rock\C - D.mp3", rules, there.Contains)); // at its own path
        Assert.Equal(@"Y:\Muziekx\E.mp3", MediaPaths.Resolve(@"Y:\Muziekx\E.mp3", rules, there.Contains));                 // other folder
        Assert.Equal(@"Z:\F.mp3", MediaPaths.Resolve(@"Z:\F.mp3", rules, _ => false));                                     // nowhere: unchanged
        Assert.Equal([@"Z:\F.mp3"], MediaPaths.Candidates(@"Z:\F.mp3", rules));                                            // rule switched off
    }
}

[Collection("bass")]
public sealed class RelocateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-relocate-" + Guid.NewGuid().ToString("N"));

    public RelocateTests()
    {
        Directory.CreateDirectory(_dir);
        if (ReaRadioDj.Core.Audio.BassRuntime.IsAvailable) ReaRadioDj.Core.Audio.BassRuntime.Initialize(0);
    }

    public void Dispose()
    {
        MediaPaths.Rules = [];
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [BassFact]
    public async Task All_tags_and_a_cover_go_into_the_file_and_read_back()
    {
        var options = new ReaRadioDj.Core.ReaRadioDjOptions
        {
            DataFolder = Path.Combine(_dir, "data"), MusicFolder = Path.Combine(_dir, "music"), JingleFolder = Path.Combine(_dir, "jingles"),
            WatchLibrary = false, NightlyScanHour = -1,
        };
        Directory.CreateDirectory(Path.Combine(options.MusicFolder, "Pop"));
        Directory.CreateDirectory(options.JingleFolder);
        var file = Path.Combine(options.MusicFolder, "Pop", "A - Walk.wav");
        DemoContent.WriteSong(file, 330, seconds: 20, intro: 2);
        var library = new MediaLibrary(options, new ReaRadioDj.Core.Data.StationDb(Path.Combine(options.DataFolder, "station.db")));
        await library.ScanAsync();
        long id = Assert.Single(library.Songs).Id;

        Assert.True(library.UpdateTags(id, "Artiest", "Lopen", "Plaat", "Pop", 1999, "nl", "vrolijk", 4));
        Assert.Equal("", library.WriteAllTags(id));
        using (var f = TagLib.File.Create(file))
        {
            Assert.Equal("Artiest", f.Tag.FirstPerformer);
            Assert.Equal("Lopen", f.Tag.Title);
            Assert.Equal("Plaat", f.Tag.Album);
            Assert.Equal(1999u, f.Tag.Year);
        }

        // a 1x1 PNG
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        Assert.Equal("image/png", MediaLibrary.CoverMime(png));
        Assert.NotEqual("", library.SetCover(id, [1, 2, 3, 4]));     // not a picture
        Assert.Equal("", library.SetCover(id, png));
        var cover = library.Cover(id);
        Assert.NotNull(cover);
        Assert.Equal(png, cover!.Value.Data);
        Assert.Equal("", library.SetCover(id, null));
        Assert.Null(library.Cover(id));
        Assert.Null(library.WriteAllTags(999_999));

        // writing into the file is not seen as a changed file: a new scan keeps the library tags
        await library.ScanAsync();
        Assert.Equal("Lopen", library.Get(id)!.Title);
    }

    [BassFact]
    public async Task A_moved_share_plays_from_its_replacement_and_can_be_moved_for_good()
    {
        var options = new ReaRadioDj.Core.ReaRadioDjOptions
        {
            DataFolder = Path.Combine(_dir, "data"), MusicFolder = Path.Combine(_dir, "music"), JingleFolder = Path.Combine(_dir, "jingles"),
            WatchLibrary = false, NightlyScanHour = -1,
        };
        Directory.CreateDirectory(options.MusicFolder);
        Directory.CreateDirectory(options.JingleFolder);
        var library = new MediaLibrary(options, new ReaRadioDj.Core.Data.StationDb(Path.Combine(options.DataFolder, "station.db")));

        string old = Path.Combine(_dir, "oud"), now = Path.Combine(_dir, "nieuw");
        Directory.CreateDirectory(Path.Combine(old, "Pop"));
        DemoContent.WriteSong(Path.Combine(old, "Pop", "A - Walk.wav"), 330, seconds: 20, intro: 2);
        var (_, error) = library.AddFolder(old, MediaKind.Song, "", watch: false, writeTags: false);
        Assert.Null(error);
        for (int i = 0; i < 100 && library.IsScanning; i++) await Task.Delay(50);
        await library.ScanAsync();
        var song = Assert.Single(library.Songs);
        long id = song.Id;

        Directory.Move(old, now);
        Assert.False(MediaPaths.Exists(song.Path));
        MediaPaths.Rules = [new PathSubstitute(old, now)];
        Assert.True(MediaPaths.Exists(song.Path));
        Assert.Equal(Path.Combine(now, "Pop", "A - Walk.wav"), MediaPaths.Resolve(song.Path));

        MediaPaths.Rules = [];
        var (changed, found) = library.Relocate(old, now);
        Assert.Equal((1, 1), (changed, found));
        Assert.Equal(Path.Combine(now, "Pop", "A - Walk.wav"), library.Get(id)!.Path);
        Assert.Contains(library.Folders(), f => string.Equals(f.Path, now, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(library.Folders(), f => string.Equals(f.Path, old, StringComparison.OrdinalIgnoreCase));
    }
}
