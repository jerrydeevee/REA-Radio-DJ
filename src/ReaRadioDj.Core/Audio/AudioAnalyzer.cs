using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

public sealed record AnalysisResult(double Duration, CuePoints Cues, double? Lufs);

/// <summary>
/// Decodes a file once (faster than real time) and measures its level envelope and EBU R128 loudness.
/// </summary>
public static class AudioAnalyzer
{
    private const double BlockSeconds = 0.02;

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
                }
                peaks.Add(CuePointCalculator.ToDb(peak));
            }

            double? lufs = null;
            if (loud != 0 && BASS_Loudness_GetLevel(loud, BASS_LOUDNESS_INTEGRATED, out float level)
                && !float.IsInfinity(level) && level > -70)
                lufs = Math.Round(level, 2);

            var cues = CuePointCalculator.Compute(peaks, BlockSeconds, options);
            return new AnalysisResult(Math.Round(duration, 3), cues, lufs);
        }
        finally
        {
            if (loud != 0) BASS_Loudness_Stop(loud);
            BASS_StreamFree(stream);
        }
    }
}
