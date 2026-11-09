using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Serializer;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly DockFactory _factory = new();
    private readonly DockSerializer _serializer = new(typeof(ObservableCollection<>));
    private readonly StudioSettings _settings;

    public MainWindowViewModel()
    {
        // Command line: --engine http://host:port (another engine for this session), --no-engine (do not start one),
        // --profile <folder> (settings and layouts in another folder).
        var args = Environment.GetCommandLineArgs();
        int pa = Array.IndexOf(args, "--profile");
        if (pa >= 0 && pa + 1 < args.Length) StudioSettings.FolderOverride = Path.GetFullPath(args[pa + 1]);
        _settings = StudioSettings.Load();
        int ea = Array.IndexOf(args, "--engine");
        if (ea >= 0 && ea + 1 < args.Length) _settings.SessionEngineUrl = args[ea + 1];
        if (args.Contains("--no-engine")) _settings.StartEngine = false;
        Hub = StudioHub.Start(_settings);
        if (_settings.StartEngine) _ = EnsureEngineAsync();
        Hub.StateReceived += OnState;
        Hub.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(StudioHub.Status) or nameof(StudioHub.ConnectionText)) OnPropertyChanged(nameof(StatusLine)); };
        Hub.EditCuesRequested += id => ShowWindow("cues");
        RefreshLayoutNames();
        _factory.AutomationView = _settings.LastLayout == AutomationLayoutName;
        Layout = LoadLayoutFile(_settings.LastLayout) ?? NewDefaultLayout();
        CurrentLayout = _settings.LastLayout;
        // --show <window id> (repeatable): bring windows to the front at start; --cues <media id>: open in the cue editor.
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--show") ShowWindow(args[i + 1]);
            else if (args[i] == "--float") FloatWindow(args[i + 1]);
        int ca = Array.IndexOf(args, "--cues");
        if (ca >= 0 && ca + 1 < args.Length && long.TryParse(args[ca + 1], out var cueId))
            _ = Task.Delay(1500).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => Hub.RequestCueEditor(cueId)));
    }

    public StudioHub Hub { get; }
    public IReadOnlyList<WindowEntry> WindowMenu { get; } = DockFactory.Windows.Select(w => new WindowEntry(w.Id, w.Name)).ToList();
    public ObservableCollection<string> LayoutNames { get; } = [];

    [ObservableProperty] private IRootDock? _layout;
    [ObservableProperty] private string _currentLayout = "Standaard";
    [ObservableProperty] private string _newLayoutName = "";

    // Top bar
    [ObservableProperty] private string _station = "REA-Radio-DJ";
    [ObservableProperty] private string _clock = "--:--:--";
    [ObservableProperty] private bool _onAir;
    [ObservableProperty] private bool _autoPlay;
    [ObservableProperty] private bool _micOpen;
    [ObservableProperty] private string _micStatus = "";
    [ObservableProperty] private string _nowLabel = "";
    [ObservableProperty] private string _nowRemaining = "";
    [ObservableProperty] private string _nextLabel = "";
    [ObservableProperty] private string _clockName = "";
    [ObservableProperty] private double _levelLeft;
    [ObservableProperty] private double _levelRight;
    [ObservableProperty] private string _nextEvent = "";
    /// <summary>0-100: how much of the current item is left (the bar runs empty).</summary>
    [ObservableProperty] private double _nowLeft;
    /// <summary>normal, warn (last 20 s), end (last 10 s, flashes).</summary>
    [ObservableProperty] private string _endState = "normal";
    [ObservableProperty] private bool _flash;
    [ObservableProperty] private string _introText = "";
    [ObservableProperty] private bool _automationView;

    public const string AutomationLayoutName = "Automatisering";
    public const string AssistLayoutName = "Standaard";

    public string ModeText => AutoPlay ? "AUTOMATISCH" : "LIVE-ASSIST";
    public string StatusLine => string.IsNullOrEmpty(Hub.Status) ? Hub.ConnectionText : $"{Hub.ConnectionText} · {Hub.Status}";

    partial void OnAutoPlayChanged(bool value) => OnPropertyChanged(nameof(ModeText));

    private void OnState(StateDto s)
    {
        Station = s.Station;
        Clock = s.Time.ToString("HH:mm:ss");
        ClockName = s.Clock is null ? "geen klok" : $"klok: {s.Clock}";
        AutoPlay = s.Engine.AutoPlay;
        OnAir = s.Engine.Current is not null;
        MicOpen = s.Mixer.Mic.Open;
        MicStatus = $"Microfoon: {s.Mixer.Mic.Status}";
        LevelLeft = s.Mixer.ProgramLeft * 100;
        LevelRight = s.Mixer.ProgramRight * 100;
        if (s.Engine.Current is { } c)
        {
            NowLabel = c.Label;
            double left = Math.Max(0, c.To - c.Position), length = Math.Max(0.1, c.To - c.From);
            NowRemaining = $"-{StudioHub.Time(left)}";
            NowLeft = Math.Clamp(left / length * 100, 0, 100);
            EndState = left <= 10 ? "end" : left <= 20 ? "warn" : "normal";
            Flash = EndState == "end" && s.Time.Millisecond < 500;
            IntroText = c.Intro is double intro && c.Position < intro ? $"INTRO {Math.Ceiling(intro - c.Position):0}" : "";
        }
        else
        {
            NowLabel = AutoPlay ? "wachten op materiaal…" : "stil — start het volgende item (F5)";
            NowRemaining = ""; NowLeft = 0; EndState = "normal"; Flash = false; IntroText = "";
        }
        NextLabel = s.Upcoming.FirstOrDefault() is { } n ? $"Straks: {n.Label}" : "";
        NextEvent = s.Events.FirstOrDefault() is { } e ? $"{e.Name} {e.At:HH:mm:ss} (over {StudioHub.Time((e.At - s.Time).TotalSeconds)})" : "";
    }

    // ------------------------------------------------------------ transport (hotkeys in the window)

    [RelayCommand] private Task Next() => Hub.Client.PostAsync("/api/transport/next");
    [RelayCommand] private Task Skip() => Hub.Client.PostAsync("/api/transport/skip");
    [RelayCommand] private Task Stop() => Hub.Client.PostAsync("/api/transport/stop");
    [RelayCommand] private Task ToggleAuto() => Hub.Client.PostAsync("/api/transport/autoplay", new SwitchRequest(!AutoPlay));
    [RelayCommand] private Task ToggleMic() => Hub.Client.PostAsync("/api/mixer/mic/toggle");
    [RelayCommand] private Task AddBreak() => Hub.Client.PostAsync("/api/log/break", new AddBreakRequest(0));
    [RelayCommand] private Task StopCarts() => Hub.Client.PostAsync("/api/carts/stop");

    [RelayCommand]
    private Task PflToggle() => Hub.PlayerCommand("pfl", "toggle");

    /// <summary>Hotkeys: any engine command; errors show in the status line.</summary>
    [RelayCommand]
    private async Task RunEngineCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var r = await Hub.Client.RunCommandAsync(text);
        if (!r.Ok) Hub.Status = $"{text}: {r.Text}";
    }

    public StudioSettings Settings => _settings;

    [RelayCommand]
    private async Task StartEngine()
    {
        var error = EngineClient.StartLocalEngine(_settings);
        Hub.Status = error ?? "engine wordt gestart…";
        await Task.CompletedTask;
    }

    private async Task EnsureEngineAsync()
    {
        await Task.Delay(2500);
        if (!Hub.Connected && Uri.TryCreate(_settings.EffectiveEngineUrl, UriKind.Absolute, out var u) && u.IsLoopback)
        {
            var error = EngineClient.StartLocalEngine(_settings);
            Hub.Status = error ?? "engine gestart";
        }
    }

    // ------------------------------------------------------------ windows and layouts

    [RelayCommand]
    private void ShowWindow(string id)
    {
        if (Layout is null) return;
        var existing = _factory.FindDockable(Layout, d => d.Id == id);
        if (existing is not null)
        {
            _factory.SetActiveDockable(existing);
            if (existing.Owner is IDock owner) _factory.SetFocusedDockable(owner, existing);
            return;
        }
        // The window was closed: open it again in the first tool area, or float it when there is none.
        var tool = DockFactory.Create(id);
        var target = _factory.FindDockable(Layout, d => d is IToolDock) as IDock;
        if (target is null) { _factory.FloatDockable(tool); return; }
        _factory.AddDockable(target, tool);
        _factory.SetActiveDockable(tool);
        _factory.SetFocusedDockable(target, tool);
    }

    [RelayCommand]
    private void FloatWindow(string id)
    {
        if (Layout is null) return;
        if (_factory.FindDockable(Layout, d => d.Id == id) is { } d) _factory.FloatDockable(d);
        else { var tool = DockFactory.Create(id); _factory.FloatDockable(tool); }
    }

    [RelayCommand]
    private void ResetLayout()
    {
        CloseFloating();
        _factory.AutomationView = CurrentLayout == AutomationLayoutName;
        Layout = NewDefaultLayout();
        Hub.Status = "standaardindeling hersteld";
    }

    /// <summary>
    /// One click between the calm automation view and the full live-assist studio. Each view remembers its own
    /// arrangement (saved as a layout under its own name).
    /// </summary>
    [RelayCommand]
    private void SwitchView(string? view)
    {
        bool automation = view == "auto" || (view is null && !AutomationView);
        string target = automation ? AutomationLayoutName : AssistLayoutName;
        if (target == CurrentLayout) return;
        SaveOnExit();
        CloseFloating();
        _factory.AutomationView = automation;
        Layout = LoadLayoutFile(target) ?? NewDefaultLayout();
        CurrentLayout = target;
        _settings.LastLayout = target;
        _settings.Save();
        RefreshLayoutNames();
    }

    partial void OnCurrentLayoutChanged(string value) => AutomationView = value == AutomationLayoutName;

    [RelayCommand]
    private void SaveLayout()
    {
        var name = string.IsNullOrWhiteSpace(NewLayoutName) ? CurrentLayout : NewLayoutName.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (Layout is null || name.Length == 0) return;
        try
        {
            Directory.CreateDirectory(StudioSettings.LayoutFolder);
            File.WriteAllText(Path.Combine(StudioSettings.LayoutFolder, name + ".json"), SerializeLayout(Layout));
            CurrentLayout = name;
            NewLayoutName = "";
            _settings.LastLayout = name;
            _settings.Save();
            RefreshLayoutNames();
            Hub.Status = $"indeling '{name}' opgeslagen";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Hub.Status = ex.Message; }
    }

    [RelayCommand]
    private void LoadLayout(string name)
    {
        var layout = LoadLayoutFile(name);
        if (layout is null && name == "Standaard") { ResetLayout(); CurrentLayout = name; return; }
        if (layout is null) { Hub.Status = $"indeling '{name}' niet te laden"; return; }
        CloseFloating();
        Layout = layout;
        CurrentLayout = name;
        _settings.LastLayout = name;
        _settings.Save();
    }

    public void SaveOnExit()
    {
        if (Layout is null) return;
        try
        {
            Directory.CreateDirectory(StudioSettings.LayoutFolder);
            File.WriteAllText(Path.Combine(StudioSettings.LayoutFolder, CurrentLayout + ".json"), SerializeLayout(Layout));
            _settings.LastLayout = CurrentLayout;
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Serialises the layout with only the Dock fields of each studio window (position, size, docking state), not the
    /// window contents: those come from the engine when the layout is opened again.
    /// </summary>
    private string SerializeLayout(IRootDock layout)
    {
        var json = Newtonsoft.Json.Linq.JObject.Parse(_serializer.Serialize(layout));
        var allowed = typeof(Dock.Model.Mvvm.Controls.Tool).GetProperties().Select(p => p.Name).ToHashSet();
        foreach (var obj in json.DescendantsAndSelf().OfType<Newtonsoft.Json.Linq.JObject>().ToList())
        {
            if (obj["$type"]?.ToString().StartsWith("ReaRadioDj.Studio.ViewModels.", StringComparison.Ordinal) != true) continue;
            foreach (var prop in obj.Properties().ToList())
                if (!prop.Name.StartsWith('$') && !allowed.Contains(prop.Name)) prop.Remove();
        }
        return json.ToString();
    }

    private IRootDock NewDefaultLayout()
    {
        var layout = _factory.CreateLayout();
        _factory.InitLayout(layout);
        return layout;
    }

    private IRootDock? LoadLayoutFile(string name)
    {
        var path = Path.Combine(StudioSettings.LayoutFolder, name + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            var layout = _serializer.Deserialize<IRootDock>(File.ReadAllText(path));
            if (layout is null) return null;
            _factory.InitLayout(layout);
            return layout;
        }
        catch (Exception) { return null; } // an old or damaged layout file: fall back to the default
    }

    private void CloseFloating()
    {
        if (Layout?.Windows is { } windows)
            foreach (var w in windows.ToList()) w.Exit();
    }

    private void RefreshLayoutNames()
    {
        LayoutNames.Clear();
        LayoutNames.Add("Standaard");
        if (Directory.Exists(StudioSettings.LayoutFolder))
            foreach (var f in Directory.EnumerateFiles(StudioSettings.LayoutFolder, "*.json").Select(Path.GetFileNameWithoutExtension).Order())
                if (f is not null && f != "Standaard") LayoutNames.Add(f);
    }
}

public sealed record WindowEntry(string Id, string Name);
