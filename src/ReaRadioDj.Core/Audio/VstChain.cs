using System.Runtime.InteropServices;
using System.Text;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>A VST effect in a slot of a mixer channel: the plugin file, on or bypassed, and its parameters (each 0..1).</summary>
public sealed record VstSlot(string Path, bool On = true, IReadOnlyList<float>? Params = null);

/// <summary>What a plugin file is (from loading it once, without a channel).</summary>
public sealed record VstPluginInfo(string Path, string Name, string Vendor, int Params, string? Error);

/// <summary>One parameter of a loaded plugin: its name, unit, the value as the plugin shows it, and the value 0..1.</summary>
public sealed record VstParam(int Index, string Name, string Unit, string Display, float Value);

/// <summary>Calls into bass_vst (VST 2.4 effects as a DSP on a BASS channel).</summary>
internal static unsafe partial class BassVst
{
    private const string Lib = "bass_vst";

    [StructLayout(LayoutKind.Sequential)]
    internal struct ParamInfo { public fixed byte Name[16]; public fixed byte Unit[16]; public fixed byte Display[16]; public float DefaultValue; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Info
    {
        public uint ChannelHandle, UniqueId;
        public fixed byte EffectName[80];
        public uint EffectVersion, EffectVstVersion, HostVstVersion;
        public fixed byte ProductName[80];
        public fixed byte VendorName[80];
        public uint VendorVersion, ChansIn, ChansOut, InitialDelay, HasEditor, EditorWidth, EditorHeight;
        public IntPtr AEffect;
        public uint IsInstrument, DspHandle;
    }

    [LibraryImport(Lib, EntryPoint = "BASS_VST_ChannelSetDSP", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SetDsp(int channel, string dllFile, uint flags, int priority);

    [LibraryImport(Lib, EntryPoint = "BASS_VST_ChannelRemoveDSP")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveDsp(int channel, int vst);

    [LibraryImport(Lib, EntryPoint = "BASS_VST_SetBypass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetBypass(int vst, [MarshalAs(UnmanagedType.Bool)] bool state);

    [LibraryImport(Lib, EntryPoint = "BASS_VST_GetParamCount")]
    internal static partial int GetParamCount(int vst);

    [LibraryImport(Lib, EntryPoint = "BASS_VST_GetParam")]
    internal static partial float GetParam(int vst, int index);

    [LibraryImport(Lib, EntryPoint = "BASS_VST_SetParam")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetParam(int vst, int index, float value);

    [LibraryImport(Lib, EntryPoint = "BASS_VST_GetParamInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetParamInfo(int vst, int index, out ParamInfo info);

    [LibraryImport(Lib, EntryPoint = "BASS_VST_GetInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetInfo(int vst, out Info info);

    internal static string Text(byte* p, int max)
    {
        int n = 0;
        while (n < max && p[n] != 0) n++;
        return Encoding.Latin1.GetString(p, n).Trim();
    }

    internal static string Error(string what) => BASS_ErrorGetCode() switch
    {
        2 => $"{what}: bestand niet gevonden",
        41 => $"{what}: geen VST 2-plug-in voor 64-bit (VST 3 wordt niet ondersteund)",
        3000 => $"{what}: dit is een instrument, geen effect",
        3001 => $"{what}: de plug-in heeft geen uitgangen",
        3002 => $"{what}: de plug-in kan niet realtime werken",
        var code => $"{what}: laden mislukt (BASS-fout {code})",
    };
}

/// <summary>
/// The VST slots of one mixer channel. They sit after the built-in effect and before the compressor. A plugin that
/// does not load leaves its slot empty with an error, so the rest of the channel keeps working.
/// </summary>
public sealed class VstChain(int channel)
{
    public const int Slots = 3;
    private const int FirstPriority = 14;   // between the built-in effect (15) and the compressor (10)

    private readonly string[] _paths = ["", "", ""];
    private readonly int[] _handles = new int[Slots];
    private readonly string?[] _errors = new string?[Slots];

    public string? Error(int slot) => _errors[slot];
    public bool Loaded(int slot) => _handles[slot] != 0;

    /// <summary>Loads, swaps or removes plugins so the slots match; sets bypass and stored parameters.</summary>
    public void Apply(IReadOnlyList<VstSlot> slots)
    {
        for (int i = 0; i < Slots; i++)
        {
            var want = i < slots.Count ? slots[i] : null;
            string path = want?.Path ?? "";
            if (path != _paths[i])
            {
                if (_handles[i] != 0) BassVst.RemoveDsp(channel, _handles[i]);
                _handles[i] = 0;
                _errors[i] = null;
                _paths[i] = path;
                if (path.Length > 0)
                {
                    try
                    {
                        _handles[i] = BassVst.SetDsp(channel, path, BASS_UNICODE, FirstPriority - i);
                        if (_handles[i] == 0) _errors[i] = BassVst.Error(System.IO.Path.GetFileName(path));
                        else if (want!.Params is { } values) SetAll(_handles[i], values);
                    }
                    catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
                    {
                        _errors[i] = "bass_vst.dll ontbreekt (voer scripts\\fetch-bass.ps1 uit)";
                    }
                }
            }
            if (_handles[i] != 0) BassVst.SetBypass(_handles[i], !(want?.On ?? false));
        }
    }

    private static void SetAll(int vst, IReadOnlyList<float> values)
    {
        int n = Math.Min(values.Count, BassVst.GetParamCount(vst));
        for (int p = 0; p < n; p++) BassVst.SetParam(vst, p, Math.Clamp(values[p], 0f, 1f));
    }

    public unsafe (string Name, string Vendor)? Info(int slot)
    {
        if (_handles[slot] == 0 || !BassVst.GetInfo(_handles[slot], out var info)) return null;
        string name = BassVst.Text(info.EffectName, 80);
        if (name.Length == 0) name = BassVst.Text(info.ProductName, 80);
        if (name.Length == 0) name = System.IO.Path.GetFileNameWithoutExtension(_paths[slot]);
        return (name, BassVst.Text(info.VendorName, 80));
    }

    public unsafe List<VstParam> Params(int slot)
    {
        var list = new List<VstParam>();
        int h = _handles[slot];
        if (h == 0) return list;
        int n = Math.Min(BassVst.GetParamCount(h), 256);
        for (int p = 0; p < n; p++)
        {
            BassVst.GetParamInfo(h, p, out var info);
            string name = BassVst.Text(info.Name, 16);
            list.Add(new VstParam(p, name.Length > 0 ? name : $"Parameter {p + 1}", BassVst.Text(info.Unit, 16), BassVst.Text(info.Display, 16), BassVst.GetParam(h, p)));
        }
        return list;
    }

    /// <summary>Sets one parameter; returns all current values (to store with the channel).</summary>
    public List<float>? SetParam(int slot, int index, float value)
    {
        int h = _handles[slot];
        if (h == 0 || index < 0 || index >= BassVst.GetParamCount(h)) return null;
        BassVst.SetParam(h, index, Math.Clamp(value, 0f, 1f));
        return Values(slot);
    }

    public List<float> Values(int slot)
    {
        int h = _handles[slot];
        if (h == 0) return [];
        int n = Math.Min(BassVst.GetParamCount(h), 256);
        var v = new List<float>(n);
        for (int p = 0; p < n; p++) v.Add(BassVst.GetParam(h, p));
        return v;
    }

    /// <summary>Loads a plugin file once without a channel to see whether it is a usable VST effect.</summary>
    public static unsafe VstPluginInfo Probe(string path)
    {
        string file = System.IO.Path.GetFileNameWithoutExtension(path);
        try
        {
            int h = BassVst.SetDsp(0, path, BASS_UNICODE, 0);
            if (h == 0) return new VstPluginInfo(path, file, "", 0, BassVst.Error(System.IO.Path.GetFileName(path)));
            try
            {
                BassVst.GetInfo(h, out var info);
                string name = BassVst.Text(info.EffectName, 80);
                if (name.Length == 0) name = BassVst.Text(info.ProductName, 80);
                return new VstPluginInfo(path, name.Length > 0 ? name : file, BassVst.Text(info.VendorName, 80), BassVst.GetParamCount(h), null);
            }
            finally { BassVst.RemoveDsp(0, h); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new VstPluginInfo(path, file, "", 0, "bass_vst.dll ontbreekt (voer scripts\\fetch-bass.ps1 uit)");
        }
    }
}
