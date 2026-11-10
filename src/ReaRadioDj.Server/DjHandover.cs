using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>
/// Hands the broadcast over from the automation to the DJ desk without a gap. While the desk is off air, deck A can
/// follow the automation: it plays the same track at the same place, silently (the desk is off air), so that the DJ
/// sees it running and can prepare deck B. Putting the desk on air then stops the automation at that moment; deck A
/// already plays, so the listener hears the track go on.
/// </summary>
public sealed class DjHandover : IDisposable
{
    private readonly DjMixer _dj;
    private readonly PlayoutEngine _engine;
    private readonly MediaLibrary _library;
    private readonly ILogger<DjHandover> _logger;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private long _logId;

    public DjHandover(DjMixer dj, PlayoutEngine engine, MediaLibrary library, ILogger<DjHandover> logger)
    {
        _dj = dj;
        _engine = engine;
        _library = library;
        _logger = logger;
        _timer = new Timer(_ => Tick(), null, 500, 500);
    }

    /// <summary>True while deck A copies what the automation plays.</summary>
    public bool Following { get; private set; }

    private DjDeck DeckA => _dj.Decks[0];

    /// <summary>Starts following on deck A. Returns why not, or null when it follows.</summary>
    public string? Follow()
    {
        lock (_gate)
        {
            if (_dj.OnAir) return "de DJ-tafel is al ON AIR";
            if (Following) return null;
            if (DeckA.Media is not null) return "deck A heeft al een nummer; haal het eraf om de automatisering te volgen";
            Following = true;
            _logId = 0;
        }
        Tick();
        return null;
    }

    /// <summary>The DJ touched deck A: it is theirs now.</summary>
    public void Release() { lock (_gate) Following = false; }

    /// <summary>
    /// The desk goes on air. When deck A follows a playing track, the automation stops here and now, so the track is
    /// not heard twice. Returns whether the broadcast was taken over.
    /// </summary>
    public bool TakeOver()
    {
        lock (_gate)
        {
            if (!Following) return false;
            Following = false;
            if (!DeckA.Playing) return false;
            _engine.Stop(0.05);
            return true;
        }
    }

    private void Tick()
    {
        try
        {
            lock (_gate)
            {
                if (!Following || _dj.OnAir) return;
                var current = _engine.GetState().Current;
                if (current is null)
                {
                    // Nothing on air: an empty deck says so better than a track that stopped somewhere.
                    if (_logId != 0) { DeckA.Eject(); _logId = 0; }
                    return;
                }
                if (current.Id != _logId)
                {
                    _logId = current.Id;
                    if (current.MediaId <= 0 || _library.Get(current.MediaId) is not { } media) { DeckA.Eject(); return; } // a rendered break: nothing to load
                    DeckA.Load(media);
                    DeckA.SetFader(1);
                    // Loading took a moment: ask again where the automation is.
                    DeckA.Seek((_engine.GetState().Current ?? current).Position);
                    DeckA.Play();
                    return;
                }
                // Somebody jumped in the track on air, or the deck drifted: line it up again.
                if (DeckA.Media is not null && DeckA.Playing && Math.Abs(DeckA.Ahead - current.Position) > 0.35) DeckA.Seek(current.Position);
            }
        }
        catch (Exception ex) when (ex is BassException or DllNotFoundException or IOException)
        {
            _logger.LogWarning("Deck A cannot follow the automation: {Message}", ex.Message);
            lock (_gate) Following = false;
        }
    }

    public void Dispose() => _timer.Dispose();
}
