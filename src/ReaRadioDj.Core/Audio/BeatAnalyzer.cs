using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>Tempo of a track: beats per minute and where the first beat falls (seconds from the start of the file).</summary>
public sealed record BeatInfo(double Bpm, double FirstBeat)
{
    public double BeatSeconds => Bpm > 0 ? 60 / Bpm : 0;
}

/// <summary>Measures the tempo of a file with the beat detection of BASS_FX (decodes faster than real time).</summary>
public static class BeatAnalyzer
{
    public const int MinBpm = 70, MaxBpm = 180;

    /// <summary>True when bass_fx.dll could be loaded.</summary>
    public static bool IsAvailable
    {
        get
        {
            try { return BASS_FX_GetVersion() != 0; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

    /// <summary>Null when the file has no steady beat (speech, a jingle) or cannot be read.</summary>
    public static unsafe BeatInfo? Analyze(string path)
    {
        // BassRuntime.Initialize must have been called (by the engine, or by tests with device 0).
        int stream = BASS_StreamCreateFile(false, path, 0, 0, BASS_STREAM_DECODE | BASS_STREAM_PRESCAN | BASS_UNICODE);
        if (stream == 0) return null;
        try
        {
            double duration = BASS_ChannelBytes2Seconds(stream, BASS_ChannelGetLength(stream, BASS_POS_BYTE));
            if (duration < 10) return null;
            // The middle of the track says most: intros and endings often have no beat.
            double start = Math.Min(20, duration * 0.1), end = Math.Min(duration, start + 120);
            float bpm = BASS_FX_BPM_DecodeGet(stream, start, end, (uint)((MaxBpm << 16) | MinBpm), 0, IntPtr.Zero, IntPtr.Zero);
            BASS_FX_BPM_Free(stream);
            if (bpm < MinBpm || bpm > MaxBpm) return null;

            var beats = new List<double>();
            var handle = GCHandle.Alloc(beats);
            try
            {
                delegate* unmanaged[Stdcall]<int, double, IntPtr, void> proc = &BeatProc;
                BASS_FX_BPM_BeatDecodeGet(stream, start, Math.Min(end, start + 60), 0, (IntPtr)proc, GCHandle.ToIntPtr(handle));
                BASS_FX_BPM_BeatFree(stream);
            }
            finally { handle.Free(); }
            return new BeatInfo(Math.Round(bpm, 2), FirstBeat(beats, 60.0 / bpm));
        }
        finally { BASS_StreamFree(stream); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void BeatProc(int channel, double position, IntPtr user)
    {
        if (GCHandle.FromIntPtr(user).Target is List<double> beats) beats.Add(position);
    }

    /// <summary>
    /// Where the beat grid starts: the detected beats as points on a circle of one beat long, and the direction most
    /// of them point in (a plain average would be thrown off by beats just before and just after the wrap).
    /// </summary>
    public static double FirstBeat(IReadOnlyList<double> beats, double beatSeconds)
    {
        if (beats.Count == 0 || beatSeconds <= 0) return 0;
        double x = 0, y = 0;
        foreach (double b in beats)
        {
            double angle = b % beatSeconds / beatSeconds * Math.Tau;
            x += Math.Cos(angle);
            y += Math.Sin(angle);
        }
        double phase = Math.Atan2(y, x) / Math.Tau;
        if (phase < 0) phase += 1;
        return Math.Round(phase * beatSeconds, 4);
    }
}
