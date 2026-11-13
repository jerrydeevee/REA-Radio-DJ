using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ReaRadioDj.Studio.Controls;

/// <summary>
/// A stereo level meter in dBFS (-48 to 0): lit segments in green, amber from -9 dB and red from -3 dB, unlit segments
/// faintly, and a peak mark that holds for a moment and then falls. <see cref="Left"/> and <see cref="Right"/> are the
/// linear peak levels (0..1) as the engine sends them, five times a second; the meter rises at once and falls smoothly
/// in between, so it moves like a hardware meter instead of jumping.
/// </summary>
public sealed class LevelMeter : Control
{
    public static readonly StyledProperty<double> LeftProperty = AvaloniaProperty.Register<LevelMeter, double>(nameof(Left));
    public static readonly StyledProperty<double> RightProperty = AvaloniaProperty.Register<LevelMeter, double>(nameof(Right));
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<LevelMeter, Orientation>(nameof(Orientation), Orientation.Horizontal);
    /// <summary>Draw the dB marks (-40, -20, -9, -3, 0) next to the bars when there is room.</summary>
    public static readonly StyledProperty<bool> ShowScaleProperty = AvaloniaProperty.Register<LevelMeter, bool>(nameof(ShowScale));
    /// <summary>One bar (the louder side) instead of two.</summary>
    public static readonly StyledProperty<bool> MonoProperty = AvaloniaProperty.Register<LevelMeter, bool>(nameof(Mono));

    /// <summary>The value that means full scale (0 dBFS): 1 for the engine's levels, 100 for view models that keep percentages.</summary>
    public static readonly StyledProperty<double> FullScaleProperty = AvaloniaProperty.Register<LevelMeter, double>(nameof(FullScale), 1);
    public double FullScale { get => GetValue(FullScaleProperty); set => SetValue(FullScaleProperty, value); }

    public double Left { get => GetValue(LeftProperty); set => SetValue(LeftProperty, value); }
    public double Right { get => GetValue(RightProperty); set => SetValue(RightProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public bool ShowScale { get => GetValue(ShowScaleProperty); set => SetValue(ShowScaleProperty, value); }
    public bool Mono { get => GetValue(MonoProperty); set => SetValue(MonoProperty, value); }

    public const double FloorDb = -48, AmberDb = -9, RedDb = -3;
    private const double FallDbPerSecond = 24, HoldSeconds = 1.2, PeakFallDbPerSecond = 12;

    private static readonly Color Green = Color.Parse("#3FB950"), Amber = Color.Parse("#F5A524"), Red = Color.Parse("#F25F5C");
    private static readonly IBrush GreenOn = new SolidColorBrush(Green), AmberOn = new SolidColorBrush(Amber), RedOn = new SolidColorBrush(Red);
    private static readonly IBrush GreenOff = new SolidColorBrush(Green, 0.14), AmberOff = new SolidColorBrush(Amber, 0.14), RedOff = new SolidColorBrush(Red, 0.16);
    private static readonly IBrush ScaleBrush = new SolidColorBrush(Color.Parse("#8B95A5"));
    private static readonly double[] Marks = [-40, -20, -9, -3, 0];

    private readonly Side[] _sides = [new(), new()];
    private DispatcherTimer? _timer;
    private DateTime _last = DateTime.UtcNow;

    private sealed class Side
    {
        public double Shown = FloorDb, Peak = FloorDb, Target = FloorDb;
        public DateTime PeakAt;
    }

    static LevelMeter()
    {
        AffectsRender<LevelMeter>(OrientationProperty, ShowScaleProperty, MonoProperty);
        LeftProperty.Changed.AddClassHandler<LevelMeter>((m, _) => m.Feed());
        RightProperty.Changed.AddClassHandler<LevelMeter>((m, _) => m.Feed());
    }

    /// <summary>Linear level (0..1) to dBFS, clamped to the meter's range.</summary>
    public static double ToDb(double linear) => linear <= 0.0000001 ? FloorDb : Math.Clamp(20 * Math.Log10(linear), FloorDb, 0);

    private void Feed()
    {
        double fs = FullScale > 0 ? FullScale : 1;
        _sides[0].Target = ToDb(Left / fs);
        _sides[1].Target = ToDb(Right / fs);
        Tick();
        // Only animate while there is something to show; a silent meter costs nothing.
        if (_timer is null && VisualRoot is not null)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => Tick());
            _timer.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _last).TotalSeconds, 0, 0.5);
        _last = now;
        bool moving = false;
        foreach (var s in _sides)
        {
            // rise at once, fall at a steady rate
            s.Shown = s.Target >= s.Shown ? s.Target : Math.Max(s.Target, s.Shown - FallDbPerSecond * dt);
            if (s.Shown >= s.Peak) { s.Peak = s.Shown; s.PeakAt = now; }
            else if ((now - s.PeakAt).TotalSeconds > HoldSeconds) s.Peak = Math.Max(s.Shown, s.Peak - PeakFallDbPerSecond * dt);
            moving |= s.Shown > FloorDb || s.Peak > FloorDb;
        }
        InvalidateVisual();
        if (!moving && _timer is not null) { _timer.Stop(); _timer = null; }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        bool h = Orientation == Orientation.Horizontal;
        double thick = Mono ? 6 : 13;
        return h ? new Size(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, thick)
                 : new Size(thick + (ShowScale ? 18 : 0), double.IsInfinity(availableSize.Height) ? 120 : availableSize.Height);
    }

    private static double Fraction(double db) => (db - FloorDb) / -FloorDb;

    public override void Render(DrawingContext g)
    {
        var b = Bounds;
        if (b.Width < 2 || b.Height < 2) return;
        bool h = Orientation == Orientation.Horizontal;
        int bars = Mono ? 1 : 2;
        double scale = !h && ShowScale && b.Width >= 22 ? 16 : 0;
        double length = h ? b.Width : b.Height;
        double across = (h ? b.Height : b.Width - scale);
        double gap = bars == 2 ? Math.Max(1, across * 0.12) : 0;
        double thick = (across - gap) / bars;
        // segments of about 3 px with a 1 px gap; very short meters draw one continuous bar
        double step = length >= 60 ? Math.Max(3, length / 40) : 0;

        for (int i = 0; i < bars; i++)
        {
            var side = Mono ? (_sides[0].Shown >= _sides[1].Shown ? _sides[0] : _sides[1]) : _sides[i];
            double off = scale + i * (thick + gap);
            Rect Along(double from, double to) => h
                ? new Rect(b.Width * from, off - scale, b.Width * (to - from), thick)
                : new Rect(off, b.Height * (1 - to), thick, b.Height * (to - from));

            double lit = Fraction(side.Shown);
            if (step > 0)
            {
                int count = (int)Math.Floor(length / step);
                for (int k = 0; k < count; k++)
                {
                    double f0 = k * step / length, f1 = (k * step + step - 1) / length;
                    double db = FloorDb + f1 * -FloorDb;
                    bool on = f1 <= lit + 0.0001;
                    var brush = db > RedDb ? (on ? RedOn : RedOff) : db > AmberDb ? (on ? AmberOn : AmberOff) : (on ? GreenOn : GreenOff);
                    g.DrawRectangle(brush, null, Along(f0, f1));
                }
            }
            else
            {
                g.DrawRectangle(GreenOff, null, Along(0, 1));
                double a = Fraction(AmberDb), r = Fraction(RedDb);
                if (lit > 0) g.DrawRectangle(GreenOn, null, Along(0, Math.Min(lit, a)));
                if (lit > a) g.DrawRectangle(AmberOn, null, Along(a, Math.Min(lit, r)));
                if (lit > r) g.DrawRectangle(RedOn, null, Along(r, lit));
            }
            if (side.Peak > FloorDb + 1)
            {
                double p = Fraction(side.Peak), w = Math.Max(2, length * 0.012) / length;
                var pb = side.Peak > RedDb ? RedOn : side.Peak > AmberDb ? AmberOn : GreenOn;
                g.DrawRectangle(pb, null, Along(Math.Max(0, p - w), p));
            }
        }

        if (scale > 0)
        {
            // from the top down; a mark that would touch the one above is left out (a short meter shows fewer marks)
            double lastBottom = double.NegativeInfinity;
            foreach (var db in Marks.Reverse())
            {
                var t = new FormattedText(db == 0 ? "0" : ((int)-db).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 9, ScaleBrush);
                double top = Math.Clamp(b.Height * (1 - Fraction(db)) - t.Height / 2, 0, b.Height - t.Height);
                if (top < lastBottom + 1) continue;
                g.DrawText(t, new Point(scale - 3 - t.Width, top));
                lastBottom = top + t.Height;
            }
        }
    }
}
