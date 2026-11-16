namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Faders that follow their use, like on a radio desk: a channel that starts (a cart, the PFL, a deck, the mic) gets
/// its fader opened when it was closed, and a channel that stops being used (an empty or finished deck, a closed mic)
/// goes to 0. The level it had is remembered and comes back when it opens again. Only changes count: a fader you
/// move yourself stays where you put it until the channel starts or stops again.
/// </summary>
public sealed class FaderFollow
{
    public const float Closed = 0.001f;

    private sealed class State
    {
        public bool Active, Idle;
        public float Level = 1f;
    }

    private readonly Dictionary<string, State> _states = [];

    /// <summary>
    /// One look at a channel: <paramref name="active"/> = something plays on it now, <paramref name="idle"/> = it is not
    /// in use (it should be down). Returns the volume to set, or null to leave the fader alone.
    /// </summary>
    public float? Step(string id, bool active, bool idle, float volume)
    {
        if (!_states.TryGetValue(id, out var s)) _states[id] = s = new State();
        float? set = null;
        if (active && !s.Active && volume <= Closed) set = s.Level;
        else if (idle && !s.Idle && volume > Closed)
        {
            s.Level = volume;
            set = 0f;
        }
        s.Active = active;
        s.Idle = idle;
        return set;
    }
}
