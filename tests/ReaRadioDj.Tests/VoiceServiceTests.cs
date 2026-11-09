using System.Net;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Presenter;

namespace ReaRadioDj.Tests;

/// <summary>Answers every request with a fixed function and remembers what was asked.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Seen.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
        return answer(request);
    }
}

public sealed class SpeechCheckScoreTests
{
    [Fact]
    public void Same_words_score_one_regardless_of_case_accents_and_punctuation()
    {
        Assert.Equal(1, SpeechCheck.Score("Goedemorgen, Gieten! Het is café-tijd.", "goedemorgen gieten het is cafe tijd"));
    }

    [Fact]
    public void Control_tags_are_not_counted()
    {
        Assert.Equal(1, SpeechCheck.Score("<|emotion:happy|>Dat was Anouk.", "Dat was Anouk."));
    }

    [Fact]
    public void One_letter_off_in_a_long_name_still_counts_as_the_same_word()
    {
        Assert.Equal(1, SpeechCheck.Score("Groeten aan Hofstede", "groeten aan Hofsteede"));
    }

    [Fact]
    public void How_whisper_writes_numbers_names_and_ampersands_is_not_counted_as_wrong()
    {
        const string script = "Goedemorgen Aa en Hunze! Het is tien over acht en dat was Years and Years met King. Straks hoor je Louane.";
        Assert.Equal(1, SpeechCheck.Score(script, "Goedemorgen AA en Hunze. Het is 10 over 8 en dat was Jeers en Jeers met King. Straks hoor je Louanje."));
        Assert.Equal(1, SpeechCheck.Score(script, "Goedemorgen A&Hunze! Het is 10 over 8 en dat was Jeers en Jeers met King. Straks hoor je Luane."));
        Assert.Equal(1, SpeechCheck.Score("het is tweeëntwintig graden", "het is 22 graden"));
    }

    [Fact]
    public void Missing_and_wrong_words_lower_the_score()
    {
        double s = SpeechCheck.Score("dat was een mooi nummer van Anouk", "dat was een nummer van Marco");
        Assert.InRange(s, 0.6, 0.8); // one dropped, one wrong of seven
        Assert.Equal(0, SpeechCheck.Score("hallo daar", "iets heel anders en nog veel meer woorden"));
    }

    [Fact]
    public async Task Check_posts_the_wav_and_scores_what_was_heard()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"text\":\"Dat was Anouk met Birds.\"}", System.Text.Encoding.UTF8, "application/json"),
        });
        var wav = Path.Combine(Path.GetTempPath(), $"rea-chk-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(wav, VoiceProcessor.Write(24000, 1, new float[24000]));
        try
        {
            var check = new SpeechCheck(new HttpClient(handler), new SpeechCheckOptions { Enabled = true, BaseUrl = "http://whisper:8002/" });
            var r = await check.CheckAsync(wav, "Dat was Anouk met Birds.", CancellationToken.None);
            Assert.Equal(1, r.Score);
            Assert.Equal("http://whisper:8002/v1/audio/transcriptions", handler.Seen[0].Request.RequestUri!.ToString());
            Assert.Contains("name=language", handler.Seen[0].Body);
        }
        finally { File.Delete(wav); }
    }
}

public sealed class BackupVoiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-voices-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public BackupVoiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static HttpResponseMessage Wav() =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(VoiceProcessor.Write(24000, 1, VoiceProcessorTests.Voice())) };

    [Fact]
    public async Task A_failing_voice_hands_over_to_its_backup()
    {
        var store = new VoiceStore(_db);
        long azure = store.Save(new VoiceProfile(0, "Fenna (Azure)", "azure", "", "", "", 0.7, "nl-NL-FennaNeural", false, "", false));
        long higgs = store.Save(new VoiceProfile(0, "Higgs", "higgs", "", "", "", 0.7, "", false, "", true, azure));
        Assert.Equal(azure, store.Get(higgs)!.FallbackId);

        var tts = new TtsOptions { BaseUrl = "http://higgs:8001", AzureKey = "k", AzureRegion = "westeurope" };
        var higgsDown = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("gpu vol") });
        var azureUp = new FakeHandler(_ => Wav());
        var engine = new ProfileSpeechEngine(store, new HiggsSpeechEngine(new HttpClient(higgsDown), tts), new SapiSpeechEngine(tts, "nl-NL"), tts,
            azure: new AzureSpeechEngine(new HttpClient(azureUp), tts));

        var wav = Path.Combine(_dir, "break.wav");
        string used = await engine.SpeakAsync("Dat was <|emotion:happy|>Anouk & co.", wav, store.Default, CancellationToken.None);

        Assert.Equal("Fenna (Azure)", used);
        Assert.Contains("reserve Fenna (Azure)", engine.LastFallback);
        Assert.True(new FileInfo(wav).Length > 1000);
        var (request, body) = azureUp.Seen.Single();
        Assert.Equal("https://westeurope.tts.speech.microsoft.com/cognitiveservices/v1", request.RequestUri!.ToString());
        Assert.Contains("<voice name='nl-NL-FennaNeural'>Dat was  Anouk &amp; co.</voice>", body);
    }

    [Fact]
    public async Task A_loop_of_backups_stops_instead_of_spinning()
    {
        var store = new VoiceStore(_db);
        long a = store.Save(new VoiceProfile(0, "A", "higgs", "", "", "", 0.7, "", false, "", true));
        long b = store.Save(new VoiceProfile(0, "B", "higgs", "", "", "", 0.7, "", false, "", false, a));
        store.Save(store.Get(a)! with { FallbackId = b });

        var tts = new TtsOptions { BaseUrl = "http://higgs:8001" };
        var down = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var engine = new ProfileSpeechEngine(store, new HiggsSpeechEngine(new HttpClient(down), tts), new SapiSpeechEngine(tts, "nl-NL"), tts);

        await Assert.ThrowsAsync<HttpRequestException>(() => engine.SpeakAsync("test", Path.Combine(_dir, "x.wav"), store.Get(a), CancellationToken.None));
        Assert.Equal(2, down.Seen.Count); // A, then B, then not A again
    }

    [Fact]
    public async Task ElevenLabs_pcm_is_wrapped_in_a_wav()
    {
        var pcm = new byte[24000 * 2];
        for (int i = 0; i < 24000; i++) BitConverter.GetBytes((short)(8000 * Math.Sin(i / 10.0))).CopyTo(pcm, i * 2);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pcm) });
        var tts = new TtsOptions { ElevenLabsKey = "secret", ElevenLabsModel = "eleven_multilingual_v2" };
        var engine = new ElevenLabsSpeechEngine(new HttpClient(handler), tts);
        var profile = new VoiceProfile(1, "Eleven", "elevenlabs", "", "", "", 0.7, "voice123", false, "", true);

        var wav = Path.Combine(_dir, "el.wav");
        await engine.SynthesizeAsync("<|calm|>Hallo", wav, profile, CancellationToken.None);

        var (rate, channels, samples) = VoiceProcessor.Read(await File.ReadAllBytesAsync(wav));
        Assert.Equal((24000, 1, 24000), (rate, channels, samples.Length));
        var (request, body) = handler.Seen.Single();
        Assert.Equal("https://api.elevenlabs.io/v1/text-to-speech/voice123?output_format=pcm_24000", request.RequestUri!.ToString());
        Assert.Equal("secret", request.Headers.GetValues("xi-api-key").Single());
        Assert.Contains("\"text\":\"Hallo\"", body);
    }

    [Fact]
    public void An_elevenlabs_voice_without_an_id_is_flagged()
    {
        Assert.NotNull(new VoiceProfile(1, "x", "elevenlabs", "", "", "", 0.7, "", false, "", false).Problem());
        Assert.Null(new VoiceProfile(1, "x", "azure", "", "", "", 0.7, "", false, "", false).Problem());
    }
}
