using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Remote;

namespace ReaRadioDj.Server.Remote;

/// <summary>
/// Line-based TCP control: one command per line, the answer is one line starting with "OK" or "ERR".
/// With a password set, the first line must be "AUTH &lt;password&gt;". "QUIT" closes the connection.
/// </summary>
public sealed class TcpControlService(ReaRadioDjOptions options, CommandBus bus, RemoteClientStore clients, ILogger<TcpControlService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Control;
        if (o.TcpPort <= 0) return;
        TcpListener listener;
        try
        {
            listener = new TcpListener(RemoteControl.Address(o.BindAddress), o.TcpPort);
            listener.Start();
        }
        catch (SocketException ex)
        {
            logger.LogWarning("TCP control on port {Port} not available: {Error}", o.TcpPort, ex.Message);
            return;
        }
        logger.LogInformation("TCP control on {Address}:{Port}", o.BindAddress, o.TcpPort);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => Serve(client, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task Serve(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
            // a remote control known by its address works with its own limits, without AUTH
            var from = RemoteClients.Of(client.Client.RemoteEndPoint);
            RemoteClient? limited = clients.Find(null, from);
            bool authed = string.IsNullOrEmpty(options.Control.Password) || limited is not null;
            await writer.WriteLineAsync(authed ? "OK REA-Radio-DJ" : "OK REA-Radio-DJ, AUTH vereist");

            while (!ct.IsCancellationRequested && await reader.ReadLineAsync(ct) is { } line)
            {
                line = line.Trim();
                if (line.Length == 0) continue;
                if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase)) { await writer.WriteLineAsync("OK bye"); break; }
                if (line.StartsWith("AUTH ", StringComparison.OrdinalIgnoreCase))
                {
                    var given = line[5..].Trim();
                    if (!string.IsNullOrEmpty(options.Control.Password) && RemoteControl.SamePassword(given, options.Control.Password))
                    {
                        authed = true; limited = null;
                        await writer.WriteLineAsync("OK");
                    }
                    else if (RemoteClients.ByKey(clients.Clients, given, from) is { } c)
                    {
                        authed = true; limited = c;
                        await writer.WriteLineAsync($"OK {c.Name}");
                    }
                    else
                    {
                        logger.LogWarning("TCP control: wrong password or key from {Remote}", remote);
                        await writer.WriteLineAsync(authed ? "ERR onbekende sleutel" : "ERR AUTH vereist");
                    }
                    continue;
                }
                if (!authed)
                {
                    await writer.WriteLineAsync("ERR AUTH vereist");
                    continue;
                }
                var result = bus.Run(line, $"tcp {remote}", limited);
                await writer.WriteLineAsync((result.Ok ? "OK " : "ERR ") + result.Text.ReplaceLineEndings(" "));
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException) { }
    }
}

/// <summary>
/// OSC over UDP. Addresses map to commands: <c>/rea/next</c> → NEXT, <c>/rea/cart/1/5</c> → CART 1 5,
/// <c>/rea/mic/toggle</c> → MIC TOGGLE. String and number arguments are appended (<c>/rea/volume/log 80</c>), and
/// <c>/rea/cmd "AUTO OFF"</c> runs the text as is. A single argument 0 is a button release and is ignored.
/// </summary>
public sealed class OscControlService(ReaRadioDjOptions options, CommandBus bus, RemoteClientStore clients, ILogger<OscControlService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Control;
        if (o.OscPort <= 0) return;
        UdpClient udp;
        try { udp = new UdpClient(new IPEndPoint(RemoteControl.Address(o.BindAddress), o.OscPort)); }
        catch (SocketException ex)
        {
            logger.LogWarning("OSC control on port {Port} not available: {Error}", o.OscPort, ex.Message);
            return;
        }
        logger.LogInformation("OSC control on {Address}:{Port}", o.BindAddress, o.OscPort);
        using (udp)
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult packet;
                try { packet = await udp.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { continue; }
                foreach (var message in Osc.Parse(packet.Buffer))
                    if (Osc.ToCommand(message) is { } command)
                        bus.Run(command, $"osc {packet.RemoteEndPoint}", clients.Find(null, RemoteClients.Of(packet.RemoteEndPoint)));
            }
        }
    }
}

/// <summary>
/// Plain text commands over UDP: one command per datagram, the answer ("OK ..." or "ERR ...") goes back to the sender.
/// With a password set, the datagram starts with the password and a space ("geheim NEXT").
/// </summary>
public sealed class UdpControlService(ReaRadioDjOptions options, CommandBus bus, RemoteClientStore clients, ILogger<UdpControlService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var o = options.Control;
        if (o.UdpPort <= 0) return;
        UdpClient udp;
        try { udp = new UdpClient(new IPEndPoint(RemoteControl.Address(o.BindAddress), o.UdpPort)); }
        catch (SocketException ex)
        {
            logger.LogWarning("UDP control on port {Port} not available: {Error}", o.UdpPort, ex.Message);
            return;
        }
        logger.LogInformation("UDP control on {Address}:{Port}", o.BindAddress, o.UdpPort);
        using (udp)
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult packet;
                try { packet = await udp.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { continue; }
                var text = Encoding.UTF8.GetString(packet.Buffer).Trim();
                string answer;
                var from = RemoteClients.Of(packet.RemoteEndPoint);
                // the first word may be the key of a remote control; else one known by its address; else the password
                int sp = text.IndexOf(' ');
                RemoteClient? limited = sp > 0 ? RemoteClients.ByKey(clients.Clients, text[..sp], from) : null;
                if (limited is not null) text = text[(sp + 1)..];
                else if (!(sp > 0 && !string.IsNullOrEmpty(o.Password) && RemoteControl.SamePassword(text[..sp], o.Password)))
                    limited = clients.Find(null, from);
                if (limited is null && !string.IsNullOrEmpty(o.Password))
                {
                    int space = text.IndexOf(' ');
                    if (space <= 0 || !RemoteControl.SamePassword(text[..space], o.Password))
                    {
                        logger.LogWarning("UDP control: wrong password from {Remote}", packet.RemoteEndPoint);
                        answer = "ERR wachtwoord";
                        await Reply(udp, answer, packet.RemoteEndPoint, ct);
                        continue;
                    }
                    text = text[(space + 1)..];
                }
                var r = bus.Run(text, $"udp {packet.RemoteEndPoint}", limited);
                answer = (r.Ok ? "OK " : "ERR ") + r.Text.ReplaceLineEndings(" ");
                await Reply(udp, answer, packet.RemoteEndPoint, ct);
            }
        }
    }

    private static async Task Reply(UdpClient udp, string text, IPEndPoint to, CancellationToken ct)
    {
        try { await udp.SendAsync(Encoding.UTF8.GetBytes(text), to, ct); }
        catch (SocketException) { }
    }
}

public static class RemoteControl
{
    public static IPAddress Address(string bind) =>
        string.IsNullOrWhiteSpace(bind) || bind is "*" or "0.0.0.0" ? IPAddress.Any
        : bind is "localhost" ? IPAddress.Loopback
        : IPAddress.TryParse(bind, out var ip) ? ip : IPAddress.Loopback;

    public static bool SamePassword(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
}
