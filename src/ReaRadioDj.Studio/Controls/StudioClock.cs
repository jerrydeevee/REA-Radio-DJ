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
/// <item>digital: only large digits;</item>
/// <item>combi: the analog face with the digital time beside it (wide) or under it (tall);</item>
/// <item>led: seven-segment digits with unlit segments faintly visible, a bar of 60 seconds and the date;</item>
/// <item>hour: a ring that fills over the hour with quarter marks, the minutes and seconds left to the top of the hour in the middle.</item>
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
            case "combi": RenderCombi(context, b.Size, now); break;
            case "led": RenderLed(context, new Rect(b.Size), now); break;
            case "hour": RenderHour(context, b.Size, now); break;
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

    private static readonly CultureInfo Dutch = new("nl-NL");

    private void RenderCombi(DrawingContext g, Size size, DateTime now)
    {
        bool wide = size.Width >= size.Height * 1.25;
        var face = wide ? new Size(Math.Min(size.Height, size.Width * 0.55), size.Height) : new Size(size.Width, Math.Min(size.Width, size.Height * 0.68));
        using (g.PushTransform(Matrix.CreateTranslation(wide ? 0 : (size.Width - face.Width) / 2, 0)))
            RenderAnalog(g, face, now);
        var rest = wide ? new Rect(face.Width, 0, size.Width - face.Width, size.Height) : new Rect(0, face.Height, size.Width, size.Height - face.Height);
        if (rest.Width < 20 || rest.Height < 14) return;
        var t = Fit(now.ToString(ShowSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture), rest.Width * 0.9, rest.Height * 0.5, Text);
        var at = new Point(rest.Center.X - t.Width / 2, rest.Center.Y - t.Height * 0.65);
        g.DrawText(t, at);
        var d = Fit(now.ToString("dddd d MMMM", Dutch), rest.Width * 0.9, t.Height * 0.32, Muted);
        g.DrawText(d, new Point(rest.Center.X - d.Width / 2, at.Y + t.Height * 1.02));
    }

    // Seven segments of the digits 0-9: bit 0 = a (top), b (top right), c (bottom right), d (bottom), e, f, g (middle).
    private static readonly int[] Segments = [0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F];

    private void RenderLed(DrawingContext g, Rect area, DateTime now)
    {
        string text = now.ToString(ShowSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
        int digits = text.Count(char.IsDigit), colons = text.Length - digits;
        // in units of the digit height: a digit 0.6 wide, a colon 0.3, a gap of 0.12 between all
        double units = digits * 0.6 + colons * 0.3 + (text.Length - 1) * 0.12;
        double h = Math.Min(area.Height * 0.6, area.Width * 0.94 / units);
        if (h < 8) return;
        double total = units * h, left = area.Center.X - total / 2;
        double x = left, y = area.Y + (area.Height - h) / 2 - h * 0.12;
        var on = Accent;
        var off = new SolidColorBrush(Services.Appearance.Surface.Grid, 0.45);
        foreach (char ch in text)
        {
            if (ch == ':')
            {
                // the colons blink with the seconds
                var dot = now.Millisecond < 500 ? on : off;
                double r = h * 0.055;
                g.DrawEllipse(dot, null, new Point(x + h * 0.15, y + h * 0.3), r, r);
                g.DrawEllipse(dot, null, new Point(x + h * 0.15, y + h * 0.7), r, r);
                x += h * 0.42;
                continue;
            }
            DrawDigit(g, x, y, h, Segments[ch - '0'], on, off);
            x += h * 0.72;
        }
        // 60 second blocks under the digits; full for the one second at :00
        double barY = y + h * 1.12, barH = Math.Max(3, h * 0.07), cell = total / 60;
        for (int i = 0; i < 60; i++)
            g.DrawRectangle(i < now.Second || now.Second == 0 ? on : off, null, new Rect(left + i * cell + cell * 0.12, barY, cell * 0.76, barH));
        var date = Fit(now.ToString("dddd d MMMM yyyy", Dutch), total, h * 0.16, Muted);
        g.DrawText(date, new Point(area.Center.X - date.Width / 2, barY + barH + h * 0.06));
        if (ItemLeft >= 0)
        {
            // the item on air as a thin bar above the digits
            double iy = y - h * 0.16, ih = Math.Max(3, h * 0.05);
            g.DrawRectangle(Dim, null, new Rect(left, iy, total, ih), ih / 2, ih / 2);
            g.DrawRectangle(ItemBrush, null, new Rect(left, iy, total * Math.Clamp(ItemLeft, 0, 1), ih), ih / 2, ih / 2);
        }
    }

    /// <summary>One seven-segment digit, <paramref name="h"/> high and 0.6·h wide; unlit segments faintly.</summary>
    private static void DrawDigit(DrawingContext g, double x, double y, double h, int mask, IBrush on, IBrush off)
    {
        double w = h * 0.6, t = h * 0.1, half = h / 2;
        void Seg(int bit, double sx, double sy, bool horizontal)
        {
            double len = horizontal ? w - t * 1.2 : half - t * 1.1;
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(new Point(sx, sy), true);
                if (horizontal)
                {
                    c.LineTo(new Point(sx + t / 2, sy - t / 2)); c.LineTo(new Point(sx + len - t / 2, sy - t / 2));
                    c.LineTo(new Point(sx + len, sy)); c.LineTo(new Point(sx + len - t / 2, sy + t / 2)); c.LineTo(new Point(sx + t / 2, sy + t / 2));
                }
                else
                {
                    c.LineTo(new Point(sx + t / 2, sy + t / 2)); c.LineTo(new Point(sx + t / 2, sy + len - t / 2));
                    c.LineTo(new Point(sx, sy + len)); c.LineTo(new Point(sx - t / 2, sy + len - t / 2)); c.LineTo(new Point(sx - t / 2, sy + t / 2));
                }
                c.EndFigure(true);
            }
            g.DrawGeometry(((mask >> bit) & 1) == 1 ? on : off, null, geo);
        }
        double l = x + t / 2, r = x + w - t / 2, hx = x + t * 0.6;
        Seg(0, hx, y + t / 2, true);
        Seg(1, r, y + t * 0.55, false);
        Seg(2, r, y + half + t * 0.05, false);
        Seg(3, hx, y + h - t / 2, true);
        Seg(4, l, y + half + t * 0.05, false);
        Seg(5, l, y + t * 0.55, false);
        Seg(6, hx, y + half, true);
    }

    private void RenderHour(DrawingContext g, Size size, DateTime now)
    {
        double r = Math.Min(size.Width, size.Height) / 2;
        var c = new Point(size.Width / 2, size.Height / 2);
        g.DrawEllipse(Face, null, c, r, r);
        DrawItemArc(g, c, r * 0.985, r * 0.03);
        double ring = r * 0.82, thick = r * 0.09;
        int secondsIn = now.Minute * 60 + now.Second;
        double passed = (secondsIn + now.Millisecond / 1000.0) / 3600;
        g.DrawEllipse(null, new Pen(Dim, thick), c, ring, ring);
        // the last five minutes before the hour turn amber, the last minute red
        int toHour = 3600 - secondsIn;
        var fill = toHour <= 60 ? Red : toHour <= 300 ? Amber : Green;
        DrawArc(g, c, ring, passed * 360, new Pen(fill, thick, lineCap: PenLineCap.Flat));
        for (int q = 0; q < 4; q++)
        {
            g.DrawLine(new Pen(Text, Math.Max(2, r * 0.02)), Polar(c, ring - thick * 0.9, q * 90), Polar(c, ring + thick * 0.9, q * 90));
            var lbl = Fit((q * 15).ToString("00", CultureInfo.InvariantCulture), r * 0.14, r * 0.1, Muted);
            var lp = Polar(c, ring - thick * 2, q * 90);
            g.DrawText(lbl, new Point(lp.X - lbl.Width / 2, lp.Y - lbl.Height / 2));
        }
        var main = Fit(now.ToString("HH:mm", CultureInfo.InvariantCulture), r * 0.95, r * 0.34, Text);
        g.DrawText(main, new Point(c.X - main.Width / 2, c.Y - main.Height * 0.78));
        string left = $"{toHour / 60:00}:{toHour % 60:00}";
        var sub = Fit("nog " + left + " tot het uur", r * 1.0, r * 0.13, fill);
        g.DrawText(sub, new Point(c.X - sub.Width / 2, c.Y + main.Height * 0.32));
    }

    private static void DrawArc(DrawingContext g, Point c, double rr, double sweep, IPen pen)
    {
        if (sweep <= 0.1) return;
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
        g.DrawGeometry(null, pen, geo);
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
