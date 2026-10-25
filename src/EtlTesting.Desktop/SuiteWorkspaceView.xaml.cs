using System.Windows;
using System.Windows.Controls;
namespace EtlTesting.Desktop;
public partial class SuiteWorkspaceView : UserControl
{
    public SuiteWorkspaceView() => InitializeComponent();
    private void OnSuiteMenu(object sender, RoutedEventArgs e) { if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.IsOpen = true; } }
    private async void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && DataContext is StudioViewModel vm) try { await vm.RefreshLibraryAsync(); } catch (Exception exception) { vm.LibraryMessage = exception.Message; }
    }
}
