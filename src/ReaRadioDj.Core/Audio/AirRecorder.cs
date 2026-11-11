using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>What the station records by itself.</summary>
public sealed record RecordingSettings
{
    /// <summary>Record what goes on air, one MP3 file per clock hour.</summary>
    public bool Program { get; init; }
    /// <summary>Record the microphone, one MP3 file per time it is open.</summary>
    public bool Mic { get; init; }
    /// <summary>Put each microphone take in the library (category "Opnamen").</summary>
    public bool MicToLibrary { get; init; }
    public int Bitrate { get; init; } = 128;
    /// <summary>Days a recording is kept; 0 = keep for ever.</summary>
    public int KeepDays { get; init; } = 365;
    /// <summary>Folder for the recordings; empty = "Uitzending" in the recordings folder.</summary>
    public string Folder { get; init; } = "";

    public static readonly int[] Bitrates = [64, 96, 128, 160, 192, 256, 320];

    public RecordingSettings Clean() => this with
    {
        Bitrate = Bitrates.Contains(Bitrate) ? Bitrate : 128,
        KeepDays = Math.Clamp(KeepDays, 0, 3650),
        Folder = (Folder ?? "").Trim().Trim('"'),
    };
}

/// <summary>
/// The station's own recordings: the program (what goes on air, microphone included) in one MP3 file per clock hour,
/// and optionally each microphone take apart. Old recordings are removed after the retention time. MP3 encoding is
/// done by BASSenc_MP3 on the channel itself, so the audio thread is not held up.
/// </summary>
public sealed class AirRecorder : IDisposable
{
    public const string ProgramPrefix = "uitzending-", MicPrefix = "microfoon-";
    /// <summary>The VoIP channels whose call recordings (WAV in Gesprekken) fall under the same retention time.</summary>
    public static readonly string[] CallPrefixes = [AudioRouter.Voip1Strip + "-", AudioRouter.Voip2Strip + "-"];

    private readonly AudioRouter _router;
    private readonly string _defaultFolder, _callFolder;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private RecordingSettings _settings = new();
    private Running? _program, _mic;
    private DateTime _lastCleanup = DateTime.MinValue;

    private sealed record Running(int Encoder, string Path, DateTime Started, DateTime Hour);

    public AirRecorder(AudioRouter router, ReaRadioDjOptions options, ILogger<AirRecorder>? logger = null)
    {
        _router = router;
        _defaultFolder = Path.GetFullPath(Path.Combine(options.RecordingPath, "Uitzending"));
        _callFolder = Path.GetFullPath(Path.Combine(options.RecordingPath, "Gesprekken"));
        _log = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Raised with the file of a finished microphone take that goes to the library.</summary>
    public event Action<string>? MicTakeDone;

    public RecordingSettings Settings
    {
        get { lock (_gate) return _settings; }
        set { lock (_gate) _settings = value.Clean(); }
    }

    public string Folder => Settings.Folder is { Length: > 0 } f ? f : _defaultFolder;

    public (string? Program, string? Mic, string? Error) Status
    {
        get { lock (_gate) return (_program?.Path, _mic?.Path, LastError); }
    }

    public string? LastError { get; private set; }

    /// <summary>Called about once a second: starts, splits and stops recordings and removes old ones.</summary>
    public void Tick(DateTime now)
    {
        string? take = null;
        lock (_gate)
        {
            var s = _settings;
            var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
            if (!s.Program || _router.Program is null) Stop(ref _program);   // no bus yet: the engine is still starting
            else if (_program is null || _program.Hour != hour || !Active(_program))
            {
                // the new hour starts before the old one stops, so nothing is lost between the files
                var next = Start(_router.Program.Handle, ProgramPrefix, now, hour, s);
                var old = _program;
                _program = next;
                Stop(ref old);
            }

            bool micOpen = _router.MicOpen && MicHandle() is not null;
            if (!s.Mic || !micOpen)
            {
                var done = _mic;
                Stop(ref _mic);
                if (done is not null && s.MicToLibrary && File.Exists(done.Path)) take = done.Path;
            }
            else if (_mic is null && MicHandle() is int mic) _mic = Start(mic, MicPrefix, now, hour, s);

            if (now - _lastCleanup > TimeSpan.FromHours(1)) { _lastCleanup = now; Cleanup(now, s); }
        }
        if (take is not null) MicTakeDone?.Invoke(take);
    }

    private int? MicHandle()
    {
        try { return _router.Strip(AudioRouter.MicStrip).Handle; }
        catch (KeyNotFoundException) { return null; }
    }

    private static bool Active(Running r) => BASS_Encode_IsActive(r.Encoder) != BASS_ACTIVE_STOPPED;

    private Running? Start(int handle, string prefix, DateTime now, DateTime hour, RecordingSettings s)
    {
        try
        {
            var dir = Path.Combine(Folder, now.ToString("yyyy-MM"));
            Directory.CreateDirectory(dir);
            var name = prefix == ProgramPrefix ? $"{prefix}{now:yyyy-MM-dd-HH}" : $"{prefix}{now:yyyy-MM-dd-HHmmss}";
            var path = Path.Combine(dir, name + ".mp3");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{name}-{i}.mp3");
            int enc = BASS_Encode_MP3_StartFile(handle, $"-b{s.Bitrate}", BASS_ENCODE_AUTOFREE | BASS_UNICODE, path);
            if (enc == 0)
            {
                LastError = $"Opnemen lukte niet (BASS-fout {BASS_ErrorGetCode()}).";
                _log.LogWarning("Could not start recording {File}: BASS error {Code}", path, BASS_ErrorGetCode());
                return null;
            }
            LastError = null;
            _log.LogInformation("Recording {File}", path);
            return new Running(enc, path, now, hour);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = $"Opnemen lukte niet: {ex.Message}";
            _log.LogWarning(ex, "Could not start a recording in {Folder}", Folder);
            return null;
        }
    }

    private static void Stop(ref Running? r)
    {
        if (r is null) return;
        BASS_Encode_Stop(r.Encoder);
        r = null;
    }

    /// <summary>
    /// Removes this recorder's own files that are older than the retention time: the program and microphone MP3s,
    /// and the call recordings of the VoIP channels (WAV in Gesprekken). Other files stay.
    /// </summary>
    public int Cleanup(DateTime now, RecordingSettings s)
    {
        if (s.KeepDays <= 0) return 0;
        var limit = now.AddDays(-s.KeepDays);
        int removed = RemoveOld(Folder, "*.mp3", [ProgramPrefix, MicPrefix], limit, _log)
                    + RemoveOld(_callFolder, "*.wav", CallPrefixes, limit, _log);
        if (removed > 0) _log.LogInformation("Removed {Count} recordings older than {Days} days", removed, s.KeepDays);
        return removed;
    }

    /// <summary>Deletes the files in a folder (and its subfolders) that match the pattern, start with one of the prefixes and were last written before the limit; then removes empty subfolders.</summary>
    public static int RemoveOld(string folder, string pattern, IReadOnlyList<string> prefixes, DateTime limit, ILogger? log = null)
    {
        if (!Directory.Exists(folder)) return 0;
        int removed = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f);
                if (!prefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                if (File.GetLastWriteTime(f) >= limit) continue;
                try { File.Delete(f); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            foreach (var d in Directory.EnumerateDirectories(folder))
                if (!Directory.EnumerateFileSystemEntries(d).Any()) try { Directory.Delete(d); } catch (IOException) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log?.LogWarning(ex, "Could not clean up {Folder}", folder); }
        return removed;
    }

    public void Dispose()
    {
        lock (_gate) { Stop(ref _program); Stop(ref _mic); }
    }
}
