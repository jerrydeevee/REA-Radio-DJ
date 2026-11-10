using Avalonia;
using Avalonia.Media;
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
        ("dark", "Studio donker"), ("night", "Nacht (gedimd, voor een donkere studio)"),
        ("light", "Licht (overdag, veel daglicht)"), ("contrast", "Hoog contrast"),
    ];

    public static readonly (string Key, string Name)[] Corners = [("square", "Recht"), ("normal", "Normaal"), ("round", "Rond")];

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
        ["dark"] = ["#E5484D", "#4C8DFF", "#2563EB", "#F5A524", "#3FB950", "#E6EAF0", "#0E1116", "#11161D",
                    "#1E2530", "#3A4454", "#2D3748", "#9AA4B2", "#161B22", "#2A3240", "#2A3240"],
        ["night"] = ["#B83A3E", "#3A6FCC", "#1E4FB8", "#C98A1E", "#2F8F43", "#C9CFD8", "#07090C", "#0A0D11",
                     "#141920", "#283039", "#1E2530", "#7C8592", "#0E1217", "#1C222B", "#1C222B"],
        ["light"] = ["#C42B30", "#1F6FEB", "#1D5FD1", "#B36B00", "#1A7F37", "#141A22", "#F3F5F8", "#FFFFFF",
                     "#EEF1F5", "#B9C2CE", "#DCE6F5", "#4F5A69", "#FFFFFF", "#D5DBE3", "#5F6B7A"],
        ["contrast"] = ["#FF2D2D", "#FFD400", "#0050C8", "#FFD400", "#3CFF6B", "#FFFFFF", "#000000", "#000000",
                        "#000000", "#FFFFFF", "#1A3A6B", "#D9D9D9", "#0A0A0A", "#FFFFFF", "#1A1A1A"],
    };

    /// <summary>The corner radii the styles use (Radius2 … Radius12), per corner shape.</summary>
    private static readonly int[] Radii = [2, 3, 4, 5, 6, 8, 10, 12];

    public static void Apply(StudioSettings s)
    {
        if (Application.Current is not { } app) return;
        var theme = Palettes.ContainsKey(s.Theme) ? s.Theme : "dark";
        var colors = Palettes[theme];
        for (int i = 0; i < Keys.Length; i++) app.Resources[Keys[i]] = new SolidColorBrush(Color.Parse(colors[i]));
        app.RequestedThemeVariant = theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;

        double factor = s.Corners switch { "square" => 0, "round" => 1.75, _ => 1 };
        foreach (var r in Radii) app.Resources[$"Radius{r}"] = new CornerRadius(Math.Round(r * factor));
        app.Resources["ControlCornerRadius"] = new CornerRadius(Math.Round(4 * factor));
        app.Resources["OverlayCornerRadius"] = new CornerRadius(Math.Round(8 * factor));

        double scale = Scales.Contains(s.ScalePercent) ? s.ScalePercent / 100.0 : 1;
        if (Math.Abs(scale - Scale) > 0.001) { Scale = scale; ScaleChanged?.Invoke(); }
    }
}
