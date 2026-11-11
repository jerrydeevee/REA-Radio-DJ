using Avalonia;
using Avalonia.Media;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.Controls;

/// <summary>
/// Draws a waveform in the look chosen under Instellingen → Uiterlijk (<see cref="Appearance.WaveStyle"/>). The caller
/// works out one amplitude per pixel column (0..1); columns that are not "bright" (outside the cues, or already played)
/// are dimmed afterwards by laying the background over them.
/// </summary>
public static class WavePaint
{
    private static readonly Color Hot = Color.Parse("#FF5A36");
    private static readonly Color Warm = Color.Parse("#FFC145");

    /// <param name="amp">Amplitude per pixel column, 0..1.</param>
    /// <param name="bright">Whether a column is drawn at full strength.</param>
    /// <param name="centre">Y of the middle line.</param>
    /// <param name="half">Height from the middle to the top at amplitude 1.</param>
    /// <param name="phase">Pixels the bars are shifted by, so bars of a scrolling waveform move with the music.</param>
    public static void Draw(DrawingContext ctx, double[] amp, Func<int, bool> bright, double centre, double half,
        Color color, Color background, double dimOpacity, double phase = 0)
    {
        int n = amp.Length;
        if (n == 0) return;
        switch (Appearance.WaveStyle)
        {
            case "gradient": Gradient(ctx, amp, centre, half, color); break;
            case "bars": Bars(ctx, amp, centre, half, color, phase); break;
            case "neon": Neon(ctx, amp, centre, half, color); break;
            case "energy": Energy(ctx, amp, centre, half, color); break;
            default:
                var pen = new Pen(new SolidColorBrush(color), 1);
                for (int x = 0; x < n; x++)
                {
                    double a = Math.Max(0.5, amp[x] * half);
                    ctx.DrawLine(pen, new Point(x + 0.5, centre - a), new Point(x + 0.5, centre + a));
                }
                break;
        }

        // Dim the columns that are not bright: one rectangle per run.
        var veil = new SolidColorBrush(background, 1 - dimOpacity);
        double top = centre - half - 2, height = half * 2 + 4;
        for (int x = 0; x < n;)
        {
            if (bright(x)) { x++; continue; }
            int start = x;
            while (x < n && !bright(x)) x++;
            ctx.FillRectangle(veil, new Rect(start, top, x - start, height));
        }
    }

    /// <summary>Top half with a light tip, bottom half as a shorter, fading reflection.</summary>
    private static void Gradient(DrawingContext ctx, double[] amp, double centre, double half, Color color)
    {
        var up = new Pen(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Appearance.Mix(color, Colors.White, 0.55), 0), new GradientStop(color, 0.55), new GradientStop(Appearance.Mix(color, Colors.Black, 0.25), 1) },
        }, 1);
        var down = new Pen(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(150, color.R, color.G, color.B), 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1) },
        }, 1);
        // The middle line sits a little lower, so the wave above gets more room than its reflection.
        double mid = centre + half * 0.25, upH = half * 1.25 - 2, downH = half * 0.75 - 2;
        for (int x = 0; x < amp.Length; x++)
        {
            double a = Math.Max(1, amp[x] * upH), b = Math.Max(0.5, amp[x] * downH);
            ctx.DrawLine(up, new Point(x + 0.5, mid - a), new Point(x + 0.5, mid));
            ctx.DrawLine(down, new Point(x + 0.5, mid + 1), new Point(x + 0.5, mid + 1 + b));
        }
    }

    /// <summary>Rounded bars of three pixels with a gap, the loudest column of each bar.</summary>
    private static void Bars(DrawingContext ctx, double[] amp, double centre, double half, Color color, double phase)
    {
        const int bar = 3, step = 4;
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Appearance.Mix(color, Colors.White, 0.35), 0), new GradientStop(color, 0.5), new GradientStop(Appearance.Mix(color, Colors.White, 0.35), 1) },
        };
        int shift = (int)Math.Floor(((phase % step) + step) % step);
        for (int x0 = -shift; x0 < amp.Length; x0 += step)
        {
            double a = 0;
            for (int x = Math.Max(0, x0); x < Math.Min(amp.Length, x0 + bar); x++) a = Math.Max(a, amp[x]);
            double h = Math.Max(1.5, a * half);
            ctx.FillRectangle(fill, new Rect(x0, centre - h, bar, h * 2), 1.5f);
        }
    }

    /// <summary>A see-through filled outline with a glowing edge.</summary>
    private static void Neon(DrawingContext ctx, double[] amp, double centre, double half, Color color)
    {
        int n = amp.Length;
        var top = new StreamGeometry();
        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(new Point(0, centre - amp[0] * half), true);
            for (int x = 1; x < n; x++) g.LineTo(new Point(x, centre - amp[x] * half));
            for (int x = n - 1; x >= 0; x--) g.LineTo(new Point(x, centre + amp[x] * half));
            g.EndFigure(true);
        }
        using (var g = top.Open())
        {
            g.BeginFigure(new Point(0, centre - amp[0] * half), false);
            for (int x = 1; x < n; x++) g.LineTo(new Point(x, centre - amp[x] * half));
            g.BeginFigure(new Point(0, centre + amp[0] * half), false);
            for (int x = 1; x < n; x++) g.LineTo(new Point(x, centre + amp[x] * half));
        }
        ctx.DrawGeometry(new SolidColorBrush(color, 0.16), null, shape);
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(color, 0.18), 6, lineJoin: PenLineJoin.Round), top);
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(color, 0.35), 3, lineJoin: PenLineJoin.Round), top);
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Appearance.Mix(color, Colors.White, 0.6)), 1.2, lineJoin: PenLineJoin.Round), top);
        ctx.DrawLine(new Pen(new SolidColorBrush(color, 0.5), 1), new Point(0, centre), new Point(n, centre));
    }

    /// <summary>Classic lines whose colour runs from the base colour through amber to red as the music gets louder.</summary>
    private static void Energy(DrawingContext ctx, double[] amp, double centre, double half, Color color)
    {
        const int levels = 24;
        var pens = new Pen[levels];
        for (int i = 0; i < levels; i++)
        {
            double t = i / (double)(levels - 1);
            var c = t < 0.55 ? color : t < 0.8 ? Appearance.Mix(color, Warm, (t - 0.55) / 0.25) : Appearance.Mix(Warm, Hot, (t - 0.8) / 0.2);
            pens[i] = new Pen(new SolidColorBrush(c), 1);
        }
        for (int x = 0; x < amp.Length; x++)
        {
            double a = Math.Max(0.5, amp[x] * half);
            var pen = pens[Math.Clamp((int)Math.Round(amp[x] * (levels - 1)), 0, levels - 1)];
            ctx.DrawLine(pen, new Point(x + 0.5, centre - a), new Point(x + 0.5, centre + a));
        }
    }
}
