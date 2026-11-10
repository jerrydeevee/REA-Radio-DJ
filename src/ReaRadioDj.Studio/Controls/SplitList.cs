using Avalonia;
using Avalonia.Controls;

namespace ReaRadioDj.Studio.Controls;

/// <summary>
/// A list with its form: side by side when the panel is wide enough, the list on top and the form under it when the
/// panel is narrow (a docked window on a small screen), so the form never gets squeezed into a strip.
/// The first child is the list, the second the form.
/// </summary>
public sealed class SplitList : Panel
{
    public static readonly StyledProperty<double> ListWidthProperty = AvaloniaProperty.Register<SplitList, double>(nameof(ListWidth), 170);
    public static readonly StyledProperty<double> ListHeightProperty = AvaloniaProperty.Register<SplitList, double>(nameof(ListHeight), 170);
    public static readonly StyledProperty<double> BreakpointProperty = AvaloniaProperty.Register<SplitList, double>(nameof(Breakpoint), 600);
    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<SplitList, double>(nameof(Gap), 8);

    static SplitList() => AffectsMeasure<SplitList>(ListWidthProperty, ListHeightProperty, BreakpointProperty, GapProperty);

    /// <summary>Width of the list when side by side.</summary>
    public double ListWidth { get => GetValue(ListWidthProperty); set => SetValue(ListWidthProperty, value); }
    /// <summary>Height of the list when stacked.</summary>
    public double ListHeight { get => GetValue(ListHeightProperty); set => SetValue(ListHeightProperty, value); }
    /// <summary>Below this width the list goes on top.</summary>
    public double Breakpoint { get => GetValue(BreakpointProperty); set => SetValue(BreakpointProperty, value); }
    public double Gap { get => GetValue(GapProperty); set => SetValue(GapProperty, value); }

    private bool Stacked(double width) => !double.IsInfinity(width) && width < Breakpoint;

    protected override Size MeasureOverride(Size available)
    {
        if (Children.Count < 2) return base.MeasureOverride(available);
        Control list = Children[0], form = Children[1];
        if (Stacked(available.Width))
        {
            double listH = Math.Min(ListHeight, double.IsInfinity(available.Height) ? ListHeight : Math.Max(0, available.Height / 2));
            list.Measure(new Size(available.Width, listH));
            form.Measure(new Size(available.Width, Math.Max(0, available.Height - listH - Gap)));
            return new Size(available.Width, listH + Gap + form.DesiredSize.Height);
        }
        double listW = Math.Min(ListWidth, available.Width);
        list.Measure(new Size(listW, available.Height));
        form.Measure(new Size(Math.Max(0, available.Width - listW - Gap), available.Height));
        return new Size(listW + Gap + form.DesiredSize.Width, Math.Max(list.DesiredSize.Height, form.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Children.Count < 2) return base.ArrangeOverride(final);
        Control list = Children[0], form = Children[1];
        if (Stacked(final.Width))
        {
            double listH = Math.Min(ListHeight, Math.Max(0, final.Height / 2));
            list.Arrange(new Rect(0, 0, final.Width, listH));
            form.Arrange(new Rect(0, listH + Gap, final.Width, Math.Max(0, final.Height - listH - Gap)));
        }
        else
        {
            double listW = Math.Min(ListWidth, final.Width);
            list.Arrange(new Rect(0, 0, listW, final.Height));
            form.Arrange(new Rect(listW + Gap, 0, Math.Max(0, final.Width - listW - Gap), final.Height));
        }
        for (int i = 2; i < Children.Count; i++) Children[i].Arrange(new Rect(final));
        return final;
    }
}
