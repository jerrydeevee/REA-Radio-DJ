using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ReaRadioDj.Core.Voip;

/// <summary>How to open an RTP line. RemoteHost empty = answer whoever sends to the local port (symmetric RTP).</summary>
public sealed record RtpLineOptions
{
    public int LocalPort { get; init; } = 5004;
    public string RemoteHost { get; init; } = "";
    public int RemotePort { get; init; } = 5004;
    public string Codec { get; init; } = "pcma";
    /// <summary>How much the jitter buffer holds before it plays, in milliseconds.</summary>
    public double JitterMs { get; init; } = 60;
    /// <summary>The RTP payload type agreed in SIP/SDP; null = the codec's own (static) type.</summary>
    public int? PayloadType { get; init; }
}

/// <summary>The state of a line and what it measured.</summary>
public sealed record RtpLineStatus(string State, string Codec, int LocalPort, string? Remote, long Sent, double SecondsSinceAudio,
    JitterStats Jitter, DateTime OpenedUtc, string? Error);

/// <summary>
/// One live audio connection over RTP in both directions: what comes in goes through the jitter buffer to
/// <see cref="ReadPlayout"/> (pulled by the sound card), what is written with <see cref="WriteReturn"/> (the return
/// feed, at the engine's rate) is resampled, encoded and sent in 20 ms packets. Independent of BASS, so two lines can
/// talk to each other in a test.
/// </summary>
public sealed class RtpLine : IDisposable
{
    private readonly IRtpCodec _codec;
    private readonly int _pt;
    private readonly UdpClient _udp;
    private readonly JitterBuffer _jitter;
    private readonly Resampler _toCodec;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _returnGate = new();
    private readonly float[] _return;
    private int _returnStart, _returnCount;
    private readonly float[] _frame;
    private int _frameUsed;
    private readonly Thread _receiver, _sender;
    private volatile bool _running = true;
    private volatile IPEndPoint? _remote;
    private readonly bool _latch;
    private readonly uint _ssrc = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
    private ushort _seq = (ushort)Random.Shared.Next(0, ushort.MaxValue);
    private uint _timestamp = (uint)Random.Shared.NextInt64(0, uint.MaxValue);
    private long _sent;
    private long _lastAudioTicks = -1;
    private string? _error;

    public RtpLine(RtpLineOptions options, int inputRate)
    {
        Options = options;
        InputRate = inputRate;
        _codec = RtpCodecs.Create(options.Codec);
        _pt = options.PayloadType is >= 0 and <= 127 ? options.PayloadType.Value : _codec.PayloadType;
        _jitter = new JitterBuffer(_codec, options.JitterMs);
        _toCodec = new Resampler(inputRate, _codec.SampleRate);
        _return = new float[inputRate]; // one second
        _frame = new float[_codec.FrameSamples];
        _frameUsed = _frame.Length;

        _udp = new UdpClient(AddressFamily.InterNetwork);
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
        try { _udp.Client.IOControl(-1744830452 /* SIO_UDP_CONNRESET */, [0, 0, 0, 0], null); } catch (Exception) { } // Windows: ignore ICMP port unreachable
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, options.LocalPort));
        LocalPort = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        _udp.Client.ReceiveTimeout = 500;

        if (!string.IsNullOrWhiteSpace(options.RemoteHost))
        {
            var address = IPAddress.TryParse(options.RemoteHost.Trim(), out var ip) ? ip
                : Dns.GetHostAddresses(options.RemoteHost.Trim()).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                  ?? throw new ArgumentException($"Adres niet gevonden: {options.RemoteHost}");
            _remote = new IPEndPoint(address, options.RemotePort);
        }
        else _latch = true;

        _receiver = new Thread(Receive) { IsBackground = true, Name = "RTP in " + LocalPort, Priority = ThreadPriority.AboveNormal };
        _sender = new Thread(Send) { IsBackground = true, Name = "RTP out " + LocalPort, Priority = ThreadPriority.AboveNormal };
        _receiver.Start();
        _sender.Start();
    }

    public RtpLineOptions Options { get; }
    public int InputRate { get; }
    public int LocalPort { get; }
    public IRtpCodec Codec => _codec;
    /// <summary>The rate of <see cref="ReadPlayout"/>: the codec's rate.</summary>
    public int SampleRate => _codec.SampleRate;
    public DateTime OpenedUtc { get; } = DateTime.UtcNow;
    public IPEndPoint? Remote => _remote;

    /// <summary>Fills <paramref name="output"/> (mono, <see cref="SampleRate"/>) from the jitter buffer; silence when there is nothing.</summary>
    public void ReadPlayout(Span<float> output)
    {
        int i = 0;
        while (i < output.Length)
        {
            if (_frameUsed >= _frame.Length)
            {
                _jitter.Pop(_frame);
                _frameUsed = 0;
            }
            int n = Math.Min(output.Length - i, _frame.Length - _frameUsed);
            _frame.AsSpan(_frameUsed, n).CopyTo(output[i..]);
            _frameUsed += n;
            i += n;
        }
    }

    /// <summary>Adds return audio (mono, at the engine's rate) to send to the far end. Called on the audio thread.</summary>
    public void WriteReturn(ReadOnlySpan<float> mono)
    {
        lock (_returnGate)
        {
            foreach (var v in mono)
            {
                if (_returnCount == _return.Length) { _returnStart = (_returnStart + 1) % _return.Length; _returnCount--; } // full: drop the oldest
                _return[(_returnStart + _returnCount) % _return.Length] = v;
                _returnCount++;
            }
        }
    }

    private void Receive()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            byte[] data;
            try { data = _udp.Receive(ref any); }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut or SocketError.ConnectionReset) { continue; }
            catch (Exception) when (!_running) { return; }
            catch (Exception ex) { _error = ex.Message; continue; }

            if (RtpPacket.Read(data) is not { } p || p.PayloadType != _pt) continue;
            if (_latch && (_remote is null || !_remote.Equals(any))) _remote = new IPEndPoint(any.Address, any.Port);
            _jitter.Push(p.Sequence, p.Timestamp, p.Payload.Span, _clock.Elapsed.TotalMilliseconds);
            Interlocked.Exchange(ref _lastAudioTicks, _clock.ElapsedTicks);
        }
    }

    private void Send()
    {
        int inFrame = InputRate / 50;
        var input = new float[inFrame];
        var resampled = new List<float>(_codec.FrameSamples * 2);
        var frame = new float[_codec.FrameSamples];
        var payload = new byte[_codec.FrameSamples * 2];
        var packet = new byte[RtpPacket.HeaderSize + payload.Length];
        bool first = true;
        while (_running)
        {
            bool took = false;
            lock (_returnGate)
            {
                if (_returnCount >= inFrame)
                {
                    for (int i = 0; i < inFrame; i++) input[i] = _return[(_returnStart + i) % _return.Length];
                    _returnStart = (_returnStart + inFrame) % _return.Length;
                    _returnCount -= inFrame;
                    took = true;
                }
            }
            if (!took) { Thread.Sleep(4); continue; }

            _toCodec.Process(input, resampled);
            while (resampled.Count >= frame.Length)
            {
                resampled.CopyTo(0, frame, 0, frame.Length);
                resampled.RemoveRange(0, frame.Length);
                int len = _codec.Encode(frame, payload);
                if (_remote is { } to)
                {
                    int size = new RtpPacket((byte)_pt, first, _seq, _timestamp, _ssrc, payload.AsMemory(0, len)).Write(packet);
                    try { _udp.Send(packet, size, to); Interlocked.Increment(ref _sent); first = false; }
                    catch (SocketException ex) { _error = ex.Message; }
                    catch (ObjectDisposedException) { return; }
                }
                _seq++;
                _timestamp += (uint)(_codec.FrameSamples * _codec.ClockRate / _codec.SampleRate);
            }
        }
    }

    public RtpLineStatus Status()
    {
        long last = Interlocked.Read(ref _lastAudioTicks);
        double since = last < 0 ? -1 : (_clock.ElapsedTicks - last) / (double)Stopwatch.Frequency;
        string state = last < 0 ? "wacht op audio" : since < 2 ? "verbonden" : "geen audio";
        return new RtpLineStatus(state, _codec.Name, LocalPort, _remote?.ToString(), Interlocked.Read(ref _sent), Math.Round(since, 1),
            _jitter.Stats(), OpenedUtc, _error);
    }

    public void Dispose()
    {
        _running = false;
        _udp.Dispose();
        _receiver.Join(1000);
        _sender.Join(1000);
    }
}
