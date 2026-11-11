using System.Runtime.InteropServices;
using System.Text;

namespace ReaRadioDj.Core.Data;

/// <summary>
/// Keeps a secret (a SIP password) encrypted for the Windows account the engine runs under (DPAPI). What is stored
/// is useless on another account or pc; it is never written to a log or handed back over the API.
/// </summary>
public static class WindowsSecret
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Size; public IntPtr Data; }

    private const int UiForbidden = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    public static string Protect(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return Crypt(Encoding.UTF8.GetBytes(text), protect: true) is { } b ? Convert.ToBase64String(b) : "";
    }

    /// <summary>The secret, or "" when there is none or it cannot be read under this account.</summary>
    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try { return Crypt(Convert.FromBase64String(stored), protect: false) is { } b ? Encoding.UTF8.GetString(b) : ""; }
        catch (FormatException) { return ""; }
    }

    private static byte[]? Crypt(byte[] data, bool protect)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var input = new Blob { Size = data.Length, Data = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
        var output = new Blob();
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            bool ok = protect
                ? CryptProtectData(ref input, "REA-Radio-DJ", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
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
