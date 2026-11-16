using System.Diagnostics;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Presenter;
using Xunit.Abstractions;

namespace ReaRadioDj.Tests;

/// <summary>Needs the model (data\models\htdemucs.onnx, or REA_STEM_MODEL); without it the tests do nothing.</summary>
public sealed class StemSeparatorTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-stems-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string? Model() =>
        StemSeparator.FindModel(Environment.GetEnvironmentVariable("REA_STEM_MODEL"), Path.Combine(AppContext.BaseDirectory, "data"));

    [BassFact]
    public void The_four_stems_together_are_the_track_again()
    {
        if (Model() is not { } model) { output.WriteLine("geen model: overgeslagen"); return; }
        BassRuntime.Initialize(0);
        Directory.CreateDirectory(_dir);
        // 20 s of a chord with a beat at 48 kHz mono: the file is resampled to 44.1 kHz stereo on the way in
        const int rate = 48000;
        var x = new float[rate * 20];
        for (int i = 0; i < x.Length; i++)
        {
            double t = (double)i / rate;
            double beat = t % 0.5 < 0.05 ? Math.Sin(2 * Math.PI * 60 * t) * (1 - (t % 0.5) / 0.05) : 0;
            x[i] = (float)(0.15 * Math.Sin(2 * Math.PI * 220 * t) + 0.1 * Math.Sin(2 * Math.PI * 330 * t) + 0.4 * beat);
        }
        var input = Path.Combine(_dir, "in.wav");
        File.WriteAllBytes(input, VoiceProcessor.Write(rate, 1, x));
        var stems = Path.Combine(_dir, "out.wav");

        using var separator = new StemSeparator(model);
        var sw = Stopwatch.StartNew();
        double last = 0;
        separator.Separate(input, stems, new Progress<double>(p => last = p));
        output.WriteLine($"CPU, {separator.Threads} threads: 20 s in {sw.Elapsed.TotalSeconds:0.0} s");

        var (outRate, channels, samples) = VoiceProcessor.Read(File.ReadAllBytes(stems));
        Assert.Equal(StemSeparator.Rate, outRate);
        Assert.Equal(8, channels);
        Assert.InRange(samples.Length / channels, 44100 * 20 - 10, 44100 * 20 + 10);
        Assert.False(File.Exists(stems + ".part"));

        // the stems added up against the original (resampled), on the left channel, away from the edges
        double dot = 0, a2 = 0, b2 = 0;
        for (int f = 44100; f < 44100 * 19; f++)
        {
            double sum = 0;
            for (int s = 0; s < 4; s++) sum += samples[f * 8 + s * 2];
            double orig = x[(int)((long)f * rate / 44100)];
            dot += sum * orig; a2 += sum * sum; b2 += orig * orig;
        }
        double correlation = dot / Math.Sqrt(a2 * b2);
        output.WriteLine($"samenhang stems ↔ origineel: {correlation:0.000}");
        Assert.True(correlation > 0.95, $"samenhang {correlation:0.000}");
    }

    [BassFact]
    public void The_library_makes_the_stems_in_the_background_and_says_when_they_are_ready()
    {
        if (Model() is not { } model) { output.WriteLine("geen model: overgeslagen"); return; }
        BassRuntime.Initialize(0);
        Directory.CreateDirectory(_dir);
        var x = new float[44100 * 2 * 9];
        for (int i = 0; i < x.Length; i++) x[i] = (float)(0.2 * Math.Sin(2 * Math.PI * 330 * (i / 2) / 44100.0));
        var input = Path.Combine(_dir, "song.wav");
        File.WriteAllBytes(input, VoiceProcessor.Write(44100, 2, x));

        using var stems = new StemLibrary(Path.Combine(_dir, "stems"), model);
        using var done = new ManualResetEventSlim();
        string? ready = null;
        stems.Finished += (track, file) => { ready = file; done.Set(); };
        Assert.Equal(StemState.Waiting, stems.Request(input, now: true).State);
        Assert.Contains(stems.Request(input).State, new[] { StemState.Waiting, StemState.Working });   // asked twice: still once in the queue
        Assert.True(stems.Queue().Count <= 1);
        Assert.True(done.Wait(TimeSpan.FromMinutes(2)), "stems klaar binnen 2 minuten");
        Assert.Equal(stems.FileFor(input), ready);
        Assert.Equal(StemState.Ready, stems.Status(input).State);
        Assert.Empty(stems.Queue());
    }
}
