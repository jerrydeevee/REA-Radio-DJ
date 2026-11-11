using System.Diagnostics;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Voip;

namespace ReaRadioDj.Tests;

internal static class Signal
{
    public static float[] Sine(double freq, int rate, int samples, double amp = 0.5, long offset = 0)
    {
        var s = new float[samples];
        for (int i = 0; i < samples; i++) s[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * (i + offset) / rate));
        return s;
    }

    /// <summary>Amplitude of one frequency in a block (Goertzel), 1 = a full-scale sine.</summary>
    public static double Level(ReadOnlySpan<float> x, double freq, int rate)
    {
        double w = 2 * Math.PI * freq / rate, c = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        foreach (var v in x) { double s0 = v + c * s1 - s2; s2 = s1; s1 = s0; }
        double power = s1 * s1 + s2 * s2 - c * s1 * s2;
        return 2 * Math.Sqrt(Math.Max(0, power)) / x.Length;
    }

    public static double Db(double a) => 20 * Math.Log10(Math.Max(a, 1e-9));
}

public sealed class VoipCodecTests
{
    [Fact]
    public void G711_has_the_standard_silence_codes_and_round_trips_within_its_steps()
    {
        Assert.Equal(0xFF, G711.MuLaw(0));
        Assert.Equal(0xD5, G711.ALaw(0));
        foreach (bool mu in new[] { true, false })
        {
            var codec = new G711(mu);
            var tone = Signal.Sine(1000, 8000, 160, 0.5);
            var payload = new byte[160];
            Assert.Equal(160, codec.Encode(tone, payload));
            var back = new float[160];
            codec.Decode(payload, back);
            double err = tone.Zip(back, (a, b) => (double)(a - b) * (a - b)).Sum(), sig = tone.Sum(v => (double)v * v);
            Assert.True(10 * Math.Log10(sig / err) > 30, $"{codec.Name}: SNR too low"); // G.711 is about 38 dB
        }
    }

    [Fact]
    public void L16_is_big_endian_and_exact_at_16_bit()
    {
        var codec = new L16();
        var payload = new byte[4];
        codec.Encode([0.5f, -1f], payload);
        Assert.Equal(new byte[] { 0x40, 0x00, 0x80, 0x01 }, payload); // 16384 and -32767
        var back = new float[2];
        codec.Decode(payload, back);
        Assert.Equal(0.5f, back[0], 4);
    }

    [Fact]
    public void Unknown_codecs_are_refused_with_the_list()
    {
        var ex = Assert.Throws<ArgumentException>(() => RtpCodecs.Create("ulcc"));
        Assert.Contains("pcma", ex.Message);
    }

    [Fact]
    public void Rtp_packets_survive_write_and_read_and_skip_csrc_extension_and_padding()
    {
        var payload = new byte[] { 1, 2, 3 };
        var buf = new byte[64];
        int n = new RtpPacket(8, true, 65535, 123456, 0xDEADBEEF, payload).Write(buf);
        var p = RtpPacket.Read(buf.AsMemory(0, n))!.Value;
        Assert.Equal((8, true, (ushort)65535, 123456u, 0xDEADBEEFu), (p.PayloadType, p.Marker, p.Sequence, p.Timestamp, p.Ssrc));
        Assert.Equal(payload, p.Payload.ToArray());

        // one CSRC, a one-word extension and two bytes of padding around the same payload
        var raw = new byte[] { 0xB1, 0x00, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, /*csrc*/ 9, 9, 9, 9, /*ext*/ 0xBE, 0xDE, 0, 1, 7, 7, 7, 7, 1, 2, 3, 0, 2 };
        var q = RtpPacket.Read(raw)!.Value;
        Assert.Equal(payload, q.Payload.ToArray());
        Assert.Null(RtpPacket.Read(new byte[] { 0x40, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })); // version 1
    }

    [Fact]
    public void The_resampler_keeps_the_tone_and_filters_what_the_lower_rate_cannot_carry()
    {
        var down = new Resampler(48000, 8000);
        var output = new List<float>();
        for (int block = 0; block < 10; block++) down.Process(Signal.Sine(1000, 48000, 4800, 0.5, block * 4800L), output);
        var steady = output.Skip(800).Take(4000).ToArray();
        Assert.InRange(Signal.Level(steady, 1000, 8000), 0.47, 0.53);
        Assert.InRange(output.Count, 7900, 8000); // one second in, one second out (minus the filter delay)

        var alias = new Resampler(48000, 8000);
        var a = new List<float>();
        alias.Process(Signal.Sine(6000, 48000, 48000, 0.5), a); // 6 kHz cannot exist at 8 kHz
        Assert.True(a.Skip(400).Max(v => Math.Abs(v)) < 0.02);

        var up = new Resampler(8000, 44100);
        var u = new List<float>();
        up.Process(Signal.Sine(440, 8000, 8000, 0.5), u);
        Assert.InRange(Signal.Level(u.Skip(4410).Take(22050).ToArray(), 440, 44100), 0.47, 0.53);
    }
}

public sealed class JitterBufferTests
{
    private static readonly G711 Codec = new(mu: false);

    private static byte[] Frame(byte marker) => Enumerable.Repeat(marker, 160).ToArray();

    // the first byte of a decoded frame tells which packet it was
    private static short Which(float[] frame) => (short)Math.Round(frame[0] * 32768);

    [Fact]
    public void It_waits_for_the_target_then_plays_in_order_also_across_the_wrap()
    {
        var jb = new JitterBuffer(Codec, targetMs: 60);
        var f = new float[160];
        ushort[] order = [65534, 65535, 1, 0]; // out of order and past 65535
        for (int i = 0; i < 2; i++) jb.Push(order[i], (uint)(i * 160), Frame((byte)(0x10 + i)), i * 20);
        Assert.False(jb.Pop(f)); // 40 ms is not enough yet
        jb.Push(order[2], 480, Frame(0x13), 60);
        jb.Push(order[3], 320, Frame(0x12), 65);
        var played = new List<short>();
        for (int i = 0; i < 4; i++) { Assert.True(jb.Pop(f)); played.Add(Which(f)); }
        var expected = new byte[] { 0x10, 0x11, 0x12, 0x13 }.Select(b => G711.ALawDecode(b)).ToList();
        Assert.Equal(expected, played);
        Assert.Equal(0, jb.Stats().Lost);
    }

    [Fact]
    public void A_missing_packet_is_filled_in_and_a_late_one_is_dropped()
    {
        var jb = new JitterBuffer(Codec, targetMs: 40);
        var f = new float[160];
        jb.Push(10, 0, Frame(0x20), 0);
        jb.Push(12, 320, Frame(0x22), 40);
        jb.Push(13, 480, Frame(0x23), 60);
        Assert.True(jb.Pop(f));             // 10
        Assert.True(jb.Pop(f));             // 11 is missing: concealed from 10, quieter
        Assert.Equal(G711.ALawDecode(0x20) * 0.7, Which(f), 1.0);
        jb.Push(11, 160, Frame(0x21), 70);  // too late now
        jb.Push(12, 320, Frame(0x22), 71);  // twice
        Assert.True(jb.Pop(f));
        Assert.Equal(G711.ALawDecode(0x22), Which(f));
        var s = jb.Stats();
        Assert.Equal((1L, 1L, 1L), (s.Lost, s.Late, s.Duplicates));
    }

    [Fact]
    public void Running_dry_counts_an_underrun_and_waits_again()
    {
        var jb = new JitterBuffer(Codec, targetMs: 20);
        var f = new float[160];
        jb.Push(1, 0, Frame(0x30), 0);
        Assert.True(jb.Pop(f));
        Assert.False(jb.Pop(f));
        Assert.Equal(1, jb.Stats().Underruns);
    }

    [Fact]
    public void Steady_packets_have_no_jitter_and_uneven_ones_do()
    {
        var even = new JitterBuffer(Codec);
        var uneven = new JitterBuffer(Codec);
        for (int i = 0; i < 100; i++)
        {
            even.Push((ushort)i, (uint)(i * 160), Frame(0), i * 20.0);
            uneven.Push((ushort)i, (uint)(i * 160), Frame(0), i * 20.0 + (i % 2 == 0 ? 0 : 15));
        }
        Assert.Equal(0, even.Stats().JitterMs);
        Assert.InRange(uneven.Stats().JitterMs, 10, 16);
    }
}

/// <summary>Two lines over the loopback: what one sends, the other plays (both directions).</summary>
public sealed class RtpLineTests
{
    private static async Task Feed(RtpLine line, double freq, int rate, TimeSpan time, CancellationToken ct)
    {
        int block = rate / 100; // 10 ms
        long written = 0;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < time && !ct.IsCancellationRequested)
        {
            long due = (long)(clock.Elapsed.TotalSeconds * rate);
            while (written + block <= due)
            {
                line.WriteReturn(Signal.Sine(freq, rate, block, 0.4, written));
                written += block;
            }
            await Task.Delay(5, ct).ContinueWith(_ => { });
        }
    }

    private static float[] Listen(RtpLine line, TimeSpan time)
    {
        var all = new List<float>();
        var buf = new float[line.SampleRate / 50];
        var clock = Stopwatch.StartNew();
        long taken = 0;
        while (clock.Elapsed < time)
        {
            long due = (long)(clock.Elapsed.TotalSeconds * line.SampleRate);
            while (taken + buf.Length <= due) { line.ReadPlayout(buf); all.AddRange(buf); taken += buf.Length; }
            Thread.Sleep(5);
        }
        return [.. all];
    }

    [Theory]
    [InlineData("pcma")]
    [InlineData("pcmu")]
    [InlineData("l16")]
    public async Task Two_lines_hear_each_other(string codec)
    {
        using var a = new RtpLine(new RtpLineOptions { LocalPort = 0, Codec = codec, JitterMs = 60 }, 44100);
        using var b = new RtpLine(new RtpLineOptions { LocalPort = 0, RemoteHost = "127.0.0.1", RemotePort = a.LocalPort, Codec = codec, JitterMs = 60 }, 48000);
        using var cts = new CancellationTokenSource();
        // b calls a; a has no address and answers whoever sends (symmetric RTP)
        var feedB = Feed(b, 440, 48000, TimeSpan.FromSeconds(2.5), cts.Token);
        var feedA = Feed(a, 1000, 44100, TimeSpan.FromSeconds(2.5), cts.Token);
        var atA = Task.Run(() => Listen(a, TimeSpan.FromSeconds(2.2)));
        var atB = Task.Run(() => Listen(b, TimeSpan.FromSeconds(2.2)));
        float[] heardA = await atA, heardB = await atB;
        cts.Cancel();
        await Task.WhenAll(feedA, feedB);

        int ra = a.SampleRate, rb = b.SampleRate;
        var tailA = heardA[^(ra / 2)..];
        var tailB = heardB[^(rb / 2)..];
        Assert.InRange(Signal.Level(tailA, 440, ra), 0.36, 0.44);
        Assert.True(Signal.Level(tailA, 1000, ra) < 0.01); // only the far end, not itself
        Assert.InRange(Signal.Level(tailB, 1000, rb), 0.36, 0.44);
        Assert.Equal("verbonden", a.Status().State);
        Assert.Equal(0, a.Status().Jitter.Lost);
        Assert.StartsWith("127.0.0.1:", a.Status().Remote);
    }
}

[Collection("bass")]
public sealed class VoipChannelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-voip-" + Guid.NewGuid().ToString("N"));

    public VoipChannelTests()
    {
        Directory.CreateDirectory(_dir);
        if (BassRuntime.IsAvailable) BassRuntime.Initialize(0);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [BassFact]
    public async Task The_return_feed_has_the_programme_but_not_the_caller()
    {
        var options = new ReaRadioDjOptions
        {
            MusicFolder = _dir, JingleFolder = _dir, DataFolder = _dir, OutputDevice = 0,
            Routing = new RoutingOptions { PreviewDevice = 0 },
        };
        using var router = new AudioRouter(options);
        router.Start();
        // programme: a 1 kHz line-up tone on air
        router.AddInput(new InputChannel("in-tone", "Toon", InputChannel.ToneSource));
        router.ApplyChannel("in-tone", new ChannelSettings { OnAir = true });

        // the studio side on VoIP 1, the caller on a line of its own over the loopback
        var studio = new RtpLine(new RtpLineOptions { LocalPort = 0, Codec = "l16", JitterMs = 60 }, BassRuntime.SampleRate);
        using var caller = new RtpLine(new RtpLineOptions { LocalPort = 0, RemoteHost = "127.0.0.1", RemotePort = studio.LocalPort, Codec = "l16" }, 48000);
        var voip = router.Voip(AudioRouter.Voip1Strip);
        voip.Attach(studio);
        router.ApplyChannel(AudioRouter.Voip1Strip, new ChannelSettings { OnAir = true });

        using var cts = new CancellationTokenSource();
        var talk = Task.Run(async () =>
        {
            // the caller says 440 Hz
            var clock = Stopwatch.StartNew();
            long written = 0;
            while (!cts.IsCancellationRequested)
            {
                long due = (long)(clock.Elapsed.TotalSeconds * 48000);
                while (written + 480 <= due) { caller.WriteReturn(Signal.Sine(440, 48000, 480, 0.4, written)); written += 480; }
                await Task.Delay(5).ConfigureAwait(false);
            }
        });
        var heard = new List<float>();
        var buf = new float[960];
        var listen = Stopwatch.StartNew();
        long taken = 0;
        while (listen.Elapsed < TimeSpan.FromSeconds(3))
        {
            long due = (long)(listen.Elapsed.TotalSeconds * 48000);
            while (taken + 960 <= due) { caller.ReadPlayout(buf); heard.AddRange(buf); taken += 960; }
            await Task.Delay(5);
        }
        // the caller's own voice is in the programme (on air) ...
        var program = router.Strip(AudioRouter.Voip1Strip).Levels();
        cts.Cancel();
        await talk;
        voip.Detach()?.Dispose();

        Assert.True(program.Left > 0.1, "the caller should be on air");
        var tail = heard.Skip(heard.Count - 24000).ToArray();
        double tone = Signal.Level(tail, 1000, 48000), self = Signal.Level(tail, 440, 48000);
        Assert.True(tone > 0.05, $"programme in the return: {tone:0.000}");
        Assert.True(Signal.Db(self) < Signal.Db(tone) - 30, $"caller hears their own voice: {Signal.Db(self):0.0} dB against {Signal.Db(tone):0.0} dB");
    }
}
