namespace ReaRadioDj.Core;

/// <summary>All settings, bound from the "REA-Radio-DJ" section of appsettings.json.</summary>
public sealed class ReaRadioDjOptions
{
    public string StationName { get; set; } = "REA-Radio-DJ";
    public string MusicFolder { get; set; } = "media/music";
    public string JingleFolder { get; set; } = "media/jingles";
    /// <summary>Watch the music and jingle folders: new, changed and removed files are processed without a button.</summary>
    public bool WatchLibrary { get; set; } = true;
    /// <summary>Hour of the nightly full check of the folders (-1 = off).</summary>
    public int NightlyScanHour { get; set; } = 4;
    /// <summary>Where rendered AI breaks and the analysis cache are written.</summary>
    public string DataFolder { get; set; } = "data";

    /// <summary>Where channel recordings go; empty = the folder "Opnamen" in the data folder.</summary>
    public string RecordingFolder { get; set; } = "";

    /// <summary>Folders searched for VST 2 effect plugins (64-bit .dll).</summary>
    public List<string> VstFolders { get; set; } =
    [
        @"C:\Program Files\VSTPlugins", @"C:\Program Files\Steinberg\VSTPlugins",
        @"C:\Program Files\Common Files\VST2", @"C:\Program Files\Common Files\Steinberg\VST2",
    ];

    public string RecordingPath => string.IsNullOrWhiteSpace(RecordingFolder) ? Path.Combine(DataFolder, "Opnamen") : RecordingFolder;

    /// <summary>-1 = default sound card, 0 = no sound card (stream only), 1.. = device index (see /api/devices).</summary>
    public int OutputDevice { get; set; } = -1;
    public bool StartAutomation { get; set; } = true;

    public MixOptions Mix { get; set; } = new();
    public RoutingOptions Routing { get; set; } = new();
    public RotationOptions Rotation { get; set; } = new();
    public PresenterOptions Presenter { get; set; } = new();
    public StreamOptions Stream { get; set; } = new();
    public ControlOptions Control { get; set; } = new();
    public SpotifyOptions Spotify { get; set; } = new();
    public SilenceOptions Silence { get; set; } = new();
}

/// <summary>Silence on air: under the threshold for longer than the given time raises an alarm.</summary>
public sealed class SilenceOptions
{
    public bool Enabled { get; set; } = true;
    public double ThresholdDb { get; set; } = -45;
    public double Seconds { get; set; } = 20;
    /// <summary>"warn" = only an alarm in the studio, "autoplay" = also start the automation, "category" = play an item
    /// from <see cref="CategoryId"/> right away (a random song when it is empty) and start the automation, "random" = play
    /// a random song from the music library right away and start the automation.</summary>
    public string Action { get; set; } = "autoplay";
    /// <summary>Emergency category for the action "category" (an emergency playlist, station IDs, ...).</summary>
    public long? CategoryId { get; set; }
}

/// <summary>
/// Spotify Web API, only for metadata: importing a playlist as a wish list (titles, artists, ISRC, cover) and matching it
/// with the library. Audio is never fetched from Spotify. Create an app on developer.spotify.com for the id and secret.
/// </summary>
public sealed class SpotifyOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Market { get; set; } = "NL";
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
    /// <summary>Plain text commands over UDP, one per datagram ("NEXT", "CART 1 5"). 0 = off.</summary>
    public int UdpPort { get; set; } = 8603;
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
    /// <summary>Mixer channels that have an output of their own (channel id → "dev:&lt;device&gt;:&lt;pair&gt;"); the rest goes on air.</summary>
    public Dictionary<string, string> StripOutputs { get; set; } = [];
    /// <summary>ASIO output for the on-air bus ("asio:&lt;device&gt;:&lt;pair&gt;"), or "" for the Windows card in OutputDevice.</summary>
    public string ProgramAsio { get; set; } = "";
    /// <summary>ASIO output for the pre-listen bus, or "" for the Windows card in PreviewDevice.</summary>
    public string PreviewAsio { get; set; } = "";
    /// <summary>44100 or 48000 Hz for the whole engine (take what the sound card runs at).</summary>
    public int SampleRate { get; set; } = 44100;
    /// <summary>Buffer of Windows sound cards in milliseconds (lower = quicker reaction, higher = safer).</summary>
    public int BufferMs { get; set; } = 250;
    /// <summary>ASIO buffer in samples (0 = what the driver prefers).</summary>
    public int AsioBuffer { get; set; }
    /// <summary>Limiter on the on-air bus: peaks stay under <see cref="LimiterCeilingDb"/>.</summary>
    public bool LimiterEnabled { get; set; } = true;
    /// <summary>Sound inputs as mixer channels (line in, a second microphone, a line-up tone).</summary>
    public List<Audio.InputChannel> Inputs { get; set; } = [];
    /// <summary>Channel strip settings per mixer channel.</summary>
    public Dictionary<string, Audio.ChannelSettings> Channels { get; set; } = [];
    public double LimiterCeilingDb { get; set; } = -1;
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
    /// <summary>Scripts wait for a person to approve (and possibly edit) them before they are spoken.</summary>
    public bool RequireApproval { get; set; } = false;
    /// <summary>High-pass, compressor, silence trim and peak level on every rendered break.</summary>
    public bool PostProcess { get; set; } = true;
    /// <summary>Library category with music beds to lay under every break; null = no bed. A persona can choose its own.</summary>
    public long? BedCategoryId { get; set; }
    /// <summary>Jingle category of which one item is played straight after every AI break; null = none.</summary>
    public long? OutroCategoryId { get; set; }
    public WeatherOptions Weather { get; set; } = new();
    public NewsOptions News { get; set; } = new();
    public LlmOptions Llm { get; set; } = new();
    public TtsOptions Tts { get; set; } = new();
    public SpeechCheckOptions Check { get; set; } = new();
    /// <summary>The creative presenter: themes in a changing tone (see Presenter/CreativePresenter.cs).</summary>
    public Presenter.CreativeOptions Creative { get; set; } = new();
}

/// <summary>Any OpenAI-compatible chat completion endpoint (Ollama, LM Studio, vLLM, OpenAI, ...).</summary>
/// <summary>Weather from Open-Meteo (free, no key). Default location: Gieten, gemeente Aa en Hunze.</summary>
public sealed class WeatherOptions
{
    public bool Enabled { get; set; } = false;
    public string Place { get; set; } = "Aa en Hunze";
    public double Latitude { get; set; } = 53.006;
    public double Longitude { get; set; } = 6.763;
    /// <summary>Mention the weather at most once per this many minutes.</summary>
    public int EveryMinutes { get; set; } = 30;
}

/// <summary>News headlines from RSS or Atom feeds; each headline is mentioned once.</summary>
public sealed class NewsOptions
{
    public bool Enabled { get; set; } = false;
    public List<string> Feeds { get; set; } = [];
    public int EveryMinutes { get; set; } = 60;
}

public sealed class LlmOptions
{
    /// <summary>Empty = no language model; template texts are used instead.</summary>
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public double Temperature { get; set; } = 0.8;
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>Listening back to every rendered break with Whisper (OpenAI-compatible /v1/audio/transcriptions).</summary>
public sealed class SpeechCheckOptions
{
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = "http://localhost:8002";
    public string Model { get; set; } = "small";
    public string Language { get; set; } = "nl";
    /// <summary>Lowest acceptable match (0..1) between script and what was heard.</summary>
    public double MinScore { get; set; } = 0.75;
    /// <summary>Renders again this many times when the match is too low.</summary>
    public int Retries { get; set; } = 1;
    /// <summary>Still too low after the retries: skip the break (a jingle plays) instead of airing it with a warning.</summary>
    public bool Reject { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class TtsOptions
{
    /// <summary>ElevenLabs API key (online voices, paid); the voice ID goes in the voice profile.</summary>
    public string ElevenLabsKey { get; set; } = "";
    public string ElevenLabsModel { get; set; } = "eleven_multilingual_v2";
    /// <summary>Azure AI Speech key and region (for example westeurope); the voice name goes in the voice profile.</summary>
    public string AzureKey { get; set; } = "";
    public string AzureRegion { get; set; } = "westeurope";

    /// <summary>"higgs" (Higgs TTS 3 via an SGLang-Omni or vLLM-Omni server) or "sapi" (Windows speech, offline).</summary>
    public string Provider { get; set; } = "sapi";
    /// <summary>Base URL of the Higgs server (start it with --port 8001; Icecast usually owns 8000).</summary>
    public string BaseUrl { get; set; } = "http://localhost:8001";
    public string Model { get; set; } = "bosonai/higgs-tts-3-4b";
    /// <summary>Optional voice-clone reference: a path or URL the Higgs *server* can read, plus its transcript.</summary>
    public string ReferenceAudio { get; set; } = "";
    public string ReferenceText { get; set; } = "";
    /// <summary>Higgs control tags put in front of every break, e.g. "&lt;|emotion:enthusiasm|&gt;".</summary>
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
