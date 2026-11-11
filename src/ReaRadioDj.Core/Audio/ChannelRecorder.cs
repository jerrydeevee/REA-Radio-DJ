using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Records one mixer channel to a WAV file (16-bit stereo). The recording is taken after the channel strip (gain,
/// equaliser, effect, compressor) and before the fader, so the fader and mute do not change the recording.
/// The audio thread only copies the samples; a background task writes them to disk.
/// </summary>
public sealed unsafe partial class ChannelRecorder
{
    [LibraryImport("bass")]
    private static partial int BASS_ChannelSetDSP(int handle, delegate* unmanaged[Stdcall]<int, int, IntPtr, uint, IntPtr, void> proc, IntPtr user, int priority);

    [LibraryImport("bass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BASS_ChannelRemoveDSP(int handle, int dsp);

    private const int Priority = -20;   // after the whole channel strip
    private readonly object _gate = new();
    private readonly int _handle;
    private int _dsp;
    private GCHandle _self;
    private BlockingCollection<float[]>? _queue;
    private Task? _writer;
    private long _frames;

    public ChannelRecorder(int handle) => _handle = handle;

    public bool IsRecording { get { lock (_gate) return _dsp != 0; } }
    /// <summary>The file of the running (or last) recording.</summary>
    public string? FilePath { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public double Seconds => Interlocked.Read(ref _frames) / (double)BassRuntime.SampleRate;

    /// <summary>Starts recording into the folder; the file name is the channel id and the start time.</summary>
    public string Start(string folder, string channelId)
    {
        lock (_gate)
        {
            if (_dsp != 0) return FilePath!;
            Directory.CreateDirectory(folder);
            StartedUtc = DateTime.UtcNow;
            var path = Path.Combine(folder, $"{Safe(channelId)}-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 16);
            var wav = new WavWriter(file, BassRuntime.SampleRate, 2);
            var queue = new BlockingCollection<float[]>(new ConcurrentQueue<float[]>(), 2000);
            Interlocked.Exchange(ref _frames, 0);
            _writer = Task.Run(() =>
            {
                using (wav) foreach (var block in queue.GetConsumingEnumerable()) wav.Write(block);
            });
            _queue = queue;
            if (!_self.IsAllocated) _self = GCHandle.Alloc(this);
            _dsp = BASS_ChannelSetDSP(_handle, &RecordProc, GCHandle.ToIntPtr(_self), Priority);
            if (_dsp == 0)
            {
                queue.CompleteAdding();
                _writer.Wait();
                File.Delete(path);
                _queue = null;
                throw new BassException("BASS_ChannelSetDSP(record)");
            }
            FilePath = path;
            return path;
        }
    }

    /// <summary>Stops the recording and closes the file; returns its path (null when nothing was recording).</summary>
    public string? Stop()
    {
        Task? writer;
        lock (_gate)
        {
            if (_dsp == 0) return null;
            BASS_ChannelRemoveDSP(_handle, _dsp);
            _dsp = 0;
            _queue?.CompleteAdding();
            _queue = null;
            writer = _writer;
            _writer = null;
        }
        writer?.Wait(5000);
        return FilePath;
    }

    private void Push(Span<float> samples)
    {
        var q = _queue;
        if (q is null || q.IsAddingCompleted) return;
        // When the disk cannot keep up the block is dropped rather than holding up the audio.
        try { if (q.TryAdd(samples.ToArray())) Interlocked.Add(ref _frames, samples.Length / 2); }
        catch (InvalidOperationException) { }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void RecordProc(int dsp, int channel, IntPtr buffer, uint length, IntPtr user)
    {
        if (GCHandle.FromIntPtr(user).Target is ChannelRecorder r) r.Push(new Span<float>((void*)buffer, (int)(length / sizeof(float))));
    }

    private static string Safe(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}

/// <summary>Writes float samples as a 16-bit PCM WAV file; the sizes in the header are filled in when it is closed.</summary>
public sealed class WavWriter : IDisposable
{
    private readonly Stream _out;
    private long _dataBytes;
    private byte[] _buffer = new byte[8192];

    public WavWriter(Stream output, int sampleRate, int channels)
    {
        _out = output;
        var w = new BinaryWriter(_out, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write("RIFF"u8); w.Write(0); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(sampleRate);
        w.Write(sampleRate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(0);
        w.Flush();
    }

    public long DataBytes => _dataBytes;

    public void Write(ReadOnlySpan<float> samples)
    {
        if (_buffer.Length < samples.Length * 2) _buffer = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * 32767f);
            _buffer[2 * i] = (byte)v;
            _buffer[2 * i + 1] = (byte)(v >> 8);
        }
        _out.Write(_buffer, 0, samples.Length * 2);
        _dataBytes += samples.Length * 2;
    }

    public void Dispose()
    {
        if (_out.CanSeek)
        {
            uint data = (uint)Math.Min(_dataBytes, uint.MaxValue - 36);
            _out.Seek(4, SeekOrigin.Begin); _out.Write(BitConverter.GetBytes(data + 36));
            _out.Seek(40, SeekOrigin.Begin); _out.Write(BitConverter.GetBytes(data));
            _out.Seek(0, SeekOrigin.End);
        }
        _out.Dispose();
    }
}
