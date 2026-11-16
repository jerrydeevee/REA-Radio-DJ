using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Instellingen › Gebruikers: who may sign in at the studio, with which role and PIN. The first user is an
/// administrator; from then on the settings need signing in. Every change is saved at once.
/// </summary>
/// <summary>A name that is blocked after wrong PINs, with the text the list shows.</summary>
public sealed record LockedNameVm(string Name, string Text);

public sealed partial class UsersPanelVm : ObservableObject
{
    private static StudioHub Hub => StudioHub.Current;

    public ObservableCollection<UserDto> Users { get; } = [];
    public IReadOnlyList<string> Roles { get; } = [UserRoles.Presenter, UserRoles.Technician, UserRoles.Admin];

    [ObservableProperty] private UserDto? _selected;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(EditTitle), nameof(IsExisting))] private long _editId;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editRole = UserRoles.Presenter;
    [ObservableProperty] private bool _editEnabled = true;
    [ObservableProperty] private string _editPin = "";
    /// <summary>The last day the user may sign in, as yyyy-MM-dd; empty = no end.</summary>
    [ObservableProperty] private string _editUntil = "";
    public ObservableCollection<LockedNameVm> Locked { get; } = [];
    [ObservableProperty] private decimal _lockAttempts = 5;
    [ObservableProperty] private decimal _lockSeconds = 30;
    [ObservableProperty] private string _lockStatus = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IntroText))] private bool _active;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IntroText))] private bool _enforced = true;

    public bool IsExisting => EditId != 0;
    public string EditTitle => EditId == 0 ? "Nieuwe gebruiker" : "Gebruiker wijzigen";

    public string IntroText => !Active
        ? "Er zijn nog geen gebruikers: iedereen kan alles. Maak eerst een beheerder aan; vanaf dan meldt iedereen zich aan met een eigen naam en pincode, en zijn de instellingen alleen voor beheerders."
        : !Enforced
            ? "De engine draait met --users-off: aanmelden en rechten worden nu niet gecontroleerd (herstelstand)."
            : "Presentator: alles in de uitzending (spelers, log, carts, microfoon). Technicus: ook geluid, routing, hardware, streams en planning. Beheerder: ook de instellingen, gebruikers en reservekopieën.";

    public async Task LoadAsync()
    {
        var d = await Hub.Client.GetAsync<UsersDto>("/api/users");
        if (d is not null) Apply(d);
        if (await Hub.Client.GetAsync<LoginLocksDto>("/api/users/locks") is { } l) ApplyLocks(l);
    }

    private void ApplyLocks(LoginLocksDto l)
    {
        LockAttempts = l.Attempts;
        LockSeconds = l.LockSeconds;
        Locked.Clear();
        foreach (var x in l.Locked) Locked.Add(new LockedNameVm(x.Name, $"{x.Name}: geblokkeerd tot {x.Until.ToLocalTime():HH:mm:ss}"));
    }

    [RelayCommand]
    private async Task SaveLocks()
    {
        var (l, error) = await Hub.Client.SendWithErrorAsync<LoginLocksDto>(HttpMethod.Put, "/api/users/locks",
            new LoginLockSettingsDto((int)LockAttempts, (int)LockSeconds));
        if (l is null) { LockStatus = error ?? "Niet gelukt."; return; }
        ApplyLocks(l);
        LockStatus = $"Opgeslagen: blokkeren na {l.Attempts} pogingen, voor {l.LockSeconds} seconden.";
    }

    [RelayCommand]
    private async Task Lift(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var (l, error) = await Hub.Client.SendWithErrorAsync<LoginLocksDto>(HttpMethod.Delete, $"/api/users/locks/{Uri.EscapeDataString(name)}", null);
        if (l is null) { LockStatus = error ?? "Niet gelukt."; return; }
        ApplyLocks(l);
        LockStatus = $"De blokkade van {name} is opgeheven.";
    }

    private void Apply(UsersDto d)
    {
        Active = d.Active;
        Enforced = d.Enforced;
        Users.Clear();
        foreach (var u in d.Users) Users.Add(u);
        if (Users.Count == 0) { New(); EditRole = UserRoles.Admin; }
    }

    partial void OnSelectedChanged(UserDto? value)
    {
        if (value is null) return;
        EditId = value.Id;
        EditName = value.Name;
        EditRole = value.Role;
        EditEnabled = value.Enabled;
        EditUntil = value.Until ?? "";
        EditPin = "";
        Status = "";
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        EditId = 0;
        EditName = "";
        EditRole = Users.Count == 0 ? UserRoles.Admin : UserRoles.Presenter;
        EditEnabled = true;
        EditUntil = "";
        EditPin = "";
        Status = "";
    }

    [RelayCommand]
    private async Task Save()
    {
        bool first = !Active;
        var request = new UserSaveRequest(EditName.Trim(), EditRole, EditEnabled, EditPin.Length == 0 ? null : EditPin,
            EditUntil.Trim().Length == 0 ? null : EditUntil.Trim());
        var (d, error) = EditId == 0
            ? await Hub.Client.SendWithErrorAsync<UsersDto>(HttpMethod.Post, "/api/users", request)
            : await Hub.Client.SendWithErrorAsync<UsersDto>(HttpMethod.Put, $"/api/users/{EditId}", request);
        if (d is null) { Status = error ?? "Niet gelukt."; return; }
        var (name, pin, wasNew) = (request.Name, EditPin, EditId == 0);
        Apply(d);
        Selected = Users.FirstOrDefault(u => u.Name == name);
        Status = pin.Length > 0 || wasNew ? $"{name} is opgeslagen." : $"{name} is opgeslagen (pincode ongewijzigd).";
        // The first administrator is signed in right away, so the settings stay open for them.
        if (first && d.Active && SessionVm.Current is { } session) await session.SignInAfterCreateAsync(name, pin);
        else if (SessionVm.Current is { } s) await s.RefreshAsync();
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (EditId == 0) return;
        var (d, error) = await Hub.Client.SendWithErrorAsync<UsersDto>(HttpMethod.Delete, $"/api/users/{EditId}", null);
        if (d is null) { Status = error ?? "Niet gelukt."; return; }
        var name = EditName;
        Apply(d);
        New();
        Status = $"{name} is verwijderd.";
        if (SessionVm.Current is { } s) await s.RefreshAsync();
    }
}
