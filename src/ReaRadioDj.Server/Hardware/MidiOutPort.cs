using System.Runtime.InteropServices;
using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Server.Hardware;

/// <summary>A MIDI output found by name that takes short messages and SysEx.</summary>
public sealed class MidiOutPort : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MidiHdr
    {
        public IntPtr Data; public uint BufferLength, BytesRecorded; public IntPtr User; public uint Flags;
        public IntPtr Next, Reserved; public uint Offset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public IntPtr[] ReservedArray;
    }

    [DllImport("winmm.dll")] private static extern int midiOutOpen(out IntPtr handle, uint id, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int midiOutShortMsg(IntPtr handle, uint message);
    [DllImport("winmm.dll")] private static extern int midiOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int midiOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int midiOutLongMsg(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int midiOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern int midiOutClose(IntPtr handle);

    private readonly object _gate = new();
    private IntPtr _out;

    /// <summary>Opens the output with this name; <see cref="Open"/> tells whether that worked (not found, or in use).</summary>
    public MidiOutPort(string name)
    {
        int index = MidiConnection.OutputNames().ToList().FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || midiOutOpen(out _out, (uint)index, IntPtr.Zero, IntPtr.Zero, 0) != 0) _out = IntPtr.Zero;
    }

    public bool Open => _out != IntPtr.Zero;

    /// <summary>Sends a message; false when the port is closed or the driver refused it.</summary>
    public bool Send(MackieOutput o)
    {
        lock (_gate)
        {
            if (_out == IntPtr.Zero) return false;
            if (o.SysEx is null) return midiOutShortMsg(_out, o.Short) == 0;
            // SysEx: the buffer and header must stay put until Windows is done with them (synchronous for most drivers).
            var data = Marshal.AllocHGlobal(o.SysEx.Length);
            int size = Marshal.SizeOf<MidiHdr>();
            var header = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(o.SysEx, 0, data, o.SysEx.Length);
                Marshal.StructureToPtr(new MidiHdr { Data = data, BufferLength = (uint)o.SysEx.Length, BytesRecorded = (uint)o.SysEx.Length, ReservedArray = new IntPtr[8] }, header, false);
                if (midiOutPrepareHeader(_out, header, (uint)size) != 0) return false;
                bool sent = midiOutLongMsg(_out, header, (uint)size) == 0;
                // MIDIERR_STILLPLAYING (65) while the driver still sends: wait a little.
                for (int i = 0; i < 50 && midiOutUnprepareHeader(_out, header, (uint)size) == 65; i++) Thread.Sleep(2);
                return sent;
            }
            finally
            {
                Marshal.FreeHGlobal(header);
                Marshal.FreeHGlobal(data);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
            if (_out != IntPtr.Zero) { midiOutReset(_out); midiOutClose(_out); _out = IntPtr.Zero; }
    }
}
