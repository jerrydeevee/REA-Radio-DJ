using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace ReaRadioDj.Studio.Services;

/// <summary>
/// Spotify Soloist on this studio computer (developer test): where soloist.exe is, the device name, the WebSocket port
/// and the user's own API key. The key is encrypted for this Windows account (DPAPI) and never leaves this computer.
/// </summary>
public sealed class SoloistSettings
{
    /// <summary>Own path of soloist.exe; empty = the Soloist folder of REA-Radio-DJ (see <see cref="SoloistLauncher.FindExe"/>).</summary>
    public string ExePath { get; set; } = "";
    public string DeviceName { get; set; } = "REA ontwikkeltest";
    public int Port { get; set; } = 9090;
    /// <summary>The API key, encrypted with DPAPI for the current Windows user (base64). Empty = use SOLOIST_API_KEY.</summary>
    public string ApiKeyProtected { get; set; } = "";
    /// <summary>Start Soloist when the studio starts (and stop it again when the studio closes).</summary>
    public bool StartWithStudio { get; set; }
    /// <summary>The Soloist window connects by itself when it opens.</summary>
    public bool AutoConnect { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore] public string Address => $"127.0.0.1:{Port}";
}

/// <summary>Starts and stops the Soloist that the studio itself started; a Soloist started elsewhere is left alone.</summary>
public static class SoloistLauncher
{
    private static Process? _process;
    private static readonly object Gate = new();

    public static bool Running { get { lock (Gate) return _process is { HasExited: false }; } }

    /// <summary>The Soloist folder of REA-Radio-DJ: %AppData%\REA-Radio-DJ\soloist (always writable, kept across updates).</summary>
    public static string ManagedFolder => Path.Combine(StudioSettings.Folder, "soloist");

    /// <summary>Also looked at: a soloist folder next to the studio program (placed there by an administrator).</summary>
    public static string ProgramFolder => Path.Combine(AppContext.BaseDirectory, "soloist");

    /// <summary>
    /// Where soloist.exe is: the own path when one is set, otherwise the Soloist folder of REA-Radio-DJ, otherwise the
    /// soloist folder next to the program. Null when it is nowhere.
    /// </summary>
    public static string? FindExe(SoloistSettings s)
    {
        string own = s.ExePath.Trim().Trim('"');
        if (own.Length > 0) return File.Exists(own) ? own : null;
        foreach (var folder in new[] { ManagedFolder, ProgramFolder })
        {
            if (!Directory.Exists(folder)) continue;
            string direct = Path.Combine(folder, "soloist.exe");
            if (File.Exists(direct)) return direct;
            // A zip may hold a folder of its own.
            var found = Directory.EnumerateFiles(folder, "soloist.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>
    /// Unpacks the official Soloist zip (downloaded by the user from Spotify for Developers) into the Soloist folder of
    /// REA-Radio-DJ, replacing an older version. Returns a problem in plain Dutch, or null.
    /// </summary>
    public static string? InstallZip(string zipPath)
    {
        if (!File.Exists(zipPath)) return "Dat bestand bestaat niet.";
        bool wasRunning = Running;
        Stop();
        string target = ManagedFolder, staging = target + ".nieuw";
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            string root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
            {
                if (!zip.Entries.Any(e => string.Equals(e.Name, "soloist.exe", StringComparison.OrdinalIgnoreCase)))
                    return "In deze zip zit geen soloist.exe: kies de Windows-versie (soloist_release_windows_x86_64.zip).";
                foreach (var entry in zip.Entries)
                {
                    string dest = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "Deze zip bevat ongeldige paden.";
                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: true);
                }
            }
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(staging, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return "Installeren mislukt: " + ex.Message;
        }
        LastMessage = "Soloist is geïnstalleerd in de map van REA-Radio-DJ.";
        WriteLog("--- geïnstalleerd uit " + Path.GetFileName(zipPath), "");
        Changed?.Invoke();
        return wasRunning ? "restart" : null;
    }
    public static string LogPath => Path.Combine(StudioSettings.Folder, "soloist.log");

    /// <summary>What happened last (started, stopped, or why it stopped).</summary>
    public static string LastMessage { get; private set; } = "";
    public static event Action? Changed;

    // ---------------------------------------------------------------- the key, encrypted for this Windows account

    public static string Protect(string text)
    {
        if (text.Length == 0) return "";
        var bytes = Crypt(Encoding.UTF8.GetBytes(text), protect: true);
        return bytes is null ? "" : Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string protectedText)
    {
        if (protectedText.Length == 0) return "";
        try { return Crypt(Convert.FromBase64String(protectedText), protect: false) is { } b ? Encoding.UTF8.GetString(b) : ""; }
        catch (FormatException) { return ""; }
    }

    /// <summary>The key to start with: the stored one, otherwise the SOLOIST_API_KEY environment variable.</summary>
    public static string ApiKey(SoloistSettings s)
    {
        var stored = Unprotect(s.ApiKeyProtected);
        return stored.Length > 0 ? stored : Environment.GetEnvironmentVariable("SOLOIST_API_KEY")?.Trim() ?? "";
    }

    public static string KeySource(SoloistSettings s) =>
        s.ApiKeyProtected.Length > 0 && Unprotect(s.ApiKeyProtected).Length > 0 ? "opgeslagen (versleuteld voor jouw Windows-account)"
        : s.ApiKeyProtected.Length > 0 ? "opgeslagen, maar niet te lezen onder dit Windows-account: vul hem opnieuw in"
        : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SOLOIST_API_KEY")) ? "uit de omgevingsvariabele SOLOIST_API_KEY"
        : "nog geen sleutel";

    // ---------------------------------------------------------------- starting and stopping

    /// <summary>Starts soloist.exe with these settings. Returns a problem in plain Dutch, or null when it started.</summary>
    public static string? Start(SoloistSettings s)
    {
        lock (Gate)
        {
            if (_process is { HasExited: false }) return null;
            if (FindExe(s) is not { } exe)
                return s.ExePath.Trim().Length > 0 ? "Op het eigen pad staat geen soloist.exe."
                    : "Soloist is nog niet geïnstalleerd: download de Windows-zip en kies Installeren uit zip.";
            if (!string.Equals(Path.GetFileName(exe), "soloist.exe", StringComparison.OrdinalIgnoreCase)) return "Dat is niet soloist.exe.";
            if (s.Port is < 1024 or > 65535) return "Kies een poort tussen 1024 en 65535.";
            string key = ApiKey(s);
            if (key.Length == 0) return "Vul je Soloist-API-sleutel in (of zet hem in SOLOIST_API_KEY).";
            string name = string.IsNullOrWhiteSpace(s.DeviceName) ? "REA ontwikkeltest" : s.DeviceName.Trim();

            var info = new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            // Only on this computer: the Soloist WebSocket has no authentication.
            foreach (var a in new[] { "--device-name", name, "--api-key", key, "--ws", s.Address }) info.ArgumentList.Add(a);
            try
            {
                Directory.CreateDirectory(StudioSettings.Folder);
                var p = new Process { StartInfo = info, EnableRaisingEvents = true };
                p.OutputDataReceived += (_, e) => WriteLog(e.Data, key);
                p.ErrorDataReceived += (_, e) => WriteLog(e.Data, key);
                p.Exited += (_, _) => OnExited(p);
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                _process = p;
                WriteLog($"--- gestart: {name} op {s.Address}", key);
                LastMessage = $"Soloist draait (\"{name}\" op {s.Address}).";
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                return "Soloist starten mislukt: " + ex.Message;
            }
        }
        Changed?.Invoke();
        return null;
    }

    /// <summary>Stops the Soloist this studio started (by its own process handle, nothing else).</summary>
    public static void Stop()
    {
        Process? p;
        lock (Gate) { p = _process; _process = null; }
        if (p is null) return;
        try
        {
            if (!p.HasExited)
            {
                p.Kill();
                p.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { p.Dispose(); }
        LastMessage = "Soloist gestopt.";
        WriteLog("--- gestopt door de studio", "");
        Changed?.Invoke();
    }

    private static void OnExited(Process p)
    {
        int code;
        try { code = p.ExitCode; } catch (InvalidOperationException) { return; }
        lock (Gate) { if (_process != p) return; _process = null; }
        // Exit code 10: this Soloist build is past its 90 days.
        LastMessage = code switch
        {
            0 => "Soloist is gestopt.",
            10 => "Deze Soloist-versie is verlopen (90 dagen): download een nieuwe.",
            _ => $"Soloist is gestopt (code {code}); zie soloist.log.",
        };
        WriteLog($"--- gestopt, code {code}", "");
        Changed?.Invoke();
    }

    private static void WriteLog(string? line, string key)
    {
        if (line is null) return;
        if (key.Length > 0) line = line.Replace(key, "••••");
        try { lock (Gate) File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---------------------------------------------------------------- DPAPI (crypt32), current user

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    private const int UiForbidden = 0x1;

    private static byte[]? Crypt(byte[] data, bool protect)
    {
        var input = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            bool ok = protect
                ? CryptProtectData(ref input, "REA-Radio-DJ Soloist", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok) return null;
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}
