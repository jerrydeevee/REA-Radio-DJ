using CommunityToolkit.Mvvm.Input;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// The first tab of a workbench: an extra studio window, usually filling a screen of its own, with its own docking
/// area. Windows are dragged onto it by their tab, or opened here with one click. The tab can be closed once other
/// windows are on the workbench.
/// </summary>
public sealed partial class WorkbenchTool : StudioTool
{
    public WorkbenchTool() : base("werkblad", "Werkblad") { }

    /// <summary>Asks the main window to move (or open) a studio window onto this workbench.</summary>
    public static event Action<WorkbenchTool, string>? OpenRequested;

    public IReadOnlyList<WindowEntry> Choices { get; } = DockFactory.Windows.Select(w => new WindowEntry(w.Id, w.Name)).ToList();

    [RelayCommand]
    private void OpenHere(string? id)
    {
        if (!string.IsNullOrEmpty(id)) OpenRequested?.Invoke(this, id);
    }
}
