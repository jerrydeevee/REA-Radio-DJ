using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Result of one analysis pass. Peaks = one linear peak per 20 ms block, scaled to 0..65535. Clips = how often the audio
/// hits the ceiling: 3 or more samples in a row on the same full-scale value (a flat, clipped top).
/// </summary>
public sealed record AnalysisResult(double Duration, CuePoints Cues, double? Lufs, ushort[] Peaks, int Clips = 0)
{
    /// <summary>Clipping events per minute.</summary>
    public double ClipsPerMinute => Duration > 1 ? Clips / (Duration / 60) : 0;

    public const int BlockMilliseconds = 20;
}

/// <summary>
/// Decodes a file once (faster than real time) and measures its level envelope and EBU R128 loudness.
/// </summary>
public static class AudioAnalyzer
{
    private const double BlockSeconds = AnalysisResult.BlockMilliseconds / 1000.0;

    public static AnalysisResult Analyze(string path, MixOptions options)
    {
        // BassRuntime.Initialize must have been called (by the engine, or by tests with device 0).
        int stream = BassRuntime.Check(
            BASS_StreamCreateFile(false, path, 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_STREAM_PRESCAN | BASS_UNICODE),
            $"BASS_StreamCreateFile({Path.GetFileName(path)})");
        int loud = 0;
        try
        {
            double duration = BASS_ChannelBytes2Seconds(stream, BASS_ChannelGetLength(stream, BASS_POS_BYTE));
            try { loud = BASS_Loudness_Start(stream, BASS_LOUDNESS_INTEGRATED, 0); }
            catch (DllNotFoundException) { loud = 0; } // bassloud is optional

            // One block = 20 ms of float stereo samples. Peak per block -> dB.
            int blockBytes = (int)BASS_ChannelSeconds2Bytes(stream, BlockSeconds);
            var buffer = new float[Math.Max(blockBytes / 4, 2)];
            var peaks = new List<double>((int)(duration / BlockSeconds) + 8);
            var linear = new List<ushort>(peaks.Capacity);
            int clips = 0;
            Span<int> run = stackalloc int[2]; // per channel (interleaved stereo; mono uses the first)
            Span<float> last = stackalloc float[2];
            while (true)
            {
                uint got = BASS_ChannelGetData(stream, buffer, (uint)(buffer.Length * 4));
                if (got == uint.MaxValue || got == 0) break; // -1 = ended / error
                int samples = (int)(got / 4);
                float peak = 0;
                for (int i = 0; i < samples; i++)
                {
                    float v = Math.Abs(buffer[i]);
                    if (v > peak) peak = v;
                    // A flat top: 3 or more samples in a row on the same full-scale value. Decoded lossy files often go
                    // a little above full scale between samples on loud masters; that is normal and is not counted.
                    int ch = i & 1;
                    if (v >= 0.999f && v <= 1.0005f && Math.Abs(v - last[ch]) < 1e-5f) { if (++run[ch] == 2) clips++; }
                    else run[ch] = 0;
                    last[ch] = v;
                }
                peaks.Add(CuePointCalculator.ToDb(peak));
                linear.Add((ushort)Math.Clamp(peak * 65535f, 0, 65535));
            }

            double? lufs = null;
            if (loud != 0 && BASS_Loudness_GetLevel(loud, BASS_LOUDNESS_INTEGRATED, out float level)
                && !float.IsInfinity(level) && level > -70)
                lufs = Math.Round(level, 2);

            var cues = CuePointCalculator.Compute(peaks, BlockSeconds, options);
            return new AnalysisResult(Math.Round(duration, 3), cues, lufs, linear.ToArray(), clips);
        }
        finally
        {
            if (loud != 0) BASS_Loudness_Stop(loud);
            BASS_StreamFree(stream);
        }
    }
}
