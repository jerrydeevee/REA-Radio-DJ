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

    public async Task SynthesizeAsync(string text, string wavPath, CancellationToken ct)
    {
        var input = string.IsNullOrWhiteSpace(options.Tags) ? text : options.Tags.Trim() + text;
        Reference[]? references = string.IsNullOrWhiteSpace(options.ReferenceAudio) ? null
            : [new Reference(options.ReferenceAudio, options.ReferenceText)];

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var response = await http.PostAsJsonAsync(options.BaseUrl.TrimEnd('/') + "/v1/audio/speech",
            new SpeechRequest(options.Model, input, "default", "wav", references, options.Temperature), cts.Token);
        response.EnsureSuccessStatusCode();

        var tmp = wavPath + ".part";
        await using (var file = File.Create(tmp))
            await response.Content.CopyToAsync(file, cts.Token);
        if (new FileInfo(tmp).Length < 1000) throw new InvalidOperationException("Higgs returned no audio");
        File.Move(tmp, wavPath, overwrite: true);
    }

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

    public async Task SynthesizeAsync(string text, string wavPath, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                using var synth = new SpeechSynthesizer();
                var voice = PickVoice(synth);
                if (voice is not null) synth.SelectVoice(voice);
                synth.Rate = 1;
                synth.SetOutputToWaveFile(wavPath);
                synth.Speak(text);
                synth.SetOutputToNull();
            }, ct);
        }
        finally { Gate.Release(); }
    }

    private string? PickVoice(SpeechSynthesizer synth)
    {
        var voices = synth.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo).ToList();
        if (!string.IsNullOrWhiteSpace(options.SapiVoice))
        {
            var named = voices.FirstOrDefault(v => v.Name.Contains(options.SapiVoice, StringComparison.OrdinalIgnoreCase));
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
