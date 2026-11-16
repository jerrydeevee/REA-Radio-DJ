using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Signing in at the studio with a name and PIN, signing out, and locking the studio. While the sign-in screen or the
/// lock is shown the broadcast simply goes on; only this studio's buttons and hotkeys wait.
/// </summary>
public sealed partial class SessionVm : ObservableObject
{
    private readonly StudioHub _hub;
    private string _token = "";

    public SessionVm(StudioHub hub)
    {
        _hub = hub;
        Current = this;
        hub.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StudioHub.Connected) && hub.Connected) _ = RefreshAsync();
        };
    }

    public static SessionVm? Current { get; private set; }

    /// <summary>The sign-in screen covers the studio.</summary>
    [ObservableProperty] private bool _open;
    /// <summary>Locked: the signed-in user unlocks with their PIN (or another user signs in).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title), nameof(ActionText))] private bool _locked;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string _pin = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _busy;
    /// <summary>The screen may be closed (nothing requires signing in right now).</summary>
    [ObservableProperty] private bool _canClose;
    public ObservableCollection<string> Names { get; } = [];

    public string Title => Locked ? "Studio vergrendeld" : "Aanmelden";
    public string ActionText => Locked ? "Ontgrendelen" : "Aanmelden";

    /// <summary>Asks the engine who is signed in and whether there are users; shows the sign-in screen when needed.</summary>
    public async Task RefreshAsync()
    {
        var me = await _hub.Client.GetAsync<SessionDto>("/api/users/me");
        if (me is null) return;
        if (me.Name.Length == 0 && _token.Length > 0) { _token = ""; _hub.Client.SetSession(null); }   // the engine restarted
        _hub.ApplySession(me);
        if (me.Active) await LoadNamesAsync();
        if (me.Active && me.Enforced && me.Name.Length == 0 && !Open) ShowSignIn();
        if (!me.Active || !me.Enforced) { Open = false; Locked = false; }
    }

    private async Task LoadNamesAsync()
    {
        var users = await _hub.Client.GetAsync<UsersDto>("/api/users");
        if (users is null) return;
        var keep = Name;
        Names.Clear();
        foreach (var u in users.Users.Where(u => u.Enabled && !u.Expired)) Names.Add(u.Name);   // not who can no longer sign in
        Name = keep is not null && Names.Contains(keep) ? keep : Names.FirstOrDefault();
    }

    private void ShowSignIn()
    {
        Locked = false;
        Pin = "";
        Error = "";
        CanClose = !(_hub.UsersActive && _hub.UsersEnforced && _hub.UserName.Length == 0);
        Open = true;
    }

    [RelayCommand]
    private async Task SignIn()
    {
        if (Busy) return;
        if (string.IsNullOrWhiteSpace(Name)) { Error = "Kies je naam."; return; }
        if (Pin.Length == 0) { Error = "Typ je pincode."; return; }
        Busy = true;
        try
        {
            var (s, error) = await _hub.Client.SendWithErrorAsync<SessionDto>(HttpMethod.Post, "/api/users/login", new LoginRequest(Name, Pin));
            Pin = "";
            if (s is null) { Error = error ?? "Niet gelukt."; return; }
            var old = _token;
            _token = s.Token;
            _hub.Client.SetSession(s.Token);
            if (old.Length > 0 && old != s.Token) await SignOutToken(old);
            _hub.ApplySession(s);
            Error = "";
            Open = false;
            Locked = false;
            _hub.Status = $"Aangemeld als {s.Name} ({s.Role}).";
        }
        finally { Busy = false; }
    }

    private async Task SignOutToken(string token)
    {
        using var c = new EngineClient(_hub.Client.BaseUri);
        c.SetSession(token);
        await c.PostQuietAsync("/api/users/logout");
    }

    [RelayCommand]
    private async Task SignOut()
    {
        if (_token.Length > 0) await _hub.Client.PostQuietAsync("/api/users/logout");
        _token = "";
        _hub.Client.SetSession(null);
        await RefreshAsync();
        if (_hub.UsersActive && _hub.UsersEnforced) ShowSignIn();
        _hub.Status = "Afgemeld.";
    }

    /// <summary>Locks the studio: the broadcast goes on, buttons and hotkeys wait for the PIN.</summary>
    [RelayCommand]
    private void Lock()
    {
        if (!_hub.UsersActive) { _hub.Status = "Vergrendelen kan als er gebruikers zijn (Instellingen › Gebruikers)."; return; }
        if (_hub.UserName.Length == 0) { ShowSignIn(); return; }
        Name = _hub.UserName;
        Pin = "";
        Error = "";
        CanClose = false;
        Locked = true;
        Open = true;
    }

    /// <summary>On the lock screen: someone else takes over (signs out the current user first).</summary>
    [RelayCommand]
    private async Task OtherUser()
    {
        await SignOut();
        ShowSignIn();
    }

    /// <summary>Signing in is offered in the menu (Aanmelden when nobody is signed in or to switch user).</summary>
    [RelayCommand]
    private async Task ShowSignInScreen()
    {
        await RefreshAsync();
        if (!_hub.UsersActive) { _hub.Status = "Er zijn nog geen gebruikers; maak ze aan bij Instellingen › Gebruikers."; return; }
        ShowSignIn();
    }

    /// <summary>The sign-in screen can be closed when nothing requires it (users switched off or someone is signed in).</summary>
    [RelayCommand]
    private void Close()
    {
        if (Locked || !CanClose) return;
        Open = false;
    }

    /// <summary>After creating the first user in Instellingen: that administrator is signed in right away.</summary>
    public async Task SignInAfterCreateAsync(string name, string pin)
    {
        await RefreshAsync();
        Name = name;
        Pin = pin;
        await SignIn();
    }

    /// <summary>Hotkeys and buttons wait while the screen is shown.</summary>
    public bool Blocks => Open;
}
