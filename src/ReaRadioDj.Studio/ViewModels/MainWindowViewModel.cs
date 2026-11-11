using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Threading;
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
        if (args.Contains("--dock-log")) StartDockLog();
        _settings = StudioSettings.Load();
        Appearance.Apply(_settings);
        int ea = Array.IndexOf(args, "--engine");
        if (ea >= 0 && ea + 1 < args.Length) _settings.SessionEngineUrl = args[ea + 1];
        if (args.Contains("--no-engine")) _settings.StartEngine = false;
        Hub = StudioHub.Start(_settings);
        Session = new SessionVm(Hub);
        if (_settings.StartEngine) _ = EnsureEngineAsync();
        Hub.StateReceived += OnState;
        Hub.StateReceived += CheckSetup;
        _forceSetup = args.Contains("--setup");   // --setup: open the setup helper at start (also for screenshots)
        // --check-updates: ask GitHub right away, whatever the last check was (for testing).
        _ = StartUpdateCheckAsync(args.Contains("--check-updates"));
        Hub.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(StudioHub.Status) or nameof(StudioHub.ConnectionText)) OnPropertyChanged(nameof(StatusLine)); };
        Hub.EditCuesRequested += id => ShowWindow("cues");
        Hub.TransitionRequested += _ => ShowWindow("transition");
        WorkbenchTool.OpenRequested += OpenOnWorkbench;
        KeepOldDefaultLayout();
        RefreshLayoutNames();
        _factory.Workspace = WorkspaceOf(_settings.LastLayout);
        Layout = LoadLayoutFile(_settings.LastLayout) ?? NewDefaultLayout();
        CurrentLayout = _settings.LastLayout;
        // --workspace live|dj|auto|prod|plan|report: start in that workspace.
        int wa = Array.IndexOf(args, "--workspace");
        if (wa >= 0 && wa + 1 < args.Length) SwitchView(args[wa + 1]);
        // --show <window id> (repeatable): bring windows to the front at start; --cues <media id>: open in the cue editor.
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--show")
            {
                // "--show settings:Systeem" opens a window on one of its tabs.
                var parts = args[i + 1].Split(':', 2);
                if (parts.Length == 2 && parts[0] == "mixer") MixerTool.StartChannel = parts[1];
                else if (parts.Length == 2) Views.SettingsView.StartTab = parts[1];
                ShowWindow(parts[0]);
            }
            else if (args[i] == "--float") FloatWindow(args[i + 1]);
            else if (args[i] == "--place-dialog") OpenPlace();
            // --place log:library:rechts:0.67 (target "*" = the whole main window), for testing Venster plaatsen
            else if (args[i] == "--place" && args[i + 1].Split(':') is [var pw, var pt, var ps, var share])
                Hub.Status = PlaceDockable(pw, pt == "*" ? WholeArea : pt, ps,
                    double.Parse(share, System.Globalization.CultureInfo.InvariantCulture)) ?? $"geplaatst: {pw}";
            else if (args[i] == "--workbench" && int.TryParse(args[i + 1], out var screen)) _startWorkbenches.Add(screen - 1);
        int ca = Array.IndexOf(args, "--cues");
        if (ca >= 0 && ca + 1 < args.Length && long.TryParse(args[ca + 1], out var cueId))
            _ = Task.Delay(1500).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => Hub.RequestCueEditor(cueId)));
    }

    public StudioHub Hub { get; }
    /// <summary>Signing in, signing out and the studio lock.</summary>
    public SessionVm Session { get; }
    public IReadOnlyList<WindowEntry> WindowMenu { get; } = DockFactory.Windows.Select(w => new WindowEntry(w.Id, w.Name)).ToList();

    // ---- Ctrl+K: go to a window by typing part of its name
    [ObservableProperty] private bool _goToOpen;
    [ObservableProperty] private string _goToQuery = "";
    [ObservableProperty] private WindowEntry? _goToSelected;
    public ObservableCollection<WindowEntry> GoToResults { get; } = [];

    partial void OnGoToQueryChanged(string value)
    {
        GoToResults.Clear();
        foreach (var w in Match(WindowMenu, value)) GoToResults.Add(w);
        GoToSelected = GoToResults.FirstOrDefault();
    }

    /// <summary>Windows whose name contains every typed word (any order, case does not matter); names that start with it first.</summary>
    public static IEnumerable<WindowEntry> Match(IEnumerable<WindowEntry> all, string query)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return all.Where(w => words.All(q => w.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || w.Id.Contains(q, StringComparison.OrdinalIgnoreCase)))
                  .OrderBy(w => words.Length > 0 && w.Name.StartsWith(words[0], StringComparison.OrdinalIgnoreCase) ? 0 : 1);
    }

    [RelayCommand]
    private void OpenGoTo()
    {
        GoToQuery = "";
        OnGoToQueryChanged("");
        GoToOpen = true;
    }

    [RelayCommand]
    private void GoTo(WindowEntry? entry)
    {
        entry ??= GoToSelected ?? GoToResults.FirstOrDefault();
        GoToOpen = false;
        if (entry is not null) ShowWindow(entry.Id);
    }
    // ---- Venster plaatsen: put a window exactly where you want it, without aiming at drop targets
    public const string WholeArea = "";
    [ObservableProperty] private bool _placeOpen;
    [ObservableProperty] private WindowEntry? _placeWindow;
    [ObservableProperty] private WindowEntry? _placeTarget;
    [ObservableProperty] private string _placeSide = "onder";
    [ObservableProperty] private string _placeSize = "1/2";
    [ObservableProperty] private string _placeResult = "";
    public ObservableCollection<WindowEntry> PlaceTargets { get; } = [];
    public IReadOnlyList<string> PlaceSides { get; } = ["links", "rechts", "boven", "onder", "als tabblad"];
    public IReadOnlyList<string> PlaceSizes { get; } = ["1/4", "1/3", "1/2", "2/3", "3/4"];
    public bool PlaceSizeUsed => PlaceSide != "als tabblad";
    partial void OnPlaceSideChanged(string value) => OnPropertyChanged(nameof(PlaceSizeUsed));

    [RelayCommand]
    private void OpenPlace()
    {
        PlaceTargets.Clear();
        PlaceTargets.Add(new WindowEntry(WholeArea, "het hele hoofdvenster"));
        foreach (var w in WindowMenu)
            if (Layout is not null && _factory.FindDockable(Layout, d => d.Id == w.Id) is not null) PlaceTargets.Add(w);
        PlaceTarget ??= PlaceTargets[0];
        if (PlaceTarget is { } t && !PlaceTargets.Contains(t)) PlaceTarget = PlaceTargets[0];
        PlaceWindow ??= WindowMenu.FirstOrDefault();
        PlaceResult = "";
        PlaceOpen = true;
    }

    [RelayCommand] private void ClosePlace() => PlaceOpen = false;

    [RelayCommand]
    private void Place()
    {
        if (PlaceWindow is not { } what || PlaceTarget is not { } where) { PlaceResult = "kies een venster en een plek"; return; }
        var error = PlaceDockable(what.Id, where.Id, PlaceSide, PlaceSize switch { "1/4" => 0.25, "1/3" => 1 / 3.0, "2/3" => 2 / 3.0, "3/4" => 0.75, _ => 0.5 });
        if (error is not null) { PlaceResult = error; return; }
        PlaceOpen = false;
        Hub.Status = $"{what.Name} staat nu {(PlaceSide == "als tabblad" ? "als tabblad bij" : PlaceSide + " van")} {where.Name}";
    }

    /// <summary>
    /// Moves a window (from wherever it is: docked, floating or closed) next to another window in the main window, or
    /// along an edge of the whole main window, taking <paramref name="share"/> of the space. Returns an error text or null.
    /// </summary>
    public string? PlaceDockable(string id, string targetId, string side, double share)
    {
        if (Layout is null) return "geen indeling";
        if (id == targetId) return "kies een ander venster als plek";
        IDock? target;
        if (targetId == WholeArea)
        {
            target = Layout.VisibleDockables?.OfType<IDock>().FirstOrDefault();
            if (target is null) return "het hoofdvenster is leeg";
        }
        else
        {
            if (_factory.FindDockable(Layout, d => d.Id == targetId) is not { Owner: IDock owner }) return "dat venster staat niet in het hoofdvenster";
            target = owner;
        }
        var dockable = FindAnywhere(d => d.Id == id);
        if (dockable is not null && ReferenceEquals(dockable.Owner, target) && side != "als tabblad" && target.VisibleDockables?.Count == 1)
            return "dit venster staat daar al alleen; kies een ander venster als plek";
        if (dockable is null) dockable = DockFactory.Create(id);
        else if (side == "als tabblad" && ReferenceEquals(dockable.Owner, target)) { _factory.SetActiveDockable(dockable); return null; }
        else _factory.RemoveDockable(dockable, true);
        CloseEmptyWindows();

        if (side == "als tabblad")
        {
            if (target is not IToolDock) return "als tabblad kan alleen bij een venster, niet bij het hele hoofdvenster";
            _factory.AddDockable(target, dockable);
            _factory.SetActiveDockable(dockable);
            _factory.SetFocusedDockable(target, dockable);
            return null;
        }
        var op = side switch { "links" => DockOperation.Left, "rechts" => DockOperation.Right, "boven" => DockOperation.Top, _ => DockOperation.Bottom };
        var dock = new Dock.Model.Mvvm.Controls.ToolDock
        {
            Id = "dock-" + id + "-" + Guid.NewGuid().ToString("N")[..6], Title = id, VisibleDockables = _factory.CreateList(dockable),
            ActiveDockable = dockable, Alignment = Alignment.Unset,
        };
        // the target may have been removed with the window (it was its last one): then use the whole main window
        if (target.Owner is null || !IsInLayout(target)) target = Layout.VisibleDockables?.OfType<IDock>().FirstOrDefault();
        if (target is null) return "geen plek gevonden";
        _factory.SplitToDock(target, dock, op);
        share = Math.Clamp(share, 0.1, 0.9);
        dock.Proportion = share;
        target.Proportion = 1 - share;
        _factory.SetActiveDockable(dockable);
        _factory.SetFocusedDockable(dock, dockable);
        return null;
    }

    /// <summary>
    /// --dock-log: the docking library writes why it accepts or refuses a drop (which target, which operation) to
    /// dock-log.txt in the settings folder, to find out why a window lands somewhere else than aimed at.
    /// </summary>
    private static void StartDockLog()
    {
        var path = Path.Combine(StudioSettings.Folder, "dock-log.txt");
        var gate = new object();
        string? last = null;
        try { Directory.CreateDirectory(StudioSettings.Folder); File.WriteAllText(path, $"dock-log {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}"); }
        catch (IOException) { return; }
        Dock.Settings.DockSettings.DiagnosticsLogHandler = message =>
        {
            lock (gate)
            {
                if (message == last) return;   // the same line on every mouse move while dragging
                last = message;
                try { File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); }
                catch (IOException) { }
            }
        };
        Dock.Settings.DockSettings.EnableDiagnosticsLogging = true;
    }

    private bool IsInLayout(IDockable d) => Layout is not null && (ReferenceEquals(d, Layout) || _factory.FindDockable(Layout, x => ReferenceEquals(x, d)) is not null);

    /// <summary>A floating window whose last studio window moved out is closed.</summary>
    private void CloseEmptyWindows()
    {
        foreach (var w in FloatingWindows.ToList())
            if (w.Layout is not { } l || _factory.FindDockable(l, d => d is StudioTool) is null) w.Exit();
    }

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
    [ObservableProperty] private string _lufsText = "– LUFS";
    [ObservableProperty] private string _lufsTip = "";
    [ObservableProperty] private double _levelRight;
    [ObservableProperty] private string _nextEvent = "";
    /// <summary>0-100: how much of the current item is left (the bar runs empty).</summary>
    [ObservableProperty] private double _nowLeft;
    /// <summary>normal, warn (last 20 s), end (last 10 s, flashes).</summary>
    [ObservableProperty] private string _endState = "normal";
    [ObservableProperty] private bool _flash;
    [ObservableProperty] private string _introText = "";
    /// <summary>Title and the line under it (artist, category, clock) of the item on air.</summary>
    [ObservableProperty] private string _nowTitle = "";
    [ObservableProperty] private string _nowSub = "";
    /// <summary>0-100: how far the item on air is; the intro and mix marks are -1 when there is none.</summary>
    [ObservableProperty] private double _nowPos;
    [ObservableProperty] private double _introMark = -1;
    [ObservableProperty] private double _mixMark = -1;
    [ObservableProperty] private string _introState = "";
    [ObservableProperty] private string _mixText = "";
    [ObservableProperty] private string _stopHint = "";

    public const string AutomationLayoutName = "Automatisering";
    public const string AssistLayoutName = "Standaard";
    public const string DjLayoutName = "DJ";

    /// <summary>Each workspace (button in the app bar) and the name its arrangement is saved under.</summary>
    private static readonly (string Key, string Layout)[] Workspaces =
    [
        ("live", AssistLayoutName), ("dj", DjLayoutName), ("auto", AutomationLayoutName),
        ("prod", "Productie"), ("plan", "Planning"), ("report", "Rapportage"),
    ];

    /// <summary>The workspace a layout belongs to; a layout saved under another name counts as Live.</summary>
    private static string WorkspaceOf(string layout) => Workspaces.FirstOrDefault(w => w.Layout == layout).Key ?? "live";

    [ObservableProperty] private string _workspace = "live";
    public bool LiveWorkspace => Workspace == "live";
    public bool DjWorkspace => Workspace == "dj";
    public bool AutomationView => Workspace == "auto";
    public bool ProductionWorkspace => Workspace == "prod";
    public bool PlanningWorkspace => Workspace == "plan";
    public bool ReportWorkspace => Workspace == "report";

    partial void OnWorkspaceChanged(string value)
    {
        foreach (var name in new[] { nameof(LiveWorkspace), nameof(DjWorkspace), nameof(AutomationView),
                                     nameof(ProductionWorkspace), nameof(PlanningWorkspace), nameof(ReportWorkspace) })
            OnPropertyChanged(name);
    }

    /// <summary>
    /// Since the workspaces Productie, Planning and Rapportage the Live default holds only what a presenter needs.
    /// A Standaard layout saved before that is kept as "Standaard (oud)", so nothing someone arranged is lost.
    /// </summary>
    private void KeepOldDefaultLayout()
    {
        if (_settings.LayoutVersion >= 2) return;
        try
        {
            var old = Path.Combine(StudioSettings.LayoutFolder, AssistLayoutName + ".json");
            var kept = Path.Combine(StudioSettings.LayoutFolder, AssistLayoutName + " (oud).json");
            if (File.Exists(old) && !File.Exists(kept)) File.Move(old, kept);
            else if (File.Exists(old)) File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _settings.LayoutVersion = 2;
        _settings.Save();
    }

    public bool StationDiffers => !string.Equals(Station, "REA-Radio-DJ", StringComparison.OrdinalIgnoreCase);
    partial void OnStationChanged(string value) => OnPropertyChanged(nameof(StationDiffers));
    public string ModeText => AutoPlay ? "AUTOMATISCH" : "LIVE-ASSIST";
    public string StatusLine => string.IsNullOrEmpty(Hub.Status) ? Hub.ConnectionText : $"{Hub.ConnectionText} · {Hub.Status}";

    partial void OnAutoPlayChanged(bool value) => OnPropertyChanged(nameof(ModeText));

    /// <summary>The layout picker in the app bar: choosing a name loads that layout.</summary>
    public string? SelectedLayoutName
    {
        get => CurrentLayout;
        set
        {
            if (string.IsNullOrEmpty(value) || value == CurrentLayout) return;
            // A workspace switches the way its button does (it may not have been saved yet); other names are saved layouts.
            if (Workspaces.Any(w => w.Layout == value)) SwitchView(WorkspaceOf(value));
            else LoadLayout(value);
        }
    }

    /// <summary>"auto" or "assist": the two halves of the mode switch.</summary>
    [RelayCommand]
    private Task SetMode(string? mode) => (mode == "auto") == AutoPlay
        ? Task.CompletedTask
        : Hub.Client.PostAsync("/api/transport/autoplay", new SwitchRequest(mode == "auto"));

    // ------------------------------------------------------------ newer version

    /// <summary>Shown in the status bar when GitHub has a newer release; a click opens its page.</summary>
    [ObservableProperty] private string _updateText = "";
    private string _updateUrl = UpdateChecker.Repo + "/releases";
    public bool HasUpdate => UpdateText.Length > 0;
    partial void OnUpdateTextChanged(string value) => OnPropertyChanged(nameof(HasUpdate));

    /// <summary>At start: the notice from the last check, and a new check when the last one is a day old.</summary>
    private async Task StartUpdateCheckAsync(bool force)
    {
        ShowRelease(_settings.LatestTag, _settings.LatestUrl);
        if (!force && (!_settings.CheckUpdates || _settings.LastUpdateCheck is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(20))) return;
        await Task.Delay(force ? 3000 : 20000);   // let the studio settle first
        await CheckNowAsync(force);
    }

    /// <summary>Info › Zoeken naar updates: asks GitHub now and says what came back.</summary>
    [RelayCommand]
    private Task CheckUpdates() => CheckNowAsync(true);

    private async Task CheckNowAsync(bool report)
    {
        if (report) Hub.Status = "Zoeken naar een nieuwere versie…";
        var (release, error) = await UpdateChecker.CheckAsync();
        if (release is null)
        {
            if (report) Hub.Status = "Zoeken naar updates lukte niet: " + error;
            return;
        }
        _settings.LastUpdateCheck = DateTime.UtcNow;
        _settings.LatestTag = release.Tag;
        _settings.LatestUrl = release.Url;
        _settings.Save();
        var newer = ShowRelease(release.Tag, release.Url);
        if (report)
            Hub.Status = newer
                ? $"Versie {release.Version} is uit (je hebt {UpdateChecker.CurrentVersion})."
                : $"Je hebt de nieuwste versie ({UpdateChecker.CurrentVersion}; nieuwste uitgave {release.Version}).";
    }

    private bool ShowRelease(string? tag, string? url)
    {
        var latest = ReleaseVersion.Parse(tag);
        var current = ReleaseVersion.Parse(UpdateChecker.CurrentVersion);
        var newer = latest is not null && current is not null && latest > current;
        if (newer && url is not null && url.StartsWith(UpdateChecker.Repo, StringComparison.OrdinalIgnoreCase)) _updateUrl = url;
        UpdateText = newer ? $"Nieuwe versie {latest} beschikbaar" : "";
        return newer;
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_updateUrl) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    // ------------------------------------------------------------ setup helper

    public SetupWizardVm Setup { get; } = new();
    private readonly bool _forceSetup;
    private bool _setupChecked;

    [RelayCommand] private Task OpenSetup() => Setup.OpenAsync();

    /// <summary>Once, at the first state from the engine: open the setup helper on a new installation (nothing in the library yet).</summary>
    private void CheckSetup(StateDto _)
    {
        if (_setupChecked) return;
        _setupChecked = true;
        Hub.StateReceived -= CheckSetup;
        Dispatcher.UIThread.Post(async () =>
        {
            if (_forceSetup)
            {
                await Setup.OpenAsync();
                // --setup-step <1-4>: show that step (for screenshots without a mouse)
                var a = Environment.GetCommandLineArgs();
                int si = Array.IndexOf(a, "--setup-step");
                if (si >= 0 && si + 1 < a.Length && int.TryParse(a[si + 1], out int step))
                    Setup.Step = Math.Clamp(step - 1, 0, SetupWizardVm.Steps - 1);
                return;
            }
            if (_settings.SetupDone) return;
            var folders = await Hub.Client.GetAsync<List<LibraryFolderDto>>("/api/library/folders");
            if (folders is null) return;
            bool empty = folders.All(f => f.Fixed) && folders.All(f => (f.Files ?? 0) == 0);
            if (empty) await Setup.OpenAsync();
            else { _settings.SetupDone = true; _settings.Save(); }   // an existing station: never ask
        });
    }

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
        LufsText = s.Mixer.Lufs is double lufs ? $"{lufs:0.0} LUFS" : "– LUFS";
        LufsTip = $"Loudness van de zender (EBU R128). Kort (3 s): {s.Mixer.Lufs?.ToString("0.0") ?? "–"} LUFS · "
            + $"geïntegreerd: {s.Mixer.LufsIntegrated?.ToString("0.0") ?? "–"} LUFS · true peak: {s.Mixer.TruePeakDb?.ToString("0.0") ?? "–"} dBTP · "
            + (s.Mixer.Limiter ? "begrenzer aan" : "begrenzer uit");
        LevelRight = s.Mixer.ProgramRight * 100;
        if (s.Engine.Current is { } c)
        {
            NowLabel = c.Label;
            double left = Math.Max(0, c.To - c.Position), length = Math.Max(0.1, c.To - c.From);
            NowTitle = string.IsNullOrWhiteSpace(c.Title) ? c.Label : c.Title;
            NowSub = string.Join(" \u00b7 ", new[] { c.Artist, c.Category, s.Clock is null ? "" : $"klok {s.Clock}" }.Where(t => !string.IsNullOrWhiteSpace(t)));
            double Mark(double at) => Math.Clamp((at - c.From) / length * 100, 0, 100);
            NowPos = Mark(c.Position);
            IntroMark = c.Intro is double i && i > c.From ? Mark(i) : -1;
            MixMark = c.MixAt > c.From && c.MixAt < c.To ? Mark(c.MixAt) : -1;
            IntroState = c.Intro is not double end ? "" : c.Position < end ? StudioHub.Time(end - c.Position) : "voorbij";
            MixText = c.MixAt > c.Position ? StudioHub.Time(c.MixAt - c.Position) : "";
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
            NowTitle = NowLabel; NowSub = s.Clock is null ? "" : $"klok {s.Clock}";
            NowPos = 0; IntroMark = -1; MixMark = -1; IntroState = ""; MixText = "";
        }
        // Overlays play on top of everything; show them in the on-air bar so a stray one is never hidden in a tab.
        OverlayOn = s.Overlays.Count > 0;
        OverlayText = s.Overlays.Count switch
        {
            0 => "",
            1 => $"{s.Overlays[0].Label}  -{StudioHub.Time(s.Overlays[0].Length - s.Overlays[0].Position)}",
            int n2 => $"{n2} overlays · {s.Overlays[0].Label}",
        };
        OverlayTip = "Overlay speelt over het programma: " + string.Join(", ", s.Overlays.Select(o => o.Label)) + ". Klik om alle overlays te stoppen.";
        NextLabel = s.Upcoming.FirstOrDefault() is { } n ? $"Straks: {n.Label}" : "";
        NextEvent = s.Events.FirstOrDefault() is { } e ? $"{e.Name} over {StudioHub.Time((e.At - s.Time).TotalSeconds)}" : "";
    }

    // ------------------------------------------------------------ transport (hotkeys in the window)

    [RelayCommand] private Task Next() => Hub.Client.PostAsync("/api/transport/next");
    [RelayCommand] private Task Skip() => Hub.Client.PostAsync("/api/transport/skip");
    [RelayCommand] private Task Stop() => Hub.Client.PostAsync("/api/transport/stop");
    [RelayCommand] private Task ToggleAuto() => Hub.Client.PostAsync("/api/transport/autoplay", new SwitchRequest(!AutoPlay));
    [RelayCommand] private Task ToggleMic() => Hub.Client.PostAsync("/api/mixer/mic/toggle");
    [RelayCommand] private Task AddBreak() => Hub.Client.PostAsync("/api/log/break", new AddBreakRequest(0));
    [RelayCommand] private Task StopCarts() => Hub.Client.PostAsync("/api/carts/stop");
    [RelayCommand] private Task StopOverlays() => Hub.Client.PostAsync("/api/overlays/stop");

    [ObservableProperty] private bool _overlayOn;
    [ObservableProperty] private string _overlayText = "";
    [ObservableProperty] private string _overlayTip = "";

    [RelayCommand]
    private Task PflToggle() => Hub.PlayerCommand("pfl", "toggle");

    /// <summary>Hotkeys: any engine command; errors show in the status line.</summary>
    [RelayCommand]
    private async Task RunEngineCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (Session.Blocks) { Hub.Status = "De studio is vergrendeld: sneltoetsen werken na het ontgrendelen."; return; }
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
    public void ShowWindow(string id)
    {
        if (Layout is null) return;
        var existing = FindAnywhere(d => d.Id == id);
        if (existing is not null)
        {
            _factory.SetActiveDockable(existing);
            if (existing.Owner is IDock owner) _factory.SetFocusedDockable(owner, existing);
            WindowOf(existing)?.SetActive();
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
        if (FindAnywhere(d => d.Id == id) is { } d) _factory.FloatDockable(d);
        else { var tool = DockFactory.Create(id); _factory.FloatDockable(tool); }
    }

    [RelayCommand]
    private void ResetLayout()
    {
        CloseFloating();
        _factory.Workspace = WorkspaceOf(CurrentLayout);
        Layout = NewDefaultLayout();
        Hub.Status = "standaardindeling hersteld";
    }

    /// <summary>
    /// One click to another workspace: Live, DJ, Automatisering, Productie, Planning or Rapportage. Each remembers its
    /// own arrangement (saved as a layout under its own name). Without a workspace it toggles automation and Live.
    /// </summary>
    [RelayCommand]
    private void SwitchView(string? view)
    {
        string key = view switch
        {
            null => AutomationView ? "live" : "auto",
            "assist" => "live",
            _ when Workspaces.Any(w => w.Key == view) => view,
            _ => "live",
        };
        string target = Workspaces.First(w => w.Key == key).Layout;
        if (target == CurrentLayout) return;
        SaveOnExit();
        CloseFloating();
        _factory.Workspace = key;
        // Into the DJ workspace: deck A shows what is on air (when it is free), ready to take over.
        if (key == "dj") _ = Hub.Client.PostQuietAsync("/api/dj/follow");
        Layout = LoadLayoutFile(target) ?? NewDefaultLayout();
        RestoreWindowsSoon();
        CurrentLayout = target;
        _settings.LastLayout = target;
        _settings.Save();
        RefreshLayoutNames();
    }

    partial void OnCurrentLayoutChanged(string value)
    {
        Workspace = WorkspaceOf(value);
        OnPropertyChanged(nameof(SelectedLayoutName));
    }

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
        RestoreWindowsSoon();
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
        CaptureWindows();
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
            RenameOldTitles(layout);
            _factory.InitLayout(layout);
            return layout;
        }
        catch (Exception) { return null; } // an old or damaged layout file: fall back to the default
    }

    /// <summary>Tab titles that changed since a layout was saved.</summary>
    private static readonly Dictionary<string, string> RenamedTitles = new() { ["Stem (Higgs TTS 3)"] = "Stemmen" };

    private static void RenameOldTitles(IDockable dockable)
    {
        if (dockable.Title is { } t && RenamedTitles.TryGetValue(t, out var now)) dockable.Title = now;
        if (dockable is IDock { VisibleDockables: { } children })
            foreach (var c in children) RenameOldTitles(c);
        if (dockable is IRootDock { Windows: { } windows })
            foreach (var w in windows)
                if (w.Layout is { } l) RenameOldTitles(l);
    }

    // ------------------------------------------------------------ workbenches

    /// <summary>"Nieuw werkblad op scherm n": one entry per screen, left to right.</summary>
    public ObservableCollection<ScreenEntry> ScreenMenu { get; } = [];

    /// <summary>The screens, from the main window (set when it opens).</summary>
    public Func<IReadOnlyList<Screen>>? ScreenSource { get; set; }

    private readonly List<int> _startWorkbenches = [];

    private IReadOnlyList<Screen> SortedScreens() =>
        [.. (ScreenSource?.Invoke() ?? []).OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y)];

    /// <summary>Called by the main window once it is shown.</summary>
    public void OnWindowOpened()
    {
        RefreshScreens();
        RestoreWindowsSoon();
        foreach (var screen in _startWorkbenches) NewWorkbench(ScreenMenu.ElementAtOrDefault(screen));
        _startWorkbenches.Clear();
    }

    public void RefreshScreens()
    {
        ScreenMenu.Clear();
        var screens = SortedScreens();
        for (int i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            ScreenMenu.Add(new ScreenEntry(i, $"Nieuw werkblad op scherm {i + 1} ({s.Bounds.Width} × {s.Bounds.Height}{(s.IsPrimary ? ", hoofdscherm" : "")})"));
        }
    }

    private IEnumerable<IDockWindow> FloatingWindows => Layout?.Windows ?? (IEnumerable<IDockWindow>)[];

    /// <summary>Finds a window in the main window or on any workbench / floating window.</summary>
    private IDockable? FindAnywhere(Func<IDockable, bool> match)
    {
        if (Layout is null) return null;
        if (_factory.FindDockable(Layout, match) is { } found) return found;
        foreach (var w in FloatingWindows)
            if (w.Layout is { } l && _factory.FindDockable(l, match) is { } f) return f;
        return null;
    }

    private IDockWindow? WindowOf(IDockable dockable) =>
        FloatingWindows.FirstOrDefault(w => w.Layout is { } l && _factory.FindDockable(l, d => ReferenceEquals(d, dockable)) is not null);

    /// <summary>
    /// Opens a workbench: an extra studio window with its own docking area, maximised on the chosen screen. It is
    /// saved with the layout and comes back on the same screen.
    /// </summary>
    [RelayCommand]
    private void NewWorkbench(ScreenEntry? entry)
    {
        if (Layout is null) return;
        int n = 2;
        while (FindAnywhere(d => d.Id == $"werkblad{n}") is not null) n++;
        var tool = new WorkbenchTool { Id = $"werkblad{n}", Title = $"Werkblad {n}" };
        var dock = new Dock.Model.Mvvm.Controls.ToolDock
        {
            Id = $"werkblad{n}-dock", Title = $"Werkblad {n}", VisibleDockables = _factory.CreateList<IDockable>(tool),
            ActiveDockable = tool, Alignment = Alignment.Unset,
        };
        var window = _factory.CreateWindowFrom(dock, new DockWindowOptions { OwnerMode = DockWindowOwnerMode.None, ShowInTaskbar = true });
        if (window is null) { Hub.Status = "werkblad niet te openen"; return; }
        window.Title = $"Werkblad {n} – REA-Radio-DJ";
        var screens = SortedScreens();
        var screen = entry is not null && entry.Index < screens.Count ? screens[entry.Index] : screens.FirstOrDefault(s => s.IsPrimary);
        if (screen is not null) PlaceOnScreen(window, screen, maximize: false);
        else { window.X = 100; window.Y = 100; window.Width = 1200; window.Height = 800; }
        _factory.AddWindow(Layout, window);
        window.Present(false);
        if (screen is not null) PlaceOnScreen(window, screen, maximize: true);
        Hub.Status = $"Werkblad {n} geopend{(entry is null ? "" : $" op scherm {entry.Index + 1}")}: sleep er vensters heen of kies ze op het werkblad";
    }

    [RelayCommand]
    private void CloseWorkbenches()
    {
        CloseFloating();
        Hub.Status = "werkbladen en zwevende vensters gesloten; de vensters staan in het menu Vensters";
    }

    private void OpenOnWorkbench(WorkbenchTool bench, string id)
    {
        if (bench.Owner is not IDock target) return;
        var d = FindAnywhere(x => x.Id == id);
        if (d is not null && ReferenceEquals(d.Owner, target)) { _factory.SetActiveDockable(d); return; }
        if (d is null) d = DockFactory.Create(id);
        else _factory.RemoveDockable(d, true);
        _factory.AddDockable(target, d);
        _factory.SetActiveDockable(d);
        _factory.SetFocusedDockable(target, d);
    }

    private static void PlaceOnScreen(IDockWindow w, Screen s, bool maximize)
    {
        var a = s.WorkingArea;
        double k = s.Scaling <= 0 ? 1 : s.Scaling;
        w.X = a.X + 40; w.Y = a.Y + 40;
        w.Width = Math.Max(400, a.Width / k - 80); w.Height = Math.Max(300, a.Height / k - 80);
        if (w.Host is { } h)
        {
            h.SetWindowState(DockWindowState.Normal);
            h.SetPosition(w.X, w.Y);
            h.SetSize(w.Width, w.Height);
            if (maximize) h.SetWindowState(DockWindowState.Maximized);
        }
        w.WindowState = maximize ? DockWindowState.Maximized : DockWindowState.Normal;
    }

    /// <summary>Copies where each workbench is (screen, size, maximised) into the layout before it is saved.</summary>
    private void CaptureWindows()
    {
        foreach (var w in FloatingWindows)
        {
            if (w.Host is not { } h) continue;
            var state = h.GetWindowState();
            if (state == DockWindowState.Normal) w.Save();
            else { h.GetPosition(out var x, out var y); w.X = x; w.Y = y; }   // maximised: keep its normal size, note the screen
            w.WindowState = state == DockWindowState.Minimized ? DockWindowState.Normal : state;
        }
    }

    /// <summary>After a layout opens: workbenches go back to their screen (or the main screen when it is gone) and size.</summary>
    private void RestoreWindowsSoon() => DispatcherTimer.RunOnce(RestoreWindows, TimeSpan.FromMilliseconds(400));

    private void RestoreWindows()
    {
        var screens = SortedScreens();
        if (screens.Count == 0) return;
        foreach (var w in FloatingWindows.ToList())
        {
            if (w.Host is not { } h) continue;
            bool maximize = w.WindowState is DockWindowState.Maximized or DockWindowState.FullScreen;
            var probe = new PixelPoint((int)w.X + 100, (int)w.Y + 50);
            if (!screens.Any(s => s.WorkingArea.Contains(probe)))
                PlaceOnScreen(w, screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0], maximize);
            else if (maximize) h.SetWindowState(DockWindowState.Maximized);
        }
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
        // The workspaces are always in the list, also before they were saved for the first time.
        foreach (var (_, workspace) in Workspaces)
            if (!LayoutNames.Contains(workspace)) LayoutNames.Add(workspace);
        OnPropertyChanged(nameof(SelectedLayoutName));
    }
}

public sealed record WindowEntry(string Id, string Name);

public sealed record ScreenEntry(int Index, string Name);
