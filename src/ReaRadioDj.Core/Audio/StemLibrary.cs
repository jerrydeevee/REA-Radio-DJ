using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ReaRadioDj.Core.Audio;

/// <summary>Where a track is with its stems.</summary>
public enum StemState { None, Waiting, Working, Ready, Failed }

/// <summary>The stems of one track: the state, how far (0..1) and why it failed.</summary>
public sealed record StemStatus(StemState State, double Progress = 0, string? Error = null);

/// <summary>
/// The stems of the decks: made one track at a time in the background (on the processor, below normal priority) and
/// kept in a cache folder, one 8-channel WAV per track. A track that changes on disk gets new stems; the oldest files
/// go when the cache grows past its size. A deck that wants a track now puts it at the front of the queue.
/// </summary>
public sealed class StemLibrary : IDisposable
{
    private readonly string _folder;
    private readonly string? _model;
    private readonly ILogger? _log;
    private readonly object _gate = new();
    private readonly LinkedList<string> _queue = new();
    private readonly Dictionary<string, StemStatus> _status = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _work = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread? _worker;
    private StemSeparator? _separator;
    private string? _current;
    private CancellationTokenSource? _currentCancel;

    /// <summary>The cache may grow to this size; then the stems not used longest go.</summary>
    public long MaxBytes { get; set; }

    /// <summary>A track's stems are ready (path of the track, path of the stems file).</summary>
    public event Action<string, string>? Finished;

    public StemLibrary(string folder, string? modelPath, long maxBytes = 20L * 1024 * 1024 * 1024, ILogger? log = null)
    {
        _folder = Path.GetFullPath(folder);
        _model = modelPath;
        MaxBytes = maxBytes;
        _log = log;
        if (_model is null) return;
        _worker = new Thread(Run) { IsBackground = true, Name = "stems", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    /// <summary>Whether stems can be made at all (the model is there).</summary>
    public bool Available => _model is not null;

    public string Folder => _folder;

    /// <summary>The stems file of a track if it is there (and marks it as used), else null.</summary>
    public string? Ready(string track)
    {
        var file = FileFor(track);
        if (file is null || !File.Exists(file)) return null;
        try { File.SetLastAccessTimeUtc(file, DateTime.UtcNow); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return file;
    }

    public StemStatus Status(string track)
    {
        if (Ready(track) is not null) return new StemStatus(StemState.Ready, 1);
        lock (_gate) return _status.GetValueOrDefault(track) ?? new StemStatus(StemState.None);
    }

    /// <summary>Asks for the stems of a track; <paramref name="now"/> puts it before the others (a deck wants it).</summary>
    public StemStatus Request(string track, bool now = false)
    {
        if (!Available) return new StemStatus(StemState.Failed, 0, "het scheidingsmodel ontbreekt (models\\htdemucs.onnx)");
        if (Ready(track) is not null) return new StemStatus(StemState.Ready, 1);
        lock (_gate)
        {
            if (string.Equals(_current, track, StringComparison.OrdinalIgnoreCase)) return _status[track];
            var node = _queue.Find(track);
            if (node is not null)
            {
                if (now) { _queue.Remove(node); _queue.AddFirst(track); }
                return _status[track];
            }
            if (now) _queue.AddFirst(track); else _queue.AddLast(track);
            _status[track] = new StemStatus(StemState.Waiting);
        }
        _work.Release();
        return new StemStatus(StemState.Waiting);
    }

    /// <summary>Takes a track out of the queue (it is still made when it is already being made).</summary>
    public void Cancel(string track)
    {
        lock (_gate)
            if (_queue.Remove(track)) _status.Remove(track);
    }

    /// <summary>What is waiting, in order (the one being made first).</summary>
    public IReadOnlyList<string> Queue()
    {
        lock (_gate) return (_current is null ? _queue : _queue.Prepend(_current)).ToList();
    }

    /// <summary>The cache file of a track: by its path, size and date, so a changed file gets new stems.</summary>
    public string? FileFor(string track)
    {
        try
        {
            var info = new FileInfo(track);
            if (!info.Exists) return null;
            var key = $"{info.FullName.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20].ToLowerInvariant();
            return Path.Combine(_folder, hash + ".wav");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>The size of the cache in bytes.</summary>
    public long Size() => Directory.Exists(_folder)
        ? new DirectoryInfo(_folder).EnumerateFiles("*.wav").Sum(f => f.Length)
        : 0;

    /// <summary>Removes the stems used longest ago until the cache fits (never <paramref name="keep"/>).</summary>
    public int Trim(string? keep = null)
    {
        if (!Directory.Exists(_folder)) return 0;
        var files = new DirectoryInfo(_folder).EnumerateFiles("*.wav").OrderBy(f => f.LastAccessTimeUtc).ToList();
        long size = files.Sum(f => f.Length);
        int removed = 0;
        foreach (var f in files)
        {
            if (size <= MaxBytes) break;
            if (keep is not null && string.Equals(f.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
            try { long length = f.Length; f.Delete(); size -= length; removed++; }
            catch (IOException) { } catch (UnauthorizedAccessException) { }   // in use by a deck
        }
        return removed;
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { _work.Wait(_stop.Token); } catch (OperationCanceledException) { return; }
            string? track;
            lock (_gate)
            {
                if (_queue.First is null) continue;
                track = _queue.First.Value;
                _queue.RemoveFirst();
                _current = track;
                _currentCancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                _status[track] = new StemStatus(StemState.Working);
            }
            var target = FileFor(track);
            try
            {
                if (target is null) throw new FileNotFoundException("het bestand is er niet (meer)", track);
                if (!File.Exists(target))
                {
                    _separator ??= new StemSeparator(_model!);
                    var started = DateTime.UtcNow;
                    _separator.Separate(track, target, new SyncProgress(p => { lock (_gate) _status[track] = new StemStatus(StemState.Working, p); }),
                        _currentCancel.Token);
                    _log?.LogInformation("Stems of {File} made in {Seconds:0} s", Path.GetFileName(track), (DateTime.UtcNow - started).TotalSeconds);
                    Trim(keep: target);
                }
                lock (_gate) _status.Remove(track);
                Finished?.Invoke(track, target);
            }
            catch (OperationCanceledException) { lock (_gate) _status.Remove(track); }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "Stems of {File} failed", track);
                lock (_gate) _status[track] = new StemStatus(StemState.Failed, 0, ex.Message);
            }
            finally
            {
                lock (_gate) { _current = null; _currentCancel?.Dispose(); _currentCancel = null; }
            }
        }
    }

    /// <summary>Progress reported on the worker itself (Progress&lt;T&gt; would post to a thread pool thread later).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _worker?.Join(TimeSpan.FromSeconds(5));
        _separator?.Dispose();
    }
}
