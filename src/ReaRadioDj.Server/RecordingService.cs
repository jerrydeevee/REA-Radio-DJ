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

    /// <summary>Moves a finished recording into the library folder (jingles, category Opnamen) and scans it in.</summary>
    public void AddToLibrary(string file, string what)
    {
        try
        {
            Directory.CreateDirectory(LibraryFolder);
            if (!library.Folders().Any(f => string.Equals(f.Path.TrimEnd('\\'), LibraryFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
            {
                var (_, error) = library.AddFolder(LibraryFolder, MediaKind.Jingle, "Opnamen", watch: true, writeTags: false);
                if (error is not null) { messages.Warning($"Opnamenmap niet in de bibliotheek: {error}"); return; }
            }
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
}
