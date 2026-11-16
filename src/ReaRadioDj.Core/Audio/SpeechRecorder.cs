using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Records a mixer channel (normally the microphone) only while there is sound on it: a <see cref="SpeechGate"/>
/// decides what is kept, and the kept audio goes into one MP3 file per clock hour, so a day of presenting becomes a
/// few files with only the talking in them (a check of what was said, without hours of music in between). A file is
/// only made once there is something to put in it. The audio is taken after the channel strip and before the fader,
/// like REC in the mixer; the audio thread only copies, a background thread runs the gate and the encoder.
/// </summary>
public sealed unsafe partial class SpeechRecorder : IDisposable
{
    public const string Prefix = "spraak-";
    public const double PreRollSeconds = 0.5;

    [LibraryImport("bass")]
    private static partial int BASS_ChannelSetDSP(int handle, delegate* unmanaged[Stdcall]<int, int, IntPtr, uint, IntPtr, void> proc, IntPtr user, int priority);

    [LibraryImport("bass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BASS_ChannelRemoveDSP(int handle, int dsp);

    [LibraryImport("bassenc")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BASS_Encode_Write(int handle, float* buffer, uint length);

    private const int Priority = -20;   // after the whole channel strip, before the fader
    private readonly int _handle, _bitrate, _rate;
    private readonly string _folder;
    private readonly ILogger _log;
    private readonly SpeechGate _gate;
    private readonly BlockingCollection<float[]> _queue = new(new ConcurrentQueue<float[]>(), 2000);
    private readonly Task _worker;
    private GCHandle _self;
    private int _dsp, _dummy, _encoder;
    private DateTime _fileHour;
    private volatile bool _allowed;
    private volatile string? _file, _error;
    private long _takes;

    public SpeechRecorder(int handle, string folder, double thresholdDb, double hangSeconds, int bitrate, ILogger? log = null)
    {
        _handle = handle;
        _folder = folder;
        _bitrate = bitrate;
        _rate = BassRuntime.SampleRate;
        _log = log ?? NullLogger.Instance;
        _gate = new SpeechGate(_rate, 2, thresholdDb, hangSeconds, PreRollSeconds);
        ThresholdDb = thresholdDb;
        HangSeconds = hangSeconds;
        _worker = Task.Factory.StartNew(Work, TaskCreationOptions.LongRunning);
        _self = GCHandle.Alloc(this);
        _dsp = BASS_ChannelSetDSP(_handle, &Proc, GCHandle.ToIntPtr(_self), Priority);
        if (_dsp == 0)
        {
            _queue.CompleteAdding();
            _self.Free();
            throw new BassException("BASS_ChannelSetDSP(speech)");
        }
    }

    public int Handle => _handle;
    public double ThresholdDb { get; }
    public double HangSeconds { get; }
    public int Bitrate => _bitrate;
    /// <summary>Whether sound may open the gate now (for the microphone: it is open).</summary>
    public bool Allowed { get => _allowed; set => _allowed = value; }
    /// <summary>The file being written to, or null while there is no talking this hour yet.</summary>
    public string? FilePath => _file;
    public bool Recording => _gate.IsOpen;
    public long Takes => Interlocked.Read(ref _takes);
    public string? Error => _error;

    private void Work()
    {
        foreach (var block in _queue.GetConsumingEnumerable())
        {
            try { Feed(block, DateTime.Now); }
            catch (Exception ex) { _error = $"Spraakopname: {ex.Message}"; _log.LogWarning(ex, "Speech recording failed"); }
        }
        CloseFile();
    }

    private void Feed(float[] block, DateTime now)
    {
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        // a new hour: the old file is finished as soon as the gate is closed (a take is never split in two)
        if (_encoder != 0 && hour != _fileHour && !_gate.IsOpen) CloseFile();
        var change = _gate.Process(block, _allowed, Write);
        if (change == GateChange.Opened) Interlocked.Increment(ref _takes);

        void Write(ReadOnlySpan<float> samples)
        {
            if (_encoder == 0 && !OpenFile(now, hour)) return;
            fixed (float* p = samples) BASS_Encode_Write(_encoder, p, (uint)(samples.Length * sizeof(float)));
        }
    }

    private bool OpenFile(DateTime now, DateTime hour)
    {
        try
        {
            var dir = Path.Combine(_folder, now.ToString("yyyy-MM"));
            Directory.CreateDirectory(dir);
            var name = $"{Prefix}{now:yyyy-MM-dd-HH}";
            var path = Path.Combine(dir, name + ".mp3");
            for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{name}-{i}.mp3");
            // a stream without sound of its own: the gate writes the kept audio into its encoder
            _dummy = BASS_StreamCreate((uint)_rate, 2, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, IntPtr.Zero, IntPtr.Zero);
            if (_dummy == 0) { _error = $"Spraakopname lukte niet (BASS-fout {BASS_ErrorGetCode()})."; return false; }
            _encoder = BASS_Encode_MP3_StartFile(_dummy, $"-b{_bitrate}", BASS_UNICODE, path);
            if (_encoder == 0)
            {
                _error = $"Spraakopname lukte niet (BASS-fout {BASS_ErrorGetCode()}).";
                BASS_StreamFree(_dummy); _dummy = 0;
                return false;
            }
            _fileHour = hour;
            _file = path;
            _error = null;
            _log.LogInformation("Speech recording {File}", path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _error = $"Spraakopname lukte niet: {ex.Message}";
            return false;
        }
    }

    private void CloseFile()
    {
        if (_encoder != 0) BASS_Encode_Stop(_encoder);
        if (_dummy != 0) BASS_StreamFree(_dummy);
        _encoder = _dummy = 0;
        _file = null;
    }

    private void Push(Span<float> samples)
    {
        if (_queue.IsAddingCompleted) return;
        // when the disk cannot keep up the block is dropped rather than holding up the audio
        try { _queue.TryAdd(samples.ToArray()); } catch (InvalidOperationException) { }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void Proc(int dsp, int channel, IntPtr buffer, uint length, IntPtr user)
    {
        if (GCHandle.FromIntPtr(user).Target is SpeechRecorder r) r.Push(new Span<float>((void*)buffer, (int)(length / sizeof(float))));
    }

    public void Dispose()
    {
        if (_dsp != 0) { BASS_ChannelRemoveDSP(_handle, _dsp); _dsp = 0; }
        _queue.CompleteAdding();
        _worker.Wait(5000);
        if (_self.IsAllocated) _self.Free();
    }
}
