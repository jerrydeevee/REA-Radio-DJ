using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One manual player (standby or PFL) as shown in a window.</summary>
public sealed partial class PlayerCard(string id) : ObservableObject
{
    public string Id { get; } = id;
    [ObservableProperty] private string _name = id;
    [ObservableProperty] private string _label = "leeg";
    [ObservableProperty] private string _state = "empty";
    [ObservableProperty] private string _elapsed = "";
    [ObservableProperty] private string _remaining = "";
    [ObservableProperty] private string _intro = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _stack = "";
    [ObservableProperty] private bool _playing;
    [ObservableProperty] private long? _mediaId;

    public string StateText => State switch { "playing" => "speelt", "paused" => "pauze", "cued" => "klaar", _ => "leeg" };
    partial void OnStateChanged(string value) => OnPropertyChanged(nameof(StateText));

    public void Update(PlayerDto p)
    {
        Name = p.Name;
        State = p.State;
        Playing = p.State == "playing";
        MediaId = p.MediaId;
        Label = p.Label ?? "leeg — sleep een item hierheen";
        double len = Math.Max(0.1, p.CueOut - p.CueIn);
        Elapsed = p.Label is null ? "" : StudioHub.Time(p.Position - p.CueIn);
        Remaining = p.Label is null ? "" : "-" + StudioHub.Time(p.CueOut - p.Position);
        Progress = p.Label is null ? 0 : Math.Clamp((p.Position - p.CueIn) / len * 100, 0, 100);
        Intro = p.Intro is double i && i > p.Position && p.Label is not null ? $"intro {StudioHub.TimeTenths(i - p.Position)}" : "";
        Stack = p.Stack.Count == 0 ? "" : $"daarna: {string.Join(", ", p.Stack.Take(3))}{(p.Stack.Count > 3 ? $" (+{p.Stack.Count - 3})" : "")}";
    }

    private static StudioHub Hub => StudioHub.Current;

    [RelayCommand] private Task Toggle() => Hub.PlayerCommand(Id, "toggle");
    [RelayCommand] private Task Stop() => Hub.PlayerCommand(Id, "stop");
    [RelayCommand] private Task Eject() => Hub.PlayerCommand(Id, "eject");
    [RelayCommand] private Task ClearStack() => Hub.PlayerCommand(Id, "clear");
    [RelayCommand] private Task LoadSelected() => Hub.SelectedMedia is { } m ? Hub.LoadPlayer(Id, m.Id) : Task.CompletedTask;
    [RelayCommand] private Task EnqueueSelected() => Hub.SelectedMedia is { } m ? Hub.Client.PostAsync($"/api/players/{Id}/enqueue", new { mediaId = m.Id }) : Task.CompletedTask;
    [RelayCommand] private void EditCues() { if (MediaId is long id) Hub.RequestCueEditor(id); }

    public Task Seek(double fraction, double cueIn, double cueOut) => Hub.PlayerCommand(Id, "seek", cueIn + fraction * (cueOut - cueIn));

    /// <summary>Drop: loads the item, or puts it on the stack when the player is busy.</summary>
    public Task Drop(long mediaId) => Hub.Client.PostAsync($"/api/players/{Id}/{(State == "playing" ? "enqueue" : "load")}", new { mediaId });
}

/// <summary>The standby players: manually started tracks on the on-air bus, next to the automation.</summary>
public sealed class StandbyTool : StudioTool
{
    public StandbyTool() : base("standby", "Standby-spelers") { }

    public ObservableCollection<PlayerCard> Players { get; } = [];

    protected override void OnState(StateDto s)
    {
        while (Players.Count > s.Players.Count) Players.RemoveAt(Players.Count - 1);
        for (int i = 0; i < s.Players.Count; i++)
        {
            if (i >= Players.Count || Players[i].Id != s.Players[i].Id)
            {
                if (i < Players.Count) Players.RemoveAt(i);
                Players.Insert(i, new PlayerCard(s.Players[i].Id));
            }
            Players[i].Update(s.Players[i]);
        }
    }
}

/// <summary>Pre-listen on the PFL bus (headphones), never on air.</summary>
public sealed class PflTool : StudioTool
{
    public PflTool() : base("pfl", "PFL") { }

    public PlayerCard Player { get; } = new("pfl");

    protected override void OnState(StateDto s) => Player.Update(s.Pfl);
}
