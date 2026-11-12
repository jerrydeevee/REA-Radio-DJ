namespace ReaRadioDj.Core.Studio;

/// <summary>
/// Settings for two studios that watch each other. The main studio ("hoofd") just answers; the backup ("reserve")
/// asks the main studio every few seconds how it is doing and takes the air over when it no longer answers (or, when
/// chosen, when it is silent), and gives it back when the main studio plays again.
/// </summary>
public sealed record FailoverSettings
{
    /// <summary>"uit", "hoofd" or "reserve".</summary>
    public string Role { get; init; } = "uit";
    /// <summary>The other engine, e.g. http://studio1:8600.</summary>
    public string Partner { get; init; } = "";
    public int IntervalSeconds { get; init; } = 2;
    /// <summary>Take over after the partner has not answered (or not been healthy) this long.</summary>
    public int LostSeconds { get; init; } = 10;
    /// <summary>Also take over when the partner reports silence on air.</summary>
    public bool TakeOverOnSilence { get; init; }
    /// <summary>Give the air back by itself once the partner plays again this long; false = only with the button.</summary>
    public bool GiveBackAutomatically { get; init; } = true;
    public int GiveBackSeconds { get; init; } = 30;
    /// <summary>Start the stream on taking over (and stop it again on giving back).</summary>
    public bool StartStream { get; init; }
    /// <summary>Command-bus commands on taking over and on giving back (several separated by ";").</summary>
    public string OnTakeOver { get; init; } = "";
    public string OnGiveBack { get; init; } = "";

    public FailoverSettings Clean() => this with
    {
        Role = Role is "hoofd" or "reserve" ? Role : "uit",
        Partner = (Partner ?? "").Trim().TrimEnd('/'),
        IntervalSeconds = Math.Clamp(IntervalSeconds, 1, 30),
        LostSeconds = Math.Clamp(LostSeconds, 3, 600),
        GiveBackSeconds = Math.Clamp(GiveBackSeconds, 5, 3600),
        OnTakeOver = (OnTakeOver ?? "").Trim(),
        OnGiveBack = (OnGiveBack ?? "").Trim(),
    };

    /// <summary>An error text, or null when the settings can be used.</summary>
    public string? Check()
    {
        if (Role == "uit") return null;
        if (!Uri.TryCreate(Partner, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.AbsolutePath.Length > 1)
            return "vul het adres van de andere engine in, bijv. http://studio1:8600";
        if (IntervalSeconds * 2 > LostSeconds) return "de tijd tot overnemen moet minstens twee keer de vraagtijd zijn";
        return null;
    }
}

/// <summary>What the partner says about itself.</summary>
public sealed record FailoverBeat(string Station, string Role, bool Healthy, bool Playing, bool Silent, bool HasAir);

public enum FailoverAction { None, TakeOver, GiveBack }

/// <summary>
/// The decision of the backup studio, as a pure state machine: feed it each answer (or null when there was none) with
/// the time, and it says when to take over or give back. Also answers what the operator sees.
/// </summary>
public sealed class FailoverMonitor(FailoverSettings settings)
{
    private DateTime? _lastGood, _playingSince, _started;
    public bool HasAir { get; private set; }
    public FailoverBeat? Last { get; private set; }
    public DateTime? LastAnswer { get; private set; }
    public string State { get; private set; } = settings.Role == "uit" ? "uit" : "wacht op de andere studio";

    /// <summary>The operator took over by hand, or gave back by hand.</summary>
    public void SetAir(bool on, DateTime now)
    {
        HasAir = on;
        _playingSince = null;
        if (!on) _lastGood = now; // give the partner a full period before acting again
        State = on ? "overgenomen (met de hand)" : "teruggegeven";
    }

    public FailoverAction Update(DateTime now, FailoverBeat? beat)
    {
        _started ??= now;
        Last = beat;
        if (beat is not null) LastAnswer = now;
        if (settings.Role != "reserve")
        {
            State = settings.Role == "uit" ? "uit" : beat is null ? "reservestudio antwoordt niet" : beat.HasAir ? "reservestudio is op de lucht" : "reservestudio paraat";
            return FailoverAction.None;
        }

        bool good = beat is { Healthy: true } && !(settings.TakeOverOnSilence && beat.Silent && !HasAir);
        if (good) _lastGood = now;

        if (!HasAir)
        {
            if (good) { State = beat!.Playing ? "paraat: de hoofdstudio speelt" : "paraat: de hoofdstudio antwoordt, maar speelt niet"; return FailoverAction.None; }
            var since = _lastGood ?? _started.Value;
            double lost = (now - since).TotalSeconds;
            if (lost < settings.LostSeconds)
            {
                State = beat is null ? $"hoofdstudio antwoordt niet ({lost:0} s)" : beat.Healthy ? $"stilte bij de hoofdstudio ({lost:0} s)" : $"hoofdstudio meldt een storing ({lost:0} s)";
                return FailoverAction.None;
            }
            HasAir = true;
            _playingSince = null;
            State = beat is null ? "overgenomen: de hoofdstudio antwoordde niet" : beat.Healthy ? "overgenomen: stilte bij de hoofdstudio" : "overgenomen: storing bij de hoofdstudio";
            return FailoverAction.TakeOver;
        }

        // we have the air: is the main studio back and playing?
        if (beat is { Healthy: true, Playing: true, Silent: false })
        {
            _playingSince ??= now;
            double back = (now - _playingSince.Value).TotalSeconds;
            if (!settings.GiveBackAutomatically) { State = "op de lucht; de hoofdstudio speelt weer: geef terug met de knop"; return FailoverAction.None; }
            if (back < settings.GiveBackSeconds) { State = $"op de lucht; de hoofdstudio speelt weer, teruggeven over {settings.GiveBackSeconds - back:0} s"; return FailoverAction.None; }
            HasAir = false;
            _playingSince = null;
            State = "teruggegeven aan de hoofdstudio";
            return FailoverAction.GiveBack;
        }
        _playingSince = null;
        if (State.StartsWith("op de lucht", StringComparison.Ordinal) || State.StartsWith("teruggegeven", StringComparison.Ordinal))
            State = beat is null ? "op de lucht; de hoofdstudio antwoordt niet" : "op de lucht; de hoofdstudio speelt nog niet";
        return FailoverAction.None;
    }
}
