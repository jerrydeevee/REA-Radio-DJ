using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Users;

namespace ReaRadioDj.Tests;

/// <summary>Accounts with a last day, and blocking a name after wrong PINs.</summary>
public sealed class UserEndAndLockTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-users-end-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public UserEndAndLockTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static readonly DateOnly Day = new(2026, 11, 13);

    [Fact]
    public void An_account_signs_in_up_to_and_including_its_last_day()
    {
        var users = new UserStore(_db);
        users.Save(0, "Beheer", UserRole.Admin, true, "2580");
        var (guest, err) = users.Save(0, "Gast", UserRole.Presenter, true, "1234", Day);
        Assert.Null(err);
        Assert.Equal(Day, guest!.Until);
        Assert.NotNull(users.Verify("Gast", "1234", Day));
        Assert.Null(users.Verify("Gast", "1234", Day.AddDays(1)));
        Assert.True(users.All().Single(u => u.Name == "Gast").Expired(Day.AddDays(1)));

        // extended: signs in again, and stays in the list throughout
        users.Save(guest.Id, "Gast", UserRole.Presenter, true, null, Day.AddDays(30));
        Assert.NotNull(users.Verify("Gast", "1234", Day.AddDays(1)));
        users.Save(guest.Id, "Gast", UserRole.Presenter, true, null, null);
        Assert.NotNull(users.Verify("Gast", "1234", Day.AddYears(5)));
    }

    [Fact]
    public void One_administrator_without_an_end_always_remains()
    {
        var users = new UserStore(_db);
        Assert.NotNull(users.Save(0, "Beheer", UserRole.Admin, true, "2580", Day).Error);   // the first one has no end
        var (admin, _) = users.Save(0, "Beheer", UserRole.Admin, true, "2580");
        Assert.NotNull(users.Save(admin!.Id, "Beheer", UserRole.Admin, true, null, Day).Error);
        var (second, _) = users.Save(0, "Invaller", UserRole.Admin, true, "1357", Day);
        Assert.NotNull(second);
        Assert.NotNull(users.Delete(admin.Id));                                              // only one with an end left
        Assert.Null(users.Delete(second!.Id));
    }

    [Fact]
    public void A_session_ends_with_the_account()
    {
        var now = new DateTime(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);
        var sessions = new SessionStore(() => now);
        var user = new StationUser(7, "Gast", UserRole.Presenter, true, "", "", DateOnly.FromDateTime(now.ToLocalTime()));
        var s = sessions.Create(user);
        Assert.NotNull(sessions.Get(s.Token));
        now = user.EndsUtc!.Value.AddSeconds(1);
        Assert.Null(sessions.Get(s.Token));
    }

    [Fact]
    public void A_name_is_blocked_after_the_set_number_of_wrong_pins_and_can_be_lifted()
    {
        var guard = new LoginGuard { Settings = new LoginLockSettings(3, 60) };
        var t = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(guard.Fail("Anna", t));
        Assert.False(guard.Fail("anna", t));
        Assert.True(guard.Fail("Anna ", t));                       // the third in a row, any case and spaces
        Assert.True(guard.IsLocked("ANNA", t.AddSeconds(59)));
        Assert.False(guard.IsLocked("Anna", t.AddSeconds(61)));    // over by itself
        Assert.Single(guard.Locked(t));
        Assert.True(guard.Lift("Anna", t.AddSeconds(5)));
        Assert.False(guard.IsLocked("Anna", t.AddSeconds(5)));
        Assert.False(guard.Lift("Anna", t.AddSeconds(5)));         // nothing to lift any more
        Assert.Empty(guard.Locked(t));
    }

    [Fact]
    public void A_right_pin_resets_the_count_and_settings_stay_within_reason()
    {
        var guard = new LoginGuard();
        var t = DateTime.UtcNow;
        for (int i = 0; i < 4; i++) guard.Fail("Piet", t);
        guard.Succeed("Piet");
        Assert.False(guard.Fail("Piet", t));                       // counting starts again
        guard.Settings = new LoginLockSettings(1, 99999);
        Assert.Equal(new LoginLockSettings(3, 3600), guard.Settings);
    }
}
