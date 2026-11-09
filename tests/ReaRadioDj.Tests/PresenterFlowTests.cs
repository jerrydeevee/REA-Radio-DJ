using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;

namespace ReaRadioDj.Tests;

public sealed class VoiceProcessorTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "rea-voice-" + Guid.NewGuid().ToString("N") + ".wav");

    public void Dispose() { try { File.Delete(_file); } catch (IOException) { } }

    /// <summary>A "voice": 0.5 s silence, 1 s of a quiet tone with a loud burst and DC rumble, 0.5 s silence; 24 kHz mono.</summary>
    internal static float[] Voice(int rate = 24000)
    {
        var x = new float[rate * 2];
        for (int i = rate / 2; i < rate * 3 / 2; i++)
        {
            double t = i / (double)rate;
            double amp = i is > 30000 and < 32000 ? 0.9 : 0.1;
            x[i] = (float)(amp * Math.Sin(2 * Math.PI * 300 * t) + 0.05 * Math.Sin(2 * Math.PI * 20 * t));
        }
        return x;
    }

    [Fact]
    public void Wav_round_trips_as_16_bit()
    {
        var x = new float[] { 0, 0.5f, -0.5f, 0.25f };
        var (rate, channels, y) = VoiceProcessor.Read(VoiceProcessor.Write(24000, 1, x));
        Assert.Equal(24000, rate);
        Assert.Equal(1, channels);
        for (int i = 0; i < x.Length; i++) Assert.Equal(x[i], y[i], 3);
    }

    [Fact]
    public void Process_trims_silence_and_sets_the_peak()
    {
        File.WriteAllBytes(_file, VoiceProcessor.Write(24000, 1, Voice()));
        var r = VoiceProcessor.Process(_file);

        Assert.Equal(2.0, r.SecondsIn, 2);
        Assert.InRange(r.SecondsOut, 1.0, 1.2);   // 1 s of sound plus a little room at both ends
        var (_, _, y) = VoiceProcessor.Read(File.ReadAllBytes(_file));
        double peakDb = 20 * Math.Log10(y.Max(Math.Abs));
        Assert.InRange(peakDb, -1.2, -0.8);
        Assert.Equal(0, y[0], 3);                 // faded in, no click
    }

    [Fact]
    public void Compressor_narrows_the_difference_between_loud_and_quiet()
    {
        var x = Voice();
        static double Ratio(float[] v) => v.Skip(30500).Take(1000).Max(Math.Abs) / v.Skip(20000).Take(1000).Max(Math.Abs);
        double before = Ratio(x);
        VoiceProcessor.Compress(x, 24000, 1, new VoiceProcessor.Settings());
        Assert.True(Ratio(x) < before * 0.75, $"loud/quiet {before:0.0} should shrink, is {Ratio(x):0.0}");
    }

    [Fact]
    public void High_pass_removes_rumble()
    {
        int rate = 24000;
        var x = Enumerable.Range(0, rate).Select(i => (float)Math.Sin(2 * Math.PI * 20 * i / rate)).ToArray();
        VoiceProcessor.HighPass(x, rate, 1, 80);
        Assert.True(x.Skip(rate / 2).Max(Math.Abs) < 0.1);
    }

    [Fact]
    public void Server_root_accepts_both_forms() =>
        Assert.Equal(OpenAiScriptWriter.Root("http://localhost:11434/v1/"), OpenAiScriptWriter.Root("http://localhost:11434"));
}
