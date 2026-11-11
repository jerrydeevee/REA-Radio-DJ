using System.Net.Sockets;
using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Voip;

namespace ReaRadioDj.Server.Voip;

/// <summary>
/// The RTP lines on the two VoIP channels: connect, hang up, pre-listen, and what they measure. The last settings of
/// each channel are kept, so the window opens with them.
/// </summary>
public sealed class VoipService(AudioRouter router, StationDb db, ILogger<VoipService> logger) : IDisposable
{
    public const string Key = "voip-lines";
    private readonly object _gate = new();

    private Dictionary<string, VoipConnectRequest> Saved()
    {
        if (db.GetSetting(Key) is not { } json) return [];
        try { return JsonSerializer.Deserialize<Dictionary<string, VoipConnectRequest>>(json, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return []; }
    }

    public static VoipConnectRequest Default(string id) =>
        new(id == AudioRouter.Voip2Strip ? 5006 : 5004, "", id == AudioRouter.Voip2Strip ? 5006 : 5004, "pcma", 60);

    public IReadOnlyList<VoipLineDto> Status()
    {
        if (!router.IsStarted) return [];
        var saved = Saved();
        return router.VoipChannels.Select(c => Dto(c, saved.GetValueOrDefault(c.Id) ?? Default(c.Id))).ToList();
    }

    private VoipLineDto Dto(VoipChannel c, VoipConnectRequest settings)
    {
        var s = c.Strip;
        var (l, r) = s.Levels();
        if (c.Line is not { } line)
            return new VoipLineDto(c.Id, s.Name, false, "niet verbonden", settings.Codec, null, null, 0, new JitterDto(0, 0, 0, 0, 0, 0, 0, 0, 0, settings.JitterMs, 0),
                -1, null, null, c.Pfl, s.OnAir, Math.Max(l, r), settings);
        var st = line.Status();
        var j = st.Jitter;
        return new VoipLineDto(c.Id, s.Name, true, st.State, st.Codec, st.LocalPort, st.Remote, st.Sent,
            new JitterDto(j.Received, j.Played, j.Lost, j.Late, j.Duplicates, j.Underruns, j.Skipped, j.JitterMs, j.BufferMs, j.TargetMs, j.LossPercent),
            st.SecondsSinceAudio, st.OpenedUtc, st.Error, c.Pfl, s.OnAir, Math.Max(l, r), settings);
    }

    /// <summary>Opens a line on a channel (an open line there is hung up first). Returns an error text or null.</summary>
    public string? Connect(string id, VoipConnectRequest r)
    {
        if (!router.IsStarted) return "de audio van de engine is niet gestart";
        if (!AudioRouter.IsVoip(id)) return $"geen VoIP-kanaal: {id}";
        if (!RtpCodecs.Names.Contains((r.Codec ?? "").ToLowerInvariant())) return $"onbekende codec: {r.Codec}";
        if (r.LocalPort is < 1024 or > 65535) return "lokale poort moet tussen 1024 en 65535 liggen";
        if (!string.IsNullOrWhiteSpace(r.RemoteHost) && r.RemotePort is < 1 or > 65535) return "poort van de overkant ongeldig";
        lock (_gate)
        {
            var channel = router.Voip(id);
            channel.Detach()?.Dispose(); // frees the local port too
            RtpLine line;
            try
            {
                line = new RtpLine(new RtpLineOptions
                {
                    LocalPort = r.LocalPort, RemoteHost = r.RemoteHost?.Trim() ?? "", RemotePort = r.RemotePort,
                    Codec = r.Codec!.ToLowerInvariant(), JitterMs = Math.Clamp(r.JitterMs, 20, 1000),
                }, BassRuntime.SampleRate);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse) { return $"poort {r.LocalPort} is al in gebruik"; }
            catch (Exception ex) when (ex is SocketException or ArgumentException) { return ex.Message; }
            try { channel.Attach(line); }
            catch (BassException ex) { line.Dispose(); return ex.Message; }

            var saved = Saved();
            saved[id] = r with { Codec = r.Codec!.ToLowerInvariant() };
            db.SetSetting(Key, JsonSerializer.Serialize(saved, JsonSerializerOptions.Web));
            logger.LogInformation("VoIP {Channel}: line open on port {Port} to {Remote} ({Codec})", id, line.LocalPort,
                string.IsNullOrWhiteSpace(r.RemoteHost) ? "whoever calls" : $"{r.RemoteHost}:{r.RemotePort}", line.Codec.Name);
            return null;
        }
    }

    public bool Hangup(string id)
    {
        if (!router.IsStarted || !AudioRouter.IsVoip(id)) return false;
        lock (_gate)
        {
            var line = router.Voip(id).Detach();
            if (line is null) return false;
            line.Dispose();
            logger.LogInformation("VoIP {Channel}: hung up", id);
            return true;
        }
    }

    public bool SetPfl(string id, bool on)
    {
        if (!router.IsStarted || !AudioRouter.IsVoip(id)) return false;
        router.Voip(id).Pfl = on;
        return true;
    }

    public void Dispose()
    {
        if (!router.IsStarted) return;
        foreach (var c in router.VoipChannels) c.Detach()?.Dispose();
    }
}
