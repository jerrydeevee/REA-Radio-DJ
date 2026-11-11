using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Users;

/// <summary>What someone at the studio may do. Each role may also do everything of the roles before it.</summary>
public enum UserRole
{
    /// <summary>Presents: everything on air (players, log, carts, microphone), no settings.</summary>
    Presenter = 0,
    /// <summary>Technician: also the sound, hardware, streams and channels.</summary>
    Technician = 1,
    /// <summary>Administrator: also users, the system and backups.</summary>
    Admin = 2,
}

public sealed record StationUser(long Id, string Name, UserRole Role, bool Enabled, string PinHash, string PinSalt);

/// <summary>
/// The people who sign in at the studio with their own name and PIN. The PIN is stored only as a salted
/// PBKDF2 hash. As long as nobody is listed, the station works as before (everything open);
/// the first user must be an administrator, so the station can never lock itself out.
/// </summary>
public sealed class UserStore(StationDb db)
{
    private const string Key = "users";
    private const int Iterations = 100_000;
    private readonly object _lock = new();

    public static string RoleName(UserRole r) => r switch
    {
        UserRole.Admin => "beheerder",
        UserRole.Technician => "technicus",
        _ => "presentator",
    };

    public static UserRole? ParseRole(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "beheerder" or "admin" => UserRole.Admin,
        "technicus" or "technician" => UserRole.Technician,
        "presentator" or "presenter" => UserRole.Presenter,
        _ => null,
    };

    public IReadOnlyList<StationUser> All()
    {
        lock (_lock) return Load();
    }

    /// <summary>True as soon as one user is listed: from then on settings need a signed-in technician or administrator.</summary>
    public bool Active => All().Any(u => u.Enabled);

    /// <summary>A PIN is 4 to 12 digits.</summary>
    public static string? CheckPin(string? pin) =>
        pin is null || pin.Length is < 4 or > 12 || !pin.All(char.IsAsciiDigit) ? "De pincode is 4 tot 12 cijfers." : null;

    /// <summary>Adds (id 0) or changes a user. An empty PIN keeps the old one. Returns the user or the reason it was refused.</summary>
    public (StationUser? User, string? Error) Save(long id, string name, UserRole role, bool enabled, string? pin)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 40) return (null, "Geef een naam (hoogstens 40 tekens).");
        lock (_lock)
        {
            var list = Load();
            var old = list.FirstOrDefault(u => u.Id == id);
            if (id != 0 && old is null) return (null, "Deze gebruiker bestaat niet meer.");
            if (list.Any(u => u.Id != id && string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)))
                return (null, $"Er is al een gebruiker '{name}'.");
            if (old is null || !string.IsNullOrEmpty(pin))
            {
                if (CheckPin(pin) is { } bad) return (null, bad);
            }
            var others = list.Where(u => u.Id != id).ToList();
            if (!(enabled && role == UserRole.Admin) && !others.Any(u => u.Enabled && u.Role == UserRole.Admin))
                return (null, others.Any(u => u.Enabled)
                    ? "Er moet altijd een actieve beheerder overblijven."
                    : "De eerste gebruiker moet een beheerder zijn.");
            string hash = old?.PinHash ?? "", salt = old?.PinSalt ?? "";
            if (!string.IsNullOrEmpty(pin)) (hash, salt) = Hash(pin);
            var user = new StationUser(old?.Id ?? (list.Count == 0 ? 1 : list.Max(u => u.Id) + 1), name, role, enabled, hash, salt);
            others.Add(user);
            Store(others.OrderBy(u => u.Id).ToList());
            return (user, null);
        }
    }

    public string? Delete(long id)
    {
        lock (_lock)
        {
            var list = Load();
            if (list.All(u => u.Id != id)) return null;
            var rest = list.Where(u => u.Id != id).ToList();
            if (rest.Count > 0 && !rest.Any(u => u.Enabled && u.Role == UserRole.Admin))
                return "Er moet altijd een actieve beheerder overblijven.";
            Store(rest);
            return null;
        }
    }

    /// <summary>The user with this name and PIN, or null (unknown, wrong PIN or switched off).</summary>
    public StationUser? Verify(string? name, string? pin)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(pin)) return null;
        var user = All().FirstOrDefault(u => u.Enabled && string.Equals(u.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (user is null || user.PinSalt.Length == 0) return null;
        byte[] salt, expected;
        try { salt = Convert.FromBase64String(user.PinSalt); expected = Convert.FromBase64String(user.PinHash); }
        catch (FormatException) { return null; }
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected) ? user : null;
    }

    private static (string Hash, string Salt) Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    private List<StationUser> Load()
    {
        try { return db.GetSetting(Key) is { } json ? JsonSerializer.Deserialize<List<StationUser>>(json, JsonSerializerOptions.Web) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    private void Store(List<StationUser> list) => db.SetSetting(Key, JsonSerializer.Serialize(list, JsonSerializerOptions.Web));
}
