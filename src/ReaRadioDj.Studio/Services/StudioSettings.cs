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
