using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.Controls;

/// <summary>
/// Covers a window that needs a role the signed-in user does not have (Instellingen: administrator; hardware and
/// controls: technician). The engine refuses such changes anyway; this says why before anyone tries.
/// </summary>
public sealed class RoleGate : Panel
{
    private readonly Border _cover;
    private readonly int _rank;

    public RoleGate(Control view, int rank)
    {
        _rank = rank;
        Children.Add(view);
        var text = new TextBlock
        {
            Text = rank >= 2
                ? "Dit venster is alleen voor een beheerder.\nMeld je aan als beheerder via Studio › Aanmelden."
                : "Dit venster is alleen voor een technicus of beheerder.\nMeld je aan via Studio › Aanmelden.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            FontSize = 15,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _cover = new Border { Child = text, Padding = new Thickness(24) };
        _cover.Bind(Border.BackgroundProperty, _cover.GetResourceObservable("AppBrush"));
        Children.Add(_cover);
        Update();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StudioHub.Current.PropertyChanged += OnHub;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StudioHub.Current.PropertyChanged -= OnHub;
    }

    private void OnHub(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StudioHub.IsAdmin) or nameof(StudioHub.IsTechnician)) Update();
    }

    private void Update()
    {
        var hub = StudioHub.Current;
        bool allowed = _rank >= 2 ? hub.IsAdmin : _rank == 1 ? hub.IsTechnician : true;
        _cover.IsVisible = !allowed;
        Children[0].IsEnabled = allowed;
    }
}
