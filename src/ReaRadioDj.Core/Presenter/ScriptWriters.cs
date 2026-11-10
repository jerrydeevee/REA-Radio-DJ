using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Presenter;

/// <summary>What the presenter knows when writing a break.</summary>
/// <param name="Character">Persona instructions for the language model; null = the general ones.</param>
public sealed record BreakContext(string StationName, string PresenterName, DateTime Time, MediaItem? Previous, MediaItem? Next,
    string? Character = null, int? MaxWords = null, BreakExtra? Extra = null, CreativeIdea? Creative = null);

public interface IScriptWriter
{
    string Name { get; }
    /// <summary>False when the writer has no settings yet; the renderer then uses the template.</summary>
    bool Configured => true;
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
        if (c.Extra is { } extra)
        {
            // weather/news/note between the station line and the next song; for notes the text is read as written
            int cut = text.LastIndexOf(". ", StringComparison.Ordinal);
            text = cut > 0 ? $"{text[..(cut + 1)]} {extra.Spoken} {text[(cut + 2)..]}" : $"{extra.Spoken} {text}";
        }
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
    public bool Configured => !string.IsNullOrWhiteSpace(options.Llm.BaseUrl) && !string.IsNullOrWhiteSpace(options.Llm.Model);

    /// <summary>The server root without a trailing "/v1", so both "http://localhost:11434" and ".../v1" work.</summary>
    internal static string Root(string baseUrl)
    {
        var u = baseUrl.Trim().TrimEnd('/');
        return u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? u[..^3] : u;
    }

    /// <summary>Model names the server offers (OpenAI-compatible /v1/models; Ollama and LM Studio both have it).</summary>
    public static async Task<IReadOnlyList<string>> ListModelsAsync(HttpClient http, string baseUrl, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Root(baseUrl) + "/v1/models");
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await http.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
        return doc.RootElement.TryGetProperty("data", out var data)
            ? data.EnumerateArray().Select(m => m.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }

    /// <summary>Servers that refused "reasoning_effort" (they get requests without it).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> NoReasoningOption = new();

    public async Task<string> WriteAsync(BreakContext c, CancellationToken ct)
    {
        var llm = options.Llm;
        var (system, user) = Prompt(options, c);
        var root = Root(llm.BaseUrl);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(llm.TimeoutSeconds));
        // Thinking models (qwen3, deepseek-r1 ...) would spend all tokens on reasoning and say nothing: ask them not to
        // think. A server that does not know the option gets the request again without it.
        bool noThinking = !NoReasoningOption.ContainsKey(root);
        HttpResponseMessage response;
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, root + "/v1/chat/completions")
            {
                Content = JsonContent.Create(new ChatRequest(llm.Model,
                    [new ChatMessage("system", system), new ChatMessage("user", user)], llm.Temperature, 400, noThinking ? "none" : null)),
            };
            if (!string.IsNullOrWhiteSpace(llm.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", llm.ApiKey);
            response = await http.SendAsync(request, cts.Token);
            if (noThinking && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                response.Dispose();
                NoReasoningOption[root] = true;
                noThinking = false;
                continue;
            }
            break;
        }
        using var _ = response;
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cts.Token);
        var text = body?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Language model returned no text");
        return text;
    }

    /// <summary>
    /// The instructions for one break. Small local models write much better Dutch with firm rules and two examples of
    /// the style than with a bare request; the examples use made-up songs so they are never copied literally.
    /// </summary>
    internal static (string System, string User) Prompt(PresenterOptions options, BreakContext c)
    {
        int maxWords = c.MaxWords ?? options.MaxWords;
        if (c.Creative is { } idea) return CreativePrompt(options, c, idea, Math.Max(maxWords, options.Creative.MaxWords));
        var system = $"""
            {(string.IsNullOrWhiteSpace(c.Character) ? options.Persona : c.Character)}
            Je heet {c.PresenterName} en presenteert op {c.StationName}.
            Je schrijft ALLEEN de tekst die je hardop zegt tussen twee nummers, in correct en natuurlijk Nederlands,
            zoals een ervaren presentator het live zou zeggen.

            Regels:
            - Hoogstens {maxWords} woorden, één tot drie korte zinnen.
            - Noem titels en artiesten precies zoals ze gegeven worden; vertaal of verbuig ze niet.
            - Verzin geen feiten over artiesten, nummers, het weer of het nieuws.
            - Geen aanhalingstekens, opsommingen, regieaanwijzingen, emoji of Engelse zinnen.
            - Wissel af: begin niet altijd met "Dat was" of "Hier is".

            Voorbeelden van de toon (andere nummers):
            Dat was Zomeravond van De Kustband. Het is kwart over vier, je luistert naar {c.StationName}. Nu Lisa Mol met Nooit Meer Alleen!
            Op {c.StationName} is het tien voor half negen. Na Regen in Mei van Tom Brink zet ik er eentje op om lekker mee te zingen: Licht Aan van De Fietsers.
            """;
        string previous = TemplateScriptWriter.Describe(c.Previous), next = TemplateScriptWriter.Describe(c.Next);
        var user = $"""
            Het is {TemplateScriptWriter.SpokenTime(c.Time)} ({c.Time:HH:mm}).
            Net gedraaid: {(previous.Length > 0 ? previous : "(niets)")}.
            Hierna: {(next.Length > 0 ? next : "(nog niet bekend)")}.
            {(c.Extra is { } x ? $"Verwerk dit er kort en natuurlijk in, vóór je het volgende nummer aankondigt:\n{x.Fact}" : "")}
            Schrijf de presentatie.
            """;
        return (system, user);
    }

    /// <summary>
    /// A creative break: the presenter talks about a theme in the chosen tone and then leads into the next song. The tone
    /// changes, the limits never do.
    /// </summary>
    internal static (string System, string User) CreativePrompt(PresenterOptions options, BreakContext c, CreativeIdea idea, int maxWords)
    {
        var system = $"""
            {(string.IsNullOrWhiteSpace(c.Character) ? options.Persona : c.Character)}
            Je heet {c.PresenterName} en presenteert op {c.StationName}, een lokale radiozender.
            Je bent een creatieve presentator: tussen twee nummers praat je even over een onderwerp, op een persoonlijke manier,
            alsof je het tegen één luisteraar thuis of in de auto hebt. Je schrijft ALLEEN de tekst die je hardop zegt,
            in correct en natuurlijk Nederlands.

            Toon van deze keer: {CreativeTones.Name(idea.Tone)}.
            {CreativeTones.Instruction(idea.Tone)}

            {CreativeTones.Limits}

            Regels:
            - Hoogstens {maxWords} woorden, twee tot vier korte zinnen.
            - Maak er iets eigens van: een observatie, een herkenbaar moment of een vraag aan de luisteraars.
            - Eindig met een natuurlijke overgang naar het volgende nummer; noem titel en artiest precies zoals gegeven.
            - Geen aanhalingstekens, opsommingen, regieaanwijzingen, emoji of Engelse zinnen.
            - Begin niet met "Dat was" en niet met het onderwerp als kop.
            """;
        string previous = TemplateScriptWriter.Describe(c.Previous), next = TemplateScriptWriter.Describe(c.Next);
        var user = $"""
            Het is {TemplateScriptWriter.SpokenTime(c.Time)} op een {c.Time.ToString("dddd", new System.Globalization.CultureInfo("nl-NL"))}.
            Net gedraaid: {(previous.Length > 0 ? previous : "(niets)")}.
            Hierna: {(next.Length > 0 ? next : "(nog niet bekend)")}.
            Onderwerp: {idea.Theme}
            Schrijf de presentatie.
            """;
        return (system, user);
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] ChatMessage[] Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("reasoning_effort"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReasoningEffort = null);

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
        t = ForeignScript().Replace(t, "");            // multilingual models sometimes slip in a Chinese or Japanese word
        t = Markup().Replace(t, " ");                  // markdown and stray symbols
        t = t.Replace('“', ' ').Replace('”', ' ').Replace('"', ' ').Replace('„', ' ');
        t = Whitespace().Replace(t, " ").Trim();
        t = SpaceBeforeStop().Replace(t, "$1");        // "Hooked On You ." → "Hooked On You."
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
    [GeneratedRegex(@" ([.,!?;:])")] private static partial Regex SpaceBeforeStop();
    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}\p{IsCJKSymbolsandPunctuation}]+")]
    private static partial Regex ForeignScript();
}
