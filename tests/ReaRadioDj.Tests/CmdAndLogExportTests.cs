using System.Net;
using System.Net.Sockets;
using System.Text;
using ReaRadioDj.Cmd;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Tests;

/// <summary>rea-cmd against a fake control port (no engine, no sound), and the exported log.</summary>
public class CmdAndLogExportTests
{
    /// <summary>A control port that answers like the engine: greeting, optional AUTH, one line per command.</summary>
    private static (int Port, Task<List<string>> Seen) FakeEngine(string? password)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var seen = Task.Run(async () =>
        {
            var lines = new List<string>();
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            bool authed = password is null;
            await writer.WriteLineAsync(authed ? "OK REA-Radio-DJ" : "OK REA-Radio-DJ, AUTH vereist");
            while (await reader.ReadLineAsync() is { } line)
            {
                lines.Add(line);
                if (line == "QUIT") { await writer.WriteLineAsync("OK bye"); break; }
                if (!authed)
                {
                    authed = line == $"AUTH {password}";
                    await writer.WriteLineAsync(authed ? "OK" : "ERR AUTH vereist");
                    if (!authed) break;
                    continue;
                }
                await writer.WriteLineAsync(line.StartsWith("ONZIN") ? "ERR onbekend commando" : $"OK {line.ToLowerInvariant()}");
            }
            listener.Stop();
            return lines;
        });
        return (port, seen);
    }

    [Fact]
    public async Task Commands_go_one_per_line_and_each_gets_its_answer()
    {
        var (port, seen) = FakeEngine(null);
        var answers = await CommandClient.SendAsync("127.0.0.1", port, null, ["NEXT", "", "# commentaar", " AUTO OFF ", "ONZIN"], TimeSpan.FromSeconds(5));
        Assert.Equal(3, answers.Count);
        Assert.True(answers[0].Ok);
        Assert.Equal("next", answers[0].Text);
        Assert.Equal("AUTO OFF", answers[1].Command);
        Assert.False(answers[2].Ok);
        Assert.Equal("onbekend commando", answers[2].Text);
        Assert.Equal(["NEXT", "AUTO OFF", "ONZIN", "QUIT"], await seen);
    }

    [Fact]
    public async Task With_a_password_it_signs_in_first()
    {
        var (port, seen) = FakeEngine("geheim");
        var answers = await CommandClient.SendAsync("127.0.0.1", port, "geheim", ["NEXT"], TimeSpan.FromSeconds(5));
        Assert.True(Assert.Single(answers).Ok);
        Assert.Equal("AUTH geheim", (await seen)[0]);
    }

    [Fact]
    public async Task A_wrong_password_is_reported()
    {
        var (port, _) = FakeEngine("geheim");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CommandClient.SendAsync("127.0.0.1", port, "fout", ["NEXT"], TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_missing_password_is_reported_before_any_command()
    {
        var (port, _) = FakeEngine("geheim");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CommandClient.SendAsync("127.0.0.1", port, null, ["NEXT"], TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Answers_are_read_as_ok_or_err()
    {
        Assert.Equal(new CommandAnswer("X", true, ""), CommandClient.Parse("X", "OK"));
        Assert.Equal(new CommandAnswer("X", false, "kapot"), CommandClient.Parse("X", "ERR kapot"));
        Assert.False(CommandClient.Parse("X", "iets anders").Ok);
    }

    private static LogItem Song(string artist, string title, double length) => new()
    {
        Type = LogItemType.Song, Origin = "klok Standaard",
        Media = new MediaItem { Path = title + ".mp3", Kind = MediaKind.Song, Artist = artist, Title = title, Category = "Pop", Duration = length },
    };

    [Fact]
    public void The_log_gets_expected_start_times_and_a_csv_line_each()
    {
        var start = new DateTime(2026, 11, 13, 21, 0, 0);
        var a = Song("Artiest; met puntkomma", "Eerste", 200);
        var b = Song("B", "Tweede", 180);
        var rows = LogExport.Rows([a, b], start);
        Assert.Equal(2, rows.Count);
        Assert.Equal(start, rows[0].Starts);
        Assert.Equal(start.AddSeconds(a.PlannedLength), rows[1].Starts);
        Assert.Equal("nummer", rows[0].Kind);

        var csv = LogExport.Csv(rows, "Radio Test").Split("\r\n");
        Assert.StartsWith("zender;datum;begintijd", csv[0]);
        Assert.Contains("\"Artiest; met puntkomma\"", csv[1]);
        Assert.Contains(";21:00:00;", csv[1]);

        var pdf = LogExport.Pdf(rows, "Radio Test", start);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
