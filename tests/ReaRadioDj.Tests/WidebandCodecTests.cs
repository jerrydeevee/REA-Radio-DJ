using System.Diagnostics;
using ReaRadioDj.Core.Voip;
using ReaRadioDj.Core.Voip.Sip;

namespace ReaRadioDj.Tests;

public sealed class WidebandCodecTests
{
    /// <summary>Encodes and decodes a signal frame by frame with one codec.</summary>
    private static float[] RoundTrip(IRtpCodec codec, float[] x)
    {
        var y = new float[x.Length];
        var payload = new byte[4000];
        for (int f = 0; f + codec.FrameSamples <= x.Length; f += codec.FrameSamples)
        {
            int len = codec.Encode(x.AsSpan(f, codec.FrameSamples), payload);
            Assert.True(len > 0);
            Assert.Equal(codec.FrameSamples, codec.Decode(payload.AsSpan(0, len), y.AsSpan(f, codec.FrameSamples)));
        }
        return y;
    }

    [Fact]
    public void G722_is_wideband_one_byte_per_two_samples_and_keeps_a_tone_up_to_7_khz()
    {
        var codec = new G722();
        Assert.Equal(("G722/8000", 9, 16000, 8000), (codec.Rtpmap, codec.PayloadType, codec.SampleRate, codec.ClockRate));
        var payload = new byte[400];
        Assert.Equal(160, codec.Encode(new float[320], payload));         // 20 ms = 160 bytes = 64 kbit/s

        foreach (double hz in new[] { 300.0, 1000, 5000, 6500 })
        {
            var x = Signal.Sine(hz, 16000, 16000, 0.4);
            var y = RoundTrip(new G722(), x);
            Assert.InRange(Signal.Level(y.AsSpan(8000, 4000), hz, 16000), 0.33, 0.45);  // a G.711 line stops at 3.4 kHz
        }
    }

    [Fact]
    public void G722_matches_ffmpeg_both_ways_when_ffmpeg_is_here()
    {
        string? ffmpeg = Find("ffmpeg");
        if (ffmpeg is null) return;
        string dir = Path.Combine(Path.GetTempPath(), "rea-g722-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // a chord with some movement, 16 kHz, 2 s
            var x = new float[32000];
            for (int i = 0; i < x.Length; i++)
                x[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 440 * i / 16000) * (1 + 0.5 * Math.Sin(2 * Math.PI * 3 * i / 16000))
                               + 0.1 * Math.Sin(2 * Math.PI * 3100 * i / 16000));
            var pcm = new byte[x.Length * 2];
            for (int i = 0; i < x.Length; i++) BitConverter.TryWriteBytes(pcm.AsSpan(2 * i), RtpCodecs.ToPcm16(x[i]));
            File.WriteAllBytes(Path.Combine(dir, "in.raw"), pcm);

            // ffmpeg encodes, we decode: compare with ffmpeg's own decode of the same bytes
            Run(ffmpeg, "-f s16le -ar 16000 -ac 1 -i in.raw -c:a g722 -f g722 theirs.g722", dir);
            Run(ffmpeg, "-f g722 -i theirs.g722 -f s16le -ar 16000 -ac 1 theirs.raw", dir);
            var theirBytes = File.ReadAllBytes(Path.Combine(dir, "theirs.g722"));
            var ours = new float[theirBytes.Length * 2];
            new G722().Decode(theirBytes, ours);
            var theirDecode = Shorts(File.ReadAllBytes(Path.Combine(dir, "theirs.raw")));
            int n = Math.Min(ours.Length, theirDecode.Length);
            double maxDiff = 0;
            for (int i = 0; i < n; i++) maxDiff = Math.Max(maxDiff, Math.Abs(ours[i] - theirDecode[i]));
            Assert.True(maxDiff < 0.002, $"decoder differs from ffmpeg by {maxDiff:0.0000}");

            // we encode: the same bytes as ffmpeg's encoder, and ffmpeg decodes it back to the signal
            var mine = new byte[x.Length / 2];
            new G722().Encode(x, mine);
            int same = mine.Zip(theirBytes).Count(p => p.First == p.Second);
            Assert.True(same >= mine.Length * 0.99, $"{same} of {mine.Length} bytes the same as ffmpeg");
            File.WriteAllBytes(Path.Combine(dir, "ours.g722"), mine);
            Run(ffmpeg, "-f g722 -i ours.g722 -f s16le -ar 16000 -ac 1 back.raw", dir);
            var back = Shorts(File.ReadAllBytes(Path.Combine(dir, "back.raw")));
            Assert.InRange(Signal.Level(back.AsSpan(8000, 16000), 3100, 16000), 0.08, 0.12);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Opus_carries_full_band_audio_and_fills_in_a_lost_packet()
    {
        var codec = new OpusRtp();
        Assert.Equal(("opus/48000/2", 48000, 48000, 960), (codec.Rtpmap, codec.SampleRate, codec.ClockRate, codec.FrameSamples));
        foreach (double hz in new[] { 200.0, 1000, 10000, 15000 })
        {
            var x = Signal.Sine(hz, 48000, 48000, 0.4);
            var y = RoundTrip(new OpusRtp(), x);
            Assert.InRange(Signal.Level(y.AsSpan(24000, 9600), hz, 48000), 0.3, 0.5);
        }
        var payload = new byte[4000];
        int len = codec.Encode(Signal.Sine(1000, 48000, 960, 0.4), payload);
        Assert.InRange(len, 20, 400);                                     // 64 kbit/s: about 160 bytes per 20 ms

        // a lost packet: the decoder makes up a frame that carries on, not silence
        var line = new OpusRtp();
        var tone = Signal.Sine(1000, 48000, 960 * 20, 0.4);
        var frame = new float[960];
        for (int f = 0; f < 19; f++)
        {
            int l = line.Encode(tone.AsSpan(f * 960, 960), payload);
            line.Decode(payload.AsSpan(0, l), frame);
        }
        Assert.True(line.Conceal(frame));
        Assert.True(Signal.Level(frame, 1000, 48000) > 0.1);
    }

    [Fact]
    public void The_offer_carries_opus_parameters_and_a_far_end_with_g722_or_opus_is_matched()
    {
        string offer = SdpAudio.Build("10.0.0.1", 4000, [new OpusRtp(), new G722(), new G711(false)], 1);
        Assert.Contains("m=audio 4000 RTP/AVP 111 9 8", offer);
        Assert.Contains("a=rtpmap:111 opus/48000/2", offer);
        Assert.Contains("a=fmtp:111 minptime=10;useinbandfec=1", offer);
        Assert.Contains("a=rtpmap:9 G722/8000", offer);

        // LUCI Live style: Opus on payload type 96, written in capitals
        var luci = SdpAudio.Parse("v=0\r\nc=IN IP4 10.0.0.2\r\nm=audio 5000 RTP/AVP 96 9 8\r\na=rtpmap:96 OPUS/48000/2\r\n")!;
        var chosen = luci.Choose(["opus", "g722", "pcma"])!.Value;
        Assert.Equal(("opus", 96), (chosen.Codec.Name, chosen.PayloadType));
        // a desk phone: G.722 as a static type without rtpmap
        var phone = SdpAudio.Parse("v=0\r\nc=IN IP4 10.0.0.3\r\nm=audio 6000 RTP/AVP 9 8 0\r\n")!;
        Assert.Equal("g722", phone.Choose(["opus", "g722", "pcma"])!.Value.Codec.Name);
    }

    private static float[] Shorts(byte[] b)
    {
        var s = new float[b.Length / 2];
        for (int i = 0; i < s.Length; i++) s[i] = BitConverter.ToInt16(b, 2 * i) / 32768f;
        return s;
    }

    private static string? Find(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d.Trim(), exe + ".exe")).FirstOrDefault(File.Exists);

    private static void Run(string exe, string args, string dir)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, "-hide_banner -loglevel error -y " + args)
            { WorkingDirectory = dir, RedirectStandardError = true, UseShellExecute = false })!;
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit(30000);
        Assert.True(p.ExitCode == 0, err);
    }
}
