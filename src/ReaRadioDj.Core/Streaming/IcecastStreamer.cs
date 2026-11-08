using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Streaming;

public sealed record StreamStatus(bool Enabled, bool Connected, string Server, long BytesSent, string? Error);

/// <summary>
/// Encodes the mixer output to MP3 (BASSenc_MP3) and sends it to an Icecast (or SHOUTcast v2) server with BASSenc's
/// cast support. Title updates follow the item on air.
/// </summary>
public sealed class IcecastStreamer(StreamOptions options, string stationName, ILogger<IcecastStreamer>? logger = null) : IDisposable
{
    private readonly ILogger _log = (ILogger?)logger ?? NullLogger.Instance;
    private readonly object _gate = new();
    private int _encoder;
    private string? _error;

    public void Start(int mixerHandle)
    {
        lock (_gate)
        {
            if (_encoder != 0 && BASS_Encode_IsActive(_encoder) != BASS_ACTIVE_STOPPED) return;
            _error = null;
            int enc = BASS_Encode_MP3_Start(mixerHandle, $"-b{options.Bitrate}", BASS_ENCODE_AUTOFREE, IntPtr.Zero, IntPtr.Zero);
            if (enc == 0) { _error = $"MP3 encoder failed (BASS error {BASS_ErrorGetCode()})"; _log.LogError("{Error}", _error); return; }

            if (!BASS_Encode_CastInit(enc, options.Server, options.Password, "audio/mpeg", stationName,
                    string.IsNullOrWhiteSpace(options.Url) ? null : options.Url, options.Genre, null, null, (uint)options.Bitrate, 0))
            {
                _error = $"Cannot connect to {options.Server} (BASS error {BASS_ErrorGetCode()})";
                BASS_Encode_Stop(enc);
                _log.LogError("{Error}", _error);
                return;
            }
            _encoder = enc;
            _log.LogInformation("Streaming to {Server} at {Bitrate} kbps", options.Server, options.Bitrate);
        }
    }

    private string? _pendingTitle;
    private int _titleWorker;

    /// <summary>
    /// Updates the stream title. BASS sends this as a separate HTTP request that can block for seconds when the
    /// server is slow, so it runs on a background task (latest title wins) and never on the engine thread.
    /// </summary>
    public void SetTitle(string title)
    {
        Volatile.Write(ref _pendingTitle, title);
        if (Interlocked.Exchange(ref _titleWorker, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try
            {
                while (Interlocked.Exchange(ref _pendingTitle, null) is { } next)
                {
                    int enc;
                    lock (_gate) enc = _encoder;
                    if (enc != 0 && !BASS_Encode_CastSetTitle(enc, next, null))
                        _log.LogWarning("Stream title update failed (BASS error {Error})", BASS_ErrorGetCode());
                }
            }
            finally
            {
                Volatile.Write(ref _titleWorker, 0);
                if (Volatile.Read(ref _pendingTitle) is not null) SetTitle(Volatile.Read(ref _pendingTitle)!);
            }
        });
    }

    /// <summary>The most recent title request has been handled.</summary>
    internal bool TitleIdle => Volatile.Read(ref _titleWorker) == 0 && Volatile.Read(ref _pendingTitle) is null;

    public void Stop()
    {
        lock (_gate)
        {
            if (_encoder != 0) BASS_Encode_Stop(_encoder);
            _encoder = 0;
        }
    }

    public StreamStatus GetStatus()
    {
        lock (_gate)
        {
            bool connected = _encoder != 0 && BASS_Encode_IsActive(_encoder) != BASS_ACTIVE_STOPPED;
            long sent = connected ? (long)BASS_Encode_GetCount(_encoder, BASS_ENCODE_COUNT_CAST) : 0;
            if (_encoder != 0 && !connected) _error ??= "Connection lost";
            return new StreamStatus(options.Enabled, connected, options.Server, sent < 0 ? 0 : sent, _error);
        }
    }

    public void Dispose() => Stop();
}
