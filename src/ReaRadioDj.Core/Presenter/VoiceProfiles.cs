using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core.Presenter;

/// <summary>
/// A presenter voice. Engine "higgs" uses Higgs TTS 3 (optionally cloned from a short reference clip plus its
/// transcript, with default control tags); "sapi" uses an installed Windows voice. A cloned voice is only used when
/// <see cref="Consent"/> is set: the person whose voice it is gave written permission (recorded in ConsentNote).
/// </summary>
public sealed record VoiceProfile(long Id, string Name, string Engine, string ReferenceAudio, string ReferenceText, string Tags,
    double Temperature, string SapiVoice, bool Consent, string ConsentNote, bool IsDefault)
{
    public bool IsClone => Engine == "higgs" && !string.IsNullOrWhiteSpace(ReferenceAudio);

    /// <summary>Why this profile may not be used, or null when it is fine.</summary>
    public string? Problem() =>
        IsClone && !Consent ? "gekloonde stem zonder vastgelegde toestemming" :
        IsClone && string.IsNullOrWhiteSpace(ReferenceText) ? "referentieclip zonder transcript" : null;
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
        var list = _db.Query("SELECT id, name, engine, reference_audio, reference_text, tags, temperature, sapi_voice, consent, consent_note, is_default FROM voice_profiles ORDER BY name",
            r => new VoiceProfile(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                r.GetDouble(6), r.GetString(7), r.GetInt64(8) != 0, r.GetString(9), r.GetInt64(10) != 0));
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
            ("$n", string.IsNullOrWhiteSpace(p.Name) ? "Stem" : p.Name.Trim()), ("$e", p.Engine == "higgs" ? "higgs" : "sapi"),
            ("$ra", p.ReferenceAudio.Trim()), ("$rt", p.ReferenceText.Trim()), ("$t", p.Tags.Trim()),
            ("$temp", Math.Clamp(p.Temperature, 0.1, 1.5)), ("$sv", p.SapiVoice.Trim()), ("$c", p.Consent ? 1 : 0),
            ("$cn", p.ConsentNote.Trim()), ("$d", p.IsDefault ? 1 : 0), ("$id", p.Id),
        };
        long id = p.Id;
        if (id > 0)
            _db.Execute("""
                UPDATE voice_profiles SET name=$n, engine=$e, reference_audio=$ra, reference_text=$rt, tags=$t, temperature=$temp,
                sapi_voice=$sv, consent=$c, consent_note=$cn WHERE id=$id
                """, args);
        else
            id = _db.Insert("""
                INSERT INTO voice_profiles(name, engine, reference_audio, reference_text, tags, temperature, sapi_voice, consent, consent_note, is_default)
                VALUES($n, $e, $ra, $rt, $t, $temp, $sv, $c, $cn, $d)
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
/// The presenter voice as used by the break renderer: follows the default voice profile (Higgs or Windows speech).
/// Without profiles it falls back to the configured provider.
/// </summary>
public sealed class ProfileSpeechEngine(VoiceStore store, HiggsSpeechEngine higgs, SapiSpeechEngine sapi, TtsOptions options) : ISpeechEngine
{
    public string Name => Pick(store.Default).Engine.Name;

    public Task SynthesizeAsync(string text, string wavPath, CancellationToken ct) => SynthesizeAsync(text, wavPath, store.Default, ct);

    public Task SynthesizeAsync(string text, string wavPath, VoiceProfile? profile, CancellationToken ct)
    {
        if (profile?.Problem() is { } problem) throw new InvalidOperationException($"Stem '{profile.Name}': {problem}");
        var (engine, p) = Pick(profile);
        return engine == higgs ? higgs.SynthesizeAsync(text, wavPath, p, ct) : sapi.SynthesizeAsync(text, wavPath, p, ct);
    }

    private (ISpeechEngine Engine, VoiceProfile? Profile) Pick(VoiceProfile? profile)
    {
        string engine = profile?.Engine ?? options.Provider;
        return (engine.Equals("higgs", StringComparison.OrdinalIgnoreCase) ? higgs : sapi, profile);
    }
}
