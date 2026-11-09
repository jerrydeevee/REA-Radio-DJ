using System.Globalization;
using System.Net.Http.Json;
using System.Speech.Synthesis;
using System.Text.Json.Serialization;

namespace ReaRadioDj.Core.Presenter;

public interface ISpeechEngine
{
    string Name { get; }
    /// <summary>Speaks <paramref name="text"/> into a WAV file at <paramref name="wavPath"/>.</summary>
    Task SynthesizeAsync(string text, string wavPath, CancellationToken ct);
}

/// <summary>
/// Higgs TTS 3 (Boson AI) served by SGLang-Omni or vLLM-Omni: POST {BaseUrl}/v1/audio/speech, body
/// {model, input, voice, response_format, references, temperature}; the response body is the WAV file (24 kHz mono).
/// Note: the Higgs model license is research / non-commercial; that suits this free project, not a commercial one.
/// </summary>
public sealed class HiggsSpeechEngine(HttpClient http, TtsOptions options) : ISpeechEngine
{
    public string Name => "higgs";

    public Task SynthesizeAsync(string text, string wavPath, CancellationToken ct) => SynthesizeAsync(text, wavPath, null, ct);

    /// <summary>Speaks with a voice profile (reference clip, tags, temperature); null = the appsettings voice.</summary>
    public async Task SynthesizeAsync(string text, string wavPath, VoiceProfile? profile, CancellationToken ct)
    {
        string tags = profile?.Tags ?? options.Tags;
        string refAudio = profile?.ReferenceAudio ?? options.ReferenceAudio;
        string refText = profile?.ReferenceText ?? options.ReferenceText;
        double temperature = profile?.Temperature ?? options.Temperature;
        // Tags that the script already starts with are not doubled.
        var input = string.IsNullOrWhiteSpace(tags) || text.TrimStart().StartsWith("<|", StringComparison.Ordinal) ? text : tags.Trim() + text;
        Reference[]? references = string.IsNullOrWhiteSpace(refAudio) ? null : [new Reference(refAudio, refText)];

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var response = await http.PostAsJsonAsync(options.BaseUrl.TrimEnd('/') + "/v1/audio/speech",
            new SpeechRequest(options.Model, input, "default", "wav", references, temperature), cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cts.Token);
            throw new HttpRequestException($"Higgs {(int)response.StatusCode}: {(body.Length > 300 ? body[..300] : body)}");
        }

        var tmp = wavPath + ".part";
        await using (var file = File.Create(tmp))
            await response.Content.CopyToAsync(file, cts.Token);
        if (new FileInfo(tmp).Length < 1000) throw new InvalidOperationException("Higgs returned no audio");
        File.Move(tmp, wavPath, overwrite: true);
    }

    /// <summary>Is the Higgs server up, which models does it serve and how fast does it answer?</summary>
    public async Task<HiggsServerStatus> ProbeAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var models = await http.GetFromJsonAsync<ModelList>(options.BaseUrl.TrimEnd('/') + "/v1/models", cts.Token);
            var names = models?.Data?.Select(m => m.Id).Where(id => !string.IsNullOrEmpty(id)).ToList() ?? [];
            return new HiggsServerStatus(true, names, sw.Elapsed.TotalMilliseconds, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            return new HiggsServerStatus(false, [], sw.Elapsed.TotalMilliseconds, ex is OperationCanceledException ? "geen antwoord (draait de server?)" : ex.Message);
        }
    }

    private sealed record ModelList([property: JsonPropertyName("data")] List<ModelEntry>? Data);
    private sealed record ModelEntry([property: JsonPropertyName("id")] string Id);

    private sealed record SpeechRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input,
        [property: JsonPropertyName("voice")] string Voice,
        [property: JsonPropertyName("response_format")] string ResponseFormat,
        [property: JsonPropertyName("references"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Reference[]? References,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record Reference(
        [property: JsonPropertyName("audio_path")] string AudioPath,
        [property: JsonPropertyName("text")] string Text);
}

/// <summary>Windows built-in speech (SAPI). Offline and always available on Windows; quality depends on the installed voices.</summary>
public sealed class SapiSpeechEngine(TtsOptions options, string language) : ISpeechEngine
{
    private static readonly SemaphoreSlim Gate = new(1, 1); // SAPI is not thread-safe across synthesizers writing files

    public string Name => "sapi";

    public Task SynthesizeAsync(string text, string wavPath, CancellationToken ct) => SynthesizeAsync(text, wavPath, null, ct);

    public async Task SynthesizeAsync(string text, string wavPath, VoiceProfile? profile, CancellationToken ct)
    {
        string wanted = profile?.SapiVoice is { Length: > 0 } v ? v : options.SapiVoice;
        // Higgs control tags mean nothing to Windows speech.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<\|[^|>]*\|>", "").Trim();
        await Gate.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                using var synth = new SpeechSynthesizer();
                var voice = PickVoice(synth, wanted);
                if (voice is not null) synth.SelectVoice(voice);
                synth.Rate = 1;
                synth.SetOutputToWaveFile(wavPath);
                synth.Speak(text);
                synth.SetOutputToNull();
            }, ct);
        }
        finally { Gate.Release(); }
    }

    private string? PickVoice(SpeechSynthesizer synth, string wanted)
    {
        var voices = synth.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo).ToList();
        if (!string.IsNullOrWhiteSpace(wanted))
        {
            var named = voices.FirstOrDefault(v => v.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
            if (named is not null) return named.Name;
        }
        var byLanguage = voices.FirstOrDefault(v =>
            v.Culture.TwoLetterISOLanguageName.Equals(language, StringComparison.OrdinalIgnoreCase));
        return byLanguage?.Name;
    }

    public static IReadOnlyList<string> InstalledVoices()
    {
        using var synth = new SpeechSynthesizer();
        return synth.GetInstalledVoices().Select(v => $"{v.VoiceInfo.Name} ({v.VoiceInfo.Culture.Name})").ToList();
    }
}

public sealed record HiggsServerStatus(bool Online, IReadOnlyList<string> Models, double LatencyMs, string? Error);
