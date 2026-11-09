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
    public string Theme { get; set; } = "dark";
    public string LastLayout { get; set; } = "Standaard";

    /// <summary>Tijd window: studio, analog or digital; counter size 0-3; flash colour when an item ends.</summary>
    public string ClockStyle { get; set; } = "studio";
    public int CounterSize { get; set; } = 1;
    public bool ClockSeconds { get; set; } = true;
    public string FlashColor { get; set; } = "#E5484D";

    /// <summary>Key (Avalonia gesture text, e.g. "F5", "Ctrl+B", "Shift+F1") → engine command.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = DefaultHotkeys();

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
