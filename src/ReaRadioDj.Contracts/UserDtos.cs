namespace ReaRadioDj.Contracts;

/// <summary>A user of the studio; the role is "presentator", "technicus" or "beheerder". The PIN never leaves the engine.</summary>
public sealed record UserDto(long Id, string Name, string Role, bool Enabled)
{
    public string RoleText => Role switch { "beheerder" => "Beheerder", "technicus" => "Technicus", _ => "Presentator" };
}

/// <summary>Add or change a user; an empty PIN keeps the current one.</summary>
public sealed record UserSaveRequest(string Name, string Role, bool Enabled, string? Pin);

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
