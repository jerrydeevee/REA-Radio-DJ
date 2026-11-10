using ReaRadioDj.Core.Audio;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// Lays a music bed under a rendered voice: the bed starts a moment before the voice at a clear level, dips under the
/// voice and fades out as the voice ends. The voice end stays the end of the item, so the engine's talk-up (the next
/// song starting under the voice for the length of its intro) still lands the last word on the vocal.
/// Output: 16-bit stereo WAV at the playout rate, written over the voice file.
/// </summary>
public static class BreakMixer
{
    /// <param name="PreRoll">Seconds of bed before the first word.</param>
    /// <param name="BedDb">Bed level on its own (pre-roll), relative to a full-scale peak.</param>
    /// <param name="UnderDb">Bed level under the voice.</param>
    /// <param name="DipSeconds">Time to go from the pre-roll level to the level under the voice.</param>
    /// <param name="FadeSeconds">Fade-out of the bed, ending with the voice (plus <paramref name="Tail"/>).</param>
    public sealed record Settings(double PreRoll = 1.5, double BedDb = -8, double UnderDb = -20, double DipSeconds = 0.4,
        double FadeSeconds = 1.2, double Tail = 0.25);

    /// <summary>Mixes and returns the second at which the voice starts in the new file.</summary>
    public static double Mix(string voiceWav, string bedPath, Settings? settings = null)
    {
        var s = settings ?? new Settings();
        int rate = BassRuntime.SampleRate;
        var voice = Decode(voiceWav, rate);
        var bed = Decode(bedPath, rate);
        if (voice.Length == 0) throw new InvalidDataException("Lege stem");
        if (bed.Length < rate * 2) throw new InvalidDataException("Bed is leeg of korter dan een seconde");

        // bed to a full-scale peak first, so the levels mean the same for every bed
        float bedPeak = 0;
        foreach (var v in bed) bedPeak = Math.Max(bedPeak, Math.Abs(v));
        float norm = bedPeak > 1e-6 ? 1 / bedPeak : 1;

        int pre = (int)(s.PreRoll * rate), tail = (int)(s.Tail * rate);
        int voiceFrames = voice.Length / 2, frames = pre + voiceFrames + tail, bedFrames = bed.Length / 2;
        double full = Math.Pow(10, s.BedDb / 20), under = Math.Pow(10, s.UnderDb / 20);
        int dip = Math.Max(1, (int)(s.DipSeconds * rate)), fade = Math.Max(1, (int)(s.FadeSeconds * rate));

        var output = new float[frames * 2];
        for (int f = 0; f < frames; f++)
        {
            double level = f < pre - dip ? full
                : f < pre ? full + (under - full) * (f - (pre - dip)) / (double)dip
                : under;
            int fromEnd = frames - f;
            if (fromEnd < fade) level *= fromEnd / (double)fade;

            int b = (f % bedFrames) * 2;   // a short bed loops
            int v = (f - pre) * 2;
            for (int c = 0; c < 2; c++)
            {
                double x = bed[b + c] * norm * level;
                if (v >= 0 && v < voice.Length) x += voice[v + c];
                output[f * 2 + c] = (float)Math.Clamp(x, -1, 1);
            }
        }
        var tmp = voiceWav + ".bed";
        File.WriteAllBytes(tmp, VoiceProcessor.Write(rate, 2, output));
        File.Move(tmp, voiceWav, overwrite: true);
        return s.PreRoll;
    }

    /// <summary>Decodes any file BASS can read to float stereo at <paramref name="rate"/> (resampled by the mixer).</summary>
    internal static float[] Decode(string path, int rate)
    {
        int mixer = BassRuntime.Check(BASS_Mixer_StreamCreate((uint)rate, 2, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_MIXER_END),
            "BASS_Mixer_StreamCreate(bed)");
        try
        {
            int source = BassRuntime.Check(
                BASS_StreamCreateFile(false, Library.MediaPaths.Resolve(path), 0, 0, BASS_STREAM_DECODE | BASS_SAMPLE_FLOAT | BASS_UNICODE),
                $"BASS_StreamCreateFile({Path.GetFileName(path)})");
            BassRuntime.Check(BASS_Mixer_StreamAddChannel(mixer, source, BASS_STREAM_AUTOFREE), "BASS_Mixer_StreamAddChannel(bed)");
            var all = new List<float>(rate * 2 * 30);
            var buffer = new float[rate];
            while (true)
            {
                uint got = BASS_ChannelGetData(mixer, buffer, (uint)(buffer.Length * 4));
                if (got == uint.MaxValue || got == 0) break;
                all.AddRange(buffer.AsSpan(0, (int)(got / 4)));
                if (all.Count > rate * 2 * 600) break;   // ten minutes is more than any bed needs
            }
            return all.ToArray();
        }
        finally { BASS_StreamFree(mixer); }
    }
}
