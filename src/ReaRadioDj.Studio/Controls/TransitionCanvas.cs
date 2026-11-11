using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ReaRadioDj.Studio.Controls;

/// <summary>Everything the transition timeline shows and edits. Times are in each item's own seconds.</summary>
public sealed class TransitionState
{
    public ushort[] OutPeaks = [];
    public ushort[] InPeaks = [];
    public double BlockSeconds = 0.02;
    public string OutLabel = "", InLabel = "";
    public double OutCueIn, OutCueOut, OutMix, InCueIn, InCueOut;
    public double StartAt, From;
    public List<(double Seconds, double Gain)> Out = [];
    public List<(double Seconds, double Gain)> In = [];
    public string? InsertLabel;
    public double InsertAt, InsertLength;
    /// <summary>Pre-listen position in the outgoing item's time, or NaN.</summary>
    public double Playhead = double.NaN;

    /// <summary>The outgoing item's time of a moment in the incoming item.</summary>
    public double ToOut(double inSeconds) => StartAt + (inSeconds - From);
    public double ToIn(double outSeconds) => From + (outSeconds - StartAt);
}

/// <summary>
/// Transition timeline: the outgoing item on top, an insert lane, the incoming item below, on one time axis (the
/// outgoing item's time). Drag the incoming item sideways to change where it starts; drag the insert; drag the
/// points of the volume lines; double-click a lane to add a point, right-click a point to remove it. Mouse wheel zooms,
/// shift + wheel scrolls.
/// </summary>
public sealed class TransitionCanvas : Control
{
    public static readonly StyledProperty<TransitionState?> StateProperty = AvaloniaProperty.Register<TransitionCanvas, TransitionState?>(nameof(State));
    public static readonly StyledProperty<int> VersionProperty = AvaloniaProperty.Register<TransitionCanvas, int>(nameof(Version));

    static TransitionCanvas()
    {
        AffectsRender<TransitionCanvas>(StateProperty, VersionProperty);
        StateProperty.Changed.AddClassHandler<TransitionCanvas>((c, _) => c.Frame());
        ClipToBoundsProperty.OverrideDefaultValue<TransitionCanvas>(true);
        FocusableProperty.OverrideDefaultValue<TransitionCanvas>(true);
    }

    public TransitionState? State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public int Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }

    /// <summary>Raised when an edit is finished (mouse released).</summary>
    public event Action? Edited;

    private double _viewStart, _viewLength = 30;
    private enum Lane { Out, Insert, In }
    private (Lane Lane, int Index)? _dragPoint;
    private bool _dragIn, _dragInsert, _pan;
    private double _grabSeconds, _grabValue;

    private static IBrush Back => new SolidColorBrush(Services.Appearance.Surface.Back);
    private static IBrush LaneBack => new SolidColorBrush(Services.Appearance.Surface.Lane);
    private static readonly IBrush OutWave = new SolidColorBrush(Color.Parse("#4FA3E0"));
    private static readonly IBrush InWave = new SolidColorBrush(Color.Parse("#6CC56C"));
    private static IBrush Dim => new SolidColorBrush(Services.Appearance.Surface.Faint);
    private static IBrush Text => new SolidColorBrush(Services.Appearance.Surface.Muted);
    private static readonly IBrush InsertFill = new SolidColorBrush(Color.Parse("#80F0B429"));
    private static readonly IPen Line = new Pen(new SolidColorBrush(Color.Parse("#F0B429")), 1.6);
    private static readonly IPen StartPen = new Pen(new SolidColorBrush(Color.Parse("#F25F5C")), 1.5);
    private static IPen GridPen => new Pen(new SolidColorBrush(Services.Appearance.Surface.Grid), 1);
    private static readonly IPen PlayPen = new Pen(Brushes.White, 1.5);

    /// <summary>Shows 15 seconds before and after the moment the incoming item starts.</summary>
    public void Frame()
    {
        if (State is not { } s) return;
        _viewLength = 30;
        _viewStart = s.StartAt - 15;
        InvalidateVisual();
    }

    private double X(double seconds) => (seconds - _viewStart) / _viewLength * Bounds.Width;
    private double Seconds(double x) => _viewStart + x / Math.Max(1, Bounds.Width) * _viewLength;

    private (double Top, double Height) LaneRect(Lane lane)
    {
        double h = Bounds.Height, ins = Math.Min(34, h * 0.16), wave = (h - ins - 18) / 2;
        return lane switch { Lane.Out => (16, wave), Lane.Insert => (16 + wave, ins), _ => (16 + wave + ins, wave) };
    }

    private static double Y(double gain, (double Top, double Height) r) => r.Top + r.Height - gain / 1.5 * r.Height;
    private static double Gain(double y, (double Top, double Height) r) => Math.Clamp((r.Top + r.Height - y) / r.Height * 1.5, 0, 1.5);

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Back, new Rect(Bounds.Size));
        if (State is not { } s)
        {
            Label(ctx, "Kies een overgang", 8, 8, Text);
            return;
        }
        // seconds grid
        double step = _viewLength > 60 ? 10 : _viewLength > 20 ? 5 : 1;
        // ticks counted from the moment the next item starts (0), so the labels are round numbers
        for (double t = s.StartAt + Math.Ceiling((_viewStart - s.StartAt) / step) * step; t < _viewStart + _viewLength; t += step)
        {
            double x = X(t);
            ctx.DrawLine(GridPen, new Point(x, 14), new Point(x, Bounds.Height));
            Label(ctx, $"{t - s.StartAt:+0;-0;0}", x + 2, 0, Dim);
        }

        var outLane = LaneRect(Lane.Out);
        var inLane = LaneRect(Lane.In);
        var insLane = LaneRect(Lane.Insert);
        ctx.FillRectangle(LaneBack, new Rect(0, outLane.Top, Bounds.Width, outLane.Height));
        ctx.FillRectangle(LaneBack, new Rect(0, inLane.Top, Bounds.Width, inLane.Height));

        Wave(ctx, s.OutPeaks, s, outLane, 0, s.OutCueIn, s.OutCueOut, OutWave);
        Wave(ctx, s.InPeaks, s, inLane, s.StartAt - s.From, s.From, s.InCueOut, InWave);

        Envelope(ctx, s.Out, outLane, t => t);
        Envelope(ctx, s.In, inLane, s.ToOut);

        if (s.InsertLabel is not null)
        {
            var r = new Rect(X(s.InsertAt), insLane.Top + 3, Math.Max(4, X(s.InsertAt + s.InsertLength) - X(s.InsertAt)), insLane.Height - 6);
            ctx.FillRectangle(InsertFill, r, 3);
            Label(ctx, s.InsertLabel, r.X + 4, r.Y + 2, Brushes.Black);
        }
        else Label(ctx, "geen insert", 6, insLane.Top + insLane.Height / 2 - 7, Dim);

        double sx = X(s.StartAt);
        ctx.DrawLine(StartPen, new Point(sx, 14), new Point(sx, Bounds.Height));
        double mx = X(s.OutMix);
        ctx.DrawLine(new Pen(Dim, 1, new DashStyle([4, 4], 0)), new Point(mx, outLane.Top), new Point(mx, outLane.Top + outLane.Height));
        Label(ctx, s.OutLabel, 6, outLane.Top + 2, Text);
        Label(ctx, s.InLabel, Math.Max(6, sx + 4), inLane.Top + 2, Text);

        if (!double.IsNaN(s.Playhead))
        {
            double px = X(s.Playhead);
            ctx.DrawLine(PlayPen, new Point(px, 14), new Point(px, Bounds.Height));
        }
    }

    private void Wave(DrawingContext ctx, ushort[] peaks, TransitionState s, (double Top, double Height) lane, double offset,
        double from, double to, IBrush brush)
    {
        if (peaks.Length == 0) return;
        double mid = lane.Top + lane.Height / 2;
        for (int px = 0; px < (int)Bounds.Width; px++)
        {
            double t0 = Seconds(px) - offset, t1 = Seconds(px + 1) - offset;
            int b0 = (int)(t0 / s.BlockSeconds), b1 = Math.Max(b0 + 1, (int)(t1 / s.BlockSeconds));
            if (b1 <= 0 || b0 >= peaks.Length) continue;
            ushort peak = 0;
            for (int b = Math.Max(0, b0); b < Math.Min(peaks.Length, b1); b++) peak = Math.Max(peak, peaks[b]);
            double half = peak / 65535.0 * lane.Height / 2;
            var fill = t0 >= from && t0 <= to ? brush : Dim;
            ctx.FillRectangle(fill, new Rect(px, mid - half, 1, Math.Max(1, half * 2)));
        }
    }

    private void Envelope(DrawingContext ctx, List<(double Seconds, double Gain)> points, (double Top, double Height) lane, Func<double, double> toX)
    {
        if (points.Count == 0)
        {
            ctx.DrawLine(new Pen(Line.Brush, 1, new DashStyle([3, 3], 0)), new Point(0, Y(1, lane)), new Point(Bounds.Width, Y(1, lane)));
            return;
        }
        var prev = new Point(0, Y(points[0].Gain, lane));
        foreach (var p in points)
        {
            var pt = new Point(X(toX(p.Seconds)), Y(p.Gain, lane));
            ctx.DrawLine(Line, prev, pt);
            prev = pt;
        }
        ctx.DrawLine(Line, prev, new Point(Bounds.Width, prev.Y));
        foreach (var p in points)
            ctx.FillRectangle(Line.Brush!, new Rect(X(toX(p.Seconds)) - 4, Y(p.Gain, lane) - 4, 8, 8));
    }

    private static void Label(DrawingContext ctx, string text, double x, double y, IBrush brush) =>
        ctx.DrawText(new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, brush),
            new Point(x, y));

    // ------------------------------------------------------------------ editing

    private Lane? LaneAt(double y)
    {
        foreach (var lane in new[] { Lane.Out, Lane.Insert, Lane.In })
        {
            var r = LaneRect(lane);
            if (y >= r.Top && y <= r.Top + r.Height) return lane;
        }
        return null;
    }

    private List<(double Seconds, double Gain)>? Points(Lane lane) => lane switch { Lane.Out => State?.Out, Lane.In => State?.In, _ => null };
    private double ToX(Lane lane, double t) => X(lane == Lane.In ? State!.ToOut(t) : t);
    private double FromX(Lane lane, double x) => lane == Lane.In ? State!.ToIn(Seconds(x)) : Seconds(x);

    private int HitPoint(Lane lane, Point p)
    {
        if (Points(lane) is not { } pts) return -1;
        var r = LaneRect(lane);
        for (int i = 0; i < pts.Count; i++)
            if (Math.Abs(ToX(lane, pts[i].Seconds) - p.X) <= 6 && Math.Abs(Y(pts[i].Gain, r) - p.Y) <= 6) return i;
        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (State is not { } s) return;
        Focus();
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (LaneAt(p.Y) is not { } lane) return;
        int hit = HitPoint(lane, p);

        if (props.IsRightButtonPressed)
        {
            if (hit >= 0) { Points(lane)!.RemoveAt(hit); InvalidateVisual(); Edited?.Invoke(); }
            return;
        }
        if (e.ClickCount == 2 && hit < 0 && Points(lane) is { } pts)
        {
            pts.Add((Math.Round(FromX(lane, p.X), 2), Math.Round(Gain(p.Y, LaneRect(lane)), 2)));
            pts.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
            InvalidateVisual();
            Edited?.Invoke();
            return;
        }
        _grabSeconds = Seconds(p.X);
        if (hit >= 0) _dragPoint = (lane, hit);
        else if (lane == Lane.In) { _dragIn = true; _grabValue = s.StartAt; }
        else if (lane == Lane.Insert && s.InsertLabel is not null) { _dragInsert = true; _grabValue = s.InsertAt; }
        else { _pan = true; _grabValue = _viewStart; }
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (State is not { } s) return;
        var p = e.GetPosition(this);
        double delta = Seconds(p.X) - _grabSeconds;
        if (_dragPoint is var (lane, index) && Points(lane) is { } pts && index < pts.Count)
        {
            double t = Math.Round(Math.Max(0, FromX(lane, p.X)), 2);
            // keep the order: a point cannot pass its neighbours
            if (index > 0) t = Math.Max(t, pts[index - 1].Seconds);
            if (index < pts.Count - 1) t = Math.Min(t, pts[index + 1].Seconds);
            pts[index] = (t, Math.Round(Gain(p.Y, LaneRect(lane)), 2));
        }
        else if (_dragIn) s.StartAt = Math.Round(Math.Clamp(_grabValue + delta, s.OutCueIn, s.OutCueOut), 2);
        else if (_dragInsert) s.InsertAt = Math.Round(Math.Clamp(_grabValue + delta, s.OutCueIn, s.OutCueOut), 2);
        else if (_pan) _viewStart -= delta; // keeps the grabbed moment under the pointer
        else return;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        bool edited = _dragPoint is not null || _dragIn || _dragInsert;
        _dragPoint = null;
        _dragIn = _dragInsert = _pan = false;
        e.Pointer.Capture(null);
        if (edited) Edited?.Invoke();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double at = Seconds(e.GetPosition(this).X);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _viewStart -= e.Delta.Y * _viewLength * 0.1;
        else
        {
            double factor = e.Delta.Y > 0 ? 0.8 : 1.25;
            double len = Math.Clamp(_viewLength * factor, 2, 600);
            _viewStart = at - (at - _viewStart) * len / _viewLength;
            _viewLength = len;
        }
        InvalidateVisual();
        e.Handled = true;
    }
}
