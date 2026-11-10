using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One track waiting in the automix list.</summary>
public sealed record AutomixRow(int Index, string Number, string Title, string Artist, string Length);

/// <summary>
/// Automix on the DJ desk: a list of tracks that mixes itself on decks A and B with the chosen transition. Drop
/// tracks from the library on the list.
/// </summary>
public sealed partial class AutomixTool : StudioTool
{
    private static readonly double[] BeatSteps = [4, 8, 16, 32];
    private double _beats = 16;
    private string _signature = "";

    public AutomixTool() : base("automix", "Automix (DJ)") { }

    public ObservableCollection<AutomixRow> Queue { get; } = [];

    [ObservableProperty] private bool _available = true;
    [ObservableProperty] private bool _on;
    [ObservableProperty] private string _transition = "beat";
    [ObservableProperty] private string _beatsText = "16 tellen";
    [ObservableProperty] private string _stateText = "Automix staat uit";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _mixing;
    [ObservableProperty] private bool _empty = true;
    [ObservableProperty] private string _countText = "";

    private Task Send(DjAutomixRequest r) => Hub.Client.PostAsync("/api/dj/automix", r);

    [RelayCommand] private Task Toggle() => Send(new DjAutomixRequest(On: !On));
    [RelayCommand] private Task SetTransition(string? type) => Send(new DjAutomixRequest(Transition: type));
    [RelayCommand] private Task Clear() => Send(new DjAutomixRequest(Clear: true));
    [RelayCommand] private Task Remove(AutomixRow? row) => row is null ? Task.CompletedTask : Send(new DjAutomixRequest(Remove: row.Index));
    [RelayCommand] private Task Up(AutomixRow? row) =>
        row is null || row.Index == 0 ? Task.CompletedTask : Send(new DjAutomixRequest(MoveFrom: row.Index, MoveTo: row.Index - 1));

    [RelayCommand]
    private async Task MixNow()
    {
        if (!await Hub.Client.PostAsync("/api/dj/automix", new DjAutomixRequest(MixNow: true)))
            Hub.Status = "Nu mixen kan niet: er moet een nummer spelen en een volgend nummer klaarstaan";
    }

    [RelayCommand]
    private Task BeatsStep(string? direction)
    {
        int i = Array.IndexOf(BeatSteps, _beats);
        i = Math.Clamp((i < 0 ? 2 : i) + (direction == "-" ? -1 : 1), 0, BeatSteps.Length - 1);
        return Send(new DjAutomixRequest(Beats: BeatSteps[i]));
    }

    /// <summary>A track dropped on the list: at the end, or before the row it was dropped on.</summary>
    public Task Add(long mediaId, int? before = null) => Send(new DjAutomixRequest(MediaIds: [mediaId], Index: before));

    protected override void OnState(StateDto s)
    {
        Available = s.Dj?.Automix is not null;
        if (s.Dj?.Automix is not { } a) return;
        On = a.On;
        Transition = a.Transition;
        _beats = a.Beats;
        BeatsText = a.Transition == "cut" ? "op de tel" : $"{a.Beats:0} tellen";
        Mixing = a.State == "mixen";
        Progress = a.Progress * 100;
        StateText = a.State switch
        {
            "uit" => "Automix staat uit: zet hem aan en de lijst speelt zichzelf op deck A en B",
            "leeg" => "De lijst is leeg: sleep nummers uit de bibliotheek hierheen",
            "klaar" => "Klaar om te starten",
            "mixen" => $"Mixt van deck {a.OutDeck} naar het volgende nummer…",
            _ => a.SecondsToMix is double t ? $"Deck {a.OutDeck} speelt · volgende mix over {TimeSpan.FromSeconds(t):m\\:ss}" : $"Deck {a.OutDeck} speelt · laatste nummer",
        };
        CountText = a.Queue.Count == 0 ? "" : $"{a.Queue.Count} nummers · {TimeSpan.FromSeconds(a.Queue.Sum(q => q.Duration)):h\\:mm\\:ss}";
        var sig = string.Join("|", a.Queue.Select(q => q.Id));
        if (sig == _signature) return;
        _signature = sig;
        Queue.Clear();
        for (int i = 0; i < a.Queue.Count; i++)
        {
            var q = a.Queue[i];
            Queue.Add(new AutomixRow(i, $"{i + 1}.", q.Title, q.Artist, TimeSpan.FromSeconds(q.Duration).ToString(@"m\:ss")));
        }
        Empty = Queue.Count == 0;
    }
}
