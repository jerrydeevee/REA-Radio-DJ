using System.Text.Json;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// The roles a voice speaks in: the AI presenter (the default voice profile), the news reader and the weather
/// man or woman. A news or weather block without its own voice uses the voice of its role; without one, the
/// presenter's voice. A voice only speaks in a role its <see cref="VoiceProfile.Uses"/> allows.
/// </summary>
public sealed record VoiceRoles(long? News = null, long? Weather = null)
{
    public const string Presenter = "presenter";
    public const string NewsReader = "news";
    public const string WeatherReader = "weather";
    public static readonly string[] All = [Presenter, NewsReader, WeatherReader];

    /// <summary>Dutch name of a role, for messages and the studio.</summary>
    public static string Label(string role) => role switch
    {
        Presenter => "presentator",
        NewsReader => "nieuwslezer",
        WeatherReader => "weerman/weervrouw",
        _ => role,
    };

    /// <summary>The known roles in a comma list, in a fixed order, without doubles.</summary>
    public static IReadOnlyList<string> Parse(string? uses)
    {
        var given = (uses ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(u => u.ToLowerInvariant()).ToHashSet();
        return All.Where(given.Contains).ToList();
    }

    public static string Format(IEnumerable<string> roles) => string.Join(",", Parse(string.Join(",", roles)));
}

/// <summary>Which voice speaks the news and which the weather (station settings).</summary>
public sealed class VoiceRoleStore(StationDb db, VoiceStore voices)
{
    public const string Key = "voice-roles";
    private readonly object _gate = new();
    private VoiceRoles? _roles;

    public VoiceRoles Current
    {
        get
        {
            lock (_gate)
            {
                if (_roles is not null) return _roles;
                try { _roles = db.GetSetting(Key) is { } json ? JsonSerializer.Deserialize<VoiceRoles>(json, JsonSerializerOptions.Web) : null; }
                catch (JsonException) { _roles = null; }
                return _roles ??= new VoiceRoles();
            }
        }
    }

    /// <summary>Saves the news and weather voice; refuses a voice that may not speak in that role.</summary>
    public string? Save(VoiceRoles roles)
    {
        foreach (var (id, role) in new[] { (roles.News, VoiceRoles.NewsReader), (roles.Weather, VoiceRoles.WeatherReader) })
        {
            if (id is not long v) continue;
            if (voices.Get(v) is not { } p) return "die stem bestaat niet (meer)";
            if (!p.Allows(role)) return $"'{p.Name}' mag niet als {VoiceRoles.Label(role)} spreken (zie 'gebruiken voor' bij de stem)";
        }
        lock (_gate)
        {
            db.SetSetting(Key, JsonSerializer.Serialize(roles, JsonSerializerOptions.Web));
            _roles = roles;
        }
        return null;
    }

    /// <summary>
    /// The voice for a news or weather block: its own voice, else the voice of its role, else null (the presenter's
    /// voice). A voice that may not speak in the role is passed over.
    /// </summary>
    public long? Resolve(string role, long? own)
    {
        var current = Current;
        long? fromRole = role switch { VoiceRoles.NewsReader => current.News, VoiceRoles.WeatherReader => current.Weather, _ => null };
        foreach (var id in new[] { own, fromRole })
            if (id is long v && voices.Get(v) is { } p && p.Allows(role)) return v;
        return null;
    }
}

/// <summary>
/// Reference recordings of cloned voices, kept in the engine's data folder (voices\clip-*.wav). A stored clip is sent
/// to the Higgs server inside the request, so the server needs no access to this computer's files (WSL or another PC).
/// </summary>
public sealed class VoiceClips(string folder)
{
    public const double MinSeconds = 4;
    public const double MaxSeconds = 40;
    /// <summary>Quieter than this (peak) and the voice is too soft to clone well.</summary>
    public const double MinPeakDb = -30;
    public const int MaxBytes = 25 * 1024 * 1024;

    public string Folder => folder;

    /// <summary>Is this reference a clip in the voice folder (and not a path or URL for the server)?</summary>
    public static bool IsStored(string? reference) =>
        reference is { Length: > 0 } r && r.StartsWith("clip-", StringComparison.Ordinal) && r.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
        && r.IndexOfAny(['/', '\\', ':']) < 0 && !r.Contains("..", StringComparison.Ordinal);

    public string? PathOf(string? reference) =>
        IsStored(reference) && Path.Combine(folder, reference!) is var p && File.Exists(p) ? p : null;

    public byte[]? Read(string? reference) => PathOf(reference) is { } p ? File.ReadAllBytes(p) : null;

    /// <summary>Checks and stores a recording for a voice; returns the reference to put in the profile.</summary>
    public string Store(long profileId, byte[] wav)
    {
        var check = Check(wav);
        if (check.Problem is not null) throw new InvalidOperationException(check.Problem);
        Directory.CreateDirectory(folder);
        string name = $"clip-{profileId}-{DateTime.Now:yyyyMMdd-HHmmss}.wav";
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path + ".part", wav);
        File.Move(path + ".part", path, overwrite: true);
        return name;
    }

    public void Delete(string? reference)
    {
        if (PathOf(reference) is not { } p) return;
        try { File.Delete(p); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public sealed record ClipCheck(double Seconds, double PeakDb, string? Problem);

    /// <summary>Is this a usable voice recording: a WAV of 4 to 40 seconds with enough level?</summary>
    public static ClipCheck Check(byte[] wav)
    {
        if (wav.Length > MaxBytes) return new(0, -96, "het bestand is te groot (maximaal 25 MB)");
        int rate, channels;
        float[] samples;
        try { (rate, channels, samples) = VoiceProcessor.Read(wav); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or NotSupportedException)
        {
            return new(0, -96, "geen bruikbaar WAV-bestand (16-bit PCM of 32-bit float)");
        }
        if (rate <= 0 || channels <= 0 || samples.Length == 0) return new(0, -96, "het WAV-bestand bevat geen geluid");
        double seconds = samples.Length / (double)channels / rate;
        float peak = 0;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
        double peakDb = Math.Round(20 * Math.Log10(Math.Max(peak, 1e-6)), 1);
        string? problem =
            seconds < MinSeconds ? $"de opname is te kort ({seconds:0.0} s; minimaal {MinSeconds:0} s)" :
            seconds > MaxSeconds ? $"de opname is te lang ({seconds:0} s; maximaal {MaxSeconds:0} s)" :
            peakDb < MinPeakDb ? $"de opname is te zacht (piek {peakDb:0} dB); spreek dichter bij de microfoon" : null;
        return new(Math.Round(seconds, 2), peakDb, problem);
    }

    /// <summary>A microphone recording (mono float) as a WAV, with the silence before and after taken off.</summary>
    public static byte[] FromRecording(float[] mono, int rate)
    {
        var trimmed = mono.Length == 0 ? mono : VoiceProcessor.Trim(mono, rate, 1, -45, 250);
        return VoiceProcessor.Write(rate, 1, trimmed);
    }
}
