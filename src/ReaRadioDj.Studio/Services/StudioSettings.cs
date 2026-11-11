using System.Text.Json;

namespace ReaRadioDj.Studio.Services;

/// <summary>Settings of the desktop studio, in %AppData%\REA-Radio-DJ\studio.json. Layouts are stored next to it.</summary>
public sealed class StudioSettings
{
    public string EngineUrl { get; set; } = "http://127.0.0.1:8600";
    /// <summary>Path of REA-Radio-DJ.exe, to start the engine from the studio (optional).</summary>
    public string? EnginePath { get; set; }
    public string? EngineWorkingDirectory { get; set; }
    public bool StartEngine { get; set; } = true;
    /// <summary>The setup helper was finished or skipped, so it does not open by itself again.</summary>
    public bool SetupDone { get; set; }
    /// <summary>Colour style: regiekamer, daglicht, dark, night, light, contrast … (see Appearance).</summary>
    public string Theme { get; set; } = "regiekamer";
    /// <summary>Corner shape: square, normal or round.</summary>
    public string Corners { get; set; } = "normal";
    /// <summary>Size of the whole studio in percent (80-150).</summary>
    public int ScalePercent { get; set; } = 100;
    /// <summary>Touch mode: buttons, list rows, tabs and scroll bars big enough for a finger.</summary>
    public bool Touch { get; set; }

    /// <summary>Calmer, less saturated colours for kinds of items and waveforms.</summary>
    public bool CalmColors { get; set; }

    /// <summary>Ids of the tracks on the clipboard, newest first.</summary>
    public List<long> Clipboard { get; set; } = [];
    /// <summary>Accent colour (hex) instead of the style's own; empty = the style's.</summary>
    public string Accent { get; set; } = "";
    /// <summary>Button look: flat, raised or outline.</summary>
    public string ButtonStyle { get; set; } = "flat";
    /// <summary>Waveform look: classic, gradient, bars, neon or energy.</summary>
    public string WaveStyle { get; set; } = "classic";
    /// <summary>Waveform colour (hex) in the cue editor and players; empty = blue.</summary>
    public string WaveColor { get; set; } = "";
    /// <summary>The buttons of the Eigen knoppen window, in order.</summary>
    public List<CustomButton> CustomButtons { get; set; } = CustomButton.Defaults();
    /// <summary>Colour per kind of item (song, jingle, voice, other) in the log, as-run and library.</summary>
    public Dictionary<string, string> TypeColors { get; set; } = new(Appearance.DefaultTypeColors);
    public string LastLayout { get; set; } = "Standaard";
    /// <summary>Verkenner: the last folder shown and the saved favourite folders (paths on the engine machine).</summary>
    public string ExplorerPath { get; set; } = "";
    public List<string> ExplorerFavorites { get; set; } = [];
    /// <summary>2 = the Standaard layout is the Live workspace (management windows moved to their own workspaces).</summary>
    public int LayoutVersion { get; set; }

    /// <summary>Tijd window: studio, analog or digital; counter size 0-3; flash colour when an item ends.</summary>
    public string ClockStyle { get; set; } = "studio";
    public int CounterSize { get; set; } = 1;
    public bool ClockSeconds { get; set; } = true;
    public string FlashColor { get; set; } = "#E5484D";

    /// <summary>Spotify Soloist on this computer (developer test): program, device name, port and the encrypted key.</summary>
    public SoloistSettings Soloist { get; set; } = new();

    /// <summary>Standby/PFL players: "wave" (waveform, cue markers, big counter) or "compact".</summary>
    public string PlayerStyle { get; set; } = "wave";

    /// <summary>Colours of deck A (on air), B (next) and C (after that), also used in the log; colour of big counters.</summary>
    public string[] DeckColors { get; set; } = ["#E5484D", "#F0B429", "#3FA7FF"];
    public string CounterColor { get; set; } = "#F5F7FA";
    public bool DecksHorizontal { get; set; } = true;

    /// <summary>Library window: detail pane, large rows for touch, sort order, what a double click does.</summary>
    public bool LibraryDetails { get; set; } = true;
    public bool LibraryLarge { get; set; }
    public string LibrarySort { get; set; } = "relevance";
    public string LibraryDoubleClick { get; set; } = "pfl";
    /// <summary>The fields under the title of a library row, in this order (null = the default set).</summary>
    public List<string>? LibraryFields { get; set; }

    /// <summary>Key (Avalonia gesture text, e.g. "F5", "Ctrl+B", "Shift+F1") → engine command.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = DefaultHotkeys();
    /// <summary>Hotkeys (as in <see cref="Hotkeys"/>) that also work while another program has the focus.</summary>
    public List<string> GlobalHotkeys { get; set; } = [];

    public static Dictionary<string, string> DefaultHotkeys() => new()
    {
        ["F5"] = "NEXT",
        ["F6"] = "AUTO TOGGLE",
        ["F7"] = "SKIP",
        ["F8"] = "MIC TOGGLE",
        ["F9"] = "STOP",
        ["F10"] = "CARTS STOP",
        ["Ctrl+B"] = "BREAK",
        ["Ctrl+P"] = "PLAYER PFL TOGGLE",
        ["Shift+F5"] = "PLAYER 1 TOGGLE",
        ["Shift+F6"] = "PLAYER 2 TOGGLE",
        ["Shift+F9"] = "OVERLAY STOP",
    };

    /// <summary>Engine for this session only (command line --engine); not saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string? SessionEngineUrl { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string EffectiveEngineUrl => SessionEngineUrl ?? EngineUrl;

    /// <summary>Settings folder; --profile on the command line points it somewhere else (for testing).</summary>
    public static string? FolderOverride { get; set; }
    public static string Folder => FolderOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "REA-Radio-DJ");
    public static string LayoutFolder => Path.Combine(Folder, "layouts");
    private static string FilePath => Path.Combine(Folder, "studio.json");

    public static StudioSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<StudioSettings>(File.ReadAllText(FilePath), JsonSerializerOptions.Web) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>A button in the Eigen knoppen window: what it says, the engine command it runs and its colour.</summary>
public sealed class CustomButton
{
    public string Label { get; set; } = "";
    public string Command { get; set; } = "";
    public string Color { get; set; } = "#3FA7FF";

    /// <summary>A few useful buttons to start with; change or remove them under Bewerken.</summary>
    public static List<CustomButton> Defaults() =>
    [
        new() { Label = "AI-break als volgende", Command = "BREAK", Color = "#A78BFA" },
        new() { Label = "Alle carts stoppen", Command = "CARTS STOP", Color = "#F27A2E" },
        new() { Label = "Log opnieuw vullen", Command = "REPLAN", Color = "#3FA7FF" },
        new() { Label = "Bibliotheek inlezen", Command = "SCAN", Color = "#8B95A5" },
    ];
}
