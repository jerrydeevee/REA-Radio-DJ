using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Regiekamer: one glance for the studio screen. Time and date, what is ON AIR and next, the weather now, the news
/// headlines and the latest messages. Weather and news come from the engine with the presenter's sources and settings.
/// </summary>
public sealed partial class ControlRoomTool : StudioTool
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");
    private DispatcherTimer? _timer;
    private long _lastMessage;

    public ControlRoomTool() : base("regie", "Regiekamer") { }

    [ObservableProperty] private string _time = "--:--";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private bool _onAir;
    [ObservableProperty] private string _now = "";
    [ObservableProperty] private string _next = "";
    [ObservableProperty] private string _remaining = "";

    [ObservableProperty] private bool _hasWeather;
    [ObservableProperty] private string _weatherIcon = "";
    [ObservableProperty] private string _temperature = "";
    [ObservableProperty] private string _sky = "";
    [ObservableProperty] private string _weatherDetail = "";
    [ObservableProperty] private string _weatherNote = "";
    [ObservableProperty] private string _newsNote = "";
    [ObservableProperty] private bool _busy;

    public ObservableCollection<HeadlineDto> Headlines { get; } = [];
    public ObservableCollection<MessageDto> Messages { get; } = [];

    protected override void OnFirstShow()
    {
        Hub.MessagesChanged += () => _ = FetchMessagesAsync();
        _ = FetchMessagesAsync();
        _ = LoadAsync(false);
        // The engine caches weather (15 min) and news (10 min); asking every 5 minutes keeps the screen fresh.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _timer.Tick += (_, _) => _ = LoadAsync(false);
        _timer.Start();
    }

    protected override void OnState(StateDto s)
    {
        var t = s.Time.ToLocalTime();
        Time = t.ToString("HH:mm", Nl);
        Date = t.ToString("dddd d MMMM", Nl);
        var c = s.Engine.Current;
        OnAir = s.Engine.Running && c is not null;
        Now = c is null ? "Niets ON AIR" : string.IsNullOrWhiteSpace(c.Artist) ? c.Label : $"{c.Artist} – {c.Title}";
        Remaining = c is null ? "" : "nog " + Services.StudioHub.Time(Math.Max(0, c.To - c.Position));
        var n = s.Upcoming.FirstOrDefault();
        Next = n is null ? "" : "Straks: " + (string.IsNullOrWhiteSpace(n.Artist) ? n.Label : $"{n.Artist} – {n.Title}");
    }

    [RelayCommand] private Task Refresh() => LoadAsync(true);

    private async Task LoadAsync(bool refresh)
    {
        Busy = true;
        var r = await Hub.Client.GetAsync<ControlRoomDto>("/api/control-room" + (refresh ? "?refresh=true" : ""));
        Busy = false;
        if (r is null) { WeatherNote = "De engine geeft geen antwoord."; return; }

        HasWeather = r.Weather is not null;
        if (r.Weather is { } w)
        {
            WeatherIcon = Icon(w.Code);
            Temperature = $"{w.Temperature}°";
            Sky = string.IsNullOrWhiteSpace(w.Place) ? Cap(w.Sky) : $"{Cap(w.Sky)} in {w.Place}";
            WeatherDetail = $"Vandaag max. {w.Max}° · wind {w.Beaufort} Bft" + (w.RainChance >= 0 ? $" · regenkans {w.RainChance}%" : "");
        }
        WeatherNote = r.WeatherNote ?? (r.WeatherAt is DateTime wa ? $"Bijgewerkt {wa.ToLocalTime().ToString("HH:mm", Nl)} · Open-Meteo" : "");

        Headlines.Clear();
        foreach (var h in r.Headlines.Take(12)) Headlines.Add(h);
        NewsNote = r.NewsProblems.Count > 0 ? string.Join(" · ", r.NewsProblems)
            : r.NewsAt is DateTime na ? $"Bijgewerkt {na.ToLocalTime().ToString("HH:mm", Nl)}" : "";
    }

    private async Task FetchMessagesAsync()
    {
        var list = await Hub.Client.GetAsync<List<MessageDto>>($"/api/messages?since={_lastMessage}");
        if (list is null) return;
        foreach (var m in list) { Messages.Insert(0, m); _lastMessage = Math.Max(_lastMessage, m.Id); }
        while (Messages.Count > 8) Messages.RemoveAt(Messages.Count - 1);
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Nl) + s[1..];

    /// <summary>A symbol for the WMO weather code.</summary>
    public static string Icon(int code) => code switch
    {
        0 => "☀",
        1 or 2 => "⛅",
        3 => "☁",
        45 or 48 => "🌫",
        >= 71 and <= 77 or 85 or 86 => "❄",
        >= 95 => "⛈",
        >= 51 and <= 82 => "🌧",
        _ => "☁",
    };
}
