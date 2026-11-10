using ReaRadioDj.Core.Model;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Mixes a transition offline (both items, the volume lines and the insert) into a stereo buffer, so it can be heard on
/// PFL before it goes on air. Uses the same volume-line maths as the engine.
/// </summary>
public static class TransitionPreview
{
    /// <summary>Interleaved stereo float at <see cref="BassRuntime.SampleRate"/>, from <paramref name="before"/> seconds
    /// before the transition until <paramref name="after"/> seconds after the incoming item starts.</summary>
    public static (float[] Samples, double Start) Render(MediaItem outgoing, MediaItem incoming, Transition t,
        float gainOut, float gainIn, float gainInsert, double before = 6, double after = 8)
    {
        int rate = BassRuntime.SampleRate;
        double first = t.Insert is null ? t.StartAt : Math.Min(t.StartAt, t.InsertAt);
        if (t.Out.Count > 0) first = Math.Min(first, t.Out[0].Seconds);
        double start = Math.Clamp(first - before, outgoing.CueIn, Math.Max(outgoing.CueIn, t.StartAt));
        double end = t.StartAt + after;
        var mix = new float[(int)((end - start) * rate) * 2];

        double outEnd = Math.Min(end, outgoing.CueOut);
        if (t.Out.Count > 0 && t.Out[^1].Gain < 0.001) outEnd = Math.Min(outEnd, t.Out[^1].Seconds);
        Add(mix, Read(outgoing.Path, start, outEnd - start, rate), 0, rate, s => gainOut * Transition.GainAt(t.Out, start + s));

        double inLength = Math.Min(end - t.StartAt, incoming.CueOut - t.From);
        Add(mix, Read(incoming.Path, t.From, inLength, rate), t.StartAt - start, rate, s => gainIn * Transition.GainAt(t.In, t.From + s));

        if (t.Insert is { } ins)
            Add(mix, Read(ins.Path, ins.CueIn, Math.Min(ins.CueOut - ins.CueIn, end - t.InsertAt), rate), t.InsertAt - start, rate, _ => gainInsert);

        for (int i = 0; i < mix.Length; i++) mix[i] = Math.Clamp(mix[i], -1f, 1f);
        return (mix, start);
    }

    private static void Add(float[] mix, float[] source, double offsetSeconds, int rate, Func<double, double> gain)
    {
        int offset = (int)Math.Round(offsetSeconds * rate) * 2;
        int frames = source.Length / 2;
        double g = gain(0);
        for (int f = 0; f < frames; f++)
        {
            if ((f & 63) == 0) g = gain(f / (double)rate); // every 1.3 ms is smooth enough
            int i = offset + f * 2;
            if (i < 0) continue;
            if (i + 1 >= mix.Length) break;
            mix[i] += (float)(source[f * 2] * g);
            mix[i + 1] += (float)(source[f * 2 + 1] * g);
        }
    }

    /// <summary>Decodes a piece of a file to stereo float at the given rate (resampled by a decoding mixer).</summary>
    internal static float[] Read(string path, double from, double seconds, int rate)
    {
        if (seconds <= 0) return [];
        int source = BassRuntime.Check(BASS_StreamCreateFile(false, Library.MediaPaths.Resolve(path), 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_UNICODE),
            $"BASS_StreamCreateFile({Path.GetFileName(path)})");
        int mixer = 0;
        try
        {
            if (from > 0) BASS_ChannelSetPosition(source, BASS_ChannelSeconds2Bytes(source, from), BASS_POS_BYTE);
            mixer = BassRuntime.Check(BASS_Mixer_StreamCreate((uint)rate, 2, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_MIXER_END),
                "BASS_Mixer_StreamCreate");
            BassRuntime.Check(BASS_Mixer_StreamAddChannel(mixer, source, 0), "BASS_Mixer_StreamAddChannel");
            var result = new float[(int)(seconds * rate) * 2];
            var buffer = new float[16384];
            int filled = 0;
            while (filled < result.Length)
            {
                int want = Math.Min(buffer.Length, result.Length - filled);
                uint got = BASS_ChannelGetData(mixer, buffer, (uint)(want * 4));
                if (got == uint.MaxValue || got == 0) break;
                int n = (int)(got / 4);
                Array.Copy(buffer, 0, result, filled, n);
                filled += n;
            }
            return result;
        }
        finally
        {
            if (mixer != 0) BASS_StreamFree(mixer);
            BASS_StreamFree(source);
        }
    }
}
