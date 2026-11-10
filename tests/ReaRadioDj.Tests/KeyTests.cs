using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Tests;

public sealed class MusicalKeyTests
{
    [Theory]
    [InlineData("Am", 9, true, "8A")]
    [InlineData("C", 0, false, "8B")]
    [InlineData("G", 7, false, "9B")]
    [InlineData("F", 5, false, "7B")]
    [InlineData("B", 11, false, "1B")]
    [InlineData("F#m", 6, true, "11A")]
    [InlineData("Bbm", 10, true, "3A")]
    [InlineData("Ebmin", 3, true, "2A")]
    [InlineData("C major", 0, false, "8B")]
    [InlineData("8A", 9, true, "8A")]
    [InlineData("12b", 4, false, "12B")]
    [InlineData("1A", 8, true, "1A")]
    public void Notation_and_camelot_agree(string text, int tonic, bool minor, string camelot)
    {
        var k = MusicalKey.Parse(text);
        Assert.NotNull(k);
        Assert.Equal(tonic, k.Tonic);
        Assert.Equal(minor, k.Minor);
        Assert.Equal(camelot, k.Camelot);
        Assert.Equal(k, MusicalKey.Parse(k.Name));       // the stored name reads back the same
        Assert.Equal(k, MusicalKey.Parse(k.Camelot));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("H")]
    [InlineData("13A")]
    [InlineData("Cx")]
    public void Rubbish_is_no_key(string? text) => Assert.Null(MusicalKey.Parse(text));

    [Fact]
    public void Harmonic_neighbours_are_the_relative_and_one_step_round_the_wheel()
    {
        var am = MusicalKey.Parse("8A")!;
        Assert.True(MusicalKey.Harmonic(am, MusicalKey.Parse("8B")!));   // C major
        Assert.True(MusicalKey.Harmonic(am, MusicalKey.Parse("7A")!));
        Assert.True(MusicalKey.Harmonic(am, MusicalKey.Parse("9A")!));
        Assert.False(MusicalKey.Harmonic(am, MusicalKey.Parse("10A")!));
        Assert.False(MusicalKey.Harmonic(am, MusicalKey.Parse("9B")!));
        Assert.True(MusicalKey.Harmonic(MusicalKey.Parse("12A")!, MusicalKey.Parse("1A")!));  // the wheel wraps
    }

    [Fact]
    public void ReplayGain_brings_a_track_to_minus_18_lufs()
    {
        Assert.Equal(-4, ReplayGain.TrackGain(-14));
        Assert.Equal(3.5, ReplayGain.TrackGain(-21.5));
        Assert.Null(ReplayGain.TrackGain(null));
    }

    [Fact]
    public void A_chroma_with_the_notes_of_a_triad_gives_that_key_and_flat_noise_gives_none()
    {
        var chroma = new double[12];
        foreach (int pc in new[] { 9, 0, 4 }) chroma[pc] = 3;   // A C E
        chroma[2] = chroma[11] = chroma[7] = 1;               // D B G: the rest of the scale
        Assert.Equal("Am", KeyAnalyzer.Best(chroma)?.Name);
        Assert.Null(KeyAnalyzer.Best(Enumerable.Repeat(1.0, 12).ToArray()));
    }
}

[Collection("bass")]
public sealed class KeyAnalyzerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-key-tests-" + Guid.NewGuid().ToString("N"));

    public KeyAnalyzerTests()
    {
        Directory.CreateDirectory(_dir);
        if (BassRuntime.IsAvailable) BassRuntime.Initialize(0);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Chords of two seconds each, with a bass note and a few overtones: 16 bit stereo, 44.1 kHz.</summary>
    private string Chords(string name, int[][] chords, double seconds)
    {
        const int rate = 44100;
        int frames = (int)(seconds * rate);
        var data = new byte[frames * 4];
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / rate;
            var chord = chords[(int)(t / 2) % chords.Length];
            double v = 0;
            foreach (int midi in chord.Append(chord[0] - 12))
            {
                double hz = 440 * Math.Pow(2, (midi - 69) / 12.0);
                for (int h = 1; h <= 4; h++) v += 0.05 / h * Math.Sin(Math.Tau * hz * h * t);
            }
            short s = (short)(Math.Clamp(v, -1, 1) * 32000);
            data[i * 4] = data[i * 4 + 2] = (byte)s;
            data[i * 4 + 1] = data[i * 4 + 3] = (byte)(s >> 8);
        }
        string path = Path.Combine(_dir, name + ".wav");
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + data.Length); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2);
        w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16); w.Write("data"u8); w.Write(data.Length); w.Write(data);
        return path;
    }

    [BassFact]
    public void The_key_of_a_chord_progression_is_measured()
    {
        // A minor: Am - Dm - E - Am; C major: C - F - G - C (MIDI note numbers around middle C).
        int[][] aMinor = [[57, 60, 64], [62, 65, 69], [64, 68, 71], [57, 60, 64]];
        int[][] cMajor = [[60, 64, 67], [65, 69, 72], [67, 71, 74], [60, 64, 67]];
        Assert.Equal("Am", KeyAnalyzer.Analyze(Chords("am", aMinor, 60))?.Name);
        Assert.Equal("C", KeyAnalyzer.Analyze(Chords("c", cMajor, 60))?.Name);
        Assert.Null(KeyAnalyzer.Analyze(Chords("short", cMajor, 8)));   // too short to say
    }
}
