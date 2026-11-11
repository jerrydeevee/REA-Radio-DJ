using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>The window of the track clipboard (<see cref="MediaClipboard"/>).</summary>
public sealed partial class ClipboardTool : StudioTool
{
    public ClipboardTool() : base("clipboard", "Klembord") { }

    public ObservableCollection<MediaDto> Items => MediaClipboard.Items;

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isEmpty = true;

    protected override void OnFirstShow()
    {
        MediaClipboard.Changed += Update;
        Update();
        _ = MediaClipboard.EnsureLoadedAsync();
    }

    private void Update()
    {
        IsEmpty = Items.Count == 0;
        double seconds = Items.Sum(m => Math.Max(0, m.Cues.CueOut - m.Cues.CueIn));
        Summary = Items.Count == 0 ? "" : $"{Items.Count} {(Items.Count == 1 ? "nummer" : "nummers")} · {StudioHub.Time(seconds)}";
    }

    public Task Drop(long mediaId) => MediaClipboard.AddAsync(mediaId);

    [RelayCommand] private Task AddSelected() => Hub.SelectedMedia is { } m ? MediaClipboard.AddAsync(m.Id) : Task.CompletedTask;
    [RelayCommand] private Task Next(MediaDto? m) => m is null ? Task.CompletedTask : Hub.AddToLog(m.Id, true);
    [RelayCommand] private Task End(MediaDto? m) => m is null ? Task.CompletedTask : Hub.AddToLog(m.Id, false);
    [RelayCommand] private void Remove(MediaDto? m) { if (m is not null) MediaClipboard.Remove(m); }
    [RelayCommand] private void Clear() => MediaClipboard.Clear();
}
