using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Server;

/// <summary>Runs the station's own recordings (program per hour, microphone per take) and their settings.</summary>
public sealed class RecordingService(AirRecorder recorder, MediaLibrary library, StationDb db, ReaRadioDjOptions options,
    MessageCenter messages, ILogger<RecordingService> logger) : BackgroundService
{
    public const string Key = "recordings";

    /// <summary>Microphone takes for the library land here; the folder is added to the library as jingles, category Opnamen.</summary>
    public string LibraryFolder => Path.Combine(options.RecordingPath, "Bibliotheek");

    public static RecordingSettings Load(StationDb db)
    {
        if (db.GetSetting(Key) is not { } json) return new();
        try { return JsonSerializer.Deserialize<RecordingSettings>(json, JsonSerializerOptions.Web) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public void Save(RecordingSettings s)
    {
        recorder.Settings = s;
        db.SetSetting(Key, JsonSerializer.Serialize(recorder.Settings, JsonSerializerOptions.Web));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        recorder.Settings = Load(db);
        recorder.MicTakeDone += MicToLibrary;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            string? lastError = null;
            do
            {
                try { recorder.Tick(DateTime.Now); }
                catch (Exception ex) { logger.LogError(ex, "Recording tick failed"); }
                if (recorder.LastError is { } e && e != lastError) messages.Warning(e);
                lastError = recorder.LastError;
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
        finally
        {
            recorder.MicTakeDone -= MicToLibrary;
            recorder.Dispose();
        }
    }

    private void MicToLibrary(string file) => AddToLibrary(file, "Microfoonopname");

    /// <summary>Makes the library folder for recordings and adds it to the library once; null or the reason it failed.</summary>
    private string? EnsureLibraryFolder()
    {
        Directory.CreateDirectory(LibraryFolder);
        if (library.Folders().Any(f => string.Equals(f.Path.TrimEnd('\\'), LibraryFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) return null;
        var (_, error) = library.AddFolder(LibraryFolder, MediaKind.Jingle, "Opnamen", watch: true, writeTags: false);
        return error is null ? null : $"Opnamenmap niet in de bibliotheek: {error}";
    }

    /// <summary>Moves a finished recording into the library folder (jingles, category Opnamen) and scans it in.</summary>
    public void AddToLibrary(string file, string what)
    {
        try
        {
            if (EnsureLibraryFolder() is { } error) { messages.Warning(error); return; }
            var target = Path.Combine(LibraryFolder, Path.GetFileName(file));
            File.Move(file, target, overwrite: false);
            _ = library.ScanAsync();
            messages.Info($"{what} in de bibliotheek (categorie Opnamen): {Path.GetFileName(target)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not move {File} into the library", file);
        }
    }

    /// <summary>The program recordings of a day (and the night after it) with their real start and end.</summary>
    public IReadOnlyList<RecordedFile> ProgramFiles(DateTime from, DateTime to) =>
        RecordingCutter.Candidates(recorder.Folder, from, to).Select(RecordingCutter.Measure).OfType<RecordedFile>().OrderBy(f => f.Start).ToList();

    private readonly SemaphoreSlim _cutting = new(1, 1);

    /// <summary>
    /// Cuts the stretch from <paramref name="from"/> to <paramref name="to"/> out of the program recordings into a new
    /// item in the library (jingles, category Opnamen) with the given title. Returns the file and its length, or the reason.
    /// </summary>
    public async Task<(string? File, double Seconds, string? Error)> CutToLibraryAsync(DateTime from, DateTime to, string title, string artist, DateTime now)
    {
        title = title.Trim();
        artist = artist.Trim();
        if (to <= from) return (null, 0, "Het einde ligt niet na het begin.");
        if (to - from > RecordingCutter.MaxLength) return (null, 0, $"Hoogstens {RecordingCutter.MaxLength.TotalHours:0} uur in één keer.");
        if (from > now) return (null, 0, "Dat stuk is nog niet uitgezonden.");
        if (to > now) to = now;
        if (title.Length == 0) title = $"Fragment {from:dd-MM-yyyy HH:mm}";
        if (!await _cutting.WaitAsync(0)) return (null, 0, "Er wordt al een stuk geknipt; probeer het zo opnieuw.");
        try
        {
            return await Task.Run(() =>
            {
                var pieces = RecordingCutter.Plan(ProgramFiles(from, to), from, to);
                if (pieces.Count == 0) return ((string?)null, 0.0, (string?)"Van die tijd is geen uitzendopname (stond de opname aan?).");
                if (EnsureLibraryFolder() is { } error) return (null, 0, error);
                var safe = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                if (safe.Length > 80) safe = safe[..80];
                var target = Path.Combine(LibraryFolder, $"{from:yyyy-MM-dd-HHmmss} {safe}.mp3");
                for (int i = 2; File.Exists(target); i++) target = Path.Combine(LibraryFolder, $"{from:yyyy-MM-dd-HHmmss} {safe} ({i}).mp3");
                double seconds = RecordingCutter.Cut(pieces, target, recorder.Settings.Bitrate);
                try { RecordingCutter.Tag(target, title, artist, from); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not tag {File}", target); }
                _ = library.ScanAsync();
                logger.LogInformation("Cut {From}-{To} from the program recording into {File}", from, to, target);
                messages.Info($"Uit de uitzending in de bibliotheek (categorie Opnamen): {title}, {TimeSpan.FromSeconds(seconds):h\\:mm\\:ss}");
                return (target, seconds, null);
            });
        }
        catch (Exception ex) when (ex is BassException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not cut {From}-{To}", from, to);
            return (null, 0, $"Knippen lukte niet: {ex.Message}");
        }
        finally { _cutting.Release(); }
    }
}
