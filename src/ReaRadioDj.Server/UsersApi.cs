using System.Collections.Concurrent;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Studio;
using ReaRadioDj.Core.Users;

namespace ReaRadioDj.Server;

/// <summary>
/// Signing in at the studio with a name and PIN, the list of users (administrators only) and the check on every
/// change: <see cref="AccessRules"/> says which role a request needs. Started with --users-off, nothing is checked
/// (for an administrator who forgot their PIN: that needs access to the computer itself).
/// </summary>
public static class UsersApi
{
    public const string LockKey = "login-lock";

    public static bool Enforced { get; private set; } = true;

    /// <summary>What a remote control with limits may do over HTTP: read, and commands through /api/do.</summary>
    public static class RemoteHttp
    {
        public static bool Allowed(string method, string path)
        {
            path = path.TrimEnd('/').ToLowerInvariant();
            bool read = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
            bool command = path == "/api/do" || path.StartsWith("/api/do/", StringComparison.Ordinal);
            // reading the users or the remote controls is not for a remote control
            bool secret = path.StartsWith("/api/users", StringComparison.Ordinal) || path.StartsWith("/api/remote-clients", StringComparison.Ordinal)
                || path.StartsWith("/api/backup", StringComparison.Ordinal);
            return command || read && !secret;
        }
    }

    /// <summary>The check on every request to /api; added before the endpoints.</summary>
    public static void UseAccessCheck(WebApplication app, bool usersOff)
    {
        Enforced = !usersOff;
        app.Use(async (http, next) =>
        {
            // From another computer: every action needs a signed-in user or the control password.
            if (Enforced && http.Request.Path.StartsWithSegments("/api"))
            {
                var ip = http.Connection.RemoteIpAddress;
                string? key = http.Request.Headers[NetworkRules.KeyHeader].ToString() is { Length: > 0 } h ? h : http.Request.Query["key"].ToString();
                // A remote control with its own limits (its key, or its address without a session): only commands
                // through /api/do, and only the groups it may use; reading stays open.
                bool signedIn = http.RequestServices.GetRequiredService<SessionStore>().Get(http.Request.Headers[UserRoles.SessionHeader].ToString()) is not null;
                if (!signedIn && http.RequestServices.GetRequiredService<Remote.RemoteClientStore>()
                        .Find(key, ip is { IsIPv4MappedToIPv6: true } v4 ? v4.MapToIPv4() : ip) is { } client)
                {
                    http.Items[Remote.RemoteClientStore.Key] = client;
                    if (!RemoteHttp.Allowed(http.Request.Method, http.Request.Path.Value ?? ""))
                    {
                        http.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await http.Response.WriteAsJsonAsync($"De afstandsbediening {client.Name} mag alleen commando's geven (via /api/do).");
                        return;
                    }
                    await next();
                    return;
                }
                var access = NetworkRules.Check(ip is null || System.Net.IPAddress.IsLoopback(ip), http.Request.Method, http.Request.Path.Value ?? "",
                    http.RequestServices.GetRequiredService<SessionStore>().Get(http.Request.Headers[UserRoles.SessionHeader].ToString()) is not null,
                    key, http.RequestServices.GetRequiredService<ReaRadioDj.Core.ReaRadioDjOptions>().Control.Password,
                    http.RequestServices.GetRequiredService<UserStore>().Active);
                if (access != NetworkAccess.Allowed)
                {
                    http.Response.StatusCode = access == NetworkAccess.SignIn ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
                    await http.Response.WriteAsJsonAsync(access == NetworkAccess.SignIn
                        ? "Van een andere computer: meld je eerst aan of geef het bedieningswachtwoord."
                        : "Bediening over het netwerk staat uit. Stel op de studio-pc een bedieningswachtwoord in (Instellingen › Op afstand) of maak gebruikers aan.");
                    return;
                }
            }
            if (Enforced && http.Request.Path.StartsWithSegments("/api") &&
                AccessRules.Required(http.Request.Method, http.Request.Path.Value ?? "") is UserRole needed &&
                http.RequestServices.GetRequiredService<UserStore>().Active)
            {
                var session = http.RequestServices.GetRequiredService<SessionStore>().Get(http.Request.Headers[UserRoles.SessionHeader].ToString());
                if (session is null || session.Role < needed)
                {
                    http.Response.StatusCode = session is null ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;
                    await http.Response.WriteAsJsonAsync(session is null
                        ? $"Meld je eerst aan (dit mag een {UserStore.RoleName(needed)})."
                        : $"Dit mag alleen een {UserStore.RoleName(needed)}; {session.Name} is {UserStore.RoleName(session.Role)}.");
                    return;
                }
            }
            await next();
        });
    }

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/users");

        g.MapGet("", (UserStore users) => View(users));

        g.MapGet("/me", (HttpContext http, SessionStore sessions, UserStore users) =>
            Session(sessions.Get(http.Request.Headers[UserRoles.SessionHeader].ToString()), users, includeToken: false));

        g.MapPost("/login", async (LoginRequest r, UserStore users, SessionStore sessions, AsRunLog asRun, MessageCenter messages, LoginGuard guard) =>
        {
            var name = (r.Name ?? "").Trim();
            if (guard.IsLocked(name, DateTime.UtcNow))
                return Results.Json($"Te vaak een verkeerde pincode; probeer het over {LockText(guard.Settings.LockSeconds)} opnieuw, of vraag een beheerder de blokkade op te heffen.", statusCode: 429);
            var user = users.Verify(name, r.Pin);
            if (user is null)
            {
                await Task.Delay(400);   // no quick guessing
                // an account past its last day says so, but only to someone who knows the PIN
                var known = users.All().FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));
                if (known is { Enabled: true, Until: { } last } && known.Expired(DateOnly.FromDateTime(DateTime.Now)) && users.Verify(name, r.Pin, last) is not null)
                    return Results.Json($"Dit account liep tot en met {last:yyyy-MM-dd}; vraag een beheerder het te verlengen.", statusCode: 401);
                if (guard.Fail(name, DateTime.UtcNow)) messages.Warning($"Aanmelden als '{name}' is geblokkeerd na te veel verkeerde pincodes.");
                return Results.Json("Naam of pincode klopt niet.", statusCode: 401);
            }
            guard.Succeed(name);
            var s = sessions.Create(user);
            asRun.Presenter = user.Name;
            messages.Info($"{user.Name} is aangemeld ({UserStore.RoleName(user.Role)}).");
            return Results.Ok(Session(s, users, includeToken: true));
        });

        g.MapPost("/logout", (HttpContext http, SessionStore sessions, AsRunLog asRun) =>
        {
            var s = sessions.Get(http.Request.Headers[UserRoles.SessionHeader].ToString());
            sessions.Remove(s?.Token);
            if (s is not null && asRun.Presenter == s.Name) asRun.Presenter = "";
            return Results.Ok();
        });

        // blocking after wrong PINs: the settings, the names blocked now, and lifting a block
        g.MapGet("/locks", (LoginGuard guard) => Locks(guard));
        g.MapPut("/locks", (LoginLockSettingsDto d, LoginGuard guard, StationDb db) =>
        {
            guard.Settings = new LoginLockSettings(d.Attempts, d.LockSeconds);
            db.SetSetting(LockKey, System.Text.Json.JsonSerializer.Serialize(guard.Settings, System.Text.Json.JsonSerializerOptions.Web));
            return Locks(guard);
        });
        g.MapDelete("/locks/{name}", (string name, LoginGuard guard, MessageCenter messages) =>
        {
            if (guard.Lift(name, DateTime.UtcNow)) messages.Info($"De blokkade van '{name}' is opgeheven.");
            return Locks(guard);
        });

        g.MapPost("", (UserSaveRequest r, UserStore users) => Save(0, r, users, null));
        g.MapPut("/{id:long}", (long id, UserSaveRequest r, UserStore users, SessionStore sessions) => Save(id, r, users, sessions));
        g.MapDelete("/{id:long}", (long id, UserStore users, SessionStore sessions) =>
        {
            if (users.Delete(id) is { } error) return Results.BadRequest(error);
            sessions.RemoveUser(id);
            return Results.Ok(View(users));
        });
    }

    private static IResult Save(long id, UserSaveRequest r, UserStore users, SessionStore? sessions)
    {
        if (UserStore.ParseRole(r.Role) is not UserRole role) return Results.BadRequest("Onbekende rol.");
        var before = users.All().FirstOrDefault(u => u.Id == id);
        DateOnly? until = null;
        if (!string.IsNullOrWhiteSpace(r.Until))
        {
            if (!DateOnly.TryParseExact(r.Until.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var u))
                return Results.BadRequest("De einddatum is een datum als 2026-12-31.");
            until = u;
        }
        var (user, error) = users.Save(id, r.Name, role, r.Enabled, r.Pin, until);
        if (error is not null) return Results.BadRequest(error);
        // A new role, a new PIN, switched off or a new end: sign in again.
        if (sessions is not null && before is not null && (before.Role != user!.Role || !user.Enabled || before.PinHash != user.PinHash || before.Until != user.Until))
            sessions.RemoveUser(id);
        return Results.Ok(View(users));
    }

    private static UsersDto View(UserStore users)
    {
        var all = users.All();
        var today = DateOnly.FromDateTime(DateTime.Now);
        return new(all.Any(u => u.Enabled), Enforced, all.Select(u => new UserDto(u.Id, u.Name, UserStore.RoleName(u.Role), u.Enabled,
            u.Until?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), u.Expired(today))).ToList());
    }

    private static LoginLocksDto Locks(LoginGuard guard) =>
        new(guard.Settings.Attempts, guard.Settings.LockSeconds, guard.Locked(DateTime.UtcNow).Select(l => new LoginLockDto(l.Name, l.Until)).ToList());

    private static string LockText(int seconds) => seconds < 90 ? $"{seconds} seconden" : $"{Math.Ceiling(seconds / 60.0):0} minuten";

    /// <summary>The login guard with the stored settings.</summary>
    public static LoginGuard CreateGuard(StationDb db)
    {
        var guard = new LoginGuard();
        if (db.GetSetting(LockKey) is { } json)
            try { guard.Settings = System.Text.Json.JsonSerializer.Deserialize<LoginLockSettings>(json, System.Text.Json.JsonSerializerOptions.Web) ?? new(); }
            catch (System.Text.Json.JsonException) { }
        return guard;
    }

    private static SessionDto Session(UserSession? s, UserStore users, bool includeToken) =>
        s is null
            ? new SessionDto("", 0, "", "", users.Active, Enforced)
            : new SessionDto(includeToken ? s.Token : "", s.UserId, s.Name, UserStore.RoleName(s.Role), users.Active, Enforced);
}
