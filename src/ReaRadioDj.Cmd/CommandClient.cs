using System.Net.Sockets;
using System.Text;

namespace ReaRadioDj.Cmd;

/// <summary>The answer of the engine to one command: "OK ..." or "ERR ...".</summary>
public sealed record CommandAnswer(string Command, bool Ok, string Text);

/// <summary>
/// Talks to the line-based TCP control port of the engine: it reads the greeting, signs in with AUTH when a password
/// is given, sends each command on its own line and reads one answer line per command, then says QUIT.
/// </summary>
public static class CommandClient
{
    public static async Task<IReadOnlyList<CommandAnswer>> SendAsync(string host, int port, string? password,
        IEnumerable<string> commands, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cts.Token);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

        var greeting = await reader.ReadLineAsync(cts.Token) ?? throw new IOException("De engine gaf geen antwoord.");
        if (!greeting.StartsWith("OK", StringComparison.Ordinal)) throw new IOException($"Onverwacht antwoord van de engine: {greeting}");
        if (string.IsNullOrEmpty(password) && greeting.Contains("AUTH", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("De engine vraagt het bedieningswachtwoord. Zet het in de omgevingsvariabele REA_CONTROL_PASSWORD of geef --password-env NAAM.");
        if (!string.IsNullOrEmpty(password))
        {
            await writer.WriteLineAsync($"AUTH {password}".AsMemory(), cts.Token);
            var auth = await reader.ReadLineAsync(cts.Token) ?? "";
            if (!auth.StartsWith("OK", StringComparison.Ordinal)) throw new UnauthorizedAccessException("Het bedieningswachtwoord klopt niet.");
        }

        var answers = new List<CommandAnswer>();
        foreach (var raw in commands)
        {
            var command = raw.Trim();
            if (command.Length == 0 || command.StartsWith('#')) continue;   // empty lines and comments in a script
            await writer.WriteLineAsync(command.AsMemory(), cts.Token);
            var line = await reader.ReadLineAsync(cts.Token) ?? throw new IOException("De verbinding is verbroken.");
            answers.Add(Parse(command, line));
        }
        try { await writer.WriteLineAsync("QUIT".AsMemory(), cts.Token); } catch (IOException) { }
        return answers;
    }

    public static CommandAnswer Parse(string command, string line)
    {
        if (line.StartsWith("OK", StringComparison.Ordinal)) return new(command, true, line.Length > 2 ? line[2..].Trim() : "");
        if (line.StartsWith("ERR", StringComparison.Ordinal)) return new(command, false, line.Length > 3 ? line[3..].Trim() : "");
        return new(command, false, line.Trim());
    }
}
