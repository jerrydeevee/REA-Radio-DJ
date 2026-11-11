using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace ReaRadioDj.Studio.Controls;

/// <summary>
/// A rotary knob. Drag up or down (shift = fine), turn the mouse wheel, or use the arrow keys; double-click puts it back
/// on <see cref="Default"/>. The coloured arc runs from the default to the value, so a centred EQ shows no arc at all.
/// </summary>
public sealed class Knob : Control
{
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<Knob, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Knob, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Knob, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> DefaultProperty = AvaloniaProperty.Register<Knob, double>(nameof(Default));
    public static readonly StyledProperty<IBrush?> ArcBrushProperty = AvaloniaProperty.Register<Knob, IBrush?>(nameof(ArcBrush));
    /// <summary>Greyed out, for example while the band is killed.</summary>
    public static readonly StyledProperty<bool> MutedProperty = AvaloniaProperty.Register<Knob, bool>(nameof(Muted));

    private const double Sweep = 270, Start = 135; // degrees; 0 = pointing right, clockwise
    private Point? _dragFrom;
    private double _dragValue;

    static Knob()
    {
        AffectsRender<Knob>(ValueProperty, MinimumProperty, MaximumProperty, ArcBrushProperty, MutedProperty, IsPointerOverProperty);
        FocusableProperty.OverrideDefaultValue<Knob>(true);
        CursorProperty.OverrideDefaultValue<Knob>(new Cursor(StandardCursorType.SizeNorthSouth));
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Default { get => GetValue(DefaultProperty); set => SetValue(DefaultProperty, value); }
    public IBrush? ArcBrush { get => GetValue(ArcBrushProperty); set => SetValue(ArcBrushProperty, value); }
    public bool Muted { get => GetValue(MutedProperty); set => SetValue(MutedProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(44, 44);

    private double Fraction(double v) => Maximum > Minimum ? Math.Clamp((v - Minimum) / (Maximum - Minimum), 0, 1) : 0;
    private void Set(double v) => Value = Math.Clamp(v, Minimum, Maximum);

    private static Point On(Point c, double r, double degrees)
    {
        double a = degrees * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }

    private static Geometry Arc(Point c, double r, double from, double to)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(On(c, r, from), false);
        ctx.ArcTo(On(c, r, to), new Size(r, r), 0, Math.Abs(to - from) > 180, to > from ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
        ctx.EndFigure(false);
        return g;
    }

    private static IBrush Res(string key, string fallback) =>
        Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var r) == true && r is IBrush b ? b : new SolidColorBrush(Color.Parse(fallback));

    public override void Render(DrawingContext ctx)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 8) return;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        double ring = size / 2 - 3, body = ring - 5;

        // track, then the arc from the default to the value
        ctx.DrawGeometry(null, new Pen(Res("ControlBorderBrush", "#3A4454"), 3.5, lineCap: PenLineCap.Round), Arc(c, ring, Start, Start + Sweep));
        double a0 = Start + Sweep * Fraction(Default), a1 = Start + Sweep * Fraction(Value);
        if (Math.Abs(a1 - a0) > 0.5)
        {
            var arc = Muted ? Res("MutedTextBrush", "#9AA4B2") : ArcBrush ?? Res("AccentBrush", "#4C8DFF");
            ctx.DrawGeometry(null, new Pen(arc, 3.5, lineCap: PenLineCap.Round), Arc(c, ring, a0, a1));
        }

        // body with a soft light from the top, and the pointer
        var top = ((Res("ControlBrush", "#1E2530") as ISolidColorBrush)?.Color) ?? Color.Parse("#1E2530");
        var face = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.3, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.2, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Services.Appearance.Mix(top, Colors.White, IsPointerOver ? 0.22 : 0.14), 0), new GradientStop(Services.Appearance.Mix(top, Colors.Black, 0.25), 1) },
        };
        ctx.DrawEllipse(face, new Pen(Res("ControlBorderBrush", "#3A4454"), 1), c, body, body);
        var pointer = Muted ? Res("MutedTextBrush", "#9AA4B2") : Res("TextBrush", "#E6EAF0");
        ctx.DrawLine(new Pen(pointer, 3, lineCap: PenLineCap.Round), On(c, body * 0.25, a1), On(c, body * 0.85, a1));
        if (IsFocused) ctx.DrawEllipse(null, new Pen(Res("AccentBrush", "#4C8DFF"), 1, new DashStyle([2, 2], 0)), c, ring + 2, ring + 2);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (e.ClickCount == 2) { Set(Default); e.Handled = true; return; }
        _dragFrom = e.GetPosition(this);
        _dragValue = Value;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from) return;
        // 200 px up = the whole range (shift: 1000 px, for fine work).
        double px = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1000 : 200;
        double dy = from.Y - e.GetPosition(this).Y;
        Set(_dragValue + dy / px * (Maximum - Minimum));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragFrom = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Set(Value + Math.Sign(e.Delta.Y) * (Maximum - Minimum) / (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 200 : 40));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = (Maximum - Minimum) / 40;
        switch (e.Key)
        {
            case Key.Up or Key.Right: Set(Value + step); break;
            case Key.Down or Key.Left: Set(Value - step); break;
            case Key.Home or Key.Delete or Key.Back: Set(Default); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }
}
