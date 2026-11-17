using System.Runtime.InteropServices;
using System.Text;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Server;

/// <summary>
/// Tells the operator plainly when the database is not in order: before an upgrade the engine checks it, and when it
/// is damaged it does not touch it and does not start. The message goes to the console, to DATABASE-PROBLEEM.txt in
/// the data folder and, when someone is at the pc, to a window.
/// </summary>
public static class DatabaseReport
{
    public const string FileName = "DATABASE-PROBLEEM.txt";

    /// <summary>"--check-db": checks the database fully and prints the outcome; 0 = in order, 1 = not.</summary>
    public static int CheckAndPrint(string path)
    {
        var c = StationDb.Check(path, full: true);
        if (!c.Exists) { Console.WriteLine($"Geen database gevonden in {path}: bij de eerste start wordt een nieuwe gemaakt."); return 0; }
        Console.WriteLine($"Database: {path}");
        Console.WriteLine($"Versie: {c.Version} (dit programma: {StationDb.SchemaVersion})");
        if (c.Ok)
        {
            Console.WriteLine("Controle: in orde.");
            if (c.NeedsUpgrade) Console.WriteLine("Bij de volgende start wordt hij bijgewerkt; daarvoor wordt een kopie gemaakt.");
            if (c.Newer) Console.WriteLine("Let op: de database komt van een nieuwere versie van REA-Radio-DJ. Werk dit programma bij.");
            return 0;
        }
        Console.WriteLine("Controle: NIET IN ORDE");
        foreach (var p in c.Problems) Console.WriteLine("  " + p);
        Console.WriteLine(WhatToDo);
        return 1;
    }

    private const string WhatToDo = """
        Wat je kunt doen:
        - Laat de vorige versie van REA-Radio-DJ draaien: die werkt met deze database zoals hij is.
        - Zet een reservekopie terug (Instellingen › Systeem › Reservekopie terugzetten, of de dagelijkse reservekopie).
        - Wil je toch bijwerken, start de engine dan eenmaal met --db-upgrade-anyway (op eigen risico; er wordt eerst een kopie gemaakt).
        """;

    /// <summary>The engine does not start: says why, where and what to do.</summary>
    public static void Refuse(string path, DatabaseProblemException ex, bool interactive)
    {
        var text = new StringBuilder()
            .AppendLine($"REA-Radio-DJ start niet: {ex.Message}")
            .AppendLine();
        if (ex.Problems.Count > 0)
        {
            text.AppendLine("Gevonden:");
            foreach (var p in ex.Problems) text.AppendLine("  " + p);
            text.AppendLine();
        }
        text.Append(WhatToDo);
        var message = text.ToString();
        Console.Error.WriteLine(message);
        try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, FileName), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{message}"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (interactive && OperatingSystem.IsWindows())
            try { MessageBoxW(IntPtr.Zero, message, "REA-Radio-DJ: database niet in orde", 0x10 /* MB_ICONERROR */ | 0x40000 /* MB_TOPMOST */); }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
