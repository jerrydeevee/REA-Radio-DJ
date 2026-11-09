using Avalonia.Controls;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class TransitionView : UserControl
{
    public TransitionView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        Canvas.Edited += () => (DataContext as TransitionTool)?.Changed();
    }
}
