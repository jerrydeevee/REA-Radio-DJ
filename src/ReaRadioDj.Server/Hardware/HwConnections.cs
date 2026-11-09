using System.IO.Ports;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Server.Hardware;

/// <summary>An open device: reports events through the callback given when it was opened and shows tallies.</summary>
public interface IHwConnection : IDisposable
{
    HwDevice Device { get; }
    /// <summary>False once the connection broke; the service then opens it again.</summary>
    bool Alive { get; }
    string Status { get; }
    void Show(HwOutput output, bool on);
}

/// <summary>MIDI in and out through the Windows multimedia API (winmm), found by device name.</summary>
public sealed class MidiConnection : IHwConnection
{
    private const uint CallbackFunction = 0x30000, MimData = 0x3C3;
    private delegate void MidiInProc(IntPtr handle, uint msg, IntPtr instance, IntPtr param1, IntPtr param2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiInCaps { public ushort Mid, Pid; public uint DriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; public uint Support; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiOutCaps
    {
        public ushort Mid, Pid; public uint DriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public ushort Technology, Voices, Notes, ChannelMask; public uint Support;
    }

    [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int midiInGetDevCapsW(UIntPtr id, ref MidiInCaps caps, uint size);
    [DllImport("winmm.dll")] private static extern int midiInOpen(out IntPtr handle, uint id, MidiInProc callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiInClose(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int midiOutGetDevCapsW(UIntPtr id, ref MidiOutCaps caps, uint size);
    [DllImport("winmm.dll")] private static extern int midiOutOpen(out IntPtr handle, uint id, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] private static extern int midiOutClose(IntPtr handle);

    public static IReadOnlyList<string> InputNames()
    {
        var names = new List<string>();
        for (uint i = 0; i < midiInGetNumDevs(); i++)
        {
            var caps = new MidiInCaps();
            if (midiInGetDevCapsW((UIntPtr)i, ref caps, (uint)Marshal.SizeOf<MidiInCaps>()) == 0) names.Add(caps.Name);
        }
        return names;
    }

    public static IReadOnlyList<string> OutputNames()
    {
        var names = new List<string>();
        for (uint i = 0; i < midiOutGetNumDevs(); i++)
        {
            var caps = new MidiOutCaps();
            if (midiOutGetDevCapsW((UIntPtr)i, ref caps, (uint)Marshal.SizeOf<MidiOutCaps>()) == 0) names.Add(caps.Name);
        }
        return names;
    }

    private readonly MidiInProc _callback; // kept alive as long as the connection: Windows calls it
    private readonly Action<HwEvent> _report;
    private IntPtr _in, _out;

    public HwDevice Device { get; }
    public bool Alive => _in != IntPtr.Zero || _out != IntPtr.Zero;
    public string Status { get; }

    public MidiConnection(HwDevice device, Action<HwEvent> report)
    {
        Device = device;
        _report = report;
        _callback = OnMessage;
        int inIndex = InputNames().ToList().FindIndex(n => n.Equals(device.Port, StringComparison.OrdinalIgnoreCase));
        int outIndex = OutputNames().ToList().FindIndex(n => n.Equals(device.Port, StringComparison.OrdinalIgnoreCase));
        if (inIndex >= 0 && midiInOpen(out _in, (uint)inIndex, _callback, IntPtr.Zero, CallbackFunction) == 0) midiInStart(_in);
        else _in = IntPtr.Zero;
        if (outIndex < 0 || midiOutOpen(out _out, (uint)outIndex, IntPtr.Zero, IntPtr.Zero, 0) != 0) _out = IntPtr.Zero;
        Status = (_in, _out) switch
        {
            (0, 0) => $"MIDI-apparaat “{device.Port}” niet gevonden of in gebruik",
            (_, 0) => "verbonden (alleen ingang)",
            (0, _) => "verbonden (alleen uitgang)",
            _ => "verbonden",
        };
    }

    private void OnMessage(IntPtr handle, uint msg, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        if (msg != MimData) return;
        if (HwSignals.FromMidi(Device.Id, (uint)param1.ToInt64()) is { } e) ThreadPool.QueueUserWorkItem(_ => _report(e));
    }

    public void Show(HwOutput output, bool on)
    {
        if (_out == IntPtr.Zero || HwSignals.Parse(output.Target) is not { } a || HwSignals.ToMidi(a, on) is not uint message) return;
        midiOutShortMsg(_out, message);
    }

    public void Dispose()
    {
        if (_in != IntPtr.Zero) { midiInStop(_in); midiInReset(_in); midiInClose(_in); _in = IntPtr.Zero; }
        if (_out != IntPtr.Zero) { midiOutClose(_out); _out = IntPtr.Zero; }
    }
}

/// <summary>A serial port: every received line is an event ("on"); tallies send their on or off text.</summary>
public sealed class SerialConnection : IHwConnection
{
    private readonly SerialPort _port;
    private readonly StringBuilder _pending = new();
    private readonly Action<HwEvent> _report;
    private volatile bool _alive;

    public HwDevice Device { get; }
    public bool Alive => _alive && _port.IsOpen;
    public string Status { get; private set; }

    public SerialConnection(HwDevice device, Action<HwEvent> report)
    {
        Device = device;
        _report = report;
        _port = new SerialPort(device.Port, device.Baud) { ReadTimeout = 500, WriteTimeout = 500, Encoding = Encoding.Latin1 };
        try
        {
            _port.DataReceived += OnData;
            _port.ErrorReceived += (_, _) => { };
            _port.Open();
            _alive = true;
            Status = $"verbonden ({device.Baud} baud)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Status = $"{device.Port} niet te openen: {ex.Message}";
        }
    }

    private void OnData(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            string data = _port.ReadExisting();
            IReadOnlyList<string> lines;
            lock (_pending) lines = HwSignals.Lines(_pending, data);
            foreach (var line in lines) _report(new HwEvent(Device.Id, line, true));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { _alive = false; Status = "verbinding verbroken"; }
    }

    public void Show(HwOutput output, bool on)
    {
        string text = on ? output.OnText : output.OffText;
        if (text.Length == 0 || !Alive) return;
        try
        {
            var bytes = HwSignals.Unescape(text);
            _port.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException) { _alive = false; Status = "schrijven mislukt: " + ex.Message; }
    }

    public void Dispose()
    {
        _alive = false;
        try { _port.Close(); } catch (IOException) { }
        _port.Dispose();
    }
}

/// <summary>A Livewire node over LWRP (TCP 93): subscribes to its GPI ports and sets GPO pins for tallies.</summary>
public sealed class LivewireConnection : IHwConnection
{
    private readonly TcpClient _client = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<HwEvent> _report;
    private StreamWriter? _writer;
    private volatile bool _alive;

    public HwDevice Device { get; }
    public bool Alive => _alive;
    public string Status { get; private set; } = "verbinden…";

    public LivewireConnection(HwDevice device, Action<HwEvent> report)
    {
        Device = device;
        _report = report;
        _ = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        try
        {
            string host = Device.Port;
            int port = HwSignals.LivewirePort;
            if (host.LastIndexOf(':') is int colon and > 0 && int.TryParse(host[(colon + 1)..], out int p)) { host = host[..colon]; port = p; }
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connect.CancelAfter(TimeSpan.FromSeconds(5));
            await _client.ConnectAsync(host, port, connect.Token);
            var stream = _client.GetStream();
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
            if (Device.Password.Length > 0) await _writer.WriteLineAsync("LOGIN " + Device.Password);
            await _writer.WriteLineAsync("ADD GPI");
            _alive = true;
            Status = "verbonden";
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (!_cts.IsCancellationRequested && await reader.ReadLineAsync(_cts.Token) is { } line)
                foreach (var e in HwSignals.FromLivewire(Device.Id, line)) _report(e);
            Status = "verbinding verbroken";
        }
        catch (OperationCanceledException) { Status = _cts.IsCancellationRequested ? "gesloten" : $"{Device.Port} reageert niet"; }
        catch (Exception ex) when (ex is IOException or SocketException) { Status = $"{Device.Port}: {ex.Message}"; }
        finally { _alive = false; }
    }

    public void Show(HwOutput output, bool on)
    {
        if (!_alive || _writer is null || HwSignals.Parse(output.Target) is not { Kind: "gpo" } a) return;
        try { lock (_writer) _writer.WriteLine(HwSignals.ToLivewire(a, on)); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { _alive = false; Status = "schrijven mislukt"; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _client.Dispose();
    }
}
