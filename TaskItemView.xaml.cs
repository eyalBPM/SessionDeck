using System.Windows;
using System.Windows.Controls;
using SessionDeck.ViewModels;

namespace SessionDeck;

/// <summary>One task card (T-0116). All actions delegate to MainWindow — same
/// code-behind pattern as WorkspaceCardView.</summary>
public partial class TaskItemView : UserControl
{
    private TaskItemViewModel? Vm => DataContext as TaskItemViewModel;
    private MainWindow? Owner => Window.GetWindow(this) as MainWindow;

    public TaskItemView() => InitializeComponent();

    private void Target_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Owner?.HandleTaskActivate(Vm, TargetButton);
    }

    private void Url_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null) Owner?.OpenTaskUrl(Vm);
    }
}
