using System.Text;

namespace ReaRadioDj.Core.Remote;

public sealed record OscMessage(string Address, IReadOnlyList<object> Args);

/// <summary>Just enough OSC 1.0 to read messages and bundles (types i, f, s, T, F; others stop the argument list).</summary>
public static class Osc
{
    public static IEnumerable<OscMessage> Parse(byte[] data)
    {
        var list = new List<OscMessage>();
        try { Read(data, 0, data.Length, list, 0); } catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException) { }
        return list;
    }

    private static void Read(byte[] d, int start, int end, List<OscMessage> into, int depth)
    {
        if (depth > 4 || end - start < 4) return;
        int pos = start;
        string address = Str(d, ref pos, end);
        if (address == "#bundle")
        {
            pos += 8; // time tag: we execute immediately
            while (pos + 4 <= end)
            {
                int size = Int(d, ref pos);
                if (size <= 0 || pos + size > end) return;
                Read(d, pos, pos + size, into, depth + 1);
                pos += size;
            }
            return;
        }
        if (!address.StartsWith('/')) return;
        var args = new List<object>();
        if (pos < end && d[pos] == ',')
        {
            string tags = Str(d, ref pos, end);
            foreach (char t in tags.AsSpan(1))
            {
                if (t == 'i') args.Add(Int(d, ref pos));
                else if (t == 'f') { int bits = Int(d, ref pos); args.Add(BitConverter.Int32BitsToSingle(bits)); }
                else if (t == 's') args.Add(Str(d, ref pos, end));
                else if (t == 'T') args.Add(true);
                else if (t == 'F') args.Add(false);
                else break;
            }
        }
        into.Add(new OscMessage(address, args));
    }

    private static string Str(byte[] d, ref int pos, int end)
    {
        int zero = Array.IndexOf(d, (byte)0, pos, end - pos);
        if (zero < 0) throw new ArgumentException("unterminated string");
        string s = Encoding.UTF8.GetString(d, pos, zero - pos);
        pos = (zero + 4) & ~3;
        return s;
    }

    private static int Int(byte[] d, ref int pos)
    {
        int v = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
        pos += 4;
        return v;
    }

    /// <summary>The command for a message, or null when it is not ours or is a button release.</summary>
    public static string? ToCommand(OscMessage m)
    {
        var parts = m.Address.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("rea", StringComparison.OrdinalIgnoreCase)) return null;

        // Buttons send 1 on press and 0 on release; act on the press only.
        if (m.Args.Count == 1 && m.Args[0] is int or float or bool && IsZero(m.Args[0])) return null;

        if (parts[1].Equals("cmd", StringComparison.OrdinalIgnoreCase))
            return m.Args.FirstOrDefault() as string;

        var words = parts.Skip(1).ToList();
        foreach (var a in m.Args)
        {
            if (m.Args.Count == 1 && a is int i && i == 1 || a is true) continue; // the press itself
            words.Add(a switch
            {
                float f => f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                int n => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                bool b => b ? "ON" : "OFF",
                _ => a.ToString() ?? "",
            });
        }
        return string.Join(' ', words);
    }

    private static bool IsZero(object a) => a switch { int i => i == 0, float f => f == 0, bool b => !b, _ => false };

    /// <summary>One OSC message to send: whole numbers as i, other numbers as f, true/false as T/F, the rest as s.</summary>
    public static byte[] Build(string address, IEnumerable<object> args)
    {
        var tags = new StringBuilder(",");
        var body = new List<byte>();
        foreach (var a in args)
        {
            switch (a)
            {
                case int i: tags.Append('i'); body.AddRange(BigEndian(i)); break;
                case float f: tags.Append('f'); body.AddRange(BigEndian(BitConverter.SingleToInt32Bits(f))); break;
                case double d: tags.Append('f'); body.AddRange(BigEndian(BitConverter.SingleToInt32Bits((float)d))); break;
                case bool b: tags.Append(b ? 'T' : 'F'); break;
                default: tags.Append('s'); body.AddRange(Padded(a.ToString() ?? "")); break;
            }
        }
        return [.. Padded(address), .. Padded(tags.ToString()), .. body];
    }

    /// <summary>Words typed in a command become arguments: 5 → int, 0.8 → float, the rest text ("quoted words" stay together).</summary>
    public static IReadOnlyList<object> Arguments(IEnumerable<string> words) => words.Select(w =>
        int.TryParse(w, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) ? (object)i
        : w.Contains('.') && float.TryParse(w, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f
        : w.Equals("true", StringComparison.OrdinalIgnoreCase) ? true
        : w.Equals("false", StringComparison.OrdinalIgnoreCase) ? false
        : w).ToList();

    private static byte[] BigEndian(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] Padded(string s)
    {
        var raw = Encoding.UTF8.GetBytes(s);
        var padded = new byte[(raw.Length + 4) & ~3];   // at least one zero, then up to a multiple of four
        raw.CopyTo(padded, 0);
        return padded;
    }
}

