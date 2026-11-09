using System.Collections.Concurrent;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Server;

/// <summary>
/// Reads music catalogues from other databases over ODBC into the library: on request, and every few minutes for a
/// source that asks for it. One sync at a time; a sync runs in the background and its outcome is kept on the source.
/// </summary>
public sealed class DbSourceService(DbSourceStore store, MediaLibrary library, MessageCenter messages, ILogger<DbSourceService> logger)
    : BackgroundService
{
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly ConcurrentDictionary<long, bool> _busy = new();

    public bool Busy(long id) => _busy.ContainsKey(id);

    /// <summary>Runs the query and shows the first rows, without changing the library.</summary>
    public Task<DbTestDto> TestAsync(DbSource source) => Task.Run(() =>
    {
        var s = source.Checked();
        if (s.Connection.Length == 0 || s.Query.Length == 0)
            return new DbTestDto(false, [], [], [], "vul een verbinding en een query in");
        try
        {
            var read = DbRows.Query(s, max: 20);
            var sample = read.Rows.Select(r =>
            {
                string path = s.Translate(r.Path);
                string what = string.Join(" · ", new[] { r.Artist, r.Title, r.Category, r.Kind?.ToString().ToLowerInvariant(), r.Isrc, r.Year?.ToString() }
                    .Where(x => !string.IsNullOrEmpty(x)));
                return (File.Exists(path) ? "✓ " : "✗ niet gevonden: ") + path + (what.Length > 0 ? "  —  " + what : "");
            }).ToList();
            return new DbTestDto(read.Problem is null, read.Columns.ToList(), read.Used.ToList(), sample, read.Problem);
        }
        catch (Exception ex) when (ex is System.Data.Odbc.OdbcException or InvalidOperationException or ArgumentException)
        {
            return new DbTestDto(false, [], [], [], "verbinden of query mislukt: " + ex.Message);
        }
    });

    /// <summary>Starts a sync in the background; false when that source is already busy.</summary>
    public bool Start(long id)
    {
        if (store.Get(id) is not { } source || !_busy.TryAdd(id, true)) return false;
        _ = Task.Run(() => SyncAsync(source));
        return true;
    }

    private async Task SyncAsync(DbSource source)
    {
        await _one.WaitAsync();
        string result;
        try
        {
            var read = await Task.Run(() => DbRows.Query(source));
            if (read.Problem is { } problem) result = "niet gelukt: " + problem;
            else
            {
                var r = await Task.Run(() => library.SyncExternal(source, read.Rows));
                result = r.Text + (read.Truncated ? $" (alleen de eerste {DbRows.MaxRows} rijen)" : "")
                    + (r.Problems.Count > 0 ? "\n" + string.Join("\n", r.Problems.Take(8)) : "");
                if (r.Added + r.Missing > 0 || r.Skipped > 0) messages.Info($"Database {source.Name}: {r.Text}");
            }
        }
        catch (Exception ex)
        {
            result = "niet gelukt: " + ex.Message;
            messages.Warning($"Database {source.Name}: {ex.Message}");
            logger.LogWarning(ex, "Sync of {Source} failed", source.Name);
        }
        finally
        {
            _one.Release();
            _busy.TryRemove(source.Id, out _);
        }
        store.SetResult(source.Id, DateTime.Now, result);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                foreach (var s in store.All().Where(s => s.Due(now) && !Busy(s.Id))) Start(s.Id);
            }
            catch (Exception ex) { logger.LogError(ex, "Database source check failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    public static DbSourceDto Dto(DbSource s, bool busy) => new(s.Id, s.Name, s.Enabled, s.Connection, s.Query,
        s.Kind == MediaKind.Jingle ? "jingle" : "song", s.SyncMinutes, s.PathFrom, s.PathTo, s.LastSync, s.LastResult, busy);

    public static DbSource FromDto(DbSourceDto d) => new DbSource(d.Id, d.Name, d.Enabled, d.Connection ?? "", d.Query ?? "",
        d.Kind == "jingle" ? MediaKind.Jingle : MediaKind.Song, d.SyncMinutes, d.PathFrom ?? "", d.PathTo ?? "").Checked();
}
