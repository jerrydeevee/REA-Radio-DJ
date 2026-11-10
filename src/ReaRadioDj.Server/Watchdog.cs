using System.Diagnostics;

namespace ReaRadioDj.Server;

/// <summary>
/// Keeps the engine on air. Started with <c>--watchdog</c>, this process does not play anything itself: it starts the
/// engine as a child process with the same arguments, asks <c>/api/health</c> every two seconds and starts the engine
/// again when it has exited with an error or stopped answering. A clean stop of the engine (exit code 0, for example
/// "Afsluiten" by the tray icon) also ends the watchdog. The engine then reads its log and on-air state back and carries on.
/// </summary>
public static class Watchdog
{
    /// <summary>Environment variable that tells the restarted engine why it was restarted.</summary>
    public const string ReasonVariable = "REA_RADIO_DJ_RESTART_REASON";

    /// <summary>Exit code of an engine that asks to be started again (after restoring a backup, for example).</summary>
    public const int RestartExitCode = 75;

    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);
    private const int MissesBeforeRestart = 4;

    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REA-Radio-DJ", "watchdog.log");

    /// <summary>Handles <c>--workdir &lt;folder&gt;</c> and returns the other arguments.</summary>
    public static string[] ApplyWorkDir(string[] args)
    {
        int i = Array.IndexOf(args, "--workdir");
        if (i < 0 || i + 1 >= args.Length) return args;
        Directory.SetCurrentDirectory(args[i + 1]);
        return [.. args[..i], .. args[(i + 2)..]];
    }

    /// <summary>Address of the health check, from <c>--urls</c> (or the standard port), always on this computer.</summary>
    public static Uri HealthUri(IReadOnlyList<string> args)
    {
        string? urls = null;
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] is "--urls" && i + 1 < args.Count) urls = args[i + 1];
            else if (args[i].StartsWith("--urls=", StringComparison.Ordinal)) urls = args[i]["--urls=".Length..];
        }
        urls ??= Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:8600";
        var first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "http://localhost:8600";
        first = first.Replace("://*", "://127.0.0.1").Replace("://+", "://127.0.0.1").Replace("://0.0.0.0", "://127.0.0.1");
        var b = new UriBuilder(first) { Path = "/api/health" };
        if (b.Host is "localhost") b.Host = "127.0.0.1";
        return b.Uri;
    }

    public static int Run(string[] engineArgs)
    {
        var health = HealthUri(engineArgs);
        using var single = new Mutex(true, $@"Local\REA-Radio-DJ-watchdog-{health.Port}", out bool first);
        if (!first) { Write($"Er bewaakt al een proces de engine op poort {health.Port}; dit proces stopt."); return 2; }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Path of the executable unknown");
        var restarts = new Queue<DateTime>();
        string? reason = null;
        Write($"Bewaking gestart voor {health} in {Environment.CurrentDirectory}");

        while (true)
        {
            // An engine that already runs (started by the studio, for example) is watched until it stops.
            if (reason is null && Alive(http, health))
            {
                Write("Er draait al een engine; de bewaking wacht tot die stopt.");
                int missed = 0;
                while (missed < MissesBeforeRestart) { Thread.Sleep(Poll); missed = Alive(http, health) ? 0 : missed + 1; }
                reason = "de engine die al draaide reageerde niet meer";
            }

            // More than five restarts in ten minutes: something is structurally wrong; wait a minute between attempts.
            while (restarts.Count > 0 && DateTime.Now - restarts.Peek() > TimeSpan.FromMinutes(10)) restarts.Dequeue();
            if (restarts.Count >= 5) { Write("Vijf herstarts in tien minuten; een minuut wachten."); Thread.Sleep(TimeSpan.FromMinutes(1)); }

            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.CurrentDirectory };
            foreach (var a in engineArgs) info.ArgumentList.Add(a);
            info.Environment[SystemApi.WatchedVariable] = "1";
            if (reason is not null) info.Environment[ReasonVariable] = reason;
            using var engine = Process.Start(info);
            if (engine is null) { Write("Engine starten mislukt."); Thread.Sleep(TimeSpan.FromSeconds(10)); continue; }
            Write(reason is null ? $"Engine gestart (proces {engine.Id})" : $"Engine opnieuw gestart (proces {engine.Id}): {reason}");

            var started = DateTime.Now;
            int misses = 0;
            reason = null;
            while (reason is null)
            {
                if (engine.WaitForExit(Poll))
                {
                    int code = engine.ExitCode;
                    if (code == 0) { Write("Engine netjes afgesloten; de bewaking stopt ook."); return 0; }
                    reason = code == RestartExitCode ? "herstart gevraagd" : $"de engine stopte onverwacht (code {code})";
                    break;
                }
                if (DateTime.Now - started < StartGrace) { if (Alive(http, health)) started = DateTime.MinValue; continue; }
                misses = Alive(http, health) ? 0 : misses + 1;
                if (misses < MissesBeforeRestart) continue;
                reason = "de engine reageerde niet meer of het geluid liep niet meer door";
                try { engine.Kill(entireProcessTree: true); engine.WaitForExit(5000); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            Write(reason);
            if (reason != "herstart gevraagd") restarts.Enqueue(DateTime.Now);
        }
    }

    private static bool Alive(HttpClient http, Uri health)
    {
        try
        {
            using var r = http.GetAsync(health).GetAwaiter().GetResult();
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    private static void Write(string text)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}";
        Console.WriteLine(line);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > 1_000_000) File.Move(LogPath, LogPath + ".old", overwrite: true);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The last lines of the watchdog log (for the studio).</summary>
    public static IReadOnlyList<string> Tail(int lines)
    {
        try { return File.Exists(LogPath) ? File.ReadLines(LogPath).TakeLast(lines).ToList() : []; }
        catch (IOException) { return []; }
    }
}
