using System.Diagnostics;
using System.Globalization;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Presenter;

namespace ReaRadioDj.Server;

/// <summary>
/// Everything around the presenter voice: voice profiles, a status of the GPU and the Higgs server, a test console
/// (render a sentence and listen on PFL) and, when configured, starting and stopping the Higgs server.
/// </summary>
public sealed class VoiceService(
    ReaRadioDjOptions options,
    VoiceStore store,
    ProfileSpeechEngine voice,
    HiggsSpeechEngine higgs,
    StudioPlayers players,
    MessageCenter messages,
    ILogger<VoiceService> logger) : IHostedService, IDisposable
{
    private readonly object _gate = new();
    private readonly LinkedList<string> _serverLog = new();
    private Process? _server;
    private (DateTime At, GpuDto? Gpu) _gpu = (DateTime.MinValue, null);
    private (DateTime At, HiggsServerStatus Status) _probe = (DateTime.MinValue, new HiggsServerStatus(false, [], 0, "nog niet gecontroleerd"));

    private TtsOptions Tts => options.Presenter.Tts;
    private string TestFolder => Path.Combine(options.DataFolder, "voice-tests");

    public Task StartAsync(CancellationToken ct)
    {
        if (store.EnsureDefault(Tts)) logger.LogInformation("Default voice profile created");
        if (Tts.StartServer && !string.IsNullOrWhiteSpace(Tts.ServerCommand)) StartServer();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        StopServer();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- status

    public async Task<VoiceStatusDto> StatusAsync(CancellationToken ct)
    {
        HiggsServerStatus probe;
        lock (_gate) probe = _probe.Status;
        if (DateTime.Now - _probe.At > TimeSpan.FromSeconds(3))
        {
            probe = await higgs.ProbeAsync(ct);
            lock (_gate) _probe = (DateTime.Now, probe);
        }
        bool managed = !string.IsNullOrWhiteSpace(Tts.ServerCommand);
        bool running;
        lock (_gate) running = _server is { HasExited: false };
        var def = store.Default;
        return new VoiceStatusDto(Gpu(), probe.Online, probe.Models, Math.Round(probe.LatencyMs), probe.Error, Tts.BaseUrl, Tts.Model,
            managed, running, def?.Name, def?.Engine ?? Tts.Provider, Tts.FallbackToSapi, SapiSpeechEngine.InstalledVoices());
    }

    /// <summary>GPU of this computer via nvidia-smi (cached a few seconds). Null without an NVIDIA GPU.</summary>
    private GpuDto? Gpu()
    {
        lock (_gate)
            if (DateTime.Now - _gpu.At < TimeSpan.FromSeconds(4)) return _gpu.Gpu;
        GpuDto? gpu = null;
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=name,memory.total,memory.used,utilization.gpu,temperature.gpu,driver_version --format=csv,noheader,nounits")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is not null)
            {
                string line = p.StandardOutput.ReadLine() ?? "";
                p.WaitForExit(2000);
                var f = line.Split(',', StringSplitOptions.TrimEntries);
                if (f.Length >= 6)
                    gpu = new GpuDto(f[0], Num(f[1]), Num(f[2]), Num(f[3]), Num(f[4]), f[5]);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        lock (_gate) _gpu = (DateTime.Now, gpu);
        return gpu;

        static double Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    // ---------------------------------------------------------------- test console

    public async Task<VoiceTestResultDto> TestAsync(VoiceTestRequest request, CancellationToken ct)
    {
        var text = (request.Text ?? "").Trim();
        if (text.Length == 0) return new VoiceTestResultDto(false, "", 0, 0, null, "geen tekst");
        if (text.Length > 2000) text = text[..2000];
        var profile = request.ProfileId is long id ? store.Get(id) : store.Default;
        if (request.ProfileId is not null && profile is null) return new VoiceTestResultDto(false, "", 0, 0, null, "stemprofiel bestaat niet");
        if (!string.IsNullOrWhiteSpace(request.Tags) && profile is not null) profile = profile with { Tags = request.Tags };

        Directory.CreateDirectory(TestFolder);
        string file = $"test-{DateTime.Now:yyyyMMdd-HHmmss-fff}.wav";
        string path = Path.Combine(TestFolder, file);
        var sw = Stopwatch.StartNew();
        try
        {
            await voice.SynthesizeAsync(text, path, profile, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or OperationCanceledException
                                   or ArgumentException or UnauthorizedAccessException)
        {
            string engine = profile?.Engine ?? Tts.Provider;
            logger.LogWarning("Voice test with {Engine} failed: {Error}", engine, ex.Message);
            return new VoiceTestResultDto(false, engine, sw.Elapsed.TotalSeconds, 0, null,
                ex is OperationCanceledException ? "Higgs gaf binnen de tijd geen antwoord" : ex.Message);
        }
        double render = sw.Elapsed.TotalSeconds;
        double length = WavSeconds(path);
        Cleanup();

        if (request.Play)
        {
            var media = new MediaItem { Path = path, Kind = MediaKind.Voice, Title = $"Steltest {profile?.Name}", Duration = length, Cues = new CueSet { CueOut = length, Mix = length } };
            try { players.Pfl.Load(media); players.Pfl.Play(); }
            catch (BassException ex) { logger.LogWarning("PFL playback: {Error}", ex.Message); }
        }
        return new VoiceTestResultDto(true, profile?.Engine ?? Tts.Provider, Math.Round(render, 2), Math.Round(length, 2), file, null);
    }

    public string? TestFile(string name)
    {
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..") || !name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Path.Combine(TestFolder, name);
        return File.Exists(path) ? path : null;
    }

    private void Cleanup()
    {
        try
        {
            foreach (var f in new DirectoryInfo(TestFolder).GetFiles("test-*.wav").OrderByDescending(f => f.Name).Skip(20)) f.Delete();
        }
        catch (IOException) { }
    }

    private static double WavSeconds(string path)
    {
        try
        {
            using var r = new BinaryReader(File.OpenRead(path));
            r.BaseStream.Seek(28, SeekOrigin.Begin);
            int byteRate = r.ReadInt32();
            return byteRate > 0 ? Math.Max(0, (r.BaseStream.Length - 44) / (double)byteRate) : 0;
        }
        catch (IOException) { return 0; }
    }

    // ---------------------------------------------------------------- Higgs server process

    public string StartServer()
    {
        if (string.IsNullOrWhiteSpace(Tts.ServerCommand)) return "geen startcommando ingesteld (Presenter:Tts:ServerCommand)";
        lock (_gate)
        {
            if (_server is { HasExited: false }) return "draait al";
            var psi = new ProcessStartInfo(Tts.ServerCommand, Tts.ServerArguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(Tts.ServerWorkingDirectory) ? Environment.CurrentDirectory : Tts.ServerWorkingDirectory,
            };
            try
            {
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (_, e) => Log(e.Data);
                p.ErrorDataReceived += (_, e) => Log(e.Data);
                p.Exited += (_, _) => { Log($"[gestopt, code {SafeExit(p)}]"); messages.Warning("Higgs-server is gestopt"); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                _server = p;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return $"starten mislukt: {ex.Message}";
            }
        }
        _probe = (DateTime.MinValue, _probe.Status);
        messages.Info("Higgs-server wordt gestart (het laden van het model kan even duren)");
        return "gestart";
    }

    public string StopServer()
    {
        Process? p;
        lock (_gate) { p = _server; _server = null; }
        if (p is null || p.HasExited) return "draaide niet";
        try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        p.Dispose();
        return "gestopt";
    }

    public IReadOnlyList<string> ServerLog() { lock (_gate) return _serverLog.ToList(); }

    private void Log(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;
        lock (_gate)
        {
            _serverLog.AddLast($"{DateTime.Now:HH:mm:ss} {line}");
            while (_serverLog.Count > 300) _serverLog.RemoveFirst();
        }
    }

    private static string SafeExit(Process p) { try { return p.ExitCode.ToString(CultureInfo.InvariantCulture); } catch (InvalidOperationException) { return "?"; } }

    public void Dispose() => StopServer();

    // ---------------------------------------------------------------- mapping

    public static VoiceProfileDto Dto(VoiceProfile p) => new(p.Id, p.Name, p.Engine, p.ReferenceAudio, p.ReferenceText, p.Tags, p.Temperature,
        p.SapiVoice, p.Consent, p.ConsentNote, p.IsDefault, p.Problem(), p.FallbackId);

    public static VoiceProfile FromDto(VoiceProfileDto d) => new(d.Id, d.Name ?? "", d.Engine ?? "higgs", d.ReferenceAudio ?? "", d.ReferenceText ?? "",
        d.Tags ?? "", d.Temperature, d.SapiVoice ?? "", d.Consent, d.ConsentNote ?? "", d.IsDefault, d.FallbackId is > 0 ? d.FallbackId : null);
}

public static class VoiceApi
{
    public static void Map(RouteGroupBuilder api)
    {
        var v = api.MapGroup("/voice");
        v.MapGet("/status", (VoiceService s, CancellationToken ct) => s.StatusAsync(ct));
        v.MapGet("/profiles", (VoiceStore st) => st.Profiles.Select(VoiceService.Dto));
        v.MapPost("/profiles", (VoiceProfileDto d, VoiceStore st) => Results.Ok(new { id = st.Save(VoiceService.FromDto(d) with { Id = 0 }) }));
        v.MapPut("/profiles/{id:long}", (long id, VoiceProfileDto d, VoiceStore st) => Results.Ok(new { id = st.Save(VoiceService.FromDto(d) with { Id = id }) }));
        v.MapDelete("/profiles/{id:long}", (long id, VoiceStore st) => st.Delete(id) ? Results.Ok() : Results.NotFound());
        v.MapPost("/profiles/{id:long}/default", (long id, VoiceStore st, MessageCenter m) =>
        {
            if (st.Get(id) is not { } p) return Results.NotFound();
            if (p.Problem() is { } problem) return Results.BadRequest(new { error = problem });
            st.SetDefault(id);
            m.Info($"Presentatorstem: {p.Name}");
            return Results.Ok();
        });
        v.MapPost("/test", async (VoiceTestRequest r, VoiceService s, CancellationToken ct) => await s.TestAsync(r, ct));
        v.MapGet("/test/{file}", (string file, VoiceService s) => s.TestFile(file) is { } path ? Results.File(path, "audio/wav") : Results.NotFound());
        v.MapPost("/server/start", (VoiceService s) => Results.Ok(new { result = s.StartServer() }));
        v.MapPost("/server/stop", (VoiceService s) => Results.Ok(new { result = s.StopServer() }));
        v.MapGet("/server/log", (VoiceService s) => s.ServerLog());
    }
}
