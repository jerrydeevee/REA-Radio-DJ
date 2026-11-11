using Dock.Model.Mvvm.Controls;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Base of every studio window. A window can be docked, tabbed, floated or closed; it is (re)created from a saved
/// layout with its parameterless constructor, so all runtime state comes from <see cref="StudioHub"/>.
/// Only the layout fields of the Dock base class are written to layout files.
/// </summary>
public abstract class StudioTool : Tool
{
    private bool _attached;

    protected StudioTool(string id, string title)
    {
        Id = id;
        Title = title;
    }

    protected static StudioHub Hub => StudioHub.Current;

    /// <summary>Called by the view when it is shown; subscribes to the live state the first time.</summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        Hub.StateReceived += OnState;
        if (Hub.State is { } s) OnState(s);
        OnFirstShow();
    }

    protected virtual void OnFirstShow() { }

    protected virtual void OnState(StateDto state) { }
}
