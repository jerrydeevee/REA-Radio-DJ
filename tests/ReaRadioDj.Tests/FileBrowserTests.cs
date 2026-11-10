using ReaRadioDj.Core.Library;
using Xunit;

namespace ReaRadioDj.Tests;

public class FileBrowserTests
{
    [Fact]
    public void A_folder_lists_sub_folders_first_then_only_visible_audio_files()
    {
        var root = Directory.CreateTempSubdirectory("rea-files-");
        try
        {
            root.CreateSubdirectory("Pop");
            root.CreateSubdirectory("archief");
            var hidden = root.CreateSubdirectory("verborgen");
            hidden.Attributes |= FileAttributes.Hidden;
            File.WriteAllBytes(Path.Combine(root.FullName, "b.MP3"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root.FullName, "a.wav"), [1]);
            File.WriteAllText(Path.Combine(root.FullName, "notities.txt"), "geen audio");
            var secret = Path.Combine(root.FullName, "c.flac");
            File.WriteAllBytes(secret, [1]);
            File.SetAttributes(secret, FileAttributes.Hidden);

            var l = FileBrowser.List(root.FullName + "\\");
            Assert.Null(l.Error);
            Assert.Equal(root.FullName, l.Path);
            Assert.Equal(root.Parent!.FullName, l.Parent);
            Assert.Equal(["archief", "Pop", "a.wav", "b.MP3"], l.Entries.Select(e => e.Name));
            Assert.True(l.Entries[0].IsFolder);
            Assert.Equal(3, l.Entries[3].Size);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void Missing_folders_and_no_path_are_handled()
    {
        var gone = FileBrowser.List(Path.Combine(Path.GetTempPath(), "rea-bestaat-niet-" + Guid.NewGuid().ToString("N")));
        Assert.NotNull(gone.Error);
        Assert.Empty(gone.Entries);

        var drives = FileBrowser.List(null);
        Assert.Equal("", drives.Path);
        Assert.Null(drives.Parent);
        Assert.All(drives.Entries, e => Assert.True(e.IsFolder));
        Assert.True(FileBrowser.IsAudio(@"C:\x\Lied.Flac"));
        Assert.False(FileBrowser.IsAudio(@"C:\x\Lied.exe"));
    }
}
