using Avalonia;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace ReaRadioDj.Studio.Services;

/// <summary>
/// How the studio looks on this computer: colour style, corner shape and the size of everything. Applied at once to
/// every open window (the colours are dynamic resources; the size is a layout scale on each window).
/// </summary>
public static class Appearance
{
    public static readonly (string Key, string Name)[] Themes =
    [
        ("regiekamer", "Regiekamer (rustig donker, standaard)"), ("daglicht", "Daglicht (licht, voor overdag)"),
        ("dark", "Studio donker"), ("night", "Nacht (gedimd, voor een donkere studio)"),
        ("light", "Licht (klassiek)"), ("contrast", "Hoog contrast"),
        ("ocean", "Oceaan (diep blauw met turkoois)"), ("forest", "Bos (donkergroen)"), ("sunset", "Zonsondergang (warm rood en oranje)"),
        ("graphite", "Grafiet (neutraal grijs, zonder kleur)"), ("neon", "Neon (paars en roze, voor de avond)"),
    ];

    public static readonly (string Key, string Name)[] Corners =
        [("square", "Recht"), ("normal", "Normaal"), ("round", "Rond"), ("pill", "Extra rond (pilvormige knoppen)")];

    public static readonly (string Key, string Name)[] ButtonStyles =
        [("flat", "Vlak"), ("raised", "Reliëf (met verloop en diepte)"), ("outline", "Omlijnd (alleen een rand)")];

    public static readonly (string Key, string Name)[] WaveStyles =
    [
        ("classic", "Klassiek"), ("gradient", "Verloop met spiegeling"), ("bars", "Staafjes"),
        ("neon", "Neon-gloed"), ("energy", "Energie (kleur loopt op met het volume)"),
    ];

    /// <summary>How waveforms are drawn (one of <see cref="WaveStyles"/>).</summary>
    public static string WaveStyle { get; private set; } = "classic";

    /// <summary>The waveform colour of the cue editor and players.</summary>
    public static Color WaveColor { get; private set; } = Color.Parse("#3FA7FF");

    /// <summary>Raised after every change of the look; drawn controls (waveforms) redraw.</summary>
    public static event Action? LookChanged;

    public static readonly int[] Scales = [80, 90, 100, 110, 125, 150];

    /// <summary>The size of everything: 1 = as designed.</summary>
    public static double Scale { get; private set; } = 1;

    /// <summary>Raised when the size changed; windows rescale themselves.</summary>
    public static event Action? ScaleChanged;

    // The named colours every view uses, per style.
    private static readonly string[] Keys =
        ["OnAirBrush", "AccentBrush", "PrimaryBrush", "WarnBrush", "OkBrush", "TextBrush", "AppBrush", "BarBrush",
         "ControlBrush", "ControlBorderBrush", "SelectedBrush", "MutedTextBrush", "CardBrush", "CardBorderBrush", "CartEmptyBrush"];

    private static readonly Dictionary<string, string[]> Palettes = new()
    {
        // Regiekamer and Daglicht share one layout: red only for live and faults, green plays, blue is next and
        // anything you can press, amber is "watch out".
        ["regiekamer"] = ["#EF4444", "#3B82F6", "#2563EB", "#F59E0B", "#22C55E", "#E5E7EB", "#0F1117", "#161A22",
                          "#252B37", "#2E3542", "#1E3A5F", "#9CA3AF", "#1C212B", "#2E3542", "#252B37"],
        ["daglicht"] = ["#DC2626", "#2563EB", "#1D4ED8", "#B45309", "#15803D", "#0F172A", "#F6F7F9", "#FFFFFF",
                        "#EEF1F4", "#CBD5E1", "#DBEAFE", "#475569", "#FFFFFF", "#DDE2E8", "#64748B"],
        ["dark"] = ["#E5484D", "#4C8DFF", "#2563EB", "#F5A524", "#3FB950", "#E6EAF0", "#0E1116", "#11161D",
                    "#1E2530", "#3A4454", "#2D3748", "#9AA4B2", "#161B22", "#2A3240", "#2A3240"],
        ["night"] = ["#B83A3E", "#3A6FCC", "#1E4FB8", "#C98A1E", "#2F8F43", "#C9CFD8", "#07090C", "#0A0D11",
                     "#141920", "#283039", "#1E2530", "#7C8592", "#0E1217", "#1C222B", "#1C222B"],
        ["light"] = ["#C42B30", "#1F6FEB", "#1D5FD1", "#B36B00", "#1A7F37", "#141A22", "#F3F5F8", "#FFFFFF",
                     "#EEF1F5", "#B9C2CE", "#DCE6F5", "#4F5A69", "#FFFFFF", "#D5DBE3", "#5F6B7A"],
        ["contrast"] = ["#FF2D2D", "#FFD400", "#0050C8", "#FFD400", "#3CFF6B", "#FFFFFF", "#000000", "#000000",
                        "#000000", "#FFFFFF", "#1A3A6B", "#D9D9D9", "#0A0A0A", "#FFFFFF", "#1A1A1A"],
        ["ocean"] = ["#F05D5E", "#2EC4D6", "#0E8FA8", "#F5B841", "#34D399", "#E3F2F7", "#06141C", "#0A1D28",
                     "#12293A", "#24465C", "#16405A", "#8DB3C4", "#0C2130", "#1D3A4E", "#1D3A4E"],
        ["forest"] = ["#E5534B", "#5BD68A", "#23864F", "#E9B949", "#7ADF8C", "#E4EFE6", "#0A120D", "#0F1A13",
                      "#18261C", "#2E4535", "#21402B", "#93A99A", "#122017", "#25382B", "#25382B"],
        ["sunset"] = ["#FF4D5E", "#FF8A3D", "#D9562F", "#FFC145", "#5CCB7A", "#F6EAE4", "#170E12", "#1F1218",
                      "#2A1A21", "#4A2E38", "#45232D", "#B49AA3", "#21141A", "#3A2530", "#3A2530"],
        ["graphite"] = ["#E5484D", "#B0BAC6", "#4B5563", "#F5A524", "#3FB950", "#ECECEC", "#121212", "#181818",
                        "#232323", "#3A3A3A", "#333333", "#9B9B9B", "#1B1B1B", "#2E2E2E", "#2E2E2E"],
        ["neon"] = ["#FF3B6B", "#C77DFF", "#7C3AED", "#FFD166", "#3CF0A0", "#F1E9FF", "#0B0614", "#120A1F",
                    "#1C1230", "#3B2763", "#2E1D52", "#A897C9", "#150D25", "#2C1E4A", "#2C1E4A"],
    };

    /// <summary>The corner radii the styles use (Radius2 … Radius12), per corner shape.</summary>
    private static readonly int[] Radii = [2, 3, 4, 5, 6, 8, 10, 12];

    // ---- colour per kind of item: one brush per kind, whose colour changes in place, so every row follows at once
    public static readonly (string Key, string Name)[] ItemKinds =
        [("song", "Muziek"), ("jingle", "Jingles"), ("voice", "AI-presentator en stem"), ("other", "Overig")];

    public static readonly Dictionary<string, string> DefaultTypeColors = new()
    {
        ["song"] = "#3FA7FF", ["jingle"] = "#F0B429", ["voice"] = "#A78BFA", ["other"] = "#8B95A5",
    };

    // On a light background the standard kind colours are too pale to read; while a kind still has its standard
    // colour, a light style uses these darker ones (an own choice is kept).
    private static readonly Dictionary<string, string> LightTypeColors = new()
    {
        ["song"] = "#1D4ED8", ["jingle"] = "#A16207", ["voice"] = "#6D28D9", ["other"] = "#475569",
    };

    /// <summary>Whether a style has a light background.</summary>
    public static bool IsLight(string theme) => theme is "light" or "daglicht";

    /// <summary>The colours to choose from (name, colour).</summary>
    public static readonly (string Name, string Hex)[] KindColors =
    [
        ("Blauw", "#3FA7FF"), ("Geel", "#F0B429"), ("Paars", "#A78BFA"), ("Grijs", "#8B95A5"), ("Groen", "#3FB950"),
        ("Rood", "#E5484D"), ("Oranje", "#F27A2E"), ("Roze", "#F472B6"), ("Cyaan", "#22D3EE"), ("Wit", "#E6EAF0"),
    ];

    private static readonly Dictionary<string, SolidColorBrush> KindBrushes =
        DefaultTypeColors.ToDictionary(k => k.Key, k => new SolidColorBrush(Color.Parse(k.Value)));

    /// <summary>The brush of a kind of item ("song", "jingle", "aiBreak", "voice", anything else = other).</summary>
    public static IBrush KindBrush(string? type) => KindBrushes[type switch
    {
        "song" or "music" => "song",
        "jingle" => "jingle",
        "aiBreak" or "ai-break" or "voice" or "voicetrack" or "ai" => "voice",
        _ => "other",
    }];

    private static StyleInclude? _touch;

    public static void Apply(StudioSettings s)
    {
        if (Application.Current is not { } app) return;
        var theme = Palettes.ContainsKey(s.Theme) ? s.Theme : "regiekamer";
        bool light = IsLight(theme);
        foreach (var (key, brush) in KindBrushes)
        {
            string hex = s.TypeColors.TryGetValue(key, out var h) && Color.TryParse(h, out _) ? h : DefaultTypeColors[key];
            if (light && string.Equals(hex, DefaultTypeColors[key], StringComparison.OrdinalIgnoreCase)) hex = LightTypeColors[key];
            brush.Color = Color.Parse(hex);
        }

        // Touch mode: an extra style sheet after all others, so its sizes win.
        _touch ??= new StyleInclude(new Uri("avares://REA-Radio-DJ-Studio/")) { Source = new Uri("avares://REA-Radio-DJ-Studio/TouchStyles.axaml") };
        if (s.Touch && !app.Styles.Contains(_touch)) app.Styles.Add(_touch);
        else if (!s.Touch) app.Styles.Remove(_touch);
        var colors = Palettes[theme];
        for (int i = 0; i < Keys.Length; i++) app.Resources[Keys[i]] = new SolidColorBrush(Color.Parse(colors[i]));
        app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;

        // Own accent colour: the accent itself, and a deeper shade of it for the filled main buttons.
        Color primary = Color.Parse(colors[2]);
        if (!string.IsNullOrWhiteSpace(s.Accent) && Color.TryParse(s.Accent, out var accent))
        {
            app.Resources["AccentBrush"] = new SolidColorBrush(accent);
            primary = Mix(accent, Colors.Black, 0.28);
            app.Resources["PrimaryBrush"] = new SolidColorBrush(primary);
        }
        app.Resources["PrimaryHoverBrush"] = new SolidColorBrush(Mix(primary, Colors.White, 0.15));
        ApplyButtons(app, s.ButtonStyle, Color.Parse(colors[8]), Color.Parse(colors[9]), primary);

        WaveStyle = WaveStyles.Any(w => w.Key == s.WaveStyle) ? s.WaveStyle : "classic";
        WaveColor = !string.IsNullOrWhiteSpace(s.WaveColor) && Color.TryParse(s.WaveColor, out var wc) ? wc : Color.Parse("#3FA7FF");

        double factor = s.Corners switch { "square" => 0, "round" => 1.75, "pill" => 3, _ => 1 };
        foreach (var r in Radii) app.Resources[$"Radius{r}"] = new CornerRadius(Math.Round(r * factor));
        app.Resources["ControlCornerRadius"] = new CornerRadius(Math.Round(4 * factor));
        app.Resources["OverlayCornerRadius"] = new CornerRadius(Math.Round(8 * factor));

        double scale = Scales.Contains(s.ScalePercent) ? s.ScalePercent / 100.0 : 1;
        if (Math.Abs(scale - Scale) > 0.001) { Scale = scale; ScaleChanged?.Invoke(); }
        LookChanged?.Invoke();
    }

    // Fluent's own button colours that the button look replaces (flat = the theme's own, so they are removed).
    private static readonly string[] FluentButtonKeys =
        ["ButtonBackground", "ButtonBorderBrush", "ToggleButtonBackground", "ToggleButtonBorderBrush", "RepeatButtonBackground"];

    /// <summary>
    /// The button look: BarButtonBrush and PrimaryButtonBrush are used by the bar and main buttons; ordinary buttons
    /// follow through Fluent's own resource keys.
    /// </summary>
    private static void ApplyButtons(Application app, string style, Color control, Color border, Color primary)
    {
        foreach (var k in FluentButtonKeys) app.Resources.Remove(k);
        switch (style)
        {
            case "raised":
                var face = Vertical(Mix(control, Colors.White, 0.10), Mix(control, Colors.Black, 0.18));
                app.Resources["BarButtonBrush"] = face;
                app.Resources["PrimaryButtonBrush"] = Vertical(Mix(primary, Colors.White, 0.22), Mix(primary, Colors.Black, 0.12));
                app.Resources["ButtonBackground"] = face;
                app.Resources["ToggleButtonBackground"] = face;
                app.Resources["RepeatButtonBackground"] = face;
                app.Resources["ButtonBorderBrush"] = new SolidColorBrush(Mix(border, Colors.White, 0.08));
                app.Resources["ToggleButtonBorderBrush"] = new SolidColorBrush(Mix(border, Colors.White, 0.08));
                break;
            case "outline":
                app.Resources["BarButtonBrush"] = Brushes.Transparent;
                app.Resources["PrimaryButtonBrush"] = new SolidColorBrush(primary);
                app.Resources["ButtonBackground"] = Brushes.Transparent;
                app.Resources["ToggleButtonBackground"] = Brushes.Transparent;
                app.Resources["RepeatButtonBackground"] = Brushes.Transparent;
                app.Resources["ButtonBorderBrush"] = new SolidColorBrush(Mix(border, Colors.White, 0.2));
                app.Resources["ToggleButtonBorderBrush"] = new SolidColorBrush(Mix(border, Colors.White, 0.2));
                break;
            default:
                app.Resources["BarButtonBrush"] = new SolidColorBrush(control);
                app.Resources["PrimaryButtonBrush"] = new SolidColorBrush(primary);
                break;
        }
    }

    private static LinearGradientBrush Vertical(Color top, Color bottom) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
    };

    /// <summary>Mixes two colours: 0 = a, 1 = b.</summary>
    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }
}
