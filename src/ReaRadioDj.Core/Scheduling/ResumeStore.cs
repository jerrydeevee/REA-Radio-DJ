using System.Text.Json;
using System.Text.Json.Serialization;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Scheduling;

/// <summary>What was on air a moment ago: written every second while the engine runs and removed on a clean stop.</summary>
public sealed record ResumeState(DateTime SavedAt, bool AutoPlay, LogItemType? Type, MediaItem? Media, double Position, string Origin);

/// <summary>
/// Lets the engine carry on after a crash. While it runs the store holds what is on air; a clean stop removes the file.
/// A file that is still there at the next start therefore means the engine did not stop by itself, and the item that
/// was on air can be started again at the position where it was.
/// </summary>
public sealed class ResumeStore(string path)
{
    /// <summary>Older than this the station has been off too long to continue the old item; only the mode is restored.</summary>
    public static readonly TimeSpan ContinueItemWithin = TimeSpan.FromMinutes(5);

    /// <summary>Do not resume an item in its last seconds; the next item is the better start.</summary>
    public const double MinimumLeft = 15;

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public string Path => path;

    public void Save(ResumeState state)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Reads and removes the state left by an engine that did not stop cleanly; null after a clean stop.</summary>
    public ResumeState? Take()
    {
        if (!File.Exists(path)) return null;
        ResumeState? state;
        try { state = JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(path), Json); }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException) { state = null; }
        Clear();
        return state;
    }

    public void Clear()
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The log item that continues the interrupted one: the same audio starting at the old position, or null when there
    /// is nothing worth continuing (too long ago, file gone, almost at its end).
    /// </summary>
    public static LogItem? ContinueItem(ResumeState state, DateTime now)
    {
        if (state is not { Type: { } type, Media: { } media }) return null;
        if (now - state.SavedAt > ContinueItemWithin) return null;
        if (!File.Exists(media.Path)) return null;
        double from = state.Position + Math.Max(0, (now - state.SavedAt).TotalSeconds);
        if (from <= media.CueIn + 1 || from > media.CueOut - MinimumLeft) return null;
        return new LogItem
        {
            Type = type,
            Media = media with { Cues = media.Cues with { CueIn = from, Intro = null } },
            Origin = string.IsNullOrEmpty(state.Origin) ? "hervat" : state.Origin.EndsWith("hervat", StringComparison.Ordinal) ? state.Origin : $"{state.Origin} · hervat",
            BreakState = BreakState.Ready,
        };
    }
}
