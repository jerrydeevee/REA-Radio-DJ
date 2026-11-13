using ReaRadioDj.Core.Library;
using Xunit;

namespace ReaRadioDj.Tests;

/// <summary>A folder picture is only a song's cover in an album folder, not in a large mixed folder.</summary>
public class AlbumFolderTests
{
    [Fact]
    public void Small_folder_is_an_album_folder() =>
        Assert.True(MediaLibrary.IsAlbumFolder(Enumerable.Range(0, 12).Select(i => $"Album {i}").ToList()));

    [Fact]
    public void Large_mixed_folder_is_not() =>
        Assert.False(MediaLibrary.IsAlbumFolder(Enumerable.Range(0, MediaLibrary.AlbumFolderMax + 1).Select(i => $"Album {i}").ToList()));

    [Fact]
    public void Large_folder_of_one_album_is() =>
        Assert.True(MediaLibrary.IsAlbumFolder(Enumerable.Repeat("Box Set", 60).ToList()));

    [Fact]
    public void Large_folder_without_albums_is_not() =>
        Assert.False(MediaLibrary.IsAlbumFolder(Enumerable.Repeat("", 60).ToList()));
}
