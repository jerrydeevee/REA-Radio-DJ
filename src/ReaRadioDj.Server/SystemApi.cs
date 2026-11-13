using Microsoft.Win32;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Server;

/// <summary>
/// Keeping the station running: health check for the watchdog, start at logon, restart, the engine's system log and the
/// backup of the whole station.
/// </summary>
public static class SystemApi
{
    /// <summary>Environment variable the watchdog gives the engine it watches.</summary>
    public const string WatchedVariable = "REA_RADIO_DJ_WATCHED";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "REA-Radio-DJ";

    /// <summary>Exit code of the process (set before a requested restart).</summary>
    public static int ExitCode { get; private set; }

    public static bool Watched => Environment.GetEnvironmentVariable(WatchedVariable) == "1";

    private static FailoverSettingsDto Failover(Core.Studio.FailoverSettings s) => new(s.Role, s.Partner, s.IntervalSeconds, s.LostSeconds,
        s.TakeOverOnSilence, s.GiveBackAutomatically, s.GiveBackSeconds, s.StartStream, s.OnTakeOver, s.OnGiveBack);

    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options, string[] args)
    {
        api.MapGet("/health", (PlayoutEngine engine) =>
            engine.Healthy ? Results.Ok(new { ok = true }) : Results.Json(new { ok = false, stalled = engine.Stalled }, statusCode: 503));

        api.MapGet("/system", (StationDb db) => Dto(options, db, args));

        // main and backup studio: the heartbeat the other engine asks for, the state, settings, and by hand
        api.MapGet("/failover/beat", (FailoverService f) => f.Beat());
        api.MapGet("/failover", (FailoverService f) => f.Status());
        api.MapGet("/failover/settings", (FailoverService f) => Failover(f.Settings));
        api.MapPut("/failover/settings", (FailoverSettingsDto d, FailoverService f) =>
            f.Save(new Core.Studio.FailoverSettings
            {
                Role = d.Role, Partner = d.Partner, IntervalSeconds = d.IntervalSeconds, LostSeconds = d.LostSeconds, TakeOverOnSilence = d.TakeOverOnSilence,
                GiveBackAutomatically = d.GiveBackAutomatically, GiveBackSeconds = d.GiveBackSeconds, StartStream = d.StartStream,
                OnTakeOver = d.OnTakeOver, OnGiveBack = d.OnGiveBack,
            }) is { } error ? Results.BadRequest(new { error }) : Results.Ok(Failover(f.Settings)));
        api.MapPost("/failover/takeover", (FailoverService f) => f.TakeOverNow() is { } error ? Results.BadRequest(new { error }) : Results.Ok(f.Status()));
        api.MapPost("/failover/giveback", (FailoverService f) => f.GiveBackNow() is { } error ? Results.BadRequest(new { error }) : Results.Ok(f.Status()));

        api.MapPut("/system", (SystemSettingsRequest body, StationDb db) =>
        {
            SystemLog.Instance.Detailed = body.DetailedLog;
            db.SetSetting("systemlog", body.DetailedLog ? "detailed" : "normal");
            try { SetAutoStart(body.AutoStart ? Command(args) : null); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                return Results.Problem($"Starten bij aanmelden kon niet worden ingesteld: {ex.Message}", statusCode: 500);
            }
            return Results.Ok(Dto(options, db, args));
        });

        api.MapGet("/system/log", (long? since, bool? detailed) =>
            SystemLog.Instance.Since(since ?? 0, detailed ?? true).Select(l => new SystemLogEntryDto(l.Id, l.Time, l.Level, l.Source, l.Text)));

        api.MapPost("/system/restart", (IHostApplicationLifetime life, MessageCenter messages) =>
        {
            if (!Watched) return Results.Conflict("De engine draait zonder bewaking; start hem opnieuw met de hand.");
            messages.Info("Engine wordt opnieuw gestart");
            Restart(life);
            return Results.Ok();
        });

        api.MapGet("/backup", (HttpContext http) =>
        {
            var buffer = new MemoryStream();
            Backup.Create(options.DataFolder, options.StationName, buffer);
            buffer.Position = 0;
            var name = $"{Safe(options.StationName)}-{DateTime.Now:yyyyMMdd-HHmm}{Backup.Extension}";
            return Results.File(buffer, "application/zip", name);
        });

        api.MapPost("/backup", async (HttpRequest request, IHostApplicationLifetime life, MessageCenter messages) =>
        {
            var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer);
            buffer.Position = 0;
            Backup.Info info;
            try { info = Backup.Stage(options.DataFolder, buffer); }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
            {
                return Results.BadRequest(ex.Message);
            }
            bool restart = Watched;
            messages.Warning($"Reservekopie van {info.Station} ({info.Created:d-M-yyyy HH:mm}) wordt teruggezet bij de volgende start van de engine"
                + (restart ? "; de engine start nu opnieuw" : "; start de engine opnieuw"));
            if (restart) Restart(life);
            return Results.Ok(new BackupStagedDto(info.Station, info.Created, info.Machine, restart));
        });
    }

    /// <summary>Loads the stored level of the system log (call once at start).</summary>
    public static void Load(StationDb db) => SystemLog.Instance.Detailed = db.GetSetting("systemlog") == "detailed";

    private static void Restart(IHostApplicationLifetime life)
    {
        ExitCode = Watchdog.RestartExitCode;
        AutomationService.KeepOnAirState = true;
        // Let the answer reach the studio first.
        _ = Task.Delay(500).ContinueWith(_ => life.StopApplication());
    }

    private static SystemDto Dto(ReaRadioDjOptions options, StationDb db, string[] args)
    {
        var current = AutoStartCommand();
        return new SystemDto(Watched, current is not null, current, SystemLog.Instance.Detailed, Backup.HasPending(options.DataFolder),
            Path.GetFullPath(options.DataFolder), Watchdog.LogPath, Watchdog.Tail(30));
    }

    /// <summary>The command that starts this engine at logon: the same arguments, with the watchdog and the working folder.</summary>
    public static string Command(IEnumerable<string> args)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Path of the executable unknown");
        var parts = new List<string> { exe, "--watchdog", "--workdir", Environment.CurrentDirectory };
        parts.AddRange(args.Where(a => a is not "--demo" and not "--watchdog"));
        return string.Join(" ", parts.Select(Quote));
    }

    private static string Quote(string a) => a.Length > 0 && !a.Any(c => char.IsWhiteSpace(c) || c == '"') ? a : "\"" + a.Replace("\"", "\\\"") + "\"";

    private static string? AutoStartCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) as string;
    }

    private static void SetAutoStart(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (command is null) key.DeleteValue(RunValue, throwOnMissingValue: false);
        else key.SetValue(RunValue, command);
    }

    private static string Safe(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) || c == ' ' ? '-' : c).ToArray()).Trim('-');
        return s.Length == 0 ? "station" : s;
    }
}
