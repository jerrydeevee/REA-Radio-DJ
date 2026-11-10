using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ReaRadioDj.Studio.Controls;

/// <summary>
/// The progress of the item on air: a bar that fills up, with a mark where the intro ends (amber) and one where the
/// next item mixes in (light). All positions are 0-100; a mark below zero is not drawn.
/// </summary>
public sealed class OnAirProgress : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<OnAirProgress, double>(nameof(Value));
    public static readonly StyledProperty<double> IntroProperty = AvaloniaProperty.Register<OnAirProgress, double>(nameof(Intro), -1);
    public static readonly StyledProperty<double> MixProperty = AvaloniaProperty.Register<OnAirProgress, double>(nameof(Mix), -1);
    public static readonly StyledProperty<IBrush> FillProperty = AvaloniaProperty.Register<OnAirProgress, IBrush>(nameof(Fill), new SolidColorBrush(Color.Parse("#4C8DFF")));
    public static readonly StyledProperty<IBrush> TrackProperty = AvaloniaProperty.Register<OnAirProgress, IBrush>(nameof(Track), new SolidColorBrush(Color.Parse("#2A3240")));

    private static readonly IBrush IntroBrush = new SolidColorBrush(Color.Parse("#F5A524"));
    private static readonly IBrush MixBrush = new SolidColorBrush(Color.Parse("#E6EAF0"));
    private const double BarHeight = 10, MarkHeight = 18, MarkWidth = 2;

    static OnAirProgress() => AffectsRender<OnAirProgress>(ValueProperty, IntroProperty, MixProperty, FillProperty, TrackProperty);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Intro { get => GetValue(IntroProperty); set => SetValue(IntroProperty, value); }
    public double Mix { get => GetValue(MixProperty); set => SetValue(MixProperty, value); }
    public IBrush Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, MarkHeight);

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, top = (Bounds.Height - BarHeight) / 2;
        if (w <= 0) return;
        context.DrawRectangle(Track, null, new RoundedRect(new Rect(0, top, w, BarHeight), BarHeight / 2));
        double filled = w * Math.Clamp(Value, 0, 100) / 100;
        if (filled > 0.5)
            context.DrawRectangle(Fill, null, new RoundedRect(new Rect(0, top, Math.Max(filled, BarHeight), BarHeight), BarHeight / 2));
        DrawMark(context, Intro, IntroBrush, w);
        DrawMark(context, Mix, MixBrush, w);
    }

    private void DrawMark(DrawingContext context, double position, IBrush brush, double width)
    {
        if (position is < 0 or > 100) return;
        double x = Math.Clamp(width * position / 100, 0, width - MarkWidth);
        context.DrawRectangle(brush, null, new Rect(x, (Bounds.Height - MarkHeight) / 2, MarkWidth, MarkHeight));
    }
}
