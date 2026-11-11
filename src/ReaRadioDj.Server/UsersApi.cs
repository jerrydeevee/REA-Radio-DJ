using System.Collections.Concurrent;
using ReaRadioDj.Contracts;
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
    private static readonly ConcurrentDictionary<string, (int Count, DateTime Until)> Failures = new(StringComparer.OrdinalIgnoreCase);

    public static bool Enforced { get; private set; } = true;

    /// <summary>The check on every request to /api; added before the endpoints.</summary>
    public static void UseAccessCheck(WebApplication app, bool usersOff)
    {
        Enforced = !usersOff;
        app.Use(async (http, next) =>
        {
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

        g.MapPost("/login", async (LoginRequest r, UserStore users, SessionStore sessions, AsRunLog asRun, MessageCenter messages) =>
        {
            var name = (r.Name ?? "").Trim();
            if (Failures.TryGetValue(name, out var f) && f.Until > DateTime.UtcNow)
                return Results.Json("Te vaak een verkeerde pincode; probeer het over een halve minuut opnieuw.", statusCode: 429);
            var user = users.Verify(name, r.Pin);
            if (user is null)
            {
                await Task.Delay(400);   // no quick guessing
                var count = (Failures.TryGetValue(name, out var old) ? old.Count : 0) + 1;
                Failures[name] = count >= 5 ? (0, DateTime.UtcNow.AddSeconds(30)) : (count, DateTime.MinValue);
                return Results.Json("Naam of pincode klopt niet.", statusCode: 401);
            }
            Failures.TryRemove(name, out _);
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
        var (user, error) = users.Save(id, r.Name, role, r.Enabled, r.Pin);
        if (error is not null) return Results.BadRequest(error);
        // A new role, a new PIN or switched off: sign in again.
        if (sessions is not null && before is not null && (before.Role != user!.Role || !user.Enabled || before.PinHash != user.PinHash))
            sessions.RemoveUser(id);
        return Results.Ok(View(users));
    }

    private static UsersDto View(UserStore users)
    {
        var all = users.All();
        return new(all.Any(u => u.Enabled), Enforced, all.Select(u => new UserDto(u.Id, u.Name, UserStore.RoleName(u.Role), u.Enabled)).ToList());
    }

    private static SessionDto Session(UserSession? s, UserStore users, bool includeToken) =>
        s is null
            ? new SessionDto("", 0, "", "", users.Active, Enforced)
            : new SessionDto(includeToken ? s.Token : "", s.UserId, s.Name, UserStore.RoleName(s.Role), users.Active, Enforced);
}
