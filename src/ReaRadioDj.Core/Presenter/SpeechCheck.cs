using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// Listens back to a rendered break with a speech recogniser (Whisper behind any OpenAI-compatible
/// /v1/audio/transcriptions endpoint) and scores how well the spoken words match the script: 1 = every word,
/// 0 = nothing. Catches the rare garbled or cut-off render before it goes on air.
/// </summary>
public sealed partial class SpeechCheck(HttpClient http, SpeechCheckOptions options)
{
    public sealed record Result(double Score, string Heard);

    public bool Enabled => options.Enabled && !string.IsNullOrWhiteSpace(options.BaseUrl);

    public async Task<Result> CheckAsync(string wavPath, string script, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(await File.ReadAllBytesAsync(wavPath, ct));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", Path.GetFileName(wavPath));
        form.Add(new StringContent(options.Model), "model");
        form.Add(new StringContent(options.Language), "language");
        form.Add(new StringContent("json"), "response_format");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var response = await http.PostAsync(options.BaseUrl.TrimEnd('/') + "/v1/audio/transcriptions", form, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"spraakherkenning {(int)response.StatusCode}: {(body.Length > 200 ? body[..200] : body)}");
        using var doc = JsonDocument.Parse(body);
        string heard = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        return new Result(Score(script, heard), heard.Trim());
    }

    /// <summary>
    /// 1 - word edit distance / script words, on normalised words (lower case, no accents or punctuation, numbers
    /// written as digits both sides are compared as they are). Never below 0.
    /// </summary>
    public static double Score(string script, string heard)
    {
        var a = Words(SpeechText.Plain(script));
        var b = Words(heard);
        if (a.Length == 0) return b.Length == 0 ? 1 : 0;
        var d = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) d[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            int prev = d[0];
            d[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cur = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (Same(a[i - 1], b[j - 1]) ? 0 : 1));
                prev = cur;
            }
        }
        return Math.Max(0, 1 - d[b.Length] / (double)a.Length);
    }

    /// <summary>
    /// Equal, or close enough for a recogniser: the same sound key ("Years"/"Jeers", "Aa"/"A"), or one letter off in a
    /// longer word ("Hofstede"/"Hofsteede", "Louane"/"Louanje").
    /// </summary>
    private static bool Same(string x, string y)
    {
        if (x == y) return true;
        if (char.IsDigit(x[0]) || char.IsDigit(y[0])) return false;
        x = Sound(x);
        y = Sound(y);
        if (x == y) return true;
        if (Math.Min(x.Length, y.Length) < 4 || Math.Abs(x.Length - y.Length) > 1) return false;
        int i = 0;
        while (i < x.Length && i < y.Length && x[i] == y[i]) i++;
        int k = 0;
        while (k < x.Length - i && k < y.Length - i && x[^(k + 1)] == y[^(k + 1)]) k++;
        return Math.Max(x.Length, y.Length) - i - k <= 1;
    }

    /// <summary>A rough Dutch sound key: spellings that sound alike map to the same letters, doubled letters collapse.</summary>
    internal static string Sound(string w)
    {
        var s = new StringBuilder(w)
            .Replace("ph", "f").Replace("qu", "kw").Replace("ck", "k").Replace("c", "k").Replace("x", "ks")
            .Replace("ij", "ei").Replace("y", "j").Replace("ea", "ee").Replace("ou", "u").Replace("au", "u")
            .Replace("dt", "t").Replace("th", "t").Replace("w", "v").Replace("z", "s").ToString();
        if (s.EndsWith('d')) s = s[..^1] + "t";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (sb.Length == 0 || sb[^1] != c) sb.Append(c);
        return sb.ToString();
    }

    internal static string[] Words(string text)
    {
        var s = text.Replace("&", " en ").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return NonWord().Replace(sb.ToString(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Canonical).ToArray();
    }

    private static readonly string[] Units = ["nul", "een", "twee", "drie", "vier", "vijf", "zes", "zeven", "acht", "negen",
        "tien", "elf", "twaalf", "dertien", "veertien", "vijftien", "zestien", "zeventien", "achttien", "negentien"];
    private static readonly string[] Tens = ["", "", "twintig", "dertig", "veertig", "vijftig", "zestig", "zeventig", "tachtig", "negentig"];

    /// <summary>Number words become digits (Whisper writes "tien over acht" as "10 over 8"); "and" is "en".</summary>
    private static string Canonical(string w)
    {
        if (w == "and") return "en";
        if (w == "een") return w; // mostly the article; Whisper keeps it as a word too
        if (Array.IndexOf(Units, w) is var u and >= 0) return u.ToString(CultureInfo.InvariantCulture);
        if (Array.IndexOf(Tens, w) is var t and >= 2) return (t * 10).ToString(CultureInfo.InvariantCulture);
        for (int ten = 2; ten < Tens.Length; ten++)
            if (w.EndsWith("en" + Tens[ten], StringComparison.Ordinal) && Array.IndexOf(Units, w[..^(Tens[ten].Length + 2)]) is var one and > 0 and < 10)
                return (ten * 10 + one).ToString(CultureInfo.InvariantCulture);
        return w;
    }

    [GeneratedRegex(@"[^a-z0-9]+")] private static partial Regex NonWord();
}
