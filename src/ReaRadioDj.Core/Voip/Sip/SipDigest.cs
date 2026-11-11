using System.Security.Cryptography;
using System.Text;

namespace ReaRadioDj.Core.Voip.Sip;

/// <summary>Digest authentication for SIP (RFC 3261 §22, RFC 2617): MD5 with and without qop=auth.</summary>
public sealed record DigestChallenge(string Realm, string Nonce, string? Opaque, string Algorithm, string? Qop, bool Proxy)
{
    /// <summary>Reads a WWW-Authenticate or Proxy-Authenticate value; null when it is not a usable Digest challenge.</summary>
    public static DigestChallenge? Parse(string? header, bool proxy)
    {
        if (header is null || !header.TrimStart().StartsWith("Digest", StringComparison.OrdinalIgnoreCase)) return null;
        var values = Fields(header.TrimStart()[6..]);
        if (!values.TryGetValue("nonce", out var nonce)) return null;
        string algorithm = values.GetValueOrDefault("algorithm") ?? "MD5";
        if (!algorithm.Equals("MD5", StringComparison.OrdinalIgnoreCase)) return null;
        string? qop = values.GetValueOrDefault("qop");
        if (qop is not null) qop = qop.Split(',').Select(q => q.Trim()).Contains("auth", StringComparer.OrdinalIgnoreCase) ? "auth" : null;
        return new DigestChallenge(values.GetValueOrDefault("realm") ?? "", nonce, values.GetValueOrDefault("opaque"), "MD5", qop, proxy);
    }

    private static Dictionary<string, string> Fields(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in SipMessage.SplitList(text))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            result[part[..eq].Trim()] = part[(eq + 1)..].Trim().Trim('"');
        }
        return result;
    }

    /// <summary>The Authorization (or Proxy-Authorization) header value for a request.</summary>
    public string Answer(string user, string password, string method, string uri, int nc = 1, string? cnonce = null)
    {
        string ha1 = Md5($"{user}:{Realm}:{password}");
        string ha2 = Md5($"{method}:{uri}");
        var sb = new StringBuilder($"Digest username=\"{user}\", realm=\"{Realm}\", nonce=\"{Nonce}\", uri=\"{uri}\"");
        if (Qop == "auth")
        {
            cnonce ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            string count = nc.ToString("x8");
            sb.Append($", response=\"{Md5($"{ha1}:{Nonce}:{count}:{cnonce}:auth:{ha2}")}\", qop=auth, nc={count}, cnonce=\"{cnonce}\"");
        }
        else sb.Append($", response=\"{Md5($"{ha1}:{Nonce}:{ha2}")}\"");
        sb.Append(", algorithm=MD5");
        if (Opaque is not null) sb.Append($", opaque=\"{Opaque}\"");
        return sb.ToString();
    }

    public string HeaderName => Proxy ? "Proxy-Authorization" : "Authorization";

    internal static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
