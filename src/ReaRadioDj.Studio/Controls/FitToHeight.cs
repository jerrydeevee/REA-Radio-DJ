using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ReaRadioDj.Studio.Controls;

/// <summary>
/// Shows its child at full width, and when it is taller than the space it gets, scales it down (not below
/// <see cref="MinScale"/>) so it fits without scrolling. Below the minimum the child (typically a ScrollViewer)
/// gets less height than it wants and scrolls the rest. Off (<see cref="Enabled"/> false): a plain decorator.
/// The scale is a render transform from the top left, so clicks and drags land where they are drawn.
/// </summary>
public sealed class FitToHeight : Decorator
{
    public static readonly StyledProperty<bool> EnabledProperty = AvaloniaProperty.Register<FitToHeight, bool>(nameof(Enabled), true);
    public static readonly StyledProperty<double> MinScaleProperty = AvaloniaProperty.Register<FitToHeight, double>(nameof(MinScale), 0.7);

    static FitToHeight() => AffectsMeasure<FitToHeight>(EnabledProperty, MinScaleProperty);

    public bool Enabled { get => GetValue(EnabledProperty); set => SetValue(EnabledProperty, value); }
    public double MinScale { get => GetValue(MinScaleProperty); set => SetValue(MinScaleProperty, value); }

    /// <summary>The scale in use now (1 = full size).</summary>
    public double Scale { get; private set; } = 1;

    protected override Size MeasureOverride(Size available)
    {
        if (Child is not { } child) return default;
        Scale = 1;
        if (!Enabled || double.IsInfinity(available.Height))
        {
            child.Measure(available);
            return child.DesiredSize;
        }
        child.Measure(new Size(available.Width, double.PositiveInfinity));
        double h = child.DesiredSize.Height;
        if (h <= available.Height || h <= 0)
        {
            child.Measure(available);
            return child.DesiredSize;
        }

        // Scaled down, the child gets more width (in its own units), which can make it lower still: measure once more.
        double s = Math.Max(MinScale, available.Height / h);
        child.Measure(new Size(available.Width / s, double.PositiveInfinity));
        s = Math.Clamp(available.Height / Math.Max(1, child.DesiredSize.Height), MinScale, 1);
        if (s < 1) child.Measure(new Size(available.Width / s, double.PositiveInfinity));
        Scale = s;
        return new Size(available.Width, Math.Min(child.DesiredSize.Height * s, available.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Child is not { } child) return final;
        double s = Scale;
        // At the minimum scale the child gets less height than it wants, so a ScrollViewer in it can scroll the rest.
        child.Arrange(new Rect(0, 0, final.Width / s, final.Height / s));
        child.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        child.RenderTransform = s < 1 ? new ScaleTransform(s, s) : null;
        return final;
    }
}
