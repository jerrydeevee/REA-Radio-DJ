using System.Runtime.InteropServices;
using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Server.Hardware;

/// <summary>
/// A Mackie Control surface on a MIDI port pair (found by name, like <see cref="MidiConnection"/>). Unlike a plain
/// MIDI device it passes every message on raw (pitch bend for the faders) and can send SysEx for the display.
/// </summary>
public sealed class MackieConnection : IHwConnection
{
    private const uint CallbackFunction = 0x30000, MimData = 0x3C3;
    private delegate void MidiInProc(IntPtr handle, uint msg, IntPtr instance, IntPtr param1, IntPtr param2);

    [StructLayout(LayoutKind.Sequential)]
    private struct MidiHdr
    {
        public IntPtr Data; public uint BufferLength, BytesRecorded; public IntPtr User; public uint Flags;
        public IntPtr Next, Reserved; public uint Offset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public IntPtr[] ReservedArray;
    }

    [DllImport("winmm.dll")] private static extern int midiInOpen(out IntPtr handle, uint id, MidiInProc callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiInReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiInClose(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiOutOpen(out IntPtr handle, uint id, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] private static extern int midiOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int midiOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int midiOutLongMsg(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int midiOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiOutClose(IntPtr handle);

    private readonly MidiInProc _callback;   // kept alive as long as the connection: Windows calls it
    private readonly Action<long, uint> _received;
    private readonly object _sendGate = new();
    private IntPtr _in, _out;

    public HwDevice Device { get; }
    public bool Alive => _in != IntPtr.Zero && _out != IntPtr.Zero;
    public string Status { get; }

    public MackieConnection(HwDevice device, Action<long, uint> received)
    {
        Device = device;
        _received = received;
        _callback = OnMessage;
        int inIndex = MidiConnection.InputNames().ToList().FindIndex(n => n.Equals(device.Port, StringComparison.OrdinalIgnoreCase));
        int outIndex = MidiConnection.OutputNames().ToList().FindIndex(n => n.Equals(device.Port, StringComparison.OrdinalIgnoreCase));
        if (inIndex >= 0 && midiInOpen(out _in, (uint)inIndex, _callback, IntPtr.Zero, CallbackFunction) == 0) midiInStart(_in);
        else _in = IntPtr.Zero;
        if (outIndex < 0 || midiOutOpen(out _out, (uint)outIndex, IntPtr.Zero, IntPtr.Zero, 0) != 0) _out = IntPtr.Zero;
        Status = (_in, _out) switch
        {
            (0, 0) => $"MIDI-apparaat “{device.Port}” niet gevonden of in gebruik",
            (_, 0) => "alleen de ingang is open: faders, lampjes en display volgen niet",
            (0, _) => "alleen de uitgang is open: de controller bedient niets",
            _ => "verbonden (Mackie Control)",
        };
    }

    private void OnMessage(IntPtr handle, uint msg, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        if (msg != MimData) return;
        uint message = (uint)param1.ToInt64();
        ThreadPool.QueueUserWorkItem(_ => _received(Device.Id, message));
    }

    public void Send(MackieOutput o)
    {
        lock (_sendGate)
        {
            if (_out == IntPtr.Zero) return;
            if (o.SysEx is null) { midiOutShortMsg(_out, o.Short); return; }
            // SysEx: the buffer and header must stay put until Windows is done with them (synchronous for most drivers).
            var data = Marshal.AllocHGlobal(o.SysEx.Length);
            int size = Marshal.SizeOf<MidiHdr>();
            var header = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(o.SysEx, 0, data, o.SysEx.Length);
                Marshal.StructureToPtr(new MidiHdr { Data = data, BufferLength = (uint)o.SysEx.Length, BytesRecorded = (uint)o.SysEx.Length, ReservedArray = new IntPtr[8] }, header, false);
                if (midiOutPrepareHeader(_out, header, (uint)size) != 0) return;
                midiOutLongMsg(_out, header, (uint)size);
                // MIDIERR_STILLPLAYING (65) while the driver still sends: wait a little.
                for (int i = 0; i < 50 && midiOutUnprepareHeader(_out, header, (uint)size) == 65; i++) Thread.Sleep(2);
            }
            finally
            {
                Marshal.FreeHGlobal(header);
                Marshal.FreeHGlobal(data);
            }
        }
    }

    /// <summary>Tallies are not used on a Mackie surface: its lamps follow the mixer.</summary>
    public void Show(HwOutput output, bool on) { }

    public void Dispose()
    {
        if (_in != IntPtr.Zero) { midiInStop(_in); midiInReset(_in); midiInClose(_in); _in = IntPtr.Zero; }
        lock (_sendGate)
            if (_out != IntPtr.Zero) { midiOutReset(_out); midiOutClose(_out); _out = IntPtr.Zero; }
    }
}
