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
    [ObservableProperty] private string _containerName = "Container";
    [ObservableProperty] private string _containerResult = "";

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

    /// <summary>Everything on the clipboard (top to bottom) as one container line in the log: next or at the end.</summary>
    [RelayCommand]
    private async Task ToContainer(string? where)
    {
        if (Items.Count == 0) return;
        bool next = where == "next";
        var name = string.IsNullOrWhiteSpace(ContainerName) ? "Container" : ContainerName.Trim();
        bool ok = await Hub.Client.PostAsync("/api/log/container", new AddContainerRequest(name, Items.Select(m => m.Id).ToList(), next ? 0 : null));
        ContainerResult = ok ? $"\"{name}\" staat {(next ? "als volgende" : "achteraan")} in de log ({Items.Count} items)." : "Niet gelukt: is de engine bereikbaar?";
    }
}
