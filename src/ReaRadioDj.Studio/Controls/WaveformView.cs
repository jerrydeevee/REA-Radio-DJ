using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ReaRadioDj.Studio.Controls;

public sealed record WaveMarker(string Key, string Label, double Seconds, Color Color);

/// <summary>
/// Waveform with cue markers. Drag a marker to move it, click to place the cursor, mouse wheel to zoom around the
/// pointer, shift + wheel to scroll. Peaks are linear (0..65535), one per block.
/// </summary>
public sealed class WaveformView : Control
{
    public static readonly StyledProperty<ushort[]?> PeaksProperty = AvaloniaProperty.Register<WaveformView, ushort[]?>(nameof(Peaks));
    public static readonly StyledProperty<double> BlockSecondsProperty = AvaloniaProperty.Register<WaveformView, double>(nameof(BlockSeconds), 0.02);
    public static readonly StyledProperty<IReadOnlyList<WaveMarker>?> MarkersProperty = AvaloniaProperty.Register<WaveformView, IReadOnlyList<WaveMarker>?>(nameof(Markers));
    public static readonly StyledProperty<double> CursorSecondsProperty = AvaloniaProperty.Register<WaveformView, double>(nameof(CursorSeconds), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> PlayheadProperty = AvaloniaProperty.Register<WaveformView, double>(nameof(Playhead), -1);
    public static readonly StyledProperty<double> CueInProperty = AvaloniaProperty.Register<WaveformView, double>(nameof(CueIn));
    public static readonly StyledProperty<double> CueOutProperty = AvaloniaProperty.Register<WaveformView, double>(nameof(CueOut));
    public static readonly StyledProperty<string> EmptyTextProperty = AvaloniaProperty.Register<WaveformView, string>(nameof(EmptyText), "Kies een item in de bibliotheek en klik op Cues");
    public static readonly StyledProperty<bool> ShowCursorProperty = AvaloniaProperty.Register<WaveformView, bool>(nameof(ShowCursor), true);
    /// <summary>Player mode: markers cannot be dragged and a click raises <see cref="Clicked"/> (seek).</summary>
    public static readonly StyledProperty<bool> ReadOnlyMarkersProperty = AvaloniaProperty.Register<WaveformView, bool>(nameof(ReadOnlyMarkers));
    /// <summary>Executed with the clicked position in seconds (read-only mode), next to <see cref="Clicked"/>.</summary>
    public static readonly StyledProperty<System.Windows.Input.ICommand?> ClickCommandProperty =
        AvaloniaProperty.Register<WaveformView, System.Windows.Input.ICommand?>(nameof(ClickCommand));

    static WaveformView()
    {
        AffectsRender<WaveformView>(PeaksProperty, MarkersProperty, CursorSecondsProperty, PlayheadProperty, CueInProperty, CueOutProperty, EmptyTextProperty);
        PeaksProperty.Changed.AddClassHandler<WaveformView>((w, _) => w.ResetZoom());
        ClipToBoundsProperty.OverrideDefaultValue<WaveformView>(true);
        FocusableProperty.OverrideDefaultValue<WaveformView>(true);
    }

    public ushort[]? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
    public double BlockSeconds { get => GetValue(BlockSecondsProperty); set => SetValue(BlockSecondsProperty, value); }
    public IReadOnlyList<WaveMarker>? Markers { get => GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }
    public double CursorSeconds { get => GetValue(CursorSecondsProperty); set => SetValue(CursorSecondsProperty, value); }
    public double Playhead { get => GetValue(PlayheadProperty); set => SetValue(PlayheadProperty, value); }
    public double CueIn { get => GetValue(CueInProperty); set => SetValue(CueInProperty, value); }
    public double CueOut { get => GetValue(CueOutProperty); set => SetValue(CueOutProperty, value); }
    public string EmptyText { get => GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    public bool ShowCursor { get => GetValue(ShowCursorProperty); set => SetValue(ShowCursorProperty, value); }
    public bool ReadOnlyMarkers { get => GetValue(ReadOnlyMarkersProperty); set => SetValue(ReadOnlyMarkersProperty, value); }
    public System.Windows.Input.ICommand? ClickCommand { get => GetValue(ClickCommandProperty); set => SetValue(ClickCommandProperty, value); }

    /// <summary>Raised while and after a marker is dragged: key, new position in seconds.</summary>
    public event Action<string, double>? MarkerMoved;
    /// <summary>Raised on a click when <see cref="ReadOnlyMarkers"/> is set: position in seconds.</summary>
    public event Action<double>? Clicked;

    private double _viewStart, _viewLength = 1;
    private string? _dragKey;

    private double Total => Peaks is { Length: > 0 } p ? p.Length * BlockSeconds : 1;

    public void ResetZoom()
    {
        _viewStart = 0;
        _viewLength = Total;
        InvalidateVisual();
    }

    /// <summary>Zooms to a window around a position (for example the outro: the last 20 seconds).</summary>
    public void ZoomTo(double start, double length)
    {
        _viewLength = Math.Clamp(length, 1, Total);
        _viewStart = Math.Clamp(start, 0, Math.Max(0, Total - _viewLength));
        InvalidateVisual();
    }

    private double X(double seconds) => (seconds - _viewStart) / _viewLength * Bounds.Width;
    private double Seconds(double x) => _viewStart + x / Math.Max(1, Bounds.Width) * _viewLength;

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#0E1218")), new Rect(Bounds.Size));
        var peaks = Peaks;
        if (peaks is null || peaks.Length == 0 || w < 2)
        {
            var t = new FormattedText(EmptyText, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, 13, new SolidColorBrush(Color.Parse("#8B95A5")));
            ctx.DrawText(t, new Point(10, h / 2 - 8));
            return;
        }

        double mid = h / 2;
        var wave = new Pen(new SolidColorBrush(Color.Parse("#3FA7FF")), 1);
        var dim = new Pen(new SolidColorBrush(Color.Parse("#2A4A66")), 1);
        for (int x = 0; x < (int)w; x++)
        {
            double s0 = Seconds(x), s1 = Seconds(x + 1);
            int b0 = Math.Clamp((int)(s0 / BlockSeconds), 0, peaks.Length - 1);
            int b1 = Math.Clamp((int)Math.Ceiling(s1 / BlockSeconds), b0 + 1, peaks.Length);
            ushort max = 0;
            for (int b = b0; b < b1; b++) if (peaks[b] > max) max = peaks[b];
            double amp = max / 65535.0 * (mid - 4);
            bool inside = s0 >= CueIn && s0 <= CueOut;
            ctx.DrawLine(inside ? wave : dim, new Point(x + 0.5, mid - amp), new Point(x + 0.5, mid + amp));
        }

        // Time ruler
        var tick = new Pen(new SolidColorBrush(Color.Parse("#2A3240")), 1);
        double step = NiceStep(_viewLength / Math.Max(1, w / 90));
        for (double t = Math.Ceiling(_viewStart / step) * step; t < _viewStart + _viewLength; t += step)
        {
            double x = X(t);
            ctx.DrawLine(tick, new Point(x, h - 14), new Point(x, h));
            var label = new FormattedText($"{(int)(t / 60)}:{t % 60:00.#}", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 10, new SolidColorBrush(Color.Parse("#6B7585")));
            ctx.DrawText(label, new Point(x + 2, h - 14));
        }

        // Markers
        int row = 0;
        foreach (var m in (Markers ?? []).OrderBy(m => m.Seconds))
        {
            double x = X(m.Seconds);
            if (x < -2 || x > w + 2) continue;
            var brush = new SolidColorBrush(m.Color);
            ctx.DrawLine(new Pen(brush, _dragKey == m.Key ? 3 : 2), new Point(x, 0), new Point(x, h - 16));
            var text = new FormattedText(m.Label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold), 11, Brushes.White);
            double y = 2 + (row++ % 3) * 15;
            ctx.FillRectangle(brush, new Rect(x + 1, y, text.Width + 6, 14), 2);
            ctx.DrawText(text, new Point(x + 4, y));
        }

        // Cursor and playhead
        double cx = X(CursorSeconds);
        if (ShowCursor) ctx.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#F0B429")), 1, new DashStyle([4, 3], 0)), new Point(cx, 0), new Point(cx, h));
        if (Playhead >= 0)
        {
            double px = X(Playhead);
            ctx.DrawLine(new Pen(Brushes.White, 1.5), new Point(px, 0), new Point(px, h));
        }
    }

    private static double NiceStep(double raw)
    {
        foreach (var s in new[] { 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300 }) if (s >= raw) return s;
        return 600;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var x = e.GetPosition(this).X;
        if (ReadOnlyMarkers)
        {
            if (Peaks is { Length: > 0 })
            {
                double at = Math.Clamp(Seconds(x), 0, Total);
                Clicked?.Invoke(at);
                if (ClickCommand?.CanExecute(at) == true) ClickCommand.Execute(at);
            }
            e.Handled = true;
            return;
        }
        _dragKey = (Markers ?? []).Select(m => (m, d: Math.Abs(X(m.Seconds) - x))).Where(t => t.d < 7).OrderBy(t => t.d).Select(t => t.m.Key).FirstOrDefault();
        if (_dragKey is null) CursorSeconds = Math.Clamp(Seconds(x), 0, Total);
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;
        if (_dragKey is not null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            MarkerMoved?.Invoke(_dragKey, Math.Round(Math.Clamp(Seconds(x), 0, Total), 3));
        else if (_dragKey is null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            CursorSeconds = Math.Clamp(Seconds(x), 0, Total);
        Cursor = (Markers ?? []).Any(m => Math.Abs(X(m.Seconds) - x) < 7) ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragKey = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double at = Seconds(e.GetPosition(this).X);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _viewStart = Math.Clamp(_viewStart - e.Delta.Y * _viewLength * 0.1, 0, Math.Max(0, Total - _viewLength));
        }
        else
        {
            double factor = e.Delta.Y > 0 ? 0.8 : 1.25;
            double length = Math.Clamp(_viewLength * factor, 0.5, Total);
            double rel = (at - _viewStart) / _viewLength;
            _viewLength = length;
            _viewStart = Math.Clamp(at - rel * length, 0, Math.Max(0, Total - length));
        }
        e.Handled = true;
        InvalidateVisual();
    }
}
