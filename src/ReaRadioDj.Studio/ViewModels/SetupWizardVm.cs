using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// The setup helper for a new installation: station name and look, the music and jingle folders, the sound cards
/// for on air and preview, and whether the engine starts playing by itself. It only changes what it shows; all other
/// settings stay as they are. It opens by itself while the library is still empty, and from Studio › Inrichthulp.
/// </summary>
public sealed partial class SetupWizardVm : ObservableObject
{
    public const int Steps = 4;

    private static StudioHub Hub => StudioHub.Current;
    private SettingsDto? _settings;
    private RoutingDto? _routing;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private int _step;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "";

    [ObservableProperty] private string _stationName = "";
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private string _musicFolder = "";
    [ObservableProperty] private string _jingleFolder = "";
    [ObservableProperty] private string _folderInfo = "";
    [ObservableProperty] private DeviceDto? _outputDevice;
    [ObservableProperty] private DeviceDto? _previewDevice;
    [ObservableProperty] private bool _startAutomation = true;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CloseText))] private bool _saved;

    public string CloseText => Saved ? "Sluiten" : "Overslaan";

    public ObservableCollection<DeviceDto> Outputs { get; } = [];
    public string[] ThemeNames { get; } = Appearance.Themes.Select(t => t.Name).ToArray();

    public string StepText => $"Stap {Step + 1} van {Steps}";
    public string Title => Step switch
    {
        0 => "Welkom bij REA-Radio-DJ",
        1 => "Waar staat je muziek?",
        2 => "Welke geluidskaart?",
        _ => "Klaar om te beginnen",
    };
    public bool IsStation => Step == 0;
    public bool IsFolders => Step == 1;
    public bool IsSound => Step == 2;
    public bool IsDone => Step == 3;
    public bool CanBack => Step > 0 && !Busy;
    public bool CanNext => Step < Steps - 1 && !Busy;
    public bool CanFinish => Step == Steps - 1 && !Busy;

    partial void OnStepChanged(int value)
    {
        foreach (var n in new[] { nameof(StepText), nameof(Title), nameof(IsStation), nameof(IsFolders), nameof(IsSound), nameof(IsDone),
                     nameof(CanBack), nameof(CanNext), nameof(CanFinish) })
            OnPropertyChanged(n);
        if (value == Steps - 1) Summary = MakeSummary();
    }

    partial void OnBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanBack)); OnPropertyChanged(nameof(CanNext)); OnPropertyChanged(nameof(CanFinish));
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (value < 0 || value >= Appearance.Themes.Length || !IsOpen) return;
        Hub.Settings.Theme = Appearance.Themes[value].Key;   // shown at once, so the choice can be seen
        Appearance.Apply(Hub.Settings);
    }

    /// <summary>Reads the current settings from the engine and shows the first step.</summary>
    public async Task OpenAsync()
    {
        Status = "";
        Saved = false;
        Step = 0;
        IsOpen = true;
        Busy = true;
        try
        {
            _settings = await Hub.Client.GetAsync<SettingsDto>("/api/settings");
            _routing = await Hub.Client.GetAsync<RoutingDto>("/api/routing");
            var outs = await Hub.Client.GetAsync<List<DeviceDto>>("/api/devices");
            if (_settings is null || _routing is null || outs is null)
            {
                Status = "De engine geeft zijn instellingen niet: is hij gestart en verbonden?";
                return;
            }
            StationName = _settings.StationName;
            MusicFolder = _settings.MusicFolder;
            JingleFolder = _settings.JingleFolder;
            StartAutomation = _settings.StartAutomation;
            ThemeIndex = Math.Max(0, Array.FindIndex(Appearance.Themes, t => t.Key == Hub.Settings.Theme));
            Outputs.Clear();
            Outputs.Add(new DeviceDto(-1, "Standaard geluidskaart van Windows", true, false));
            foreach (var d in outs) Outputs.Add(d);
            OutputDevice = Outputs.FirstOrDefault(d => d.Index == _routing.OutputDevice) ?? Outputs[0];
            PreviewDevice = Outputs.FirstOrDefault(d => d.Index == _routing.PreviewDevice) ?? Outputs[0];
            await ShowFolderInfoAsync();
        }
        finally { Busy = false; }
    }

    private async Task ShowFolderInfoAsync()
    {
        var folders = await Hub.Client.GetAsync<List<LibraryFolderDto>>("/api/library/folders");
        int extra = folders?.Count(f => !f.Fixed) ?? 0;
        FolderInfo = extra == 0
            ? "Meer mappen of een netwerkshare (\\\\server\\muziek) voeg je later toe in de Bibliotheek onder \"Mappen en shares\"."
            : $"Er staan al {extra} andere {(extra == 1 ? "map" : "mappen")} in de bibliotheek; die blijven staan.";
    }

    private string MakeSummary()
    {
        string name = StationName.Trim().Length > 0 ? StationName.Trim() : "(geen naam)";
        return $"Station: {name}\n" +
               $"Muziek: {Show(MusicFolder)}\n" +
               $"Jingles: {Show(JingleFolder)}\n" +
               $"Uitzending: {OutputDevice?.Name ?? "standaard"}\n" +
               $"Voorluisteren: {PreviewDevice?.Name ?? "standaard"}";
        static string Show(string f) => f.Trim().Length > 0 ? f.Trim() : "(standaard)";
    }

    [RelayCommand] private void Back() { if (Step > 0) Step--; }

    [RelayCommand]
    private void Next()
    {
        if (Step == 0 && StationName.Trim().Length == 0) { Status = "Geef het station een naam."; return; }
        if (Step == 1 && MusicFolder.Trim().Length == 0) { Status = "Kies een map voor de muziek."; return; }
        Status = "";
        if (Step < Steps - 1) Step++;
    }

    /// <summary>A short test tone on the on-air or preview output, as it is set in the engine now.</summary>
    [RelayCommand]
    private async Task TestTone(string target)
    {
        var (_, error) = await Hub.Client.SendWithErrorAsync<object>(HttpMethod.Post, "/api/outputs/test", new OutputRouteRequest(target));
        Status = error is null
            ? "Testtoon gestuurd naar de kaart die nu in de engine staat; een nieuwe keuze geldt na Klaar."
            : "Testtoon niet gelukt: " + error;
    }

    /// <summary>Closes without changing anything; it does not open by itself again.</summary>
    [RelayCommand]
    private void Skip()
    {
        MarkDone();
        IsOpen = false;
    }

    [RelayCommand]
    private async Task Finish()
    {
        if (_settings is null || _routing is null) { Status = "Nog geen verbinding met de engine."; return; }
        Busy = true;
        try
        {
            Status = "opslaan…";
            var routing = _routing with { OutputDevice = OutputDevice?.Index ?? -1, PreviewDevice = PreviewDevice?.Index ?? -1 };
            if (routing != _routing && !await Hub.Client.PutAsync("/api/routing", routing))
            {
                Status = "De geluidskaarten konden niet worden opgeslagen.";
                return;
            }
            // Passwords are never sent to the studio; empty here keeps the stored ones.
            var settings = _settings with
            {
                StationName = StationName.Trim(), MusicFolder = MusicFolder.Trim(), JingleFolder = JingleFolder.Trim(),
                StartAutomation = StartAutomation, StreamPassword = "", ControlPassword = "", ClearControlPassword = false,
            };
            var (saved, error) = await Hub.Client.SendWithErrorAsync<SettingsSavedDto>(HttpMethod.Put, "/api/settings", settings);
            if (saved is null) { Status = "Opslaan niet gelukt: " + (error ?? "geen antwoord van de engine"); return; }
            MarkDone();
            if (saved.NeedsRestart.Count == 0) { IsOpen = false; return; }
            // Something only takes effect after a restart: say so here instead of closing silently.
            Saved = true;
            Status = $"Opgeslagen. Dit werkt na een herstart van de engine: {string.Join(", ", saved.NeedsRestart)}.";
        }
        finally { Busy = false; }
    }

    private static void MarkDone()
    {
        Hub.Settings.SetupDone = true;
        Hub.Settings.Save();
    }
}
