namespace ReaRadioDj.Core;

/// <summary>All settings, bound from the "REA-Radio-DJ" section of appsettings.json.</summary>
public sealed class ReaRadioDjOptions
{
    public string StationName { get; set; } = "REA-Radio-DJ";
    public string MusicFolder { get; set; } = "media/music";
    public string JingleFolder { get; set; } = "media/jingles";
    /// <summary>Where rendered AI breaks and the analysis cache are written.</summary>
    public string DataFolder { get; set; } = "data";

    /// <summary>-1 = default sound card, 0 = no sound card (stream only), 1.. = device index (see /api/devices).</summary>
    public int OutputDevice { get; set; } = -1;
    public bool StartAutomation { get; set; } = true;

    public MixOptions Mix { get; set; } = new();
    public RoutingOptions Routing { get; set; } = new();
    public RotationOptions Rotation { get; set; } = new();
    public PresenterOptions Presenter { get; set; } = new();
    public StreamOptions Stream { get; set; } = new();
    public ControlOptions Control { get; set; } = new();
}

/// <summary>Remote control over the network. Commands are the same everywhere (see /api/commands).</summary>
public sealed class ControlOptions
{
    /// <summary>127.0.0.1 = only this computer; 0.0.0.0 = the whole network.</summary>
    public string BindAddress { get; set; } = "127.0.0.1";
    /// <summary>Line-based TCP (telnet-style). 0 = off.</summary>
    public int TcpPort { get; set; } = 8601;
    /// <summary>OSC over UDP (TouchOSC, Companion, lighting desks). 0 = off.</summary>
    public int OscPort { get; set; } = 8602;
    /// <summary>When set, a TCP client first sends "AUTH &lt;password&gt;".</summary>
    public string Password { get; set; } = "";
}

public sealed class MixOptions
{
    /// <summary>Loudness every item is normalised to (EBU R128 integrated, LUFS).</summary>
    public double TargetLufs { get; set; } = -16;
    /// <summary>Level (dBFS) that marks the start of audio when detecting CueIn.</summary>
    public double CueInDb { get; set; } = -40;
    /// <summary>Level (dBFS) scanned from the end that marks the mix point.</summary>
    public double MixDb { get; set; } = -24;
    /// <summary>Level (dBFS) scanned from the end that marks the last audio (CueOut).</summary>
    public double CueOutDb { get; set; } = -50;
    /// <summary>Longest overlap allowed between two songs, in seconds.</summary>
    public double MaxOverlap { get; set; } = 8;
    /// <summary>Fade-out length of the outgoing item, in seconds.</summary>
    public double FadeOut { get; set; } = 2.5;
    /// <summary>Volume of the music under a voice break (talk-up), 0..1.</summary>
    public double DuckLevel { get; set; } = 0.3;
}

public sealed class RoutingOptions
{
    /// <summary>Sound card for the PFL / pre-listen bus (-1 = default, 0 = none). The on-air bus uses OutputDevice.</summary>
    public int PreviewDevice { get; set; } = -1;
    /// <summary>Record the microphone input (talkover). Off by default.</summary>
    public bool MicEnabled { get; set; } = false;
    /// <summary>Recording device for the microphone (-1 = default input).</summary>
    public int MicDevice { get; set; } = -1;
    /// <summary>Music level while the microphone is open, 0..1.</summary>
    public double MicDuckLevel { get; set; } = 0.25;
    /// <summary>Automation level under overlays and overlay carts, 0..1.</summary>
    public double OverlayDuckLevel { get; set; } = 0.35;
    public int StandbyPlayers { get; set; } = 2;
}

/// <summary>Rotation rules used by the planner when it picks songs from a category.</summary>
public sealed class RotationOptions
{
    /// <summary>The same artist is not repeated within this many songs.</summary>
    public int ArtistSeparation { get; set; } = 3;
    /// <summary>The same song is not repeated within this many songs.</summary>
    public int SongSeparation { get; set; } = 10;
    /// <summary>The log is kept at least this many items ahead of the current one.</summary>
    public int LookAhead { get; set; } = 8;
}

public sealed class PresenterOptions
{
    public bool Enabled { get; set; } = true;
    public string PresenterName { get; set; } = "Sam";
    public string Language { get; set; } = "nl";
    /// <summary>Extra persona instruction for the language model.</summary>
    public string Persona { get; set; } = "Je bent een vriendelijke, energieke radiopresentator. Je praat kort en natuurlijk.";
    public int MaxWords { get; set; } = 45;
    public LlmOptions Llm { get; set; } = new();
    public TtsOptions Tts { get; set; } = new();
}

/// <summary>Any OpenAI-compatible chat completion endpoint (Ollama, LM Studio, vLLM, OpenAI, ...).</summary>
public sealed class LlmOptions
{
    /// <summary>Empty = no language model; template texts are used instead.</summary>
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public double Temperature { get; set; } = 0.8;
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class TtsOptions
{
    /// <summary>"higgs" (Higgs TTS 3 via an SGLang-Omni or vLLM-Omni server) or "sapi" (Windows speech, offline).</summary>
    public string Provider { get; set; } = "sapi";
    /// <summary>Base URL of the Higgs server (start it with --port 8001; Icecast usually owns 8000).</summary>
    public string BaseUrl { get; set; } = "http://localhost:8001";
    public string Model { get; set; } = "bosonai/higgs-tts-3-4b";
    /// <summary>Optional voice-clone reference: a path or URL the Higgs *server* can read, plus its transcript.</summary>
    public string ReferenceAudio { get; set; } = "";
    public string ReferenceText { get; set; } = "";
    /// <summary>Higgs control tags put in front of every break, e.g. "&lt;|emotion:happy|&gt;".</summary>
    public string Tags { get; set; } = "";
    public double Temperature { get; set; } = 0.8;
    /// <summary>Name (or part of it) of the Windows voice for the "sapi" provider; empty = first voice for the language.</summary>
    public string SapiVoice { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;
    /// <summary>Use Windows speech when the configured provider fails.</summary>
    public bool FallbackToSapi { get; set; } = true;
    /// <summary>
    /// Optional command that starts the Higgs server on the GPU (e.g. a script in WSL), so the studio can start and
    /// stop it. Empty = the server is started outside REA-Radio-DJ.
    /// </summary>
    public string ServerCommand { get; set; } = "";
    public string ServerArguments { get; set; } = "";
    public string ServerWorkingDirectory { get; set; } = "";
    /// <summary>Start the Higgs server together with the engine.</summary>
    public bool StartServer { get; set; } = false;
}

public sealed class StreamOptions
{
    public bool Enabled { get; set; } = false;
    /// <summary>Icecast server as host:port/mount, e.g. localhost:8000/live.</summary>
    public string Server { get; set; } = "localhost:8000/live";
    /// <summary>Source password, or "user:password".</summary>
    public string Password { get; set; } = "hackme";
    public int Bitrate { get; set; } = 128;
    public string Genre { get; set; } = "Various";
    public string Url { get; set; } = "";
}
