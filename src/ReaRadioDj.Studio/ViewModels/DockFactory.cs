using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>All studio windows and the default arrangement.</summary>
public sealed class DockFactory : Factory
{
    /// <summary>The program icon, also for floating windows (each is a window of its own in the taskbar).</summary>
    public static readonly Lazy<Avalonia.Controls.WindowIcon> AppIcon =
        new(() => new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://REA-Radio-DJ-Studio/Assets/rea.ico"))));

    /// <summary>Every window the studio has: id, menu name, constructor.</summary>
    public static readonly IReadOnlyList<(string Id, string Name, Func<StudioTool> Create)> Windows =
    [
        ("decks", "Live-decks (A · B · C)", () => new DeckTool()),
        ("log", "Programmalog", () => new LogTool()),
        ("standby", "Standby-spelers", () => new StandbyTool()),
        ("pfl", "PFL / voorbeluisteren", () => new PflTool()),
        ("cart1", "Cartwall 1", () => new CartwallTool { Id = "cart1", Title = "Cartwall 1" }),
        ("cart2", "Cartwall 2", () => new CartwallTool { Id = "cart2", Title = "Cartwall 2" }),
        ("library", "Bibliotheek", () => new LibraryTool()),
        ("cues", "Cue-editor", () => new CueEditorTool()),
        ("mixer", "Mixer en routing", () => new MixerTool()),
        ("planner", "Planner", () => new PlannerTool()),
        ("asrun", "As-run", () => new AsRunTool()),
        ("wishlist", "Wensenlijst (Spotify)", () => new WishlistTool()),
        ("clock", "Tijd", () => new ClockTool()),
        ("presenter", "AI-presentator", () => new PresenterTool()),
        ("overlays", "Overlays", () => new OverlaysTool()),
        ("stream", "Stream", () => new StreamTool()),
        ("messages", "Berichten", () => new MessagesTool()),
        ("control", "Bediening en sneltoetsen", () => new ControlTool()),
        ("voice", "Stemmen", () => new VoiceTool()),
        ("voicetrack", "Voicetracking", () => new VoiceTrackTool()),
        ("transition", "Overgangen", () => new TransitionTool()),
    ];

    public static StudioTool Create(string id) => Windows.First(w => w.Id == id).Create();

    /// <summary>True = the calm automation view (log, time, levels, messages); false = the full live-assist studio.</summary>
    public bool AutomationView { get; set; }

    public override IRootDock CreateLayout()
    {
        ToolDock Tools(string id, params string[] ids)
        {
            var tools = ids.Select(i => (IDockable)Create(i)).ToArray();
            var dock = new ToolDock { Id = id, Title = id, VisibleDockables = CreateList(tools), ActiveDockable = tools[0], Alignment = Alignment.Unset };
            return dock;
        }

        if (AutomationView)
        {
            var log = new ProportionalDock
            {
                Id = "a-left", Proportion = 0.55, Orientation = Orientation.Vertical,
                VisibleDockables = CreateList<IDockable>(Tools("dock-log", "log", "asrun")),
            };
            var status = new ProportionalDock
            {
                Id = "a-right", Proportion = 0.45, Orientation = Orientation.Vertical,
                VisibleDockables = CreateList<IDockable>(
                    Tools("dock-clock", "clock"),
                    new ProportionalDockSplitter(),
                    Tools("dock-status", "messages", "presenter", "stream", "mixer")),
            };
            return Root(new ProportionalDock
            {
                Id = "main", Orientation = Orientation.Horizontal,
                VisibleDockables = CreateList<IDockable>(log, new ProportionalDockSplitter(), status),
            });
        }

        var left = new ProportionalDock
        {
            Id = "left", Proportion = 0.27, Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                Tools("dock-library", "library", "planner"),
                new ProportionalDockSplitter(),
                Tools("dock-players", "standby", "pfl")),
        };
        var center = new ProportionalDock
        {
            Id = "center", Proportion = 0.43, Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                Tools("dock-decks", "decks"),
                new ProportionalDockSplitter(),
                Tools("dock-log", "log", "asrun"),
                new ProportionalDockSplitter(),
                Tools("dock-editor", "cues", "transition", "presenter", "voice", "messages", "control")),
        };
        var right = new ProportionalDock
        {
            Id = "right", Proportion = 0.30, Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                Tools("dock-carts", "cart1", "cart2"),
                new ProportionalDockSplitter(),
                Tools("dock-mixer", "mixer", "clock", "overlays", "stream")),
        };
        var main = new ProportionalDock
        {
            Id = "main", Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(left, new ProportionalDockSplitter(), center, new ProportionalDockSplitter(), right),
        };

        return Root(main);
    }

    private IRootDock Root(ProportionalDock main)
    {
        var root = CreateRootDock();
        root.Id = "root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(main);
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow { Icon = AppIcon.Value },
        };
        base.InitLayout(layout);
    }
}
