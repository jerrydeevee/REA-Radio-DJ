using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.Services;

/// <summary>
/// Shared state of the studio: the engine connection, the latest live state, the categories and the media item that
/// is selected in the library (the "clipboard" for loading players, carts and the cue editor). Every window uses it.
/// </summary>
public sealed partial class StudioHub : ObservableObject
{
    private static StudioHub? _current;
    public static StudioHub Current => _current ?? throw new InvalidOperationException("Studio not started");

    private StudioHub(StudioSettings settings)
    {
        Settings = settings;
        Client = new EngineClient(new Uri(settings.EffectiveEngineUrl));
        Client.StateReceived += s => Dispatcher.UIThread.Post(() => OnState(s), DispatcherPriority.Background);
        Client.ConnectionChanged += (ok, text) => Dispatcher.UIThread.Post(() =>
        {
            bool was = Connected;
            Connected = ok;
            ConnectionText = text;
            if (ok && !was) _ = RefreshCategoriesAsync();
        });
        Client.Error += text => Dispatcher.UIThread.Post(() => Status = text);
    }

    public static StudioHub Start(StudioSettings settings)
    {
        _current = new StudioHub(settings);
        _current.Client.Start();
        return _current;
    }

    public StudioSettings Settings { get; }
    public EngineClient Client { get; }
    public ObservableCollection<CategoryDto> Categories { get; } = [];

    [ObservableProperty] private StateDto? _state;
    [ObservableProperty] private bool _connected;
    [ObservableProperty] private string _connectionText = "verbinden…";
    [ObservableProperty] private string _status = "";
    /// <summary>The item selected in the library.</summary>
    [ObservableProperty] private MediaDto? _selectedMedia;

    /// <summary>Raised on the UI thread for every state frame.</summary>
    public event Action<StateDto>? StateReceived;
    /// <summary>Raised when a window asks to open the cue editor for an item.</summary>
    public event Action<long>? EditCuesRequested;
    /// <summary>Raised after the library or the categories changed (scan, edit).</summary>
    public event Action? LibraryChanged;

    private long _lastMessage;
    /// <summary>Raised when the engine has new messages (the messages window fetches them).</summary>
    public event Action? MessagesChanged;

    private void OnState(StateDto s)
    {
        State = s;
        StateReceived?.Invoke(s);
        if (s.LastMessageId != _lastMessage) { _lastMessage = s.LastMessageId; MessagesChanged?.Invoke(); }
    }

    public void RequestCueEditor(long mediaId) => EditCuesRequested?.Invoke(mediaId);

    /// <summary>Raised when a window asks to edit the transition into a log item.</summary>
    public event Action<long>? TransitionRequested;
    /// <summary>The last log item asked for, for a transition window that opens only now.</summary>
    public long? PendingTransition { get; private set; }

    public void RequestTransition(long logItemId)
    {
        PendingTransition = logItemId;
        TransitionRequested?.Invoke(logItemId);
    }

    public void NotifyLibraryChanged() => LibraryChanged?.Invoke();

    public async Task RefreshCategoriesAsync()
    {
        var cats = await Client.GetAsync<List<CategoryDto>>("/api/categories");
        if (cats is null) return;
        Categories.Clear();
        foreach (var c in cats) Categories.Add(c);
        LibraryChanged?.Invoke();
    }

    public string CategoryName(long? id) => Categories.FirstOrDefault(c => c.Id == id)?.Name ?? "";

    // ------------------------------------------------------------ commands used by several windows

    public Task<bool> AddToLog(long mediaId, bool next) => Client.PostAsync("/api/log/media", new AddToLogRequest(mediaId, next ? 0 : null));
    public Task<bool> LoadPlayer(string player, long mediaId) => Client.PostAsync($"/api/players/{player}/load", new { mediaId });
    public Task<bool> PlayerCommand(string player, string action, double? seconds = null) =>
        Client.PostAsync($"/api/players/{player}/{action}", new { seconds });

    /// <summary>True while this item plays on PFL (pre-listen).</summary>
    public bool PflPlaying(long mediaId) => State?.Pfl is { } p && p.MediaId == mediaId && p.State == "playing";

    /// <summary>Pre-listens an item on PFL, from a position when given; loads it only when PFL holds another item.</summary>
    public async Task Listen(long mediaId, double? from = null)
    {
        if (State?.Pfl.MediaId != mediaId && !await LoadPlayer("pfl", mediaId)) return;
        await PlayerCommand("pfl", "play");
        if (from is double s) await PlayerCommand("pfl", "seek", s);
    }

    public static string Time(double seconds)
    {
        if (!double.IsFinite(seconds)) return "-:--";
        bool neg = seconds < 0;
        seconds = Math.Abs(seconds);
        var t = TimeSpan.FromSeconds(Math.Floor(seconds * 10) / 10);
        var text = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        return neg ? "-" + text : text;
    }

    public static string TimeTenths(double seconds) => double.IsFinite(seconds)
        ? $"{(int)(Math.Max(0, seconds) / 60)}:{Math.Max(0, seconds) % 60:00.0}" : "-:--.-";
}
