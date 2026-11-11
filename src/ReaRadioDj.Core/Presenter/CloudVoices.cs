using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security;
using System.Text;
using System.Text.Json.Serialization;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// ElevenLabs text-to-speech (online, paid per character): POST /v1/text-to-speech/{voice}?output_format=pcm_24000
/// with the xi-api-key header; the raw 16-bit PCM is wrapped in a WAV. The profile's voice name field holds the voice ID.
/// </summary>
public sealed class ElevenLabsSpeechEngine(HttpClient http, TtsOptions options) : ISpeechEngine
{
    public string Name => "elevenlabs";
    public bool Configured => !string.IsNullOrWhiteSpace(options.ElevenLabsKey);

    public Task SynthesizeAsync(string text, string wavPath, CancellationToken ct) => SynthesizeAsync(text, wavPath, null, ct);

    public async Task SynthesizeAsync(string text, string wavPath, VoiceProfile? profile, CancellationToken ct)
    {
        if (!Configured) throw new InvalidOperationException("ElevenLabs: geen API-sleutel ingesteld");
        var voice = profile?.SapiVoice.Trim();
        if (string.IsNullOrEmpty(voice)) throw new InvalidOperationException("ElevenLabs: geen stem-ID in het profiel");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(voice)}?output_format=pcm_24000")
        {
            Content = JsonContent.Create(new Body(SpeechText.Plain(text), options.ElevenLabsModel)),
        };
        request.Headers.Add("xi-api-key", options.ElevenLabsKey.Trim());
        await CloudAudio.SaveAsync(http, request, wavPath, raw: true, "ElevenLabs", options.TimeoutSeconds, ct);
    }

    private sealed record Body([property: JsonPropertyName("text")] string Text, [property: JsonPropertyName("model_id")] string ModelId);
}

/// <summary>
/// Azure AI Speech (online): POST https://{region}.tts.speech.microsoft.com/cognitiveservices/v1 with SSML and
/// X-Microsoft-OutputFormat riff-24khz-16bit-mono-pcm. The profile's voice name field holds the voice, for example
/// nl-NL-FennaNeural, nl-NL-ColetteNeural or nl-NL-MaartenNeural.
/// </summary>
public sealed class AzureSpeechEngine(HttpClient http, TtsOptions options) : ISpeechEngine
{
    public string Name => "azure";
    public bool Configured => !string.IsNullOrWhiteSpace(options.AzureKey) && !string.IsNullOrWhiteSpace(options.AzureRegion);

    public Task SynthesizeAsync(string text, string wavPath, CancellationToken ct) => SynthesizeAsync(text, wavPath, null, ct);

    public async Task SynthesizeAsync(string text, string wavPath, VoiceProfile? profile, CancellationToken ct)
    {
        if (!Configured) throw new InvalidOperationException("Azure: geen sleutel of regio ingesteld");
        string voice = string.IsNullOrWhiteSpace(profile?.SapiVoice) ? "nl-NL-FennaNeural" : profile.SapiVoice.Trim();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://{options.AzureRegion.Trim()}.tts.speech.microsoft.com/cognitiveservices/v1")
        {
            Content = new StringContent(Ssml(SpeechText.Plain(text), voice), Encoding.UTF8, "application/ssml+xml"),
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", options.AzureKey.Trim());
        request.Headers.Add("X-Microsoft-OutputFormat", "riff-24khz-16bit-mono-pcm");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("REA-Radio-DJ", "1.0"));
        await CloudAudio.SaveAsync(http, request, wavPath, raw: false, "Azure", options.TimeoutSeconds, ct);
    }

    internal static string Ssml(string text, string voice)
    {
        string lang = voice.Length >= 5 ? voice[..5] : "nl-NL";
        return $"<speak version='1.0' xml:lang='{lang}'><voice name='{SecurityElement.Escape(voice)}'>{SecurityElement.Escape(text)}</voice></speak>";
    }
}

/// <summary>Removes Higgs control tags (&lt;|…|&gt;) for engines that would read them aloud.</summary>
public static partial class SpeechText
{
    [System.Text.RegularExpressions.GeneratedRegex(@"<\|[^|>]*(\|[^|>]*)*\|>")] private static partial System.Text.RegularExpressions.Regex Tags();

    public static string Plain(string text) => Tags().Replace(text, " ").Trim();
}

internal static class CloudAudio
{
    public static async Task SaveAsync(HttpClient http, HttpRequestMessage request, string wavPath, bool raw, string service,
        int timeoutSeconds, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var response = await http.SendAsync(request, cts.Token);
        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 300));
            throw new HttpRequestException($"{service} {(int)response.StatusCode}: {body}");
        }
        if (bytes.Length < 1000) throw new InvalidOperationException($"{service} gaf geen geluid terug");
        if (raw)
        {
            var samples = new float[bytes.Length / 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
            bytes = VoiceProcessor.Write(24000, 1, samples);
        }
        var tmp = wavPath + ".part";
        await File.WriteAllBytesAsync(tmp, bytes, cts.Token);
        File.Move(tmp, wavPath, overwrite: true);
    }
}
