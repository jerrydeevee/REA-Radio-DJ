namespace ReaRadioDj.Contracts;

/// <summary>A user of the studio; the role is "presentator", "technicus" or "beheerder". The PIN never leaves the engine.</summary>
/// <summary>Until: the last day the user may sign in (yyyy-MM-dd), or null; Expired: that day has passed.</summary>
public sealed record UserDto(long Id, string Name, string Role, bool Enabled, string? Until = null, bool Expired = false)
{
    public string RoleText => Role switch { "beheerder" => "Beheerder", "technicus" => "Technicus", _ => "Presentator" };

    /// <summary>What the list shows after the role: switched off, ended or the last day.</summary>
    public string StateText => !Enabled ? "uitgeschakeld" : Expired ? $"verlopen ({Until})" : Until is not null ? $"tot en met {Until}" : "";
}

/// <summary>Add or change a user; an empty PIN keeps the current one. Until: last day (yyyy-MM-dd) or null.</summary>
public sealed record UserSaveRequest(string Name, string Role, bool Enabled, string? Pin, string? Until = null);

/// <summary>Blocking after wrong PINs: how many in a row, for how long, and which names are blocked now.</summary>
public sealed record LoginLocksDto(int Attempts, int LockSeconds, IReadOnlyList<LoginLockDto> Locked);

public sealed record LoginLockDto(string Name, DateTime Until);

public sealed record LoginLockSettingsDto(int Attempts, int LockSeconds);

public sealed record LoginRequest(string Name, string Pin);

/// <summary>
/// Users of the station. <c>Active</c>: there are users, so settings need signing in. <c>Enforced</c>: the engine
/// checks this (false when started with --users-off for recovery).
/// </summary>
public sealed record UsersDto(bool Active, bool Enforced, List<UserDto> Users);

/// <summary>Who is signed in (Token only in the answer to signing in).</summary>
public sealed record SessionDto(string Token, long UserId, string Name, string Role, bool Active, bool Enforced);

public static class UserRoles
{
    public const string Presenter = "presentator";
    public const string Technician = "technicus";
    public const string Admin = "beheerder";
    public const string SessionHeader = "X-Rea-Session";

    public static int Rank(string? role) => role switch { Admin => 2, Technician => 1, _ => 0 };
}
