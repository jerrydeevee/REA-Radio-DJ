using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

public sealed partial class LogRow : ObservableObject
{
    public required long Id { get; init; }
    public required string Type { get; init; }
    public required string Label { get; init; }
    public string? Detail { get; init; }
    public required string Origin { get; init; }
    public string? Error { get; init; }
    public string TypeText => Type switch { "song" => "Muziek", "jingle" => "Jingle", "aiBreak" => "AI", _ => Type };
    [ObservableProperty] private string _startsAt = "";
    [ObservableProperty] private string _length = "";
    /// <summary>Deck letter (B = next, C = after that) and its colour from the deck palette; empty for later items.</summary>
    [ObservableProperty] private string _deck = "";
    [ObservableProperty] private Avalonia.Media.IBrush? _deckBrush;
}

/// <summary>The on-air log: what plays now, what comes next (with start times) and what played.</summary>
public sealed partial class LogTool : StudioTool
{
    private string _key = "";
    private string _historyKey = "";

    public LogTool() : base("log", "Programmalog") { }

    public ObservableCollection<LogRow> Upcoming { get; } = [];
    public ObservableCollection<LogRow> History { get; } = [];

    [ObservableProperty] private string _nowType = "";
    [ObservableProperty] private string _nowLabel = "—";
    [ObservableProperty] private string _nowDetail = "";
    [ObservableProperty] private string _nowPosition = "";
    [ObservableProperty] private string _nowRemaining = "";
    [ObservableProperty] private string _nowIntro = "";
    [ObservableProperty] private string _nowMix = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private double _mixMark;
    [ObservableProperty] private double _introMark;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private LogRow? _selected;

    protected override void OnState(StateDto s)
    {
        if (s.Engine.Current is { } c)
        {
            double len = Math.Max(0.1, c.To - c.From);
            NowType = c.Type switch { "song" => "MUZIEK", "jingle" => "JINGLE", "voice" => "AI", _ => c.Type.ToUpperInvariant() };
            NowLabel = string.IsNullOrEmpty(c.Title) ? c.Label : c.Title;
            NowDetail = string.Join(" · ", new[] { c.Artist, c.Category, c.Origin }.Where(x => !string.IsNullOrWhiteSpace(x)));
            NowPosition = StudioHub.Time(c.Position - c.From);
            NowRemaining = "-" + StudioHub.Time(c.To - c.Position);
            Progress = Math.Clamp((c.Position - c.From) / len * 100, 0, 100);
            MixMark = Math.Clamp((c.MixAt - c.From) / len, 0, 1);
            IntroMark = c.Intro is double i ? Math.Clamp((i - c.From) / len, 0, 1) : 0;
            NowIntro = c.Intro is double ii && ii > c.Position ? $"intro {StudioHub.TimeTenths(ii - c.Position)}" : "";
            NowMix = s.Engine.AutoPlay ? $"mix over {StudioHub.TimeTenths(c.MixAt - c.Position)}" : "live-assist: wacht op F5";
        }
        else
        {
            NowType = ""; NowLabel = "—"; NowDetail = ""; NowPosition = ""; NowRemaining = ""; Progress = 0; NowIntro = ""; NowMix = "";
        }

        var key = string.Join("|", s.Upcoming.Select(u => $"{u.Id}:{u.Label}:{u.BreakState}:{u.Error}"));
        if (key != _key)
        {
            _key = key;
            var selectedId = Selected?.Id;
            Upcoming.Clear();
            foreach (var u in s.Upcoming) Upcoming.Add(ToRow(u));
            Selected = Upcoming.FirstOrDefault(r => r.Id == selectedId);
        }
        for (int i = 0; i < Upcoming.Count && i < s.Upcoming.Count; i++)
        {
            Upcoming[i].StartsAt = s.Upcoming[i].StartsAt.ToString("HH:mm:ss");
            Upcoming[i].Length = StudioHub.Time(s.Upcoming[i].Length);
            Upcoming[i].Deck = i < 2 ? DeckPalette.Letter(i + 1) : "";
            Upcoming[i].DeckBrush = i < 2 ? DeckPalette.Brush(i + 1) : null;
        }
        var total = s.Upcoming.Sum(u => u.Length);
        Summary = $"{s.Upcoming.Count} items · {StudioHub.Time(total)}";

        var hkey = string.Join("|", s.History.Select(h => h.Id));
        if (hkey != _historyKey)
        {
            _historyKey = hkey;
            History.Clear();
            foreach (var h in s.History) History.Add(ToRow(h));
        }
    }

    private static LogRow ToRow(LogItemDto u) => new()
    {
        Id = u.Id,
        Type = u.Type,
        Label = u.Type == "aiBreak" ? $"AI-presentator ({BreakText(u.BreakState)})" : (string.IsNullOrEmpty(u.Title) ? u.Label : u.Title),
        Detail = u.Type == "aiBreak" ? u.Script : string.Join(" · ", new[] { u.Artist, u.Category }.Where(x => !string.IsNullOrWhiteSpace(x))),
        Origin = u.UseHook ? u.Origin + " · hook" : u.Origin,
        Error = u.Error,
        Length = StudioHub.Time(u.Length),
    };

    private static string BreakText(string? state) => state switch
    {
        "pending" => "wacht", "rendering" => "wordt gemaakt", "ready" => "klaar", "failed" => "mislukt", _ => state ?? "",
    };

    [RelayCommand] private Task Remove(LogRow? row) => row is null ? Task.CompletedTask : Hub.Client.DeleteAsync($"/api/log/{row.Id}");
    [RelayCommand] private Task Up(LogRow? row) => row is null ? Task.CompletedTask : Hub.Client.PostAsync($"/api/log/{row.Id}/up");
    [RelayCommand] private Task Down(LogRow? row) => row is null ? Task.CompletedTask : Hub.Client.PostAsync($"/api/log/{row.Id}/down");
    [RelayCommand] private Task MakeNext(LogRow? row) => row is null ? Task.CompletedTask : Hub.Client.PostAsync($"/api/log/{row.Id}/move", new MoveRequest(null, 0));
    [RelayCommand] private Task AddBreak() => Hub.Client.PostAsync("/api/log/break", new AddBreakRequest(0));
    [RelayCommand] private Task Replan() => Hub.Client.PostAsync("/api/log/replan");

    [RelayCommand]
    private Task AddSelectedNext() => Hub.SelectedMedia is { } m ? Hub.AddToLog(m.Id, true) : Task.CompletedTask;

    [RelayCommand]
    private Task AddSelectedEnd() => Hub.SelectedMedia is { } m ? Hub.AddToLog(m.Id, false) : Task.CompletedTask;

    /// <summary>Drop of a library item on the log.</summary>
    public Task Drop(long mediaId, LogRow? before)
    {
        int? position = before is null ? null : Upcoming.IndexOf(before);
        return Hub.Client.PostAsync("/api/log/media", new AddToLogRequest(mediaId, position));
    }
}
