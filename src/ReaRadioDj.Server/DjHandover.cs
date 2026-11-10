using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>
/// Hands the broadcast over from the automation to the DJ desk and back without a gap. While the desk is off air, deck
/// A can follow the automation: it plays the same track at the same place, silently, so that the DJ sees it running and
/// can prepare deck B. Putting the desk on air closes the automation (log and overlays) instead of stopping it: it runs
/// on unheard, so giving back brings it in at the right place. When it was stopped meanwhile, giving back starts it. Deck A already plays, so the listener hears the track go on.
/// </summary>
public sealed class DjHandover : IDisposable
{
    /// <summary>The duck reason on the automation strips while the DJ desk is on air.</summary>
    public const string DjOnAir = "dj on air";

    private readonly DjMixer _dj;
    private readonly PlayoutEngine _engine;
    private readonly MediaLibrary _library;
    private readonly AudioRouter _router;
    private readonly IServiceProvider _services;
    private readonly ILogger<DjHandover> _logger;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private long _logId;

    public DjHandover(DjMixer dj, PlayoutEngine engine, MediaLibrary library, AudioRouter router, IServiceProvider services, ILogger<DjHandover> logger)
    {
        _dj = dj;
        _engine = engine;
        _library = library;
        _router = router;
        _services = services;
        _logger = logger;
        _timer = new Timer(_ => Tick(), null, 500, 500);
    }

    /// <summary>The strips the automation plays on: they go quiet while the DJ is on air.</summary>
    private IEnumerable<MixerStrip> AutomationStrips => [_router.Strip(AudioRouter.LogStrip), _router.Strip(AudioRouter.OverlayStrip)];

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
    /// The desk goes on air: the automation goes quiet at the same moment and runs on unheard. Returns whether deck A
    /// was following (it carries on with the track, seamlessly).
    /// </summary>
    public bool GoOnAir()
    {
        lock (_gate)
        {
            bool took = Following && DeckA.Playing;
            Following = false;
            foreach (var s in AutomationStrips) s.Duck(DjOnAir, 0f, 60);
            _dj.SetOnAir(true);
            return took;
        }
    }

    /// <summary>
    /// Back to the automation: the desk goes off air and the automation is heard again where it is now (started again
    /// when somebody stopped it meanwhile).
    /// </summary>
    public void GiveBack()
    {
        lock (_gate)
        {
            _dj.SetOnAir(false);
            foreach (var s in AutomationStrips) s.Duck(DjOnAir, null, 400);
            // Nothing plays any more (stopped meanwhile): start the automation. Assist mode with an item on air stays assist.
            if (_engine.OnAir() is null) _engine.Play();
        }
        _services.GetService<AutomationService>()?.Heard();
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
