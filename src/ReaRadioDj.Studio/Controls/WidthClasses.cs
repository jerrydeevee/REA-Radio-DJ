using Avalonia;
using Avalonia.Controls;

namespace ReaRadioDj.Studio.Controls;

/// <summary>
/// Lets a window's content follow its own width rather than the screen's: with <c>WidthClasses.Track="True"</c> the
/// control gets the class <c>wide</c> (900 px and up), <c>medium</c> (560–899) or <c>narrow</c> (below 560).
/// Styles.axaml hides what is marked <c>wide-only</c> below wide, <c>hide-narrow</c> when narrow, and shows
/// <c>narrow-only</c> only when narrow, so a view drops columns in a fixed order instead of overlapping or scrolling sideways.
/// </summary>
public static class WidthClasses
{
    public const double Wide = 900, Medium = 560;

    public static readonly AttachedProperty<bool> TrackProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Track", typeof(WidthClasses));

    public static bool GetTrack(Control c) => c.GetValue(TrackProperty);
    public static void SetTrack(Control c, bool value) => c.SetValue(TrackProperty, value);

    /// <summary>The class for a width: wide, medium or narrow (no width yet counts as wide).</summary>
    public static string Classify(double width) => width <= 0 || width >= Wide ? "wide" : width >= Medium ? "medium" : "narrow";

    /// <summary>Raised on a tracked control when its class changes (for code-behind that adapts further).</summary>
    public static event Action<Control, string>? Changed;

    static WidthClasses()
    {
        TrackProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.NewValue is not true) return;
            c.SizeChanged += (_, args) => Apply(c, args.NewSize.Width);
            Apply(c, c.Bounds.Width);
        });
    }

    private static void Apply(Control c, double width)
    {
        string now = Classify(width);
        if (c.Classes.Contains(now)) return;
        c.Classes.Remove("wide");
        c.Classes.Remove("medium");
        c.Classes.Remove("narrow");
        c.Classes.Add(now);
        Changed?.Invoke(c, now);
    }
}
