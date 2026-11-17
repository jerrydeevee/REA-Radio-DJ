using ReaRadioDj.Contracts.Localization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>Settings › Centrale bibliotheek: whether this engine is the library for the others, takes it over, or neither.</summary>
public sealed partial class CentralVm : ObservableObject
{
    public IReadOnlyList<string> Modes { get; } = ["uit", "bron", "studio"];
    [ObservableProperty] private string _mode = "uit";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private bool _hasKey;
    [ObservableProperty] private decimal? _intervalMinutes = 15;
    [ObservableProperty] private bool _alwaysFetch;
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private string _result = "";
    private DispatcherTimer? _timer;

    public bool IsStudio => Mode == "studio";
    public bool IsOn => Mode != "uit";
    public string KeyWatermark => HasKey ? Tr.T("(bewaard; leeg laten houdt het)") : Tr.T("het bedieningswachtwoord van de bibliotheek-engine");
    partial void OnModeChanged(string value) { OnPropertyChanged(nameof(IsStudio)); OnPropertyChanged(nameof(IsOn)); }
    partial void OnHasKeyChanged(bool value) => OnPropertyChanged(nameof(KeyWatermark));

    public async Task LoadAsync()
    {
        var s = await StudioHub.Current.Client.GetAsync<CentralSettingsDto>("/api/central/settings");
        Loaded = s is not null;
        if (s is null) return;
        Fill(s);
        await RefreshAsync();
        _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, async (_, _) => await RefreshAsync());
        _timer.Start();
    }

    private void Fill(CentralSettingsDto s)
    {
        Mode = s.Mode; Address = s.Address; Key = ""; HasKey = s.HasKey; IntervalMinutes = s.IntervalMinutes; AlwaysFetch = s.AlwaysFetch;
    }

    private async Task RefreshAsync()
    {
        var st = await StudioHub.Current.Client.GetAsync<CentralStatusDto>("/api/central");
        if (st is null) return;
        State = Tr.E(st.State);
        var lines = new List<string>();
        if (st.Mode == "studio")
        {
            if (st.Result is { } r) lines.Add(r);
            if (st.Fetching > 0) lines.Add(Tr.F("Nog {0} bestanden op te halen", st.Fetching) + (st.Fetched is { } f ? $" ({f})" : ""));
            else if (st.Fetched is { } f) lines.Add(f);
            if (st.Error is { } e) lines.Add(Tr.T("Fout: ") + Tr.E(e));
        }
        Detail = string.Join(Environment.NewLine, lines);
    }

    /// <summary>Saves; returns an error text or null.</summary>
    public async Task<string?> SaveAsync()
    {
        if (!Loaded) return null;
        var (saved, error) = await StudioHub.Current.Client.SendWithErrorAsync<CentralSettingsDto>(HttpMethod.Put, "/api/central/settings",
            new CentralSettingsDto(Mode, Address.Trim(), Key, HasKey, (int)(IntervalMinutes ?? 15), AlwaysFetch));
        if (error is not null) return Result = Tr.T("Centrale bibliotheek niet opgeslagen: ") + VoipLineVm.ErrorText(error);
        if (saved is not null) Fill(saved);
        Result = "";
        await RefreshAsync();
        return null;
    }

    [RelayCommand]
    private async Task SyncNow()
    {
        var (_, error) = await StudioHub.Current.Client.SendWithErrorAsync<object>(HttpMethod.Post, "/api/central/sync", null);
        Result = error is null ? "" : Tr.T("Niet gelukt: ") + VoipLineVm.ErrorText(error);
        await RefreshAsync();
    }
}
