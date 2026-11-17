using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Contracts.Localization;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>Settings › Meldingen per mail: the mail server, who gets the mails and about what.</summary>
public sealed partial class MailVm : ObservableObject
{
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private decimal? _port = 587;
    [ObservableProperty] private bool _tls = true;
    [ObservableProperty] private string _user = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _hasPassword;
    [ObservableProperty] private string _from = "";
    [ObservableProperty] private string _to = "";
    [ObservableProperty] private decimal? _minutesBetween = 10;
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private string _state = "";

    public ObservableCollection<MailTopicChoice> Topics { get; } = [];

    public string PasswordWatermark => HasPassword ? Tr.T("opgeslagen (leeg laten om te houden)") : Tr.T("wachtwoord");
    partial void OnHasPasswordChanged(bool value) => OnPropertyChanged(nameof(PasswordWatermark));

    public async Task LoadAsync()
    {
        var d = await StudioHub.Current.Client.GetAsync<MailSettingsDto>("/api/notify/mail");
        Loaded = d is not null;
        if (d is not null) Fill(d);
    }

    private void Fill(MailSettingsDto d)
    {
        Enabled = d.Enabled; Host = d.Host; Port = d.Port; Tls = d.Tls; User = d.User; HasPassword = d.HasPassword; Password = "";
        From = d.From; To = d.To; MinutesBetween = d.MinutesBetween;
        Topics.Clear();
        foreach (var t in d.AllTopics ?? []) Topics.Add(new MailTopicChoice(t.Id, Tr.T(t.Name)) { On = d.Topics.Contains(t.Id) });
        var parts = new List<string>();
        if (d.Problem is { } p) parts.Add(Tr.F("Nog niet compleet: {0}", Tr.E(p)));
        if (d.LastSent is DateTime t2) parts.Add(Tr.F("Laatste mail verstuurd: {0:ddd d MMM HH:mm}", t2));
        if (d.LastError is { } e) parts.Add(Tr.F("Laatste fout: {0}", Tr.E(e)));
        State = string.Join(Environment.NewLine, parts);
    }

    private MailSettingsDto Request() => new(Enabled, Host.Trim(), (int)(Port ?? 587), Tls, User.Trim(), Password.Length > 0 ? Password : null, HasPassword, false,
        From.Trim(), To.Trim(), Topics.Where(t => t.On).Select(t => t.Id).ToList(), (int)(MinutesBetween ?? 10));

    /// <summary>Saves; returns an error text or null.</summary>
    public async Task<string?> SaveAsync()
    {
        if (!Loaded) return null;
        var (d, error) = await StudioHub.Current.Client.SendWithErrorAsync<MailSettingsDto>(HttpMethod.Put, "/api/notify/mail", Request());
        if (d is null) return Result = Tr.F("Meldingen per mail niet opgeslagen: {0}", error);
        Fill(d);
        Result = Tr.T("Opgeslagen.");
        return null;
    }

    [RelayCommand]
    private Task Save() => SaveAsync();

    /// <summary>Saves first, then lets the engine send one test mail to the recipients.</summary>
    [RelayCommand]
    private async Task Test()
    {
        if (await SaveAsync() is not null) return;
        Result = Tr.T("Testmail versturen …");
        var (d, error) = await StudioHub.Current.Client.SendWithErrorAsync<MailSettingsDto>(HttpMethod.Post, "/api/notify/mail/test", null);
        if (d is not null) Fill(d);
        Result = error is null ? Tr.T("Testmail verstuurd. Kijk of hij aankomt (ook in de map ongewenst).") : Tr.F("Testmail niet verstuurd: {0}", error);
    }

    [RelayCommand]
    private async Task ClearPassword()
    {
        var r = Request() with { Password = null, ClearPassword = true };
        var (d, error) = await StudioHub.Current.Client.SendWithErrorAsync<MailSettingsDto>(HttpMethod.Put, "/api/notify/mail", r);
        if (d is not null) Fill(d);
        Result = error is null ? Tr.T("Wachtwoord gewist.") : Tr.F("Niet gelukt: {0}", error);
    }
}

public sealed partial class MailTopicChoice(string id, string name) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    [ObservableProperty] private bool _on;
}
