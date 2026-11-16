using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>A command group a remote control may or may not use.</summary>
public sealed partial class GroupChoiceVm(string name, bool on) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private bool _on = on;
}

/// <summary>One remote control in the list: name, on/off, addresses, its groups, and the key state.</summary>
public sealed partial class RemoteClientRowVm : ObservableObject
{
    public string Id { get; init; } = "";
    /// <summary>The list this row is in; its buttons act through it.</summary>
    public RemoteClientsVm? Owner { get; init; }
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _addresses = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(KeyText))] private bool _hasKey;
    /// <summary>A key that was just made: shown until the list is loaded again, never fetched later.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(KeyText), nameof(ShowNewKey))] private string _newKey = "";
    public ObservableCollection<GroupChoiceVm> Groups { get; } = [];

    public bool ShowNewKey => NewKey.Length > 0;
    public string KeyText => NewKey.Length > 0 ? "Nieuwe sleutel (kopieer hem nu; hij is later niet meer te zien):"
        : HasKey ? "Heeft een sleutel." : Addresses.Trim().Length > 0 ? "Zonder sleutel: herkend aan het adres." : "Zonder sleutel en zonder adres: maak een sleutel of geef een adres.";

    partial void OnAddressesChanged(string value) => OnPropertyChanged(nameof(KeyText));

    [RelayCommand] private void Remove() => Owner?.RemoveRow(this);
    [RelayCommand] private Task MakeKey() => Owner?.MakeKeyFor(this) ?? Task.CompletedTask;
    [RelayCommand] private Task ClearKey() => Owner?.ClearKeyFor(this) ?? Task.CompletedTask;

    public RemoteClientDto ToDto() => new(Id, Name, Enabled, HasKey, Addresses, Groups.Where(g => g.On).Select(g => g.Name).ToList());
}

/// <summary>
/// Instellingen › Op afstand: remote controls with their own limits (a tablet that may only use the cartwall, a button
/// box that may only start the next item). Saved with its own button; keys are made by the engine and shown once.
/// </summary>
public sealed partial class RemoteClientsVm : ObservableObject
{
    private static StudioHub Hub => StudioHub.Current;
    private IReadOnlyList<string> _groups = [];

    public ObservableCollection<RemoteClientRowVm> Clients { get; } = [];
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private string _status = "";

    public async Task LoadAsync()
    {
        try
        {
            var r = await Hub.Client.GetAsync<RemoteClientsDto>("/api/remote-clients");
            Loaded = r is not null;
            if (r is null) { Status = "De engine kent afstandsbedieners met eigen rechten nog niet (werk hem bij), of je bent geen beheerder."; return; }
            Show(r);
            Status = "";
        }
        catch (Exception ex) { Loaded = false; Status = $"Lijst niet geladen: {ex.Message}"; }
    }

    private void Show(RemoteClientsDto r, IReadOnlyDictionary<string, string>? keep = null)
    {
        _groups = r.Groups;
        Clients.Clear();
        foreach (var c in r.Clients)
        {
            var row = new RemoteClientRowVm { Owner = this, Id = c.Id, Name = c.Name, Enabled = c.Enabled, Addresses = c.Addresses, HasKey = c.HasKey };
            foreach (var g in r.Groups) row.Groups.Add(new GroupChoiceVm(g, c.Groups.Contains(g)));
            if (keep is not null && keep.TryGetValue(c.Id, out var k)) row.NewKey = k;
            Clients.Add(row);
        }
    }

    [RelayCommand]
    private void Add()
    {
        var row = new RemoteClientRowVm { Owner = this, Id = Guid.NewGuid().ToString("N")[..12], Name = "Nieuwe afstandsbediening" };
        foreach (var g in _groups) row.Groups.Add(new GroupChoiceVm(g, g is "Uitzending" or "Cartwall"));
        Clients.Add(row);
        Status = "Nieuw: geef een naam en kies wat hij mag; daarna Opslaan en een sleutel maken.";
    }

    public void RemoveRow(RemoteClientRowVm row)
    {
        Clients.Remove(row);
        Status = $"{row.Name} weg na Opslaan.";
    }

    [RelayCommand]
    private async Task Save()
    {
        var keep = Clients.Where(c => c.NewKey.Length > 0).ToDictionary(c => c.Id, c => c.NewKey);
        var (r, error) = await Hub.Client.SendWithErrorAsync<RemoteClientsDto>(HttpMethod.Put, "/api/remote-clients", Clients.Select(c => c.ToDto()).ToList());
        if (r is null) { Status = $"Niet opgeslagen: {error}"; return; }
        Show(r, keep);
        Status = "Opgeslagen; geldt meteen.";
    }

    public async Task MakeKeyFor(RemoteClientRowVm row)
    {
        await Save();   // a new client must exist on the engine first
        var target = Clients.FirstOrDefault(c => c.Id == row.Id);
        var (k, error) = await Hub.Client.SendWithErrorAsync<RemoteClientKeyDto>(HttpMethod.Post, $"/api/remote-clients/{Uri.EscapeDataString(row.Id)}/key", null);
        if (k is null || target is null) { Status = $"Geen sleutel gemaakt: {error}"; return; }
        target.HasKey = true;
        target.NewKey = k.Key;
        Status = $"Sleutel voor {target.Name} gemaakt. Een oude sleutel werkt niet meer.";
    }

    public async Task ClearKeyFor(RemoteClientRowVm row)
    {
        var (r, error) = await Hub.Client.SendWithErrorAsync<RemoteClientsDto>(HttpMethod.Delete, $"/api/remote-clients/{Uri.EscapeDataString(row.Id)}/key", null);
        if (r is null) { Status = $"Niet gelukt: {error}"; return; }
        Show(r);
        Status = $"Sleutel van {row.Name} verwijderd; hij werkt nu alleen nog vanaf zijn adres.";
    }
}
