using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ReaRadioDj.Core.Model;
using static ReaRadioDj.Core.Audio.BassNative;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// An output bus: a playing BASS mixer on one sound card. "program" goes on air (and to the stream encoder),
/// "preview" is the PFL / pre-listen output for headphones.
/// </summary>
public sealed class OutputBus
{
    internal OutputBus(string id, string name, int device)
    {
        Id = id;
        Name = name;
        Device = device;
        BassRuntime.Use(device);
        Handle = BassRuntime.Check(BASS_Mixer_StreamCreate(BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT | BASS_MIXER_NONSTOP),
            $"BASS_Mixer_StreamCreate({id})");
        BassRuntime.Check(BASS_ChannelPlay(Handle, false), $"BASS_ChannelPlay({id})");
    }

    public string Id { get; }
    public string Name { get; }
    /// <summary>Requested device index: -1 default, 0 no sound, 1.. a device from BassRuntime.GetDevices().</summary>
    public int Device { get; private set; }
    public int Handle { get; }

    /// <summary>Moves the bus to another sound card while it keeps playing.</summary>
    public void SetDevice(int device)
    {
        uint real = BassRuntime.EnsureDevice(device);
        BassRuntime.Check(BASS_ChannelSetDevice(Handle, real), $"BASS_ChannelSetDevice({Id})");
        Device = device;
    }

    public (float Left, float Right) Levels()
    {
        var l = new float[2];
        return BASS_ChannelGetLevelEx(Handle, l, 0.05f, BASS_LEVEL_STEREO) ? (l[0], l[1]) : (0, 0);
    }

    internal void Free() => BASS_StreamFree(Handle);
}

/// <summary>
/// A mixer channel strip: a decoding sub-mixer that feeds an output bus. Players add their sources to a strip.
/// The strip has a fader, a mute switch, a level meter and a duck level (set by talkover and overlays).
/// </summary>
public sealed class MixerStrip
{
    private readonly Dictionary<string, float> _ducks = [];
    private readonly object _gate = new();

    internal MixerStrip(string id, string name, OutputBus bus, int handle, bool addToBus = true)
    {
        Id = id;
        Name = name;
        Bus = bus;
        Handle = handle;
        if (addToBus)
            BassRuntime.Check(BASS_Mixer_StreamAddChannel(bus.Handle, handle, BASS_MIXER_CHAN_BUFFER | BASS_MIXER_CHAN_NORAMPIN),
                $"BASS_Mixer_StreamAddChannel({id})");
    }

    internal static MixerStrip CreateSubMixer(string id, string name, OutputBus bus)
    {
        int h = BassRuntime.Check(BASS_Mixer_StreamCreate(BassRuntime.SampleRate, 2,
            BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE | BASS_MIXER_NONSTOP), $"BASS_Mixer_StreamCreate({id})");
        return new MixerStrip(id, name, bus, h);
    }

    public string Id { get; }
    public string Name { get; }
    public OutputBus Bus { get; }
    /// <summary>The decoding mixer that players add their sources to.</summary>
    public int Handle { get; }
    public float Volume { get; private set; } = 1f;
    public bool Muted { get; private set; }

    /// <summary>The lowest active duck level (1 = not ducked).</summary>
    public float DuckLevel { get { lock (_gate) return _ducks.Count == 0 ? 1f : _ducks.Values.Min(); } }

    public void SetVolume(float volume, bool? mute = null)
    {
        Volume = Math.Clamp(volume, 0f, 1.25f);
        if (mute is bool m) Muted = m;
        Apply(60);
    }

    /// <summary>Ducks the strip under a named reason (null level = release that reason).</summary>
    public void Duck(string reason, float? level, int milliseconds)
    {
        lock (_gate)
        {
            if (level is float l) _ducks[reason] = Math.Clamp(l, 0f, 1f);
            else if (!_ducks.Remove(reason)) return;
        }
        Apply(milliseconds);
    }

    public bool IsDuckedBy(string reason) { lock (_gate) return _ducks.ContainsKey(reason); }

    private void Apply(int milliseconds)
    {
        float target = Muted ? 0f : Volume * DuckLevel;
        BASS_ChannelSlideAttribute(Handle, BASS_ATTRIB_VOL, target, (uint)Math.Max(0, milliseconds));
    }

    public (float Left, float Right) Levels()
    {
        var l = new float[2];
        return BASS_Mixer_ChannelGetLevelEx(Handle, l, 0.05f, BASS_LEVEL_STEREO) ? (l[0], l[1]) : (0, 0);
    }

    internal void Free()
    {
        BASS_Mixer_ChannelRemove(Handle);
        BASS_StreamFree(Handle);
    }
}

/// <summary>
/// The studio mixer: the output buses, the channel strips and the microphone. A 20 ms control thread drives the
/// clip players (cartwall, overlays, standby and PFL players).
/// </summary>
public sealed class AudioRouter : IDisposable
{
    public const string LogStrip = "log", StandbyStrip = "standby", CartStrip = "cart", OverlayStrip = "overlay",
        MicStrip = "mic", PflStrip = "pfl";

    private readonly ReaRadioDjOptions _options;
    private readonly ILogger _logger;
    private readonly List<ITickable> _tickables = [];
    private readonly Dictionary<string, MixerStrip> _strips = [];
    private Thread? _thread;
    private volatile bool _running;
    private int _push, _record;

    public AudioRouter(ReaRadioDjOptions options, ILogger<AudioRouter>? logger = null)
    {
        _options = options;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public bool IsStarted { get; private set; }
    public OutputBus Program { get; private set; } = null!;
    public OutputBus Preview { get; private set; } = null!;
    public IReadOnlyCollection<MixerStrip> Strips => _strips.Values;
    public MixerStrip Strip(string id) => _strips[id];

    public bool MicAvailable { get; private set; }
    public bool MicOpen { get; private set; }
    public string MicStatus { get; private set; } = "uit";

    /// <summary>Raised when the microphone opens or closes (true = open).</summary>
    public event Action<bool>? MicChanged;

    public void Start()
    {
        if (IsStarted) return;
        BassRuntime.Initialize(_options.OutputDevice);
        Program = new OutputBus("program", "Uitzending", _options.OutputDevice);
        Preview = new OutputBus("preview", "PFL", _options.Routing.PreviewDevice);

        Add(MixerStrip.CreateSubMixer(LogStrip, "Automatisering", Program));
        Add(MixerStrip.CreateSubMixer(StandbyStrip, "Standby", Program));
        Add(MixerStrip.CreateSubMixer(CartStrip, "Cartwall", Program));
        Add(MixerStrip.CreateSubMixer(OverlayStrip, "Overlay", Program));
        Add(MixerStrip.CreateSubMixer(PflStrip, "PFL", Preview));

        // Microphone: a push stream fed by the recording callback, muted until the mic is opened.
        _push = BassRuntime.Check(BASS_StreamCreate(BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, STREAMPROC_PUSH, IntPtr.Zero),
            "BASS_StreamCreate(push)");
        var mic = new MixerStrip(MicStrip, "Microfoon", Program, _push);
        Add(mic);
        mic.Duck("closed", 0f, 0);
        if (_options.Routing.MicEnabled) StartRecording(_options.Routing.MicDevice);
        else MicStatus = "uitgeschakeld in de instellingen";

        IsStarted = true;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "REA-Radio-DJ router", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Add(MixerStrip s) => _strips[s.Id] = s;

    private unsafe void StartRecording(int device)
    {
        try
        {
            if (!BASS_RecordInit(device) && BASS_ErrorGetCode() != BASS_ERROR_ALREADY) throw new BassException($"BASS_RecordInit({device})");
            delegate* unmanaged[Stdcall]<int, IntPtr, uint, IntPtr, int> proc = &RecordProc;
            _record = BassRuntime.Check(BASS_RecordStart(BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT, (IntPtr)proc, _push),
                "BASS_RecordStart");
            MicAvailable = true;
            MicStatus = "dicht";
        }
        catch (BassException ex)
        {
            _logger.LogWarning("Microphone not available: {Message}", ex.Message);
            MicStatus = "geen invoerapparaat";
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int RecordProc(int handle, IntPtr buffer, uint length, IntPtr user)
    {
        BASS_StreamPutData((int)user, buffer, length);
        return 1;
    }

    /// <summary>Talkover: opens the microphone and ducks the music strips (also works without an input device).</summary>
    public void SetMic(bool open)
    {
        if (!IsStarted || open == MicOpen) return;
        MicOpen = open;
        var mic = Strip(MicStrip);
        float duck = (float)_options.Routing.MicDuckLevel;
        if (open)
        {
            mic.Duck("closed", null, 40);
            foreach (var id in new[] { LogStrip, StandbyStrip, CartStrip }) Strip(id).Duck("mic", duck, 250);
        }
        else
        {
            mic.Duck("closed", 0f, 120);
            foreach (var id in new[] { LogStrip, StandbyStrip, CartStrip }) Strip(id).Duck("mic", null, 800);
        }
        if (MicAvailable) MicStatus = open ? "open" : "dicht";
        MicChanged?.Invoke(open);
    }

    internal void Register(ITickable t) { lock (_tickables) _tickables.Add(t); }

    private void Loop()
    {
        while (_running)
        {
            ITickable[] list;
            lock (_tickables) list = [.. _tickables];
            foreach (var t in list)
            {
                try { t.Tick(); }
                catch (Exception ex) { _logger.LogError(ex, "Router tick failed"); }
            }
            Thread.Sleep(20);
        }
    }

    public static float LoudnessGain(MediaItem media, double targetLufs)
    {
        if (media.Lufs is not double lufs) return 1f;
        double db = Math.Clamp(targetLufs - lufs, -15, 6);
        return (float)Math.Pow(10, db / 20);
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(500);
        if (!IsStarted) return;
        if (_record != 0) BASS_ChannelStop(_record);
        foreach (var s in _strips.Values) s.Free();
        Program.Free();
        Preview.Free();
        IsStarted = false;
    }
}

internal interface ITickable { void Tick(); }
