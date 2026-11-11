using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Controls;

/// <summary>
/// The DJ decks as scrolling waveforms above each other, with the playhead in the middle and the beat grid drawn on top.
/// The time axis is real time (playback rate included), so two decks that are in sync have their beat lines on top of
/// each other. Every fourth beat (from the first beat) is drawn stronger. Scroll the wheel to zoom.
/// </summary>
public sealed class DjTwinWave : Control
{
    public static readonly StyledProperty<IReadOnlyList<DjDeckVm>?> DecksProperty =
        AvaloniaProperty.Register<DjTwinWave, IReadOnlyList<DjDeckVm>?>(nameof(Decks));
    public static readonly StyledProperty<int> CountProperty = AvaloniaProperty.Register<DjTwinWave, int>(nameof(Count), 2);
    /// <summary>Seconds of real time across the full width (zoom).</summary>
    public static readonly StyledProperty<double> VisibleSecondsProperty = AvaloniaProperty.Register<DjTwinWave, double>(nameof(VisibleSeconds), 8);

    public const double MinSeconds = 2, MaxSeconds = 30;
    private bool _animating;

    static DjTwinWave()
    {
        AffectsRender<DjTwinWave>(DecksProperty, CountProperty, VisibleSecondsProperty);
    }

    public IReadOnlyList<DjDeckVm>? Decks { get => GetValue(DecksProperty); set => SetValue(DecksProperty, value); }
    public int Count { get => GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public double VisibleSeconds { get => GetValue(VisibleSecondsProperty); set => SetValue(VisibleSecondsProperty, value); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _animating = true;
        Services.Appearance.LookChanged += InvalidateVisual;
        Tick(TimeSpan.Zero);
    }

    private static readonly Color Background = Color.Parse("#0B0F14");

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _animating = false;
        Services.Appearance.LookChanged -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    // Redraw every frame while a deck plays, so the waveforms glide instead of jumping with each engine report.
    private void Tick(TimeSpan _)
    {
        if (!_animating) return;
        if (Decks is { } d && d.Take(Count).Any(x => x.Playing)) InvalidateVisual();
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Tick);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        VisibleSeconds = Math.Clamp(VisibleSeconds * (e.Delta.Y > 0 ? 0.8 : 1.25), MinSeconds, MaxSeconds);
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        ctx.FillRectangle(new SolidColorBrush(Background), new Rect(0, 0, w, h));
        if (Decks is not { Count: > 0 } decks || w < 20 || h < 20) return;
        int n = Math.Clamp(Count, 1, decks.Count);
        double laneH = h / n, mid = w / 2, pxPerSec = w / VisibleSeconds;
        var now = DateTime.UtcNow;
        var label = new Typeface("Inter, Segoe UI", FontStyle.Normal, FontWeight.Bold);

        for (int i = 0; i < n; i++)
        {
            var deck = decks[i];
            double top = i * laneH, centre = top + laneH / 2, half = laneH / 2 - 6;
            if (i > 0) ctx.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#2A313C")), 1), new Point(0, top), new Point(w, top));
            if (!deck.Loaded) { Text(ctx, $"{deck.Id}  leeg", label, 12, new Point(8, top + 4), Color.Parse("#5C6675")); continue; }

            double pos = deck.PositionNow(now), rate = deck.Rate <= 0 ? 1 : deck.Rate;
            double X(double fileSec) => mid + (fileSec - pos) / rate * pxPerSec;
            double fromSec = pos - mid / pxPerSec * rate, toSec = pos + (w - mid) / pxPerSec * rate;

            // waveform: one line per pixel column, the highest peak of the blocks under that column
            if (deck.Peaks is { Length: > 0 } peaks && deck.BlockSeconds > 0)
            {
                double secPerPx = rate / pxPerSec;
                var amps = new double[(int)w];
                for (int x = 0; x < amps.Length; x++)
                {
                    double s0 = fromSec + x * secPerPx, s1 = s0 + secPerPx;
                    if (s1 < 0 || s0 > peaks.Length * deck.BlockSeconds) continue;
                    int b0 = Math.Clamp((int)(s0 / deck.BlockSeconds), 0, peaks.Length - 1);
                    int b1 = Math.Clamp((int)Math.Ceiling(s1 / deck.BlockSeconds), b0 + 1, peaks.Length);
                    ushort max = 0;
                    for (int b = b0; b < b1; b++) if (peaks[b] > max) max = peaks[b];
                    amps[x] = max / 65535.0;
                }
                // The played part (left of the playhead) is dimmed; bars move along with the music.
                WavePaint.Draw(ctx, amps, x => x >= mid, centre, half, deck.Color, Background, 0.45, fromSec / secPerPx);
            }

            // beat grid: thin per beat, strong per bar of four
            if (deck.Bpm > 0)
            {
                double beat = 60 / deck.Bpm;
                if (beat / rate * pxPerSec >= 3)
                {
                    var thin = new Pen(new SolidColorBrush(Colors.White, 0.5), 1);
                    var bar = new Pen(new SolidColorBrush(Colors.White, 0.75), 2);
                    long k0 = (long)Math.Floor((Math.Max(0, fromSec) - deck.FirstBeat) / beat), k1 = (long)Math.Ceiling((toSec - deck.FirstBeat) / beat);
                    for (long k = Math.Max(0, k0); k <= k1; k++)
                    {
                        double x = X(deck.FirstBeat + k * beat);
                        if (x < 0 || x > w) continue;
                        bool strong = k % 4 == 0;
                        ctx.DrawLine(strong ? bar : thin, new Point(x, top + (strong ? 2 : 8)), new Point(x, top + laneH - (strong ? 2 : 8)));
                    }
                }
            }

            // cue and loop markers
            foreach (var m in deck.Markers)
            {
                double x = X(m.Seconds);
                if (x < 0 || x > w) continue;
                ctx.DrawLine(new Pen(new SolidColorBrush(m.Color), 2), new Point(x, top), new Point(x, top + laneH));
            }

            Text(ctx, deck.Id, label, 13, new Point(8, top + 4), deck.Color);
            Text(ctx, deck.BpmText + " BPM", label, 11, new Point(26, top + 6), Color.Parse("#C9D1DC"));
        }

        // playhead
        ctx.DrawLine(new Pen(Avalonia.Media.Brushes.White, 2), new Point(mid, 0), new Point(mid, h));
    }

    private static void Text(DrawingContext ctx, string text, Typeface face, double size, Point at, Color color) =>
        ctx.DrawText(new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size,
            new SolidColorBrush(color)), at);
}
