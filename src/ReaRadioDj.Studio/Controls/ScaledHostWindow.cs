using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Dock.Avalonia.Controls;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.Controls;

/// <summary>A floating window or workbench. Its windows follow the studio's size setting through <see cref="ScaledContent"/>.</summary>
public sealed class ScaledHostWindow : HostWindow
{
    protected override Type StyleKeyOverride => typeof(HostWindow);
}

/// <summary>
/// The contents of one studio window at the studio's size setting. The scaling sits inside each window and not around
/// the docking area: Dock works out its drop targets without transforms, so with a scaled docking area a released
/// window landed on another target than the one under the mouse (or on none at all).
/// </summary>
public sealed class ScaledContent : LayoutTransformControl
{
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Appearance.ScaleChanged += Rescale;
        Rescale();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Appearance.ScaleChanged -= Rescale;
        base.OnDetachedFromVisualTree(e);
    }

    private void Rescale() =>
        LayoutTransform = Math.Abs(Appearance.Scale - 1) < 0.001 ? null : new ScaleTransform(Appearance.Scale, Appearance.Scale);
}
