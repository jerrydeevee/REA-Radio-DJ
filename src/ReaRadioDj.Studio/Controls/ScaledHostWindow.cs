using Avalonia.Controls;
using Avalonia.Media;
using Dock.Avalonia.Controls;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.Controls;

/// <summary>A floating window or workbench that follows the studio's size setting, like the main window.</summary>
public sealed class ScaledHostWindow : HostWindow
{
    protected override Type StyleKeyOverride => typeof(HostWindow);

    private LayoutTransformControl? _zoom;

    public ScaledHostWindow()
    {
        Opened += (_, _) => Wrap();
        Appearance.ScaleChanged += Rescale;
        Closed += (_, _) => Appearance.ScaleChanged -= Rescale;
    }

    private void Wrap()
    {
        if (_zoom is not null || Content is not Control child) return;
        Content = null;
        _zoom = new LayoutTransformControl { Child = child };
        Content = _zoom;
        Rescale();
    }

    private void Rescale()
    {
        if (_zoom is not null) _zoom.LayoutTransform = new ScaleTransform(Appearance.Scale, Appearance.Scale);
    }
}
