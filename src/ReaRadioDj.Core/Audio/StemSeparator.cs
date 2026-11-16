using System.Buffers.Binary;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Splits a track into four stems (drums, bass, other, vocals) with HT-Demucs (Meta, MIT licence) as an ONNX model,
/// on the processor with a few threads. Never on the graphics card: on the studio computer a DirectML run hung the
/// card for minutes and took the sound on air with it. The result is one WAV file with
/// eight channels (the stems as stereo pairs, in <see cref="Names"/> order) at 44.1 kHz, as long as the track: a deck
/// plays it as one stream, so the stems can never drift apart, and mixes them to stereo with a matrix.
/// The track goes through the model in pieces of 7.8 s that overlap a quarter and are cross-faded, and is written as it
/// goes, so a long track never sits in memory as a whole.
/// </summary>
public sealed class StemSeparator : IDisposable
{
    public const int Rate = 44100, Segment = 343_980, Overlap = Segment / 4, Stride = Segment - Overlap, Count = 4;

    /// <summary>The stems in the order of the model and of the channel pairs in the file.</summary>
    public static readonly IReadOnlyList<string> Names = ["drums", "bass", "other", "vocals"];

    private readonly string _model;
    private readonly int _threads;
    private InferenceSession? _session;
    private readonly object _gate = new();

    /// <summary>How many processor threads the model may use: a quarter of them, at most 6, so the playout keeps room.</summary>
    public static int DefaultThreads => Math.Clamp(Environment.ProcessorCount / 4, 1, 6);

    public int Threads => _threads;

    public StemSeparator(string modelPath, int threads = 0)
    {
        _model = modelPath;
        _threads = threads > 0 ? threads : DefaultThreads;
    }

    /// <summary>The model file: the configured path, else models\htdemucs.onnx in the data folder or next to the program.</summary>
    public static string? FindModel(string? configured, string dataFolder)
    {
        foreach (var path in new[]
                 {
                     configured,
                     Path.Combine(dataFolder, "models", "htdemucs.onnx"),
                     Path.Combine(AppContext.BaseDirectory, "models", "htdemucs.onnx"),
                 })
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return Path.GetFullPath(path);
        return null;
    }

    private InferenceSession Session() => _session ??= new InferenceSession(_model, new SessionOptions
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        IntraOpNumThreads = _threads,
        InterOpNumThreads = 1,
    });

    /// <summary>Writes the stems of <paramref name="input"/> to <paramref name="output"/> (via a .part file). Progress 0..1.</summary>
    public void Separate(string input, string output, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        // BassRuntime.Initialize must have been called (by the engine, or by tests with device 0).
        int file = BassRuntime.Check(BASS_StreamCreateFile(false, input, 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_STREAM_PRESCAN | BASS_UNICODE),
            $"BASS_StreamCreateFile({Path.GetFileName(input)})");
        // through a decoding mixer: whatever the file is (48 kHz, mono), the model gets 44.1 kHz stereo
        int mixer = BASS_Mixer_StreamCreate(Rate, 2, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_MIXER_END);
        if (mixer == 0 || !BASS_Mixer_StreamAddChannel(mixer, file, BASS_MIXER_CHAN_NORAMPIN))
        {
            BASS_StreamFree(file);
            if (mixer != 0) BASS_StreamFree(mixer);
            throw new BassException("BASS_Mixer_StreamCreate(stems)");
        }
        string part = output + ".part";
        try
        {
            double seconds = BASS_ChannelBytes2Seconds(file, BASS_ChannelGetLength(file, BASS_POS_BYTE));
            long total = Math.Max(1, (long)Math.Round(seconds * Rate));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            using (var writer = new StemWriter(part, total))
                Run(mixer, total, writer, progress, ct);
            File.Move(part, output, overwrite: true);
        }
        catch
        {
            try { File.Delete(part); } catch (IOException) { }
            throw;
        }
        finally
        {
            BASS_StreamFree(mixer);
            BASS_StreamFree(file);
        }
    }

    private void Run(int mixer, long total, StemWriter writer, IProgress<double>? progress, CancellationToken ct)
    {
        int chunks = (int)Math.Max(1, (total + Stride - 1) / Stride);
        var input = new float[2 * Segment];          // planar: left then right
        var interleaved = new float[2 * Segment];
        var tail = new float[Count * 2 * Overlap];   // the faded-out end of the previous piece, waiting for the next
        int filled = 0;
        for (int chunk = 0; chunk < chunks; chunk++)
        {
            ct.ThrowIfCancellationRequested();
            // keep the overlap, read the rest (silence after the end)
            if (chunk > 0)
            {
                Array.Copy(input, Stride, input, 0, Overlap);
                Array.Copy(input, Segment + Stride, input, Segment, Overlap);
                filled = Overlap;
            }
            int want = Segment - filled;
            int got = Read(mixer, interleaved, want);
            for (int i = 0; i < want; i++)
            {
                input[filled + i] = i < got ? interleaved[2 * i] : 0;
                input[Segment + filled + i] = i < got ? interleaved[2 * i + 1] : 0;
            }

            float[] stems;
            lock (_gate)
            {
                var tensor = new DenseTensor<float>(input, [1, 2, Segment]);
                using var results = Session().Run([NamedOnnxValue.CreateFromTensor("mix", tensor)]);
                stems = results.First().AsTensor<float>().ToArray();   // (1, 4, 2, Segment)
            }

            long start = (long)chunk * Stride;
            bool last = chunk == chunks - 1;
            int length = (int)Math.Min(last ? Segment : Stride, total - start);
            // this piece: fade in over the overlap (not the first piece), and add what the previous piece left
            writer.Write(stems, length, chunk == 0 ? null : tail);
            if (!last)
                for (int s = 0; s < Count; s++)
                    for (int c = 0; c < 2; c++)
                        for (int i = 0; i < Overlap; i++)
                        {
                            float fadeOut = 1 - (float)i / Overlap;
                            tail[(s * 2 + c) * Overlap + i] = stems[(s * 2 + c) * Segment + Stride + i] * fadeOut;
                        }
            progress?.Report((chunk + 1) / (double)chunks);
        }
    }

    private static int Read(int mixer, float[] buffer, int frames)
    {
        int done = 0;
        var temp = new float[Math.Min(frames, 65536) * 2];
        while (done < frames)
        {
            int ask = Math.Min(frames - done, temp.Length / 2);
            uint got = BASS_ChannelGetData(mixer, temp, (uint)(ask * 2 * sizeof(float)) | BASS_DATA_FLOAT);
            if (got == uint.MaxValue || got == 0) break;
            int n = (int)(got / sizeof(float));
            Array.Copy(temp, 0, buffer, done * 2, n);
            done += n / 2;
        }
        return done;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    /// <summary>Writes an 8-channel 16-bit WAV of a known length, piece by piece.</summary>
    private sealed class StemWriter : IDisposable
    {
        private readonly FileStream _file;
        private readonly long _total;
        private long _written;

        public StemWriter(string path, long frames)
        {
            _total = frames;
            _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            const int channels = Count * 2;
            long data = frames * channels * 2;
            var h = new byte[44];
            "RIFF"u8.CopyTo(h);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), (uint)Math.Min(uint.MaxValue, 36 + data));
            "WAVEfmt "u8.CopyTo(h.AsSpan(8));
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(16), 16);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(20), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(22), channels);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(24), Rate);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(28), Rate * channels * 2);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(32), channels * 2);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(34), 16);
            "data"u8.CopyTo(h.AsSpan(36));
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(40), (uint)Math.Min(uint.MaxValue, data));
            _file.Write(h);
        }

        /// <param name="stems">The model's output (4 × 2 × Segment, planar).</param>
        /// <param name="tail">The faded-out end of the previous piece (4 × 2 × Overlap), or null for the first piece.</param>
        public void Write(float[] stems, int length, float[]? tail)
        {
            length = (int)Math.Min(length, _total - _written);
            if (length <= 0) return;
            var bytes = new byte[length * Count * 2 * 2];
            int o = 0;
            for (int i = 0; i < length; i++)
                for (int s = 0; s < Count; s++)
                    for (int c = 0; c < 2; c++)
                    {
                        int plane = s * 2 + c;
                        float v = stems[plane * Segment + i];
                        if (tail is not null && i < Overlap) v = v * ((float)i / Overlap) + tail[plane * Overlap + i];
                        short pcm = (short)Math.Clamp(MathF.Round(v * 32767f), -32768, 32767);
                        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(o), pcm);
                        o += 2;
                    }
            _file.Write(bytes);
            _written += length;
        }

        public void Dispose()
        {
            // a shorter decode than announced: pad with silence, so the length in the header is right
            if (_written < _total)
            {
                var silence = new byte[Count * 2 * 2 * 4096];
                long left = _total - _written;
                while (left > 0)
                {
                    int n = (int)Math.Min(left, 4096);
                    _file.Write(silence, 0, n * Count * 2 * 2);
                    left -= n;
                }
            }
            _file.Dispose();
        }
    }
}
