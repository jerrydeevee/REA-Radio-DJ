using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

public sealed partial class HotkeyRow : ObservableObject
{
    [ObservableProperty] private string _key = "";
    [ObservableProperty] private string _command = "";
    [ObservableProperty] private bool _valid = true;
    /// <summary>Also when another program has the focus.</summary>
    [ObservableProperty] private bool _global;

    partial void OnKeyChanged(string value) => Valid = Hotkeys.TryParse(value) is not null;
}

/// <summary>One event rule in the studio: when this happens, run that command.</summary>
public sealed partial class EventRuleRow : ObservableObject
{
    [ObservableProperty] private EventChoice? _event;
    [ObservableProperty] private string _command = "";
    [ObservableProperty] private bool _enabled = true;
}

public sealed record EventChoice(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Turns the hotkey table from the settings into key bindings that send engine commands.</summary>
public static class Hotkeys
{
    public static KeyGesture? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return KeyGesture.Parse(text.Trim()); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Raised after the hotkeys were saved, so the main window rebuilds its key bindings.</summary>
    public static event Action? Changed;

    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>The hotkeys marked to work everywhere.</summary>
    public static IEnumerable<(KeyGesture Gesture, string Command)> GlobalBindings(StudioSettings settings) =>
        Bindings(settings).Where(b => settings.GlobalHotkeys.Contains(b.Gesture.ToString(), StringComparer.OrdinalIgnoreCase));

    public static IEnumerable<(KeyGesture Gesture, string Command)> Bindings(StudioSettings settings) =>
        settings.Hotkeys
            .Select(kv => (Gesture: TryParse(kv.Key), Command: kv.Value?.Trim() ?? ""))
            .Where(x => x.Gesture is not null && x.Command.Length > 0)
            .Select(x => (x.Gesture!, x.Command));
}

/// <summary>
/// Operating the station by command: the full command list (the same for TCP, OSC, the planner and hotkeys), a line to
/// try a command, and the hotkey table.
/// </summary>
public sealed partial class ControlTool : StudioTool
{
    public ControlTool() : base("control", "Bediening") { }

    public ObservableCollection<CommandInfoDto> Commands { get; } = [];
    public ObservableCollection<HotkeyRow> HotkeyRows { get; } = [];
    public ObservableCollection<string> History { get; } = [];
    [ObservableProperty] private string _commandText = "";
    [ObservableProperty] private CommandInfoDto? _selectedCommand;
    [ObservableProperty] private HotkeyRow? _selectedHotkey;

    public string ConnectInfo =>
        "TCP: telnet <engine> 8601 (één commando per regel, antwoord OK/ERR)  ·  OSC: UDP 8602, bijv. /rea/next, /rea/cart/1/5, /rea/cmd \"AUTO OFF\"  ·  "
        + "UDP: 8603, één commando per pakket  ·  webadres per knop: http://<engine>:8600/api/do/next, /api/do/cart/1/5, /api/do?text=AUTO%20OFF (met wachtwoord: ?key=…)  ·  HTTP: POST /api/command {\"text\":\"NEXT\"}. "
        + "Standaard alleen vanaf deze computer; in appsettings.json ReaRadioDj:Control:BindAddress = 0.0.0.0 voor het netwerk (zet dan ook een wachtwoord).";

    partial void OnSelectedCommandChanged(CommandInfoDto? value)
    {
        if (value is null) return;
        // Take the syntax up to the first placeholder, ready to complete.
        var syntax = value.Syntax;
        int cut = syntax.IndexOfAny(['<', '[', '|']);
        CommandText = (cut < 0 ? syntax : syntax[..cut]).Trim() + (cut < 0 ? "" : " ");
        if (SelectedHotkey is { } h && string.IsNullOrWhiteSpace(h.Command)) h.Command = CommandText.Trim();
    }

    protected override void OnFirstShow()
    {
        LoadHotkeys();
        _ = LoadCommandsAsync();
        _ = LoadEventRulesAsync();
    }

    // ---- events linked to commands
    public ObservableCollection<EventRuleRow> EventRules { get; } = [];
    public ObservableCollection<EventChoice> EventChoices { get; } = [];
    [ObservableProperty] private string _eventStatus = "";

    private sealed record EventRuleWire(string Event, string Command, bool Enabled);
    private sealed record EventEnvelope(List<EventRuleWire> Rules, List<EventChoice> Events);

    private async Task LoadEventRulesAsync()
    {
        if (await Hub.Client.GetAsync<EventEnvelope>("/api/event-commands") is not { } e) return;
        EventChoices.Clear();
        foreach (var c in e.Events) EventChoices.Add(c);
        EventRules.Clear();
        foreach (var r in e.Rules)
            EventRules.Add(new EventRuleRow { Event = EventChoices.FirstOrDefault(c => c.Key == r.Event), Command = r.Command, Enabled = r.Enabled });
    }

    [RelayCommand]
    private void AddEventRule() => EventRules.Add(new EventRuleRow { Event = EventChoices.FirstOrDefault(), Command = CommandText.Trim() });

    [RelayCommand] private void RemoveEventRule(EventRuleRow? row) { if (row is not null) EventRules.Remove(row); }

    [RelayCommand]
    private async Task SaveEventRules()
    {
        var rules = EventRules.Where(r => r.Event is not null).Select(r => new EventRuleWire(r.Event!.Key, r.Command, r.Enabled)).ToList();
        var saved = await Hub.Client.SendForAsync<List<EventRuleWire>>(HttpMethod.Put, "/api/event-commands", rules);
        EventStatus = saved is null ? "Niet gelukt." : $"{saved.Count} koppelingen bewaard.";
        await LoadEventRulesAsync();
    }

    private async Task LoadCommandsAsync()
    {
        var list = await Hub.Client.GetAsync<List<CommandInfoDto>>("/api/commands");
        if (list is null) return;
        Commands.Clear();
        foreach (var c in list) Commands.Add(c);
    }

    private void LoadHotkeys()
    {
        HotkeyRows.Clear();
        foreach (var kv in Hub.Settings.Hotkeys.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            HotkeyRows.Add(new HotkeyRow { Key = kv.Key, Command = kv.Value,
                Global = Hub.Settings.GlobalHotkeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) });
    }

    [RelayCommand]
    private async Task Run()
    {
        var text = CommandText.Trim();
        if (text.Length == 0) return;
        var r = await Hub.Client.RunCommandAsync(text);
        History.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}  →  {(r.Ok ? "OK" : "ERR")} {r.Text}");
        while (History.Count > 50) History.RemoveAt(History.Count - 1);
    }

    [RelayCommand] private void AddHotkey() { var row = new HotkeyRow { Key = "Ctrl+1", Command = "CART 1 1" }; HotkeyRows.Add(row); SelectedHotkey = row; }
    [RelayCommand] private void RemoveHotkey(HotkeyRow? row) { if (row is not null) HotkeyRows.Remove(row); }

    [RelayCommand]
    private void ResetHotkeys()
    {
        Hub.Settings.Hotkeys = StudioSettings.DefaultHotkeys();
        LoadHotkeys();
    }

    [RelayCommand]
    private void SaveHotkeys()
    {
        var bad = HotkeyRows.FirstOrDefault(r => !r.Valid);
        if (bad is not null) { Hub.Status = $"onbekende toets: '{bad.Key}' (voorbeelden: F5, Ctrl+B, Shift+F1, Ctrl+Alt+1)"; return; }
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var global = new List<string>();
        foreach (var r in HotkeyRows.Where(r => r.Key.Trim().Length > 0 && r.Command.Trim().Length > 0))
        {
            var key = Hotkeys.TryParse(r.Key)!.ToString();
            map[key] = r.Command.Trim();
            if (r.Global) global.Add(key);
        }
        Hub.Settings.Hotkeys = map;
        Hub.Settings.GlobalHotkeys = global;
        Hub.Settings.Save();
        Hotkeys.RaiseChanged();
        LoadHotkeys();
        Hub.Status = $"{map.Count} sneltoetsen opgeslagen";
    }
}
