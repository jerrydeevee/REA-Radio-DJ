using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// A presenter voice. Engine "higgs" uses Higgs TTS 3 (optionally cloned from a short reference clip plus its
/// transcript, with default control tags); "sapi" uses an installed Windows voice; "elevenlabs" and "azure" are online
/// voices (key in the settings, voice ID or name in <see cref="SapiVoice"/>). <see cref="FallbackId"/> is the voice to
/// use when this one fails. A cloned voice is only used when
/// <see cref="Consent"/> is set: the person whose voice it is gave written permission (recorded in ConsentNote).
/// </summary>
public sealed record VoiceProfile(long Id, string Name, string Engine, string ReferenceAudio, string ReferenceText, string Tags,
    double Temperature, string SapiVoice, bool Consent, string ConsentNote, bool IsDefault, long? FallbackId = null)
{
    public static readonly string[] Engines = ["higgs", "sapi", "elevenlabs", "azure"];

    public bool IsClone => Engine == "higgs" && !string.IsNullOrWhiteSpace(ReferenceAudio);

    /// <summary>Why this profile may not be used, or null when it is fine.</summary>
    public string? Problem() =>
        IsClone && !Consent ? "gekloonde stem zonder vastgelegde toestemming" :
        IsClone && string.IsNullOrWhiteSpace(ReferenceText) ? "referentieclip zonder transcript" :
        Engine == "elevenlabs" && string.IsNullOrWhiteSpace(SapiVoice) ? "geen ElevenLabs-stem-ID" : null;
}

/// <summary>Voice profiles in the station database; exactly one is the default (the voice of the AI presenter).</summary>
public sealed class VoiceStore
{
    private readonly StationDb _db;
    private readonly object _gate = new();
    private IReadOnlyList<VoiceProfile> _profiles = [];

    public VoiceStore(StationDb db)
    {
        _db = db;
        Reload();
    }

    public event Action? Changed;

    public IReadOnlyList<VoiceProfile> Profiles { get { lock (_gate) return _profiles; } }

    /// <summary>The default profile, or null when none exists (the options from appsettings.json apply then).</summary>
    public VoiceProfile? Default { get { lock (_gate) return _profiles.FirstOrDefault(p => p.IsDefault) ?? _profiles.FirstOrDefault(); } }

    public VoiceProfile? Get(long id) { lock (_gate) return _profiles.FirstOrDefault(p => p.Id == id); }

    public void Reload()
    {
        var list = _db.Query("SELECT id, name, engine, reference_audio, reference_text, tags, temperature, sapi_voice, consent, consent_note, is_default, fallback_id FROM voice_profiles ORDER BY name",
            r => new VoiceProfile(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                r.GetDouble(6), r.GetString(7), r.GetInt64(8) != 0, r.GetString(9), r.GetInt64(10) != 0, r.GetInt64OrNull(11)));
        lock (_gate) _profiles = list;
    }

    /// <summary>Creates one profile from the appsettings voice when the table is empty.</summary>
    public bool EnsureDefault(TtsOptions tts)
    {
        if (Profiles.Count > 0) return false;
        Save(new VoiceProfile(0, tts.Provider == "higgs" ? "Higgs standaard" : "Windows-stem", tts.Provider == "higgs" ? "higgs" : "sapi",
            tts.ReferenceAudio, tts.ReferenceText, tts.Tags, tts.Temperature, tts.SapiVoice, false, "", true));
        return true;
    }

    public long Save(VoiceProfile p)
    {
        var args = new (string, object?)[]
        {
            ("$n", string.IsNullOrWhiteSpace(p.Name) ? "Stem" : p.Name.Trim()), ("$e", VoiceProfile.Engines.Contains(p.Engine) ? p.Engine : "sapi"),
            ("$ra", p.ReferenceAudio.Trim()), ("$rt", p.ReferenceText.Trim()), ("$t", p.Tags.Trim()),
            ("$temp", Math.Clamp(p.Temperature, 0.1, 1.5)), ("$sv", p.SapiVoice.Trim()), ("$c", p.Consent ? 1 : 0),
            ("$cn", p.ConsentNote.Trim()), ("$d", p.IsDefault ? 1 : 0), ("$id", p.Id),
            ("$fb", p.FallbackId is long f && f != p.Id ? f : null),
        };
        long id = p.Id;
        if (id > 0)
            _db.Execute("""
                UPDATE voice_profiles SET name=$n, engine=$e, reference_audio=$ra, reference_text=$rt, tags=$t, temperature=$temp,
                sapi_voice=$sv, consent=$c, consent_note=$cn, fallback_id=$fb WHERE id=$id
                """, args);
        else
            id = _db.Insert("""
                INSERT INTO voice_profiles(name, engine, reference_audio, reference_text, tags, temperature, sapi_voice, consent, consent_note, is_default, fallback_id)
                VALUES($n, $e, $ra, $rt, $t, $temp, $sv, $c, $cn, $d, $fb)
                """, args);
        if (p.IsDefault) SetDefault(id); else Done();
        return id;
    }

    public void SetDefault(long id)
    {
        _db.Execute("UPDATE voice_profiles SET is_default = CASE WHEN id=$id THEN 1 ELSE 0 END", ("$id", id));
        Done();
    }

    public bool Delete(long id)
    {
        int n = _db.Execute("DELETE FROM voice_profiles WHERE id=$id", ("$id", id));
        Done();
        return n > 0;
    }

    private void Done()
    {
        Reload();
        Changed?.Invoke();
    }
}

/// <summary>
/// The presenter voice as used by the break renderer: follows the default voice profile (or the one asked for) and,
/// when that voice fails, its backup voice, and that one's backup (at most four steps, no loops).
/// Without profiles it falls back to the configured provider.
/// </summary>
public sealed class ProfileSpeechEngine(VoiceStore store, HiggsSpeechEngine higgs, SapiSpeechEngine sapi, TtsOptions options,
    ElevenLabsSpeechEngine? elevenLabs = null, AzureSpeechEngine? azure = null) : ISpeechEngine
{
    public string Name => store.Default?.Engine ?? options.Provider;

    public VoiceProfile? Profile(long id) => store.Get(id);
    public VoiceProfile? DefaultProfile => store.Default;

    public Task SynthesizeAsync(string text, string wavPath, CancellationToken ct) => SpeakAsync(text, wavPath, store.Default, ct);

    public Task SynthesizeAsync(string text, string wavPath, VoiceProfile? profile, CancellationToken ct) =>
        SpeakAsync(text, wavPath, profile, ct);

    /// <summary>Speaks with the profile or its backups; returns the name of the voice that was used.</summary>
    public async Task<string> SpeakAsync(string text, string wavPath, VoiceProfile? profile, CancellationToken ct)
    {
        var tried = new List<string>();
        var visited = new HashSet<long>();
        for (var p = profile; ; )
        {
            try
            {
                await SpeakOneAsync(text, wavPath, p, ct);
                return p?.Name ?? options.Provider;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && p?.FallbackId is long next && visited.Count < 4
                                       && visited.Add(p.Id) && store.Get(next) is { } backup && !visited.Contains(backup.Id))
            {
                tried.Add($"{p.Name}: {ex.Message}");
                LastFallback = $"{p.Name} faalde ({ex.Message}); reserve {backup.Name}";
                p = backup;
            }
        }
    }

    /// <summary>Why the last render used a backup voice (for the log), or null.</summary>
    public string? LastFallback { get; private set; }

    private Task SpeakOneAsync(string text, string wavPath, VoiceProfile? profile, CancellationToken ct)
    {
        if (profile?.Problem() is { } problem) throw new InvalidOperationException($"Stem '{profile.Name}': {problem}");
        string engine = (profile?.Engine ?? options.Provider).ToLowerInvariant();
        return engine switch
        {
            "higgs" => higgs.SynthesizeAsync(text, wavPath, profile, ct),
            "elevenlabs" => (elevenLabs ?? throw new InvalidOperationException("ElevenLabs niet beschikbaar")).SynthesizeAsync(text, wavPath, profile, ct),
            "azure" => (azure ?? throw new InvalidOperationException("Azure niet beschikbaar")).SynthesizeAsync(text, wavPath, profile, ct),
            _ => sapi.SynthesizeAsync(text, wavPath, profile, ct),
        };
    }
}
