using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>Small converters used by the views.</summary>
public static class Brushes
{
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#E5484D"));
    private static readonly IBrush Off = new SolidColorBrush(Color.Parse("#2A3240"));
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#3DBD7D"));
    private static readonly IBrush Transparent = new SolidColorBrush(Colors.Transparent);

    public static readonly IValueConverter OnAir = new FuncValueConverter<bool, IBrush>(on => on ? Red : Off);
    public static readonly IValueConverter Active = new FuncValueConverter<bool, IBrush>(on => on ? Green : Off);
    public static readonly IValueConverter Highlight = new FuncValueConverter<bool, IBrush>(on => on ? new SolidColorBrush(Color.Parse("#1F3A2C")) : Transparent);

    /// <summary>True when the value equals the converter parameter (for style classes).</summary>
    public static readonly IValueConverter Is = new FuncValueConverter<string?, string?, bool>((v, p) => string.Equals(v, p, StringComparison.Ordinal));

    public static readonly IValueConverter Half = new FuncValueConverter<double, double>(v => Math.Max(12, Math.Round(v / 2)));

    public static readonly IValueConverter ValidBorder = new FuncValueConverter<bool, IBrush>(ok => ok ? Off : Red);

    public static readonly IValueConverter EnabledOpacity = new FuncValueConverter<bool, double>(on => on ? 1.0 : 0.45);
    public static readonly IValueConverter Seconds = new FuncValueConverter<double, string>(s => Services.StudioHub.Time(s));

    /// <summary>"#rrggbb" to a brush (empty or invalid = card colour).</summary>
    public static readonly IValueConverter Hex = new FuncValueConverter<string?, IBrush>(s =>
        !string.IsNullOrWhiteSpace(s) && Color.TryParse(s, out var c) ? new SolidColorBrush(c) : Off);

    public static IBrush FromHex(string? s, string fallback = "#2A3240") =>
        new SolidColorBrush(!string.IsNullOrWhiteSpace(s) && Color.TryParse(s, out var c) ? c : Color.Parse(fallback));

    public static readonly IValueConverter TypeColor = new FuncValueConverter<string?, IBrush>(t => FromHex(t switch
    {
        "song" => "#3FA7FF",
        "jingle" => "#F0B429",
        "aiBreak" => "#A78BFA",
        "voice" => "#A78BFA",
        _ => "#8B95A5",
    }));
}
