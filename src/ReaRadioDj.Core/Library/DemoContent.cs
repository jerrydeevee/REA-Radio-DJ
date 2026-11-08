namespace ReaRadioDj.Core.Library;

/// <summary>
/// Generates royalty-free demo audio (synthesised chords with silent lead-in and a fading tail) so REA-Radio-DJ can be
/// tried, and tested, without any music collection. Files are 16-bit stereo WAV at 44.1 kHz.
/// </summary>
public static class DemoContent
{
    private const int Rate = 44100;

    public static void Generate(string musicFolder, string jingleFolder, int songs = 6, double songSeconds = 40)
    {
        Directory.CreateDirectory(musicFolder);
        Directory.CreateDirectory(jingleFolder);
        // C, F, G, A minor, D minor, E minor ... (root frequencies in Hz)
        double[] roots = [261.63, 349.23, 392.00, 220.00, 293.66, 329.63, 246.94, 196.00];
        string[] artists = ["De Testband", "Sinus & Co", "Het Oscillator Orkest", "DJ Zaagtand"];
        for (int i = 0; i < songs; i++)
        {
            var path = Path.Combine(musicFolder, $"{artists[i % artists.Length]} - Demonummer {i + 1}.wav");
            if (!File.Exists(path)) WriteSong(path, roots[i % roots.Length], songSeconds, intro: 6);
            if (i == 0) File.WriteAllText(path + ".cue.json", "{ \"ramp\": 6.5 }");
        }
        var jingle = Path.Combine(jingleFolder, "Station ID.wav");
        if (!File.Exists(jingle)) WriteJingle(jingle);
    }

    /// <summary>A "song": 0.8 s silence, an intro of soft single notes, a louder chord part and a 3 s fade + 1 s silence.</summary>
    public static void WriteSong(string path, double root, double seconds, double intro)
    {
        int total = (int)(seconds * Rate);
        var samples = new short[total * 2];
        double lead = 0.8, fade = 3, tail = 1;
        for (int n = 0; n < total; n++)
        {
            double t = n / (double)Rate;
            double amp;
            if (t < lead || t > seconds - tail) amp = 0;
            else if (t < lead + intro) amp = 0.25;
            else if (t > seconds - tail - fade) amp = 0.6 * (seconds - tail - t) / fade;
            else amp = 0.6;
            double beat = 0.75 + 0.25 * Math.Cos(2 * Math.PI * 2 * t); // a gentle 120 bpm pulse
            double v = t < lead + intro
                ? Math.Sin(2 * Math.PI * root * t)
                : (Math.Sin(2 * Math.PI * root * t) + Math.Sin(2 * Math.PI * root * 1.25 * t) + Math.Sin(2 * Math.PI * root * 1.5 * t)) / 3;
            short s = (short)(Math.Clamp(v * amp * beat, -1, 1) * short.MaxValue);
            samples[2 * n] = s;
            samples[2 * n + 1] = s;
        }
        WriteWav(path, samples);
    }

    private static void WriteJingle(string path)
    {
        double[] notes = [523.25, 659.25, 783.99, 1046.5];
        int per = Rate / 4;
        var samples = new short[(notes.Length * per + Rate / 2) * 2];
        for (int k = 0; k < notes.Length; k++)
            for (int n = 0; n < per; n++)
            {
                double env = Math.Exp(-3.0 * n / per);
                short s = (short)(Math.Sin(2 * Math.PI * notes[k] * n / Rate) * 0.5 * env * short.MaxValue);
                samples[2 * (k * per + n)] = s;
                samples[2 * (k * per + n) + 1] = s;
            }
        WriteWav(path, samples);
    }

    public static void WriteWav(string path, short[] interleavedStereo)
    {
        using var w = new BinaryWriter(File.Create(path));
        int dataBytes = interleavedStereo.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var s in interleavedStereo) w.Write(s);
    }
}
