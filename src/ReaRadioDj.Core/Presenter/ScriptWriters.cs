using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Presenter;

/// <summary>What the presenter knows when writing a break.</summary>
public sealed record BreakContext(string StationName, string PresenterName, DateTime Time, MediaItem? Previous, MediaItem? Next);

public interface IScriptWriter
{
    string Name { get; }
    Task<string> WriteAsync(BreakContext context, CancellationToken ct);
}

/// <summary>Offline fallback: fills in a Dutch template. Always works, needs nothing.</summary>
public sealed class TemplateScriptWriter(Random? random = null) : IScriptWriter
{
    private readonly Random _random = random ?? Random.Shared;
    public string Name => "template";

    private static readonly string[] WithBoth =
    [
        "Dat was {prev}. Het is {time} en je luistert naar {station}. Hier komt {next}!",
        "Je hoorde {prev}. Blijf lekker hangen, want nu komt {next}, hier op {station}.",
        "{station}, het is {time}. Na {prev} gaan we door met {next}.",
    ];
    private static readonly string[] OnlyNext =
    [
        "Je luistert naar {station}, het is {time}. We gaan verder met {next}.",
        "Hier is {presenter} op {station}. Daar is {next}!",
    ];
    private static readonly string[] Nothing =
    [
        "Je luistert naar {station}. Het is {time}. Blijf erbij!",
    ];

    public Task<string> WriteAsync(BreakContext c, CancellationToken ct)
    {
        var set = c.Previous is not null && c.Next is not null ? WithBoth : c.Next is not null ? OnlyNext : Nothing;
        var text = set[_random.Next(set.Length)]
            .Replace("{prev}", Describe(c.Previous))
            .Replace("{next}", Describe(c.Next))
            .Replace("{station}", c.StationName)
            .Replace("{presenter}", c.PresenterName)
            .Replace("{time}", SpokenTime(c.Time));
        return Task.FromResult(text);
    }

    internal static string Describe(MediaItem? m) => m is null ? "" :
        string.IsNullOrWhiteSpace(m.Artist) ? m.Title : $"{m.Title} van {m.Artist}";

    /// <summary>"kwart over drie", "tien voor half vijf" ... rounded to five minutes, the way Dutch radio says it.</summary>
    internal static string SpokenTime(DateTime t)
    {
        string[] hours = ["twaalf", "een", "twee", "drie", "vier", "vijf", "zes", "zeven", "acht", "negen", "tien", "elf"];
        int minute = (int)Math.Round(t.Minute / 5.0) * 5;
        int hour = t.Hour;
        if (minute == 60) { minute = 0; hour++; }
        string H(int h) => hours[((h % 12) + 12) % 12];
        return minute switch
        {
            0 => $"{H(hour)} uur",
            5 => $"vijf over {H(hour)}",
            10 => $"tien over {H(hour)}",
            15 => $"kwart over {H(hour)}",
            20 => $"tien voor half {H(hour + 1)}",
            25 => $"vijf voor half {H(hour + 1)}",
            30 => $"half {H(hour + 1)}",
            35 => $"vijf over half {H(hour + 1)}",
            40 => $"tien over half {H(hour + 1)}",
            45 => $"kwart voor {H(hour + 1)}",
            50 => $"tien voor {H(hour + 1)}",
            _ => $"vijf voor {H(hour + 1)}",
        };
    }
}

/// <summary>Any OpenAI-compatible /v1/chat/completions endpoint: Ollama, LM Studio, vLLM, OpenAI ...</summary>
public sealed class OpenAiScriptWriter(HttpClient http, PresenterOptions options) : IScriptWriter
{
    public string Name => $"llm:{options.Llm.Model}";

    public async Task<string> WriteAsync(BreakContext c, CancellationToken ct)
    {
        var llm = options.Llm;
        var system = $"""
            {options.Persona}
            Je heet {c.PresenterName} en presenteert op {c.StationName}. Taal: {options.Language}.
            Schrijf ALLEEN de gesproken tekst voor één korte presentatie tussen twee nummers, maximaal {options.MaxWords} woorden.
            Geen aanhalingstekens, geen opsommingen, geen regieaanwijzingen, geen emoji. Verzin geen feiten over artiesten.
            """;
        string previous = TemplateScriptWriter.Describe(c.Previous), next = TemplateScriptWriter.Describe(c.Next);
        var user = $"""
            Tijd: {c.Time:HH:mm} ({TemplateScriptWriter.SpokenTime(c.Time)}).
            Vorig nummer: {(previous.Length > 0 ? previous : "(geen)")}.
            Volgend nummer: {(next.Length > 0 ? next : "(geen)")}.
            Kondig het volgende nummer aan.
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, llm.BaseUrl.TrimEnd('/') + "/v1/chat/completions")
        {
            Content = JsonContent.Create(new ChatRequest(llm.Model,
                [new ChatMessage("system", system), new ChatMessage("user", user)], llm.Temperature, 200)),
        };
        if (!string.IsNullOrWhiteSpace(llm.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", llm.ApiKey);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(llm.TimeoutSeconds));
        using var response = await http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cts.Token);
        var text = body?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Language model returned no text");
        return text;
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] ChatMessage[] Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse([property: JsonPropertyName("choices")] List<Choice>? Choices);
    private sealed record Choice([property: JsonPropertyName("message")] ChatMessage? Message);
}

/// <summary>Cleans language-model output so only speakable text reaches the voice.</summary>
public static partial class ScriptSanitizer
{
    public static string Clean(string text, int maxWords)
    {
        var t = ThinkBlock().Replace(text, " ");       // reasoning models: <think>...</think>
        t = t.Replace("**", "").Replace("__", "");     // markdown bold is emphasis, not a stage direction
        t = StageDirection().Replace(t, " ");          // (lacht), [jingle], *pauze*
        t = Markup().Replace(t, " ");                  // markdown and stray symbols
        t = t.Replace('“', ' ').Replace('”', ' ').Replace('"', ' ').Replace('„', ' ');
        t = Whitespace().Replace(t, " ").Trim();
        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > maxWords)
        {
            t = string.Join(' ', words.Take(maxWords));
            int stop = t.LastIndexOfAny(['.', '!', '?']);
            t = stop > t.Length / 2 ? t[..(stop + 1)] : t.TrimEnd(',', ';', ':') + ".";
        }
        return t;
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex ThinkBlock();
    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]|\*[^*]+\*")] private static partial Regex StageDirection();
    [GeneratedRegex(@"[#*_`~>|]+|^\s*[-•]\s*", RegexOptions.Multiline)] private static partial Regex Markup();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}
