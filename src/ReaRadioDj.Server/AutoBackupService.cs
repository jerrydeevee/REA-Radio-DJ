using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Server;

/// <summary>Makes the daily backup of the station database and removes the old ones (see <see cref="AutoBackup"/>).</summary>
public sealed class AutoBackupService(StationDb db, ReaRadioDjOptions options, MessageCenter messages, ILogger<AutoBackupService> logger) : BackgroundService
{
    public const string Key = "auto-backup";
    private readonly SemaphoreSlim _busy = new(1, 1);
    private AutoBackupSettings _settings = new();
    private DateTime _failedSlot = DateTime.MinValue;

    public AutoBackupSettings Settings => _settings;
    public string? LastError { get; private set; }
    public DateTime? LastAttempt { get; private set; }

    public string Folder => AutoBackup.FolderOf(_settings, options.DataFolder);

    public static AutoBackupSettings Load(StationDb db)
    {
        if (db.GetSetting(Key) is not { } json) return new();
        try { return (JsonSerializer.Deserialize<AutoBackupSettings>(json, JsonSerializerOptions.Web) ?? new()).Clean(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Stores the settings; returns an error text when the folder cannot be used.</summary>
    public string? Save(AutoBackupSettings s)
    {
        s = s.Clean();
        if (s.Folder.Length > 0)
        {
            if (!Path.IsPathFullyQualified(s.Folder)) return "Geef een volledige map op, bijv. D:\\Reservekopie of \\\\nas\\radio\\reservekopie.";
            try { Directory.CreateDirectory(s.Folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return $"De map {s.Folder} is niet te gebruiken: {ex.Message}";
            }
        }
        _settings = s;
        db.SetSetting(Key, JsonSerializer.Serialize(s, JsonSerializerOptions.Web));
        _failedSlot = DateTime.MinValue;
        return null;
    }

    /// <summary>Makes a backup now (also when the daily one is off). Returns the file, or null with <see cref="LastError"/>.</summary>
    public async Task<string?> RunNowAsync(CancellationToken ct = default)
    {
        await _busy.WaitAsync(ct);
        try
        {
            var now = DateTime.Now;
            LastAttempt = now;
            var folder = Folder;
            var file = await Task.Run(() => AutoBackup.Run(options.DataFolder, options.StationName, folder, now), ct);
            var removed = AutoBackup.Prune(folder, _settings.KeepDays, now);
            LastError = null;
            logger.LogInformation("Backup written to {File}; {Removed} old backup(s) removed", file, removed.Count);
            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or ArgumentException or NotSupportedException)
        {
            LastError = ex.Message;
            logger.LogError(ex, "Backup failed");
            return null;
        }
        finally { _busy.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _settings = Load(db);
        try
        {
            // Not in the first minute: the engine has enough to do while it starts.
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do
            {
                try { await TickAsync(DateTime.Now, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Backup check failed"); }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    private async Task TickAsync(DateTime now, CancellationToken ct)
    {
        var s = _settings;
        if (!s.Enabled) return;
        var slot = AutoBackup.LastSlot(s, now);
        // a failed slot is tried again after ten minutes, and reported once
        if (_failedSlot == slot && LastAttempt is { } t && now - t < TimeSpan.FromMinutes(10)) return;
        var latest = AutoBackup.List(Folder).Select(x => (DateTime?)x.Time).FirstOrDefault();
        if (!AutoBackup.IsDue(s, now, latest)) return;
        if (await RunNowAsync(ct) is null)
        {
            if (_failedSlot != slot)
                messages.Warning($"De dagelijkse reservekopie is niet gelukt: {LastError} REA-Radio-DJ probeert het elke tien minuten opnieuw. "
                    + "Kijk of de map bereikbaar is (Instellingen › Systeem).");
            _failedSlot = slot;
        }
        else if (_failedSlot == slot)
        {
            messages.Info("De dagelijkse reservekopie is alsnog gelukt.");
            _failedSlot = DateTime.MinValue;
        }
    }
}
