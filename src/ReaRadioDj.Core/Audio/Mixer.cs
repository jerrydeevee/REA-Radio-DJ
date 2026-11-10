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
    internal OutputBus(string id, string name, int device, uint speakers = 0)
    {
        Id = id;
        Name = name;
        Device = device;
        BassRuntime.Use(device);
        Handle = BassRuntime.Check(BASS_Mixer_StreamCreate((uint)BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT | BASS_MIXER_NONSTOP | speakers),
            $"BASS_Mixer_StreamCreate({id})");
        BassRuntime.Check(BASS_ChannelPlay(Handle, false), $"BASS_ChannelPlay({id})");
    }

    /// <summary>A bus on an ASIO card: a decoding mixer that the ASIO driver pulls audio from.</summary>
    private OutputBus(string id, string name, OutputTarget asio)
    {
        Id = id;
        Name = name;
        Device = -1;
        Asio = asio;
        Handle = BassRuntime.Check(BASS_Mixer_StreamCreate((uint)BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT | BASS_MIXER_NONSTOP | BASS_STREAM_DECODE),
            $"BASS_Mixer_StreamCreate({id})");
        try { AsioRuntime.Attach(asio.Device, asio.Pair * 2, Handle); }
        catch { BASS_StreamFree(Handle); throw; }
    }

    internal static OutputBus OnAsio(string id, string name, OutputTarget target) => new(id, name, target);

    /// <summary>The ASIO output of this bus, or null for a Windows card.</summary>
    public OutputTarget? Asio { get; }

    public string Id { get; }
    public string Name { get; }
    /// <summary>Requested device index: -1 default, 0 no sound, 1.. a device from BassRuntime.GetDevices().</summary>
    public int Device { get; private set; }
    public int Handle { get; }

    /// <summary>Moves the bus to another sound card while it keeps playing.</summary>
    public void SetDevice(int device)
    {
        if (Asio is not null) throw new InvalidOperationException("Deze uitgang loopt via ASIO; kies de kaart bij ASIO en start de engine opnieuw.");
        uint real = BassRuntime.EnsureDevice(device);
        BassRuntime.Check(BASS_ChannelSetDevice(Handle, real), $"BASS_ChannelSetDevice({Id})");
        Device = device;
    }

    public (float Left, float Right) Levels()
    {
        // A decoding (ASIO) mixer must not be read here: that would take audio away from the card.
        if (Asio is { } a) return AsioRuntime.Levels(a.Device, a.Pair * 2);
        var l = new float[2];
        return BASS_ChannelGetLevelEx(Handle, l, 0.05f, BASS_LEVEL_STEREO) ? (l[0], l[1]) : (0, 0);
    }

    internal void Free()
    {
        if (Asio is { } a) AsioRuntime.Detach(a.Device, a.Pair * 2);
        BASS_StreamFree(Handle);
    }
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
        Processor = new ChannelProcessor(handle);
        Recorder = new ChannelRecorder(handle);
        if (addToBus)
            BassRuntime.Check(BASS_Mixer_StreamAddChannel(bus.Handle, handle, BASS_MIXER_CHAN_BUFFER | BASS_MIXER_CHAN_NORAMPIN),
                $"BASS_Mixer_StreamAddChannel({id})");
    }

    internal static MixerStrip CreateSubMixer(string id, string name, OutputBus bus)
    {
        int h = BassRuntime.Check(BASS_Mixer_StreamCreate((uint)BassRuntime.SampleRate, 2,
            BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE | BASS_MIXER_NONSTOP), $"BASS_Mixer_StreamCreate({id})");
        return new MixerStrip(id, name, bus, h);
    }

    public string Id { get; }
    public string Name { get; }
    public OutputBus Bus { get; private set; }
    /// <summary>The bus the strip goes to when it has no output of its own.</summary>
    internal OutputBus? HomeBus { get; set; }
    /// <summary>The decoding mixer that players add their sources to.</summary>
    public int Handle { get; }
    public float Volume { get; private set; } = 1f;
    public bool Muted { get; private set; }
    /// <summary>Gain, balance, equaliser, compressor and automatic level of this channel.</summary>
    public ChannelProcessor Processor { get; }
    /// <summary>Recording of this channel to a WAV file (after the channel strip, before the fader).</summary>
    public ChannelRecorder Recorder { get; }
    /// <summary>The source of an input channel ("rec:1", "tone"); null for the players' channels.</summary>
    public string? Source { get; internal set; }
    public bool OnAir => Processor.Settings.OnAir;

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

    /// <summary>Moves the strip to another output bus; what plays on it carries on.</summary>
    internal void MoveTo(OutputBus bus)
    {
        if (bus == Bus) return;
        BASS_Mixer_ChannelRemove(Handle);
        BassRuntime.Check(BASS_Mixer_StreamAddChannel(bus.Handle, Handle, BASS_MIXER_CHAN_BUFFER | BASS_MIXER_CHAN_NORAMPIN),
            $"BASS_Mixer_StreamAddChannel({Id} -> {bus.Id})");
        Bus = bus;
        Apply(0);
    }

    internal void Free()
    {
        Recorder.Stop();
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
        MicStrip = "mic", PflStrip = "pfl", DjStrip = "dj";

    private readonly ReaRadioDjOptions _options;
    private readonly ILogger _logger;
    private readonly List<ITickable> _tickables = [];
    // Copied on every change (inputs come and go while the engine runs), so readers never see a dictionary being changed.
    private volatile Dictionary<string, MixerStrip> _strips = [];
    private readonly Dictionary<string, (int Record, GCHandle Tone)> _inputs = [];
    private readonly Dictionary<string, OutputBus> _outputs = [];   // outputs of their own, by target id
    private readonly object _routeGate = new();
    private Thread? _thread;
    private volatile bool _running;
    private int _push, _record;

    public AudioRouter(ReaRadioDjOptions options, ILogger<AudioRouter>? logger = null)
    {
        _options = options;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        AsioRuntime.UseLogger(_logger);
    }

    public bool IsStarted { get; private set; }
    public OutputBus Program { get; private set; } = null!;
    public OutputBus Preview { get; private set; } = null!;
    public IReadOnlyCollection<MixerStrip> Strips => _strips.Values;
    /// <summary>Limiter and loudness measurement on the on-air bus.</summary>
    public MasterProcessor Master { get; } = new();
    public MixerStrip Strip(string id) => _strips[id];
    public bool HasStrip(string id) => _strips.ContainsKey(id);

    public bool MicAvailable { get; private set; }
    public bool MicOpen { get; private set; }
    public string MicStatus { get; private set; } = "uit";

    /// <summary>Raised when the microphone opens or closes (true = open).</summary>
    public event Action<bool>? MicChanged;

    public void Start()
    {
        if (IsStarted) return;
        BassRuntime.Initialize(_options.OutputDevice);
        AsioRuntime.BufferSamples = _options.Routing.AsioBuffer;
        Program = Bus("program", "Uitzending", _options.Routing.ProgramAsio, _options.OutputDevice);
        Preview = Bus("preview", "PFL", _options.Routing.PreviewAsio, _options.Routing.PreviewDevice);
        Master.Attach(Program.Handle);
        try { Master.SetLimiter(_options.Routing.LimiterEnabled, _options.Routing.LimiterCeilingDb); }
        catch (Exception ex) when (ex is BassException or DllNotFoundException) { _logger.LogWarning("Limiter not available: {Message}", ex.Message); StartNotes.Add($"Begrenzer niet beschikbaar: {ex.Message}"); }

        Add(MixerStrip.CreateSubMixer(LogStrip, "Automatisering", Program));
        Add(MixerStrip.CreateSubMixer(StandbyStrip, "Standby", Program));
        Add(MixerStrip.CreateSubMixer(CartStrip, "Cartwall", Program));
        Add(MixerStrip.CreateSubMixer(OverlayStrip, "Overlay", Program));
        Add(MixerStrip.CreateSubMixer(DjStrip, "DJ", Program));
        Add(MixerStrip.CreateSubMixer(PflStrip, "PFL", Preview));

        // Microphone: a push stream fed by the recording callback, muted until the mic is opened.
        _push = BassRuntime.Check(BASS_StreamCreate((uint)BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, STREAMPROC_PUSH, IntPtr.Zero),
            "BASS_StreamCreate(push)");
        var mic = new MixerStrip(MicStrip, "Microfoon", Program, _push);
        Add(mic);
        mic.Duck("closed", 0f, 0);
        if (_options.Routing.MicEnabled) StartRecording(_options.Routing.MicDevice);
        else MicStatus = "uitgeschakeld in de instellingen";
        foreach (var s in _strips.Values) s.HomeBus = s.Bus;

        // Inputs as mixer channels. An input whose device is gone is reported and left out.
        foreach (var input in _options.Routing.Inputs.ToList())
        {
            try { AddInput(input); }
            catch (Exception ex) when (ex is BassException or ArgumentException)
            {
                _logger.LogWarning("Input {Input} not available: {Message}", input.Name, ex.Message);
                StartNotes.Add($"Ingang {input.Name} niet beschikbaar: {ex.Message}");
            }
        }

        // Channels with an output of their own (physical mixing desk). A card that is gone leaves the channel on air.
        foreach (var (id, target) in _options.Routing.StripOutputs.ToList())
        {
            if (!_strips.ContainsKey(id)) continue;
            try { Route(id, target); }
            catch (BassException ex) { _logger.LogWarning("Output {Target} for {Strip} not available, stays on its normal bus: {Message}", target, id, ex.Message); }
        }

        foreach (var s in _strips.Values)
        {
            var settings = _options.Routing.Channels.GetValueOrDefault(s.Id) ?? DefaultChannel(s);
            try { ApplyChannel(s.Id, settings); }
            catch (Exception ex) when (ex is BassException or DllNotFoundException)
            {
                _logger.LogWarning("Channel settings for {Strip} not applied: {Message}", s.Id, ex.Message);
                StartNotes.Add($"Kanaalinstellingen van {s.Name} niet toegepast: {ex.Message}");
            }
        }

        IsStarted = true;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "REA-Radio-DJ router", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Add(MixerStrip s) => _strips[s.Id] = s;

    /// <summary>An input starts closed (not on air); the players' channels start on air.</summary>
    public static ChannelSettings DefaultChannel(MixerStrip s) => new() { OnAir = s.Source is null };

    /// <summary>Applies gain, balance, equaliser, compressor, automatic level and on air to a channel.</summary>
    public void ApplyChannel(string stripId, ChannelSettings settings)
    {
        var strip = Strip(stripId);
        strip.Processor.Apply(settings);
        strip.Duck("off", strip.Processor.Settings.OnAir ? null : 0f, 150);
    }

    /// <summary>The input channels, in the order they were added.</summary>
    public IReadOnlyList<MixerStrip> InputStrips => _strips.Values.Where(s => s.Source is not null).ToList();

    /// <summary>Adds an input as a mixer channel on the on-air bus; it starts closed until it is put on air.</summary>
    public unsafe MixerStrip AddInput(InputChannel input)
    {
        if (_strips.ContainsKey(input.Id)) throw new ArgumentException($"Kanaal {input.Id} bestaat al");
        int handle, record = 0;
        GCHandle tone = default;
        if (input.Source == InputChannel.ToneSource) (handle, tone) = ToneGenerator.Create(BassRuntime.SampleRate);
        else if (InputChannel.TryRecordDevice(input.Source, out var device))
        {
            handle = BassRuntime.Check(BASS_StreamCreate((uint)BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, STREAMPROC_PUSH, IntPtr.Zero),
                "BASS_StreamCreate(input)");
            try
            {
                if (!BASS_RecordInit(device) && BASS_ErrorGetCode() != BASS_ERROR_ALREADY) throw new BassException($"BASS_RecordInit({device})");
                BassRuntime.Check(BASS_RecordSetDevice(device), $"BASS_RecordSetDevice({device})");
                delegate* unmanaged[Stdcall]<int, IntPtr, uint, IntPtr, int> proc = &RecordProc;
                record = BassRuntime.Check(BASS_RecordStart((uint)BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT, (IntPtr)proc, handle), "BASS_RecordStart(input)");
            }
            catch { BASS_StreamFree(handle); throw; }
        }
        else throw new ArgumentException($"Onbekende bron: {input.Source}");

        var strip = new MixerStrip(input.Id, input.Name, Program, handle) { Source = input.Source, HomeBus = Program };
        strip.Duck("off", 0f, 0);
        lock (_routeGate)
        {
            _strips = new Dictionary<string, MixerStrip>(_strips) { [input.Id] = strip };
            _inputs[input.Id] = (record, tone);
        }
        return strip;
    }

    /// <summary>Removes an input channel and stops its recording.</summary>
    public void RemoveInput(string id)
    {
        lock (_routeGate)
        {
            if (!_strips.TryGetValue(id, out var strip) || strip.Source is null) return;
            var copy = new Dictionary<string, MixerStrip>(_strips);
            copy.Remove(id);
            _strips = copy;
            if (_inputs.Remove(id, out var x))
            {
                if (x.Record != 0) BASS_ChannelStop(x.Record);
                strip.Free();
                if (x.Tone.IsAllocated) x.Tone.Free();
            }
            foreach (var (bid, bus) in _outputs.ToList())
                if (!_strips.Values.Any(s => s.Bus == bus)) { bus.Free(); _outputs.Remove(bid); }
        }
    }

    /// <summary>Messages about the sound cards at start (an ASIO card that was not found, for example).</summary>
    public List<string> StartNotes { get; } = [];

    private OutputBus Bus(string id, string name, string asio, int device)
    {
        if (OutputTarget.TryParse(asio, out var t) && t.Asio)
        {
            try { return OutputBus.OnAsio(id, $"{name} (ASIO {OutputTarget.Name(t.Pair)})", t); }
            catch (BassException ex)
            {
                _logger.LogError("ASIO output for {Bus} not available, using the Windows card: {Message}", id, ex.Message);
                StartNotes.Add($"{name}: ASIO-uitgang niet beschikbaar ({ex.Message}); de Windows-kaart wordt gebruikt");
            }
        }
        return new OutputBus(id, name, device);
    }

    private unsafe void StartRecording(int device)
    {
        try
        {
            if (!BASS_RecordInit(device) && BASS_ErrorGetCode() != BASS_ERROR_ALREADY) throw new BassException($"BASS_RecordInit({device})");
            delegate* unmanaged[Stdcall]<int, IntPtr, uint, IntPtr, int> proc = &RecordProc;
            _record = BassRuntime.Check(BASS_RecordStart((uint)BassRuntime.SampleRate, 2, BASS_SAMPLE_FLOAT, (IntPtr)proc, _push),
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
            foreach (var id in new[] { LogStrip, StandbyStrip, CartStrip, DjStrip }) Strip(id).Duck("mic", duck, 250);
        }
        else
        {
            mic.Duck("closed", 0f, 120);
            foreach (var id in new[] { LogStrip, StandbyStrip, CartStrip, DjStrip }) Strip(id).Duck("mic", null, 800);
        }
        if (MicAvailable) MicStatus = open ? "open" : "dicht";
        MicChanged?.Invoke(open);
    }

    /// <summary>The output a channel goes to ("" = its normal bus).</summary>
    public string RouteOf(string stripId)
    {
        var s = Strip(stripId);
        return s.Bus == s.HomeBus ? OutputTarget.Default : s.Bus.Id;
    }

    /// <summary>
    /// Sends a channel to an output of its own ("dev:&lt;device&gt;:&lt;pair&gt;") or back to its normal bus (""). A channel
    /// on an output of its own is no longer in the on-air mix, the stream or the silence check: the desk mixes it.
    /// </summary>
    public void Route(string stripId, string target)
    {
        var strip = Strip(stripId);
        lock (_routeGate)
        {
            if (string.IsNullOrEmpty(target)) strip.MoveTo(strip.HomeBus ?? Program);
            else
            {
                if (!OutputTarget.TryParse(target, out var t)) throw new ArgumentException($"Onbekende uitgang: {target}");
                strip.MoveTo(Output(t));
            }
            // Outputs nobody uses any more are closed.
            foreach (var (id, bus) in _outputs.ToList())
                if (!_strips.Values.Any(s => s.Bus == bus)) { bus.Free(); _outputs.Remove(id); }
        }
    }

    private OutputBus Output(OutputTarget t)
    {
        if (_outputs.TryGetValue(t.Id, out var bus)) return bus;
        var card = BassRuntime.GetDevices().FirstOrDefault(d => d.Index == t.Device)?.Name ?? $"apparaat {t.Device}";
        if (t.Asio)
        {
            var driver = AsioRuntime.GetDevices().FirstOrDefault(d => d.Index == t.Device)?.Name ?? $"ASIO {t.Device}";
            bus = OutputBus.OnAsio(t.Id, $"{driver} (ASIO), uitgang {OutputTarget.Name(t.Pair)}", t);
        }
        else bus = new OutputBus(t.Id, $"{card}, uitgang {OutputTarget.Name(t.Pair)}", t.Device, t.SpeakerFlags);
        _outputs[t.Id] = bus;
        return bus;
    }

    /// <summary>Plays a short 1 kHz tone at -18 dBFS on an output: "program", "preview" or an output target.</summary>
    public unsafe void TestTone(string output, double seconds = 1.5)
    {
        OutputBus bus;
        lock (_routeGate)
            bus = output switch
            {
                "program" => Program,
                "preview" => Preview,
                _ when OutputTarget.TryParse(output, out var t) => Output(t),
                _ => throw new ArgumentException($"Onbekende uitgang: {output}"),
            };
        int rate = BassRuntime.SampleRate, frames = (int)(rate * seconds);
        var data = new float[frames * 2];
        float amp = (float)Math.Pow(10, -18 / 20.0);
        for (int i = 0; i < frames; i++)
        {
            // 10 ms fade in and out, so the tone does not click.
            float env = Math.Min(1f, Math.Min(i, frames - 1 - i) / (rate * 0.01f));
            data[2 * i] = data[2 * i + 1] = amp * env * MathF.Sin(2 * MathF.PI * 1000 * i / rate);
        }
        int h = BassRuntime.Check(BASS_StreamCreate((uint)rate, 2, BASS_SAMPLE_FLOAT | BASS_STREAM_DECODE, STREAMPROC_PUSH, IntPtr.Zero), "BASS_StreamCreate(tone)");
        fixed (float* p = data)
        {
            BASS_StreamPutData(h, (IntPtr)p, (uint)(data.Length * sizeof(float)));
        }
        BASS_StreamPutData(h, IntPtr.Zero, BASS_STREAMPROC_END);
        BassRuntime.Check(BASS_Mixer_StreamAddChannel(bus.Handle, h, BASS_STREAM_AUTOFREE), "BASS_Mixer_StreamAddChannel(tone)");
        lock (_routeGate)
            foreach (var (id, b) in _outputs.ToList())   // a test of an unused output closes it again afterwards
                if (b == bus && !_strips.Values.Any(s => s.Bus == b))
                    _ = Task.Delay(TimeSpan.FromSeconds(seconds + 1)).ContinueWith(_ =>
                    {
                        lock (_routeGate)
                            if (_outputs.TryGetValue(id, out var still) && !_strips.Values.Any(s => s.Bus == still)) { still.Free(); _outputs.Remove(id); }
                    });
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
        foreach (var x in _inputs.Values) if (x.Record != 0) BASS_ChannelStop(x.Record);
        foreach (var s in _strips.Values) s.Free();
        foreach (var b in _outputs.Values) b.Free();
        Program.Free();
        Preview.Free();
        IsStarted = false;
    }
}

internal interface ITickable { void Tick(); }
