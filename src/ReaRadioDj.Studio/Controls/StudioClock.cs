using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ReaRadioDj.Studio.Controls;

/// <summary>
/// A clock that scales with its window, in three styles:
/// <list type="bullet">
/// <item>studio: a ring of 60 second dots that fill up during the minute, hour markers and the time in the middle;</item>
/// <item>analog: a classic face with hour, minute and a sweeping second hand;</item>
/// <item>digital: only large digits.</item>
/// </list>
/// An optional outer arc shows how much of the item on air is left (green, amber, red).
/// It ticks on its own (10× per second) from the engine time plus the time since the last update, so it stays smooth.
/// </summary>
public sealed class StudioClock : Control
{
    public static readonly StyledProperty<DateTime> TimeProperty = AvaloniaProperty.Register<StudioClock, DateTime>(nameof(Time));
    public static readonly StyledProperty<string> ClockStyleProperty = AvaloniaProperty.Register<StudioClock, string>(nameof(ClockStyle), "studio");
    public static readonly StyledProperty<bool> ShowSecondsProperty = AvaloniaProperty.Register<StudioClock, bool>(nameof(ShowSeconds), true);
    public static readonly StyledProperty<IBrush> AccentProperty = AvaloniaProperty.Register<StudioClock, IBrush>(nameof(Accent), new SolidColorBrush(Color.Parse("#E5484D")));
    /// <summary>0..1 of the item on air that is left; below 0 = nothing on air (no arc).</summary>
    public static readonly StyledProperty<double> ItemLeftProperty = AvaloniaProperty.Register<StudioClock, double>(nameof(ItemLeft), -1);
    public static readonly StyledProperty<double> ItemSecondsLeftProperty = AvaloniaProperty.Register<StudioClock, double>(nameof(ItemSecondsLeft), 999);

    // Face, ring and digits follow the style (dark or light).
    private static IBrush Dim => new SolidColorBrush(Services.Appearance.Surface.Grid);
    private static IBrush Face => new SolidColorBrush(Services.Appearance.Surface.Back);
    private static IBrush Text => new SolidColorBrush(Services.Appearance.Surface.Text);
    private static IBrush Muted => new SolidColorBrush(Services.Appearance.Surface.Muted);
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#3DBD7D"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#F0B429"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#E5484D"));
    private static readonly Typeface Mono = new(new FontFamily("Consolas, Cascadia Mono, monospace"), FontStyle.Normal, FontWeight.Bold);

    private readonly DispatcherTimer _timer;
    private DateTime _received = DateTime.Now;

    static StudioClock()
    {
        AffectsRender<StudioClock>(ClockStyleProperty, ShowSecondsProperty, AccentProperty, ItemLeftProperty, ItemSecondsLeftProperty);
    }

    public StudioClock()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => InvalidateVisual());
        ClipToBounds = true;
    }

    public DateTime Time { get => GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
    public string ClockStyle { get => GetValue(ClockStyleProperty); set => SetValue(ClockStyleProperty, value); }
    public bool ShowSeconds { get => GetValue(ShowSecondsProperty); set => SetValue(ShowSecondsProperty, value); }
    public IBrush Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public double ItemLeft { get => GetValue(ItemLeftProperty); set => SetValue(ItemLeftProperty, value); }
    public double ItemSecondsLeft { get => GetValue(ItemSecondsLeftProperty); set => SetValue(ItemSecondsLeftProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TimeProperty) _received = DateTime.Now;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _timer.Start(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _timer.Stop(); }

    /// <summary>Engine time, moved on by the local time since the last update (capped, so a lost connection shows).</summary>
    private DateTime Now
    {
        get
        {
            if (Time == default) return DateTime.Now;
            var since = DateTime.Now - _received;
            return Time + (since < TimeSpan.Zero ? TimeSpan.Zero : since > TimeSpan.FromSeconds(3) ? TimeSpan.FromSeconds(3) : since);
        }
    }

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        if (b.Width < 20 || b.Height < 20) return;
        var now = Now;
        switch (ClockStyle)
        {
            case "digital": RenderDigital(context, new Rect(b.Size), now); break;
            case "analog": RenderAnalog(context, b.Size, now); break;
            default: RenderStudio(context, b.Size, now); break;
        }
    }

    // ---------------------------------------------------------------- styles

    private void RenderStudio(DrawingContext g, Size size, DateTime now)
    {
        double r = Math.Min(size.Width, size.Height) / 2;
        var c = new Point(size.Width / 2, size.Height / 2);
        g.DrawEllipse(Face, null, c, r, r);
        DrawItemArc(g, c, r * 0.985, r * 0.03);

        double ring = r * 0.86, dot = Math.Max(1.5, r * 0.032);
        int sec = now.Second;
        for (int i = 0; i < 60; i++)
        {
            var p = Polar(c, ring, i * 6);
            // the dots fill up to the current second; at :00 the ring is full for that one second
            bool lit = sec == 0 ? true : i >= 1 && i <= sec;
            g.DrawEllipse(lit ? Accent : Dim, null, p, dot, dot);
        }
        double hourRing = r * 0.74;
        for (int i = 0; i < 12; i++)
        {
            var p = Polar(c, hourRing, i * 30);
            double d = i % 3 == 0 ? dot * 1.25 : dot * 0.9;
            g.DrawEllipse(Amber, null, p, d, d);
        }

        string hm = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        var main = Fit(hm, r * 1.15, r * 0.42, Text);
        g.DrawText(main, new Point(c.X - main.Width / 2, c.Y - main.Height * 0.62));
        if (ShowSeconds)
        {
            var s = Fit(now.ToString("ss", CultureInfo.InvariantCulture), r * 0.36, r * 0.2, Accent);
            g.DrawText(s, new Point(c.X - s.Width / 2, c.Y + main.Height * 0.38));
        }
    }

    private void RenderAnalog(DrawingContext g, Size size, DateTime now)
    {
        double r = Math.Min(size.Width, size.Height) / 2;
        var c = new Point(size.Width / 2, size.Height / 2);
        g.DrawEllipse(Face, new Pen(Dim, Math.Max(1, r * 0.02)), c, r * 0.96, r * 0.96);
        DrawItemArc(g, c, r * 0.985, r * 0.03);

        for (int i = 0; i < 60; i++)
        {
            bool hour = i % 5 == 0;
            var pen = new Pen(hour ? Text : Muted, hour ? Math.Max(2, r * 0.03) : Math.Max(1, r * 0.01), lineCap: PenLineCap.Round);
            g.DrawLine(pen, Polar(c, r * (hour ? 0.76 : 0.83), i * 6), Polar(c, r * 0.88, i * 6));
        }
        for (int h = 1; h <= 12; h++)
        {
            var t = Fit(h.ToString(CultureInfo.InvariantCulture), r * 0.16, r * 0.12, Muted);
            var p = Polar(c, r * 0.62, h * 30);
            g.DrawText(t, new Point(p.X - t.Width / 2, p.Y - t.Height / 2));
        }

        double seconds = now.Second + now.Millisecond / 1000.0;
        double minutes = now.Minute + seconds / 60;
        double hours = now.Hour % 12 + minutes / 60;
        Hand(g, c, hours * 30, r * 0.48, r * 0.055, Text);
        Hand(g, c, minutes * 6, r * 0.74, r * 0.035, Text);
        if (ShowSeconds) Hand(g, c, seconds * 6, r * 0.82, r * 0.012, Accent);
        g.DrawEllipse(Accent, null, c, r * 0.035, r * 0.035);
    }

    private void RenderDigital(DrawingContext g, Rect area, DateTime now)
    {
        string text = now.ToString(ShowSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
        var t = Fit(text, area.Width * 0.96, area.Height * 0.8, Text);
        var at = new Point(area.Center.X - t.Width / 2, area.Center.Y - t.Height / 2);
        g.DrawText(t, at);
        if (ItemLeft >= 0)
        {
            // a thin bar under the digits for the item on air
            double w = t.Width, y = at.Y + t.Height + 4, h = Math.Max(3, t.Height * 0.05);
            g.DrawRectangle(Dim, null, new Rect(at.X, y, w, h), h / 2, h / 2);
            g.DrawRectangle(ItemBrush, null, new Rect(at.X, y, w * Math.Clamp(ItemLeft, 0, 1), h), h / 2, h / 2);
        }
    }

    // ---------------------------------------------------------------- helpers

    private IBrush ItemBrush => ItemSecondsLeft <= 10 ? Red : ItemSecondsLeft <= 20 ? Amber : Green;

    private void DrawItemArc(DrawingContext g, Point c, double r, double thickness)
    {
        if (ItemLeft < 0) return;
        double left = Math.Clamp(ItemLeft, 0, 1);
        g.DrawEllipse(null, new Pen(Dim, thickness), c, r - thickness / 2, r - thickness / 2);
        if (left <= 0.001) return;
        // flash in the last 10 seconds
        if (ItemSecondsLeft <= 10 && DateTime.Now.Millisecond >= 500) return;
        double rr = r - thickness / 2, sweep = left * 360;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(Polar(c, rr, 0), false);
            if (sweep >= 359.9)
            {
                ctx.ArcTo(Polar(c, rr, 180), new Size(rr, rr), 0, false, SweepDirection.Clockwise);
                ctx.ArcTo(Polar(c, rr, 359.9), new Size(rr, rr), 0, false, SweepDirection.Clockwise);
            }
            else ctx.ArcTo(Polar(c, rr, sweep), new Size(rr, rr), 0, sweep > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        g.DrawGeometry(null, new Pen(ItemBrush, thickness, lineCap: PenLineCap.Round), geo);
    }

    private static void Hand(DrawingContext g, Point c, double degrees, double length, double width, IBrush brush) =>
        g.DrawLine(new Pen(brush, Math.Max(1, width), lineCap: PenLineCap.Round), Polar(c, -length * 0.12, degrees), Polar(c, length, degrees));

    /// <summary>Point on a circle; 0° = 12 o'clock, clockwise.</summary>
    private static Point Polar(Point c, double r, double degrees)
    {
        double a = (degrees - 90) * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }

    /// <summary>Text at the largest size that fits in the box.</summary>
    private static FormattedText Fit(string text, double maxWidth, double maxHeight, IBrush brush)
    {
        var probe = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 100, brush);
        double size = Math.Max(6, Math.Min(100 * maxWidth / Math.Max(1, probe.Width), 100 * maxHeight / Math.Max(1, probe.Height)));
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, brush);
    }
}
