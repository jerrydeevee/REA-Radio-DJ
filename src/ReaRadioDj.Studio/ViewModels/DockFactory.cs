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
        ("dj", "DJ-mengtafel", () => new DjTool()),
        ("log", "Programmalog", () => new LogTool()),
        ("standby", "Standby-spelers", () => new StandbyTool()),
        ("pfl", "PFL", () => new PflTool()),
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
        ("news", "Nieuws", () => new NewsTool()),
        ("ads", "Reclame", () => new AdsTool()),
        ("import", "Log-import", () => new ImportTool()),
        ("sources", "Databronnen", () => new SourcesTool()),
        ("hardware", "Hardware", () => new HardwareTool()),
        ("buttons", "Eigen knoppen", () => new ButtonsTool()),
        ("item", "Itemgegevens", () => new ItemTool()),
        ("explorer", "Verkenner", () => new ExplorerTool()),
        ("regie", "Regiekamer", () => new ControlRoomTool()),
        ("settings", "Instellingen", () => new SettingsTool()),
    ];

    public static StudioTool Create(string id) => Windows.First(w => w.Id == id).Create();

    /// <summary>
    /// The workspace whose default arrangement CreateLayout builds: live (presenting), auto (calm automation view),
    /// dj (the DJ desk), prod (production: library, cues, voice tracks), plan (planning: clocks, ads, news, import)
    /// or report (as-run, music report, stream and technical windows).
    /// </summary>
    public string Workspace { get; set; } = "live";

    public override IRootDock CreateLayout()
    {
        ToolDock Tools(string id, params string[] ids)
        {
            var tools = ids.Select(i => (IDockable)Create(i)).ToArray();
            var dock = new ToolDock { Id = id, Title = id, VisibleDockables = CreateList(tools), ActiveDockable = tools[0], Alignment = Alignment.Unset };
            return dock;
        }

        ProportionalDock Column(string id, double proportion, params IDockable[] parts) => new()
        {
            Id = id, Proportion = proportion, Orientation = Orientation.Vertical, VisibleDockables = CreateList(Between(parts)),
        };
        ProportionalDock Row(string id, params IDockable[] parts) => new()
        {
            Id = id, Orientation = Orientation.Horizontal, VisibleDockables = CreateList(Between(parts)),
        };
        static IDockable[] Between(IDockable[] parts) =>
            [.. parts.SelectMany((p, i) => i == 0 ? new[] { p } : new IDockable[] { new ProportionalDockSplitter(), p })];
        ToolDock Sized(double proportion, string id, params string[] ids)
        {
            var dock = Tools(id, ids);
            dock.Proportion = proportion;
            return dock;
        }

        if (Workspace == "prod")
            // Production: a big library, the cue editor and the voice windows; PFL to listen without going on air.
            return Root(Row("main",
                Column("p-left", 0.34, Tools("dock-library", "library", "explorer", "wishlist")),
                Column("p-center", 0.42, Sized(0.55, "dock-editor", "cues"), Sized(0.45, "dock-voice", "voicetrack", "voice", "presenter", "transition", "news")),
                Column("p-right", 0.24, Sized(0.5, "dock-pfl", "pfl", "item", "standby"), Sized(0.5, "dock-side", "overlays", "messages"))));

        if (Workspace == "plan")
            // Planning: clocks and the day plan wide, with ads, news and import beside it and the log to check the result.
            return Root(Row("main",
                Column("pl-left", 0.52, Tools("dock-planner", "planner", "sources")),
                Column("pl-right", 0.48, Sized(0.62, "dock-plan-side", "ads", "news", "import"), Sized(0.38, "dock-log", "log", "library"))));

        if (Workspace == "report")
            // Reports and technique: what went out, the music report, the stream, hardware and control.
            return Root(Row("main",
                Column("r-left", 0.58, Tools("dock-asrun", "asrun", "log")),
                Column("r-right", 0.42, Sized(0.6, "dock-tech", "stream", "hardware", "control"), Sized(0.4, "dock-status", "regie", "messages", "clock"))));

        if (Workspace == "dj")
        {
            var desk = new ProportionalDock
            {
                Id = "dj-top", Proportion = 0.74, Orientation = Orientation.Horizontal,
                VisibleDockables = CreateList<IDockable>(Tools("dock-dj", "dj")),
            };
            var under = new ProportionalDock
            {
                Id = "dj-bottom", Proportion = 0.26, Orientation = Orientation.Horizontal,
                VisibleDockables = CreateList<IDockable>(
                    Tools("dock-library", "library", "log", "asrun"),
                    new ProportionalDockSplitter(),
                    Tools("dock-side", "cart1", "cart2", "mixer", "pfl", "clock", "messages")),
            };
            return Root(new ProportionalDock
            {
                Id = "main", Orientation = Orientation.Vertical,
                VisibleDockables = CreateList<IDockable>(desk, new ProportionalDockSplitter(), under),
            });
        }

        if (Workspace == "auto")
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
                Tools("dock-library", "library", "wishlist"),
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
                Tools("dock-editor", "cues", "item", "messages", "presenter")),
        };
        var right = new ProportionalDock
        {
            Id = "right", Proportion = 0.30, Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                Tools("dock-carts", "cart1", "cart2"),
                new ProportionalDockSplitter(),
                Tools("dock-mixer", "mixer", "buttons", "clock", "overlays")),
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
            [nameof(IDockWindow)] = () => new Controls.ScaledHostWindow { Icon = AppIcon.Value },
        };
        base.InitLayout(layout);
    }
}
