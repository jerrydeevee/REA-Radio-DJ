using System.Net;
using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Presenter;
using ReaRadioDj.Core.Users;

namespace ReaRadioDj.Tests;

/// <summary>The voice database: own voices of presenters, the roles (presenter, news, weather) and stored recordings.</summary>
public sealed class VoiceLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-voicelib-" + Guid.NewGuid().ToString("N"));
    private readonly StationDb _db;

    public VoiceLibraryTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new StationDb(Path.Combine(_dir, "station.db"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>A tone as a 16-bit mono WAV: something with a level, of a chosen length.</summary>
    private static byte[] Tone(double seconds, double level = 0.5, int rate = 24000)
    {
        var samples = new float[(int)(seconds * rate)];
        for (int i = 0; i < samples.Length; i++) samples[i] = (float)(level * Math.Sin(2 * Math.PI * 220 * i / rate));
        return VoiceProcessor.Write(rate, 1, samples);
    }

    [Fact]
    public void A_recording_must_be_a_clear_wav_of_four_to_forty_seconds()
    {
        Assert.Null(VoiceClips.Check(Tone(12)).Problem);
        Assert.Equal(12, VoiceClips.Check(Tone(12)).Seconds, 1);
        Assert.Contains("te kort", VoiceClips.Check(Tone(2)).Problem);
        Assert.Contains("te lang", VoiceClips.Check(Tone(45)).Problem);
        Assert.Contains("te zacht", VoiceClips.Check(Tone(10, level: 0.01)).Problem);
        Assert.Contains("WAV", VoiceClips.Check("geen geluid"u8.ToArray()).Problem);
    }

    [Fact]
    public void A_microphone_recording_loses_the_silence_around_it()
    {
        const int rate = 24000;
        var mono = new float[rate * 8];
        for (int i = rate * 2; i < rate * 6; i++) mono[i] = (float)(0.4 * Math.Sin(2 * Math.PI * 200 * i / rate));
        var check = VoiceClips.Check(VoiceClips.FromRecording(mono, rate));
        Assert.InRange(check.Seconds, 4.0, 4.7);   // 4 s of voice plus a quarter second at both ends
    }

    [Fact]
    public void Stored_recordings_live_in_the_voice_folder_only()
    {
        var clips = new VoiceClips(Path.Combine(_dir, "voices"));
        var name = clips.Store(7, Tone(6));
        Assert.True(VoiceClips.IsStored(name));
        Assert.StartsWith("clip-7-", name);
        Assert.NotNull(clips.Read(name));

        Assert.False(VoiceClips.IsStored("/home/jeroen/higgs/voices/anna.wav"));
        Assert.False(VoiceClips.IsStored(@"..\station.db"));
        Assert.False(VoiceClips.IsStored("clip-..\\x.wav"));
        Assert.False(VoiceClips.IsStored("https://example.org/clip-1.wav"));
        Assert.Null(clips.Read("clip-99-nergens.wav"));
        Assert.Throws<InvalidOperationException>(() => clips.Store(8, Tone(1)));

        clips.Delete(name);
        Assert.Null(clips.Read(name));
    }

    [Fact]
    public void Roles_are_a_fixed_list_and_empty_means_every_role()
    {
        Assert.Equal(["presenter", "weather"], VoiceRoles.Parse(" Weather, presenter ,onzin,weather"));
        Assert.Equal("news,weather", VoiceRoles.Format(["weather", "news"]));
        var all = new VoiceProfile(1, "Sam", "higgs", "", "", "", 0.8, "", false, "", false);
        Assert.True(all.Allows(VoiceRoles.NewsReader));
        var onlyNews = all with { Uses = "news" };
        Assert.True(onlyNews.Allows(VoiceRoles.NewsReader));
        Assert.False(onlyNews.Allows(VoiceRoles.Presenter));
    }

    [Fact]
    public void An_own_voice_keeps_its_owner_and_roles()
    {
        var store = new VoiceStore(_db);
        long id = store.Save(new VoiceProfile(0, "Anna (eigen stem)", "higgs", "clip-1-x.wav", "Goedemorgen", "", 0.7, "", true,
            "Eigen stem", false, null, "Anna", "weather,presenter"));
        var again = new VoiceStore(_db);
        Assert.Equal(id, again.OwnedBy(" anna ")?.Id);
        Assert.Equal("presenter,weather", again.Get(id)!.Uses);
        Assert.Null(again.OwnedBy("Piet"));
        Assert.Null(again.OwnedBy(""));
    }

    [Fact]
    public void News_and_weather_speak_with_the_voice_of_their_role_when_it_is_allowed()
    {
        var store = new VoiceStore(_db);
        long station = store.Save(new VoiceProfile(0, "Higgs · Zakelijk", "higgs", "", "", "", 0.4, "", false, "", true));
        long anna = store.Save(new VoiceProfile(0, "Anna (eigen stem)", "higgs", "clip-1-x.wav", "Goedemorgen", "", 0.7, "", true,
            "Eigen stem", false, null, "Anna", "weather"));
        var roles = new VoiceRoleStore(_db, store);

        Assert.NotNull(roles.Save(new VoiceRoles(News: anna)));            // Anna only said yes to the weather
        Assert.Null(roles.Save(new VoiceRoles(News: station, Weather: anna)));

        var reread = new VoiceRoleStore(_db, store);
        Assert.Equal(station, reread.Resolve(VoiceRoles.NewsReader, null));
        Assert.Equal(anna, reread.Resolve(VoiceRoles.WeatherReader, null));
        Assert.Equal(station, reread.Resolve(VoiceRoles.WeatherReader, station));   // a block's own voice comes first
        Assert.Equal(station, reread.Resolve(VoiceRoles.NewsReader, anna));         // ... unless it may not read the news

        store.Delete(anna);
        Assert.Null(reread.Resolve(VoiceRoles.WeatherReader, null));                 // gone: the presenter's voice
    }

    [Fact]
    public async Task A_stored_recording_is_sent_to_Higgs_inside_the_request()
    {
        var clips = new VoiceClips(Path.Combine(_dir, "voices"));
        var wav = Tone(6);
        var name = clips.Store(3, wav);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Tone(2)) });
        var tts = new TtsOptions { BaseUrl = "http://higgs.test:8001" };
        var higgs = new HiggsSpeechEngine(new HttpClient(handler), tts, clips);
        var profile = new VoiceProfile(3, "Anna", "higgs", name, "Goedemorgen allemaal", "", 0.7, "", true, "ja", false);

        await higgs.SynthesizeAsync("Het weer.", Path.Combine(_dir, "out.wav"), profile, CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Seen.Single().Body);
        var reference = body.RootElement.GetProperty("references")[0];
        Assert.Equal(Convert.ToBase64String(wav), reference.GetProperty("data").GetString());
        Assert.Equal("audio/wav", reference.GetProperty("media_type").GetString());
        Assert.Equal("Goedemorgen allemaal", reference.GetProperty("text").GetString());
        Assert.False(reference.TryGetProperty("audio_path", out _));

        // a path for the server itself is passed on as it is
        handler.Seen.Clear();
        await higgs.SynthesizeAsync("Het weer.", Path.Combine(_dir, "out.wav"), profile with { ReferenceAudio = "/home/x/anna.wav" }, CancellationToken.None);
        using var second = JsonDocument.Parse(handler.Seen.Single().Body);
        Assert.Equal("/home/x/anna.wav", second.RootElement.GetProperty("references")[0].GetProperty("audio_path").GetString());

        // a missing recording is an error, never a voice without its clone
        await Assert.ThrowsAsync<InvalidOperationException>(() => higgs.SynthesizeAsync("x", Path.Combine(_dir, "out.wav"),
            profile with { ReferenceAudio = "clip-3-weg.wav" }, CancellationToken.None));
    }

    [Theory]
    [InlineData("POST", "/api/voice/mine", null)]
    [InlineData("POST", "/api/voice/mine/record/start", null)]
    [InlineData("DELETE", "/api/voice/mine", null)]
    [InlineData("PUT", "/api/voice/roles", UserRole.Technician)]
    [InlineData("POST", "/api/voice/profiles/3/clip", UserRole.Technician)]
    [InlineData("PUT", "/api/voice/server/settings", UserRole.Admin)]
    public void Own_voice_is_for_everybody_the_station_voices_for_technicians(string method, string path, UserRole? role) =>
        Assert.Equal(role, AccessRules.Required(method, path));
}
