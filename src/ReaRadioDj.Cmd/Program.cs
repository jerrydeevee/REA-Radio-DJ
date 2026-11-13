using ReaRadioDj.Cmd;

// rea-cmd: send commands to the REA-Radio-DJ engine over its TCP control port.
//   rea-cmd NEXT
//   rea-cmd "AUTO OFF" "CART 1 5"
//   rea-cmd --file commands.txt
//   type commands.txt | rea-cmd -
// The password comes from --password-env <NAME> (an environment variable), so it is not visible in the process list.
// Exit code: 0 all OK, 1 a command gave ERR, 2 no connection or wrong password, 3 wrong use.

string host = "127.0.0.1";
int port = 8601;
string? password = Environment.GetEnvironmentVariable("REA_CONTROL_PASSWORD");
double timeout = 10;
bool quiet = false;
var commands = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    string? Next() => i + 1 < args.Length ? args[++i] : null;
    switch (a)
    {
        case "-h" or "--help" or "/?":
            Usage();
            return 0;
        case "--host":
            host = Next() ?? host;
            break;
        case "--port":
            if (!int.TryParse(Next(), out port) || port is <= 0 or > 65535) return Fail("Geef een poort tussen 1 en 65535.");
            break;
        case "--password-env":
            var name = Next();
            password = name is null ? null : Environment.GetEnvironmentVariable(name);
            if (password is null) return Fail($"De omgevingsvariabele {name} bestaat niet.");
            break;
        case "--timeout":
            if (!double.TryParse(Next(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out timeout) || timeout <= 0)
                return Fail("Geef een wachttijd in seconden.");
            break;
        case "-q" or "--quiet":
            quiet = true;
            break;
        case "--file":
            var file = Next();
            if (file is null || !File.Exists(file)) return Fail($"Bestand niet gevonden: {file}");
            commands.AddRange(File.ReadAllLines(file));
            break;
        case "-":
            while (Console.In.ReadLine() is { } line) commands.Add(line);
            break;
        default:
            if (a.StartsWith("--", StringComparison.Ordinal)) return Fail($"Onbekende optie: {a}");
            commands.Add(a);
            break;
    }
}

if (commands.All(c => c.Trim().Length == 0 || c.TrimStart().StartsWith('#')))
{
    Usage();
    return 3;
}

try
{
    var answers = await CommandClient.SendAsync(host, port, password, commands, TimeSpan.FromSeconds(timeout));
    foreach (var r in answers)
    {
        if (r.Ok && !quiet) Console.WriteLine(r.Text.Length > 0 ? $"OK  {r.Command}: {r.Text}" : $"OK  {r.Command}");
        else if (!r.Ok) Console.Error.WriteLine($"ERR {r.Command}: {r.Text}");
    }
    return answers.All(r => r.Ok) ? 0 : 1;
}
catch (UnauthorizedAccessException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException)
{
    Console.Error.WriteLine($"Geen verbinding met de engine op {host}:{port}: {(ex is OperationCanceledException ? "geen antwoord op tijd" : ex.Message)}");
    Console.Error.WriteLine("Staat de TCP-bediening aan? Instellingen › Op afstand in de studio.");
    return 2;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 3;
}

static void Usage() => Console.WriteLine("""
    rea-cmd - stuur commando's naar de engine van REA-Radio-DJ (TCP-bediening)

    Gebruik:
      rea-cmd [opties] COMMANDO [COMMANDO ...]
      rea-cmd [opties] --file commando's.txt
      type commando's.txt | rea-cmd [opties] -

    Opties:
      --host ADRES          engine-computer (standaard 127.0.0.1)
      --port POORT          TCP-bedieningspoort (standaard 8601)
      --password-env NAAM   lees het bedieningswachtwoord uit deze omgevingsvariabele
                            (standaard REA_CONTROL_PASSWORD, als die bestaat)
      --timeout SECONDEN    wachttijd voor de hele opdracht (standaard 10)
      -q, --quiet           alleen fouten tonen

    Voorbeelden:
      rea-cmd NEXT
      rea-cmd "AUTO OFF" "CART 1 5"

    Lege regels en regels die met # beginnen worden overgeslagen.
    Afsluitcode: 0 alles OK, 1 een commando gaf ERR, 2 geen verbinding of verkeerd wachtwoord, 3 verkeerd gebruik.
    """);
