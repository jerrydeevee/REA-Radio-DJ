namespace ReaRadioDj.Core.Library;

/// <summary>One folder or audio file in a <see cref="FileBrowser"/> listing.</summary>
public sealed record FileEntry(string Name, string Path, bool IsFolder, long Size, DateTime Modified);

/// <summary>The contents of one folder. <c>Path</c> is empty for the list of drives; <c>Parent</c> is null at the top.</summary>
public sealed record FolderListing(string Path, string? Parent, IReadOnlyList<FileEntry> Entries, string? Error);

/// <summary>
/// Read-only view of the folders on the engine machine for the Verkenner window: drives, sub folders and the audio files
/// the player can load. It never opens a file and never shows hidden or system items.
/// </summary>
public static class FileBrowser
{
    public const int MaxEntries = 2000;

    public static bool IsAudio(string path) =>
        MediaLibrary.Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Lists <paramref name="path"/>; with no path the ready drives.</summary>
    public static FolderListing List(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Drives();
        DirectoryInfo dir;
        try
        {
            var full = System.IO.Path.GetFullPath(path.Trim());
            // "D:\Muziek\" and "D:\Muziek" are the same folder; a drive root keeps its separator.
            if (System.IO.Path.GetPathRoot(full) != full) full = System.IO.Path.TrimEndingDirectorySeparator(full);
            dir = new DirectoryInfo(full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return new FolderListing(path, null, [], "Ongeldige map.");
        }
        var parent = dir.Parent?.FullName ?? "";
        if (!dir.Exists) return new FolderListing(dir.FullName, parent, [], "Deze map bestaat niet (meer).");
        try
        {
            var folders = dir.EnumerateDirectories()
                .Where(Visible)
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(d => new FileEntry(d.Name, d.FullName, true, 0, d.LastWriteTime));
            var files = dir.EnumerateFiles()
                .Where(f => Visible(f) && IsAudio(f.Name))
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(f => new FileEntry(f.Name, f.FullName, false, f.Length, f.LastWriteTime));
            var all = folders.Concat(files).Take(MaxEntries + 1).ToList();
            string? note = all.Count > MaxEntries ? $"Alleen de eerste {MaxEntries} items worden getoond." : null;
            if (note is not null) all.RemoveAt(all.Count - 1);
            return new FolderListing(dir.FullName, parent, all, note);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return new FolderListing(dir.FullName, parent, [], "Geen toegang tot deze map.");
        }
    }

    private static FolderListing Drives()
    {
        var drives = new List<FileEntry>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name.TrimEnd('\\') : $"{d.Name.TrimEnd('\\')} {d.VolumeLabel}";
                drives.Add(new FileEntry(label, d.RootDirectory.FullName, true, 0, default));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new FolderListing("", null, drives, null);
    }

    private static bool Visible(FileSystemInfo i) => (i.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
}
