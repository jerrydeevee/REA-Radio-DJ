using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>Small converters used by the views.</summary>
public static class Brushes
{
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#C42B30"));
    private static readonly IBrush Ok = new SolidColorBrush(Color.Parse("#3FB950"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#F5A524"));
    // "Off" (an empty pad, a dark lamp, a quiet border) follows the style, so it is light grey in a light style.
    private static IBrush Off => Themed("CardBorderBrush");
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#3DBD7D"));
    private static readonly IBrush Transparent = new SolidColorBrush(Colors.Transparent);

    public static readonly IValueConverter OnAir = new FuncValueConverter<bool, IBrush>(on => on ? Red : Off);
    public static readonly IValueConverter OnAirText = new FuncValueConverter<bool, string>(on => on ? "ON AIR" : "STIL");
    /// <summary>Engine connection: green when connected, amber when not (a word next to it says which).</summary>
    public static readonly IValueConverter WarnOrOk = new FuncValueConverter<bool, IBrush>(warn => warn ? Amber : Green);
    public static readonly IValueConverter Connection =new FuncValueConverter<bool, IBrush>(ok => ok ? Ok : Amber);
    public static readonly IValueConverter BoldIf = new FuncValueConverter<bool, Avalonia.Media.FontWeight>(b => b ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal);
    public static readonly IValueConverter TwinWaveHeight = new FuncValueConverter<int, double>(n => n >= 4 ? 140 : 110);
    /// <summary>Height of the transport buttons of a deck: lower in the compact (four-deck) view.</summary>
    public static readonly IValueConverter TransportHeight = new FuncValueConverter<bool, double>(compact => compact ? 40 : 50);
    public static readonly IValueConverter TempoFaderHeight = new FuncValueConverter<bool, double>(compact => compact ? 56 : 80);
    public static readonly IValueConverter Negate = new FuncValueConverter<double, double>(v => -v);
    /// <summary>True for an id above zero (a saved item).</summary>
    public static readonly IValueConverter Positive = new FuncValueConverter<long, bool>(v => v > 0);
    public static readonly IValueConverter Active = new FuncValueConverter<bool, IBrush>(on => on ? Green : Off);
    public static readonly IValueConverter Highlight = new FuncValueConverter<bool, IBrush>(on => on ? new SolidColorBrush(Color.Parse("#1F3A2C")) : Transparent);

    /// <summary>True when the value equals the converter parameter (for style classes).</summary>
    public static readonly IValueConverter Is = new FuncValueConverter<string?, string?, bool>((v, p) => string.Equals(v, p, StringComparison.Ordinal));

    /// <summary>Fader height of a mixer strip for a panel of this height: what is left after the buttons, 60-220 px.</summary>
    public static readonly IValueConverter FaderHeight = new FuncValueConverter<double, double>(h => h <= 0 ? 150 : Math.Clamp(h - 225, 60, 220));

    public static readonly IValueConverter Half = new FuncValueConverter<double, double>(v => Math.Max(12, Math.Round(v / 2)));

    public static readonly IValueConverter ValidBorder = new FuncValueConverter<bool, IBrush>(ok => ok ? Off : Red);

    public static readonly IValueConverter EnabledOpacity = new FuncValueConverter<bool, double>(on => on ? 1.0 : 0.45);
    public static readonly IValueConverter Seconds = new FuncValueConverter<double, string>(s => Services.StudioHub.Time(s));

    /// <summary>"#rrggbb" to a brush (empty or invalid = card colour).</summary>
    public static readonly IValueConverter Hex = new FuncValueConverter<string?, IBrush>(s =>
        !string.IsNullOrWhiteSpace(s) && Color.TryParse(s, out var c) ? new SolidColorBrush(c) : Off);

    /// <summary>A colour of the current style (Instellingen › Uiterlijk), e.g. "ControlBrush".</summary>
    public static IBrush Themed(string key, string fallback = "#2A3240") =>
        Avalonia.Application.Current?.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out var r) == true && r is IBrush b
            ? b : FromHex(fallback);

    public static IBrush FromHex(string? s, string fallback = "#2A3240") =>
        new SolidColorBrush(!string.IsNullOrWhiteSpace(s) && Color.TryParse(s, out var c) ? c : Color.Parse(fallback));

    /// <summary>The colour of a kind of item (Instellingen › Uiterlijk › Kleuren per soort item).</summary>
    public static readonly IValueConverter TypeColor = new FuncValueConverter<string?, IBrush>(Services.Appearance.KindBrush);
}
