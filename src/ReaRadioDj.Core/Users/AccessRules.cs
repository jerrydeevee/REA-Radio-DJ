using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ReaRadioDj.Core.Users;

/// <summary>
/// Which role a change through the web interface needs. Reading is always allowed, and so is everything
/// on air (players, log, carts, microphone, calls), so the browser studio, a Stream Deck and the hardware keep working;
/// the sound and the programming need a technician, the station settings an administrator.
/// Only applies once there are users (see <see cref="UserStore.Active"/>).
/// </summary>
public static class AccessRules
{
    // Longest prefix wins; a prefix matches whole path segments.
    private static readonly (string Prefix, UserRole? Role)[] Rules =
    [
        // Signing in and out is for everybody.
        ("/api/users/login", null), ("/api/users/logout", null),
        // Station settings: administrator.
        ("/api/users", UserRole.Admin),
        ("/api/system", UserRole.Admin),
        ("/api/backup", UserRole.Admin),
        ("/api/settings", UserRole.Admin),
        ("/api/voice/services", UserRole.Admin),
        ("/api/voice/server", UserRole.Admin),
        ("/api/spotify/settings", UserRole.Admin),
        ("/api/acoustid/settings", UserRole.Admin),
        ("/api/title-log", UserRole.Admin),
        ("/api/recordings/settings", UserRole.Admin),
        ("/api/presenter/extras", UserRole.Admin),
        ("/api/presenter/creative", UserRole.Admin),
        ("/api/presenter/creative/try", null),
        ("/api/event-commands", UserRole.Admin),
        ("/api/sources", UserRole.Admin),
        ("/api/stream/targets", UserRole.Admin),
        ("/api/sip/account", UserRole.Admin),
        ("/api/library/folders", UserRole.Admin),
        ("/api/library/relocate", UserRole.Admin),
        ("/api/vst/folders", UserRole.Admin),
        ("/api/library/substitutes", UserRole.Admin),
        // Sound and technique: technician.
        ("/api/audio", UserRole.Technician),
        ("/api/routing", UserRole.Technician),
        ("/api/outputs", UserRole.Technician),
        ("/api/inputs", UserRole.Technician),
        ("/api/master", UserRole.Technician),
        ("/api/mixing-mode", UserRole.Technician),
        ("/api/channels", UserRole.Technician),
        ("/api/hardware", UserRole.Technician),
        ("/api/dj/midi", UserRole.Technician),
        ("/api/voip/stations", UserRole.Technician),
        ("/api/voip/signals", UserRole.Technician),
        ("/api/stream", UserRole.Technician),
        ("/api/library/scan", UserRole.Technician),
        // Programming: technician.
        ("/api/planner", UserRole.Technician),
        ("/api/categories", UserRole.Technician),
        ("/api/ads", UserRole.Technician),
        ("/api/news", UserRole.Technician),
        ("/api/import", UserRole.Technician),
        ("/api/voice/profiles", UserRole.Technician),
        ("/api/presenter/personas", UserRole.Technician),
        ("/api/transition-rules", UserRole.Technician),
        ("/api/transition", UserRole.Technician),
    ];

    /// <summary>On-air actions below a protected prefix, open to everybody (the channel's on-air and record buttons).</summary>
    private static bool OnAir(string[] segments) =>
        segments.Length == 4 && segments[1] == "channels" && segments[3] is "onair" or "record";

    /// <summary>The role needed for this request, or null when anybody may do it.</summary>
    public static UserRole? Required(string method, string path)
    {
        if (IsRead(method)) return null;
        path = path.TrimEnd('/').ToLowerInvariant();
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (OnAir(segments)) return null;
        (string Prefix, UserRole? Role)? best = null;
        foreach (var rule in Rules)
            if ((path == rule.Prefix || path.StartsWith(rule.Prefix + "/", StringComparison.Ordinal)) && (best is null || rule.Prefix.Length > best.Value.Prefix.Length))
                best = rule;
        return best?.Role;
    }

    private static bool IsRead(string method) =>
        method.Equals("GET", StringComparison.OrdinalIgnoreCase) || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A signed-in studio: who, and until when (renewed with every request).</summary>
public sealed record UserSession(string Token, long UserId, string Name, UserRole Role, DateTime Expires);

/// <summary>Sessions of signed-in studios, in memory only: an engine restart means signing in again.</summary>
public sealed class SessionStore(Func<DateTime>? clock = null)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, UserSession> _sessions = new(StringComparer.Ordinal);
    private DateTime Now => clock?.Invoke() ?? DateTime.UtcNow;

    public UserSession Create(StationUser user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var s = new UserSession(token, user.Id, user.Name, user.Role, Now + Lifetime);
        _sessions[token] = s;
        return s;
    }

    /// <summary>The session for this token, renewed; null when unknown or expired.</summary>
    public UserSession? Get(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_sessions.TryGetValue(token, out var s)) return null;
        if (s.Expires <= Now) { _sessions.TryRemove(token, out _); return null; }
        var renewed = s with { Expires = Now + Lifetime };
        _sessions[token] = renewed;
        return renewed;
    }

    public bool Remove(string? token) => !string.IsNullOrEmpty(token) && _sessions.TryRemove(token, out _);

    /// <summary>A user was changed or removed: their sessions end (a new role or PIN needs signing in again).</summary>
    public void RemoveUser(long userId)
    {
        foreach (var (t, s) in _sessions) if (s.UserId == userId) _sessions.TryRemove(t, out _);
    }
}
