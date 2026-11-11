using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio;

/// <summary>Finds the view for a window: ViewModels.XTool -> Views.XView.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null) return null;
        var name = data.GetType().FullName!.Replace(".ViewModels.", ".Views.").Replace("Tool", "View");
        var type = Type.GetType(name);
        // scaled per window, not around the docking area (see ScaledContent)
        Control view = type is null ? new TextBlock { Text = $"Geen weergave voor {name}" } : (Control)Activator.CreateInstance(type)!;
        return new Controls.ScaledContent { Child = view };
    }

    public bool Match(object? data) => data is StudioTool;
}
