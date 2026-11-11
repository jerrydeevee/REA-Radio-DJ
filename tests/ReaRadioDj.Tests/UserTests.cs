using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Studio;
using ReaRadioDj.Core.Users;

namespace ReaRadioDj.Tests;

public sealed class UserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-users-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public UserTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void First_user_must_be_an_administrator_and_one_must_remain()
    {
        var users = new UserStore(_db);
        Assert.False(users.Active);
        Assert.NotNull(users.Save(0, "Anna", UserRole.Presenter, true, "1234").Error);

        var (admin, err) = users.Save(0, "Jeroen", UserRole.Admin, true, "2580");
        Assert.Null(err);
        Assert.True(users.Active);
        var (anna, _) = users.Save(0, "Anna", UserRole.Presenter, true, "1234");
        Assert.NotNull(anna);

        Assert.NotNull(users.Save(admin!.Id, "Jeroen", UserRole.Technician, true, null).Error);   // last administrator
        Assert.NotNull(users.Save(admin.Id, "Jeroen", UserRole.Admin, false, null).Error);       // or switching it off
        Assert.NotNull(users.Delete(admin.Id));
        Assert.NotNull(users.Save(0, "anna", UserRole.Presenter, true, "9999").Error);            // same name
        Assert.Null(users.Delete(anna!.Id));
        Assert.Single(users.All());
    }

    [Fact]
    public void Pin_is_hashed_and_checked()
    {
        var users = new UserStore(_db);
        Assert.NotNull(users.Save(0, "Jeroen", UserRole.Admin, true, "12a4").Error);
        Assert.NotNull(users.Save(0, "Jeroen", UserRole.Admin, true, "123").Error);
        var (u, _) = users.Save(0, "Jeroen", UserRole.Admin, true, "2580");

        Assert.DoesNotContain("2580", _db.GetSetting("users"));
        Assert.Equal(u!.Id, users.Verify(" jeroen ", "2580")?.Id);
        Assert.Null(users.Verify("Jeroen", "2581"));
        Assert.Null(users.Verify("Iemand", "2580"));

        // an empty PIN keeps the old one; a new PIN replaces it
        users.Save(u.Id, "Jeroen B", UserRole.Admin, true, "");
        Assert.NotNull(users.Verify("Jeroen B", "2580"));
        users.Save(u.Id, "Jeroen B", UserRole.Admin, true, "1111");
        Assert.Null(users.Verify("Jeroen B", "2580"));
        Assert.NotNull(users.Verify("Jeroen B", "1111"));
    }

    [Fact]
    public void Switched_off_user_cannot_sign_in()
    {
        var users = new UserStore(_db);
        users.Save(0, "Jeroen", UserRole.Admin, true, "2580");
        var (anna, _) = users.Save(0, "Anna", UserRole.Presenter, true, "1234");
        users.Save(anna!.Id, "Anna", UserRole.Presenter, false, null);
        Assert.Null(users.Verify("Anna", "1234"));
    }

    [Theory]
    [InlineData("GET", "/api/settings", null)]
    [InlineData("PUT", "/api/settings", UserRole.Admin)]
    [InlineData("POST", "/api/users", UserRole.Admin)]
    [InlineData("DELETE", "/api/users/3", UserRole.Admin)]
    [InlineData("POST", "/api/users/login", null)]
    [InlineData("POST", "/api/users/logout", null)]
    [InlineData("POST", "/api/system/restart", UserRole.Admin)]
    [InlineData("PUT", "/api/stream/targets", UserRole.Admin)]
    [InlineData("POST", "/api/stream/start", UserRole.Technician)]
    [InlineData("PUT", "/api/routing", UserRole.Technician)]
    [InlineData("PUT", "/api/hardware/mackie", UserRole.Technician)]
    [InlineData("PUT", "/api/channels/mic", UserRole.Technician)]
    [InlineData("POST", "/api/channels/mic/onair", null)]
    [InlineData("POST", "/api/channels/mic/record", null)]
    [InlineData("PUT", "/api/transition-rules", UserRole.Technician)]
    [InlineData("PUT", "/api/planner/grid", UserRole.Technician)]
    [InlineData("POST", "/api/transport/next", null)]
    [InlineData("POST", "/api/carts/1/5/press", null)]
    [InlineData("POST", "/api/log/media", null)]
    [InlineData("POST", "/api/mixer/mic/toggle", null)]
    [InlineData("POST", "/api/voip/1/connect", null)]
    [InlineData("PUT", "/api/settingsx", null)]
    [InlineData("PUT", "/API/Settings/", UserRole.Admin)]
    public void Changes_need_the_right_role(string method, string path, UserRole? role) =>
        Assert.Equal(role, AccessRules.Required(method, path));

    [Fact]
    public void Sessions_expire_and_renew()
    {
        var now = new DateTime(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc);
        var sessions = new SessionStore(() => now);
        var user = new StationUser(4, "Anna", UserRole.Presenter, true, "", "");
        var s = sessions.Create(user);
        Assert.Equal(48, s.Token.Length);

        now = now.AddHours(11);
        Assert.Equal("Anna", sessions.Get(s.Token)?.Name);   // renewed
        now = now.AddHours(11);
        Assert.NotNull(sessions.Get(s.Token));
        now = now.AddHours(13);
        Assert.Null(sessions.Get(s.Token));

        var t = sessions.Create(user);
        sessions.RemoveUser(4);
        Assert.Null(sessions.Get(t.Token));
        Assert.Null(sessions.Get(""));
    }

    [Fact]
    public void As_run_log_records_the_presenter()
    {
        var log = new AsRunLog(_db);
        log.Begin("cartwall", "jingle", null, null, false);
        log.Presenter = "Anna";
        log.Begin("automatisering", "song", null, 200, false);
        var day = log.Day(DateOnly.FromDateTime(DateTime.Now));
        Assert.Equal(["", "Anna"], day.Select(e => e.Presenter));
        Assert.Contains(";presentator\r\n", AsRunLog.ToCsv(day));
        Assert.EndsWith(";Anna\r\n", AsRunLog.ToCsv(day));
    }
}
