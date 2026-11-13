using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>Settings › Reservestudio: the role of this engine, the other engine, when to take over and give back, and the live state.</summary>
public sealed partial class FailoverVm : ObservableObject
{
    public IReadOnlyList<string> Roles { get; } = ["uit", "hoofd", "reserve"];
    [ObservableProperty] private string _role = "uit";
    [ObservableProperty] private string _partner = "";
    [ObservableProperty] private decimal? _intervalSeconds = 2;
    [ObservableProperty] private decimal? _lostSeconds = 10;
    [ObservableProperty] private bool _takeOverOnSilence;
    [ObservableProperty] private bool _giveBackAutomatically = true;
    [ObservableProperty] private decimal? _giveBackSeconds = 30;
    [ObservableProperty] private bool _startStream;
    [ObservableProperty] private string _onTakeOver = "";
    [ObservableProperty] private string _onGiveBack = "";
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private string _partnerText = "";
    [ObservableProperty] private bool _hasAir;
    [ObservableProperty] private bool _isBackup;
    [ObservableProperty] private string _result = "";
    private DispatcherTimer? _timer;

    public bool IsOn => Role != "uit";
    partial void OnRoleChanged(string value) => OnPropertyChanged(nameof(IsOn));

    public async Task LoadAsync()
    {
        var s = await StudioHub.Current.Client.GetAsync<FailoverSettingsDto>("/api/failover/settings");
        Loaded = s is not null;
        if (s is null) return;
        Fill(s);
        await RefreshAsync();
        _timer ??= new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, async (_, _) => await RefreshAsync());
        _timer.Start();
    }

    private void Fill(FailoverSettingsDto s)
    {
        Role = s.Role; Partner = s.Partner; IntervalSeconds = s.IntervalSeconds; LostSeconds = s.LostSeconds; TakeOverOnSilence = s.TakeOverOnSilence;
        GiveBackAutomatically = s.GiveBackAutomatically; GiveBackSeconds = s.GiveBackSeconds; StartStream = s.StartStream;
        OnTakeOver = s.OnTakeOver; OnGiveBack = s.OnGiveBack;
    }

    private async Task RefreshAsync()
    {
        var st = await StudioHub.Current.Client.GetAsync<FailoverStatusDto>("/api/failover");
        if (st is null) return;
        IsBackup = st.Role == "reserve";
        HasAir = st.HasAir;
        State = st.Role == "uit" ? "Uit: deze engine let niet op een andere studio." : char.ToUpper(st.State[0]) + st.State[1..];
        PartnerText = st.Role == "uit" ? "" : st.Partner is { } p
            ? $"{p.Station} ({p.Role}) · {(p.Healthy ? "gezond" : "storing")} · {(p.Playing ? "speelt" : "speelt niet")}{(p.Silent ? " · stil" : "")}{(p.HasAir ? " · heeft de uitzending overgenomen" : "")}"
              + (st.LastAnswerUtc is DateTime t ? $" · laatste antwoord {t.ToLocalTime():HH:mm:ss}" : "")
            : $"Geen antwoord van {st.PartnerUrl}" + (st.Error is { } e ? $": {e}" : "");
    }

    public FailoverSettingsDto Request() => new(Role, Partner.Trim(), (int)(IntervalSeconds ?? 2), (int)(LostSeconds ?? 10), TakeOverOnSilence,
        GiveBackAutomatically, (int)(GiveBackSeconds ?? 30), StartStream, OnTakeOver.Trim(), OnGiveBack.Trim());

    /// <summary>Saves; returns an error text or null.</summary>
    public async Task<string?> SaveAsync()
    {
        if (!Loaded) return null;
        var (saved, error) = await StudioHub.Current.Client.SendWithErrorAsync<FailoverSettingsDto>(HttpMethod.Put, "/api/failover/settings", Request());
        if (error is not null) return Result = "Reservestudio niet opgeslagen: " + VoipLineVm.ErrorText(error);
        if (saved is not null) Fill(saved);
        Result = "";
        await RefreshAsync();
        return null;
    }

    [RelayCommand]
    private async Task TakeOver()
    {
        var (_, error) = await StudioHub.Current.Client.SendWithErrorAsync<object>(HttpMethod.Post, "/api/failover/takeover", null);
        Result = error is null ? "" : "Niet gelukt: " + VoipLineVm.ErrorText(error);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task GiveBack()
    {
        var (_, error) = await StudioHub.Current.Client.SendWithErrorAsync<object>(HttpMethod.Post, "/api/failover/giveback", null);
        Result = error is null ? "" : "Niet gelukt: " + VoipLineVm.ErrorText(error);
        await RefreshAsync();
    }
}
