using System.Text;

namespace ReaRadioDj.Core.Voip.Sip;

/// <summary>
/// A SIP request or response (RFC 3261): start line, headers in their order (names compared without case, compact
/// forms understood) and a body.
/// </summary>
public sealed class SipMessage
{
    private static readonly Dictionary<string, string> Compact = new(StringComparer.OrdinalIgnoreCase)
    {
        ["v"] = "Via", ["f"] = "From", ["t"] = "To", ["i"] = "Call-ID", ["m"] = "Contact", ["l"] = "Content-Length",
        ["c"] = "Content-Type", ["k"] = "Supported", ["s"] = "Subject", ["e"] = "Content-Encoding",
    };

    public string? Method { get; init; }
    public string? Uri { get; init; }
    public int Status { get; init; }
    public string Reason { get; init; } = "";
    public List<(string Name, string Value)> Headers { get; } = [];
    public string Body { get; set; } = "";

    public bool IsRequest => Method is not null;

    public static SipMessage Request(string method, string uri) => new() { Method = method, Uri = uri };
    public static SipMessage Response(int status, string reason) => new() { Status = status, Reason = reason };

    public string? this[string name] => Headers.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    public IEnumerable<string> All(string name) => Headers.Where(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value);

    public SipMessage Add(string name, string value) { Headers.Add((name, value)); return this; }

    public SipMessage Set(string name, string value)
    {
        int i = Headers.FindIndex(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) Headers[i] = (name, value); else Headers.Add((name, value));
        return this;
    }

    public void Remove(string name) => Headers.RemoveAll(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public string CallId => this["Call-ID"] ?? "";

    /// <summary>CSeq number and method.</summary>
    public (int Number, string Method) CSeq
    {
        get
        {
            var parts = (this["CSeq"] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 && int.TryParse(parts[0], out var n) ? (n, parts[1].ToUpperInvariant()) : (0, "");
        }
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append(IsRequest ? $"{Method} {Uri} SIP/2.0\r\n" : $"SIP/2.0 {Status} {Reason}\r\n");
        foreach (var (n, v) in Headers)
            if (!n.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) sb.Append(n).Append(": ").Append(v).Append("\r\n");
        sb.Append("Content-Length: ").Append(Encoding.UTF8.GetByteCount(Body)).Append("\r\n\r\n").Append(Body);
        return sb.ToString();
    }

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(ToText());

    /// <summary>Reads a message; null when it is not SIP (a keep-alive, STUN, garbage).</summary>
    public static SipMessage? Parse(ReadOnlySpan<byte> data)
    {
        string text;
        try { text = Encoding.UTF8.GetString(data); } catch (ArgumentException) { return null; }
        int headEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        string head = headEnd < 0 ? text : text[..headEnd];
        string body = headEnd < 0 ? "" : text[(headEnd + 4)..];
        var lines = head.Split("\r\n");
        if (lines.Length == 0 || lines[0].Length == 0) return null;

        SipMessage msg;
        var first = lines[0].Split(' ', 3);
        if (first.Length < 3) return null;
        if (first[0] == "SIP/2.0")
        {
            if (!int.TryParse(first[1], out var status)) return null;
            msg = Response(status, first[2]);
        }
        else if (first[2] == "SIP/2.0") msg = Request(first[0].ToUpperInvariant(), first[1]);
        else return null;

        string? name = null, value = null;
        void Flush()
        {
            if (name is null) return;
            string full = Compact.GetValueOrDefault(name, name);
            // headers that may hold a comma-separated list are split, except where commas belong to the value
            if (full is "Via" or "Route" or "Record-Route" or "Contact" && value!.Contains(','))
                foreach (var part in SplitList(value)) msg.Headers.Add((full, part));
            else msg.Headers.Add((full, value!.Trim()));
        }
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t')) { value += " " + line.Trim(); continue; } // folded
            Flush();
            int colon = line.IndexOf(':');
            if (colon <= 0) { name = null; continue; }
            name = line[..colon].Trim();
            value = line[(colon + 1)..];
        }
        Flush();
        if (int.TryParse(msg["Content-Length"], out var length) && length >= 0)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            if (length < bytes.Length) body = Encoding.UTF8.GetString(bytes, 0, length);
        }
        msg.Body = body;
        return msg;
    }

    /// <summary>Splits "a, b" on commas outside quotes and angle brackets.</summary>
    internal static IEnumerable<string> SplitList(string value)
    {
        int depth = 0, start = 0;
        bool quote = false;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '"') quote = !quote;
            else if (!quote && c == '<') depth++;
            else if (!quote && c == '>') depth--;
            else if (!quote && depth == 0 && c == ',')
            {
                yield return value[start..i].Trim();
                start = i + 1;
            }
        }
        yield return value[start..].Trim();
    }

    /// <summary>A parameter of a header value: ";tag=abc" → "abc"; "" for a parameter without a value; null when missing.</summary>
    public static string? Param(string? header, string name)
    {
        if (header is null) return null;
        int gt = header.LastIndexOf('>');
        var tail = gt >= 0 ? header[(gt + 1)..] : header;
        foreach (var p in tail.Split(';').Skip(gt >= 0 ? 0 : 1))
        {
            var kv = p.Split('=', 2);
            if (kv[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return kv.Length > 1 ? kv[1].Trim().Trim('"') : "";
        }
        return null;
    }

    /// <summary>The URI of a name-addr ("Bob" &lt;sip:bob@x&gt;;tag=1 → sip:bob@x) or of a bare addr-spec.</summary>
    public static string UriOf(string? header)
    {
        if (string.IsNullOrEmpty(header)) return "";
        int lt = header.IndexOf('<'), gt = header.IndexOf('>');
        if (lt >= 0 && gt > lt) return header[(lt + 1)..gt];
        int semi = header.IndexOf(';');
        return (semi >= 0 ? header[..semi] : header).Trim();
    }

    /// <summary>Host and port of a SIP URI (sip:user@host:port;params); port 5060 when none is given.</summary>
    public static (string Host, int Port) HostPort(string uri)
    {
        var s = uri;
        if (s.StartsWith("sip:", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        else if (s.StartsWith("sips:", StringComparison.OrdinalIgnoreCase)) s = s[5..];
        int at = s.IndexOf('@');
        if (at >= 0) s = s[(at + 1)..];
        int end = s.IndexOfAny([';', '?', '>']);
        if (end >= 0) s = s[..end];
        int colon = s.LastIndexOf(':');
        return colon > 0 && int.TryParse(s[(colon + 1)..], out var port) ? (s[..colon], port) : (s, 5060);
    }

    /// <summary>The user part of a SIP URI (sip:0612@host → 0612), or "".</summary>
    public static string UserOf(string uri)
    {
        var s = UriOf(uri);
        int colon = s.IndexOf(':'), at = s.IndexOf('@');
        return colon >= 0 && at > colon ? s[(colon + 1)..at] : "";
    }
}
