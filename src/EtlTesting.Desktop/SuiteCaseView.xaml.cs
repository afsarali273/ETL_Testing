using System.Windows;
using System.Windows.Controls;
namespace EtlTesting.Desktop;
public partial class SuiteCaseView : UserControl
{
    public SuiteCaseView() { InitializeComponent(); SizeChanged += (_, _) => UpdateLayoutMode(); }
    private void OnMoreActions(object sender, RoutedEventArgs e) { if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.IsOpen = true; } }
    private void OnDuplicateValidation(object sender, RoutedEventArgs e) { if (DataContext is SuiteDesignerViewModel vm && sender is MenuItem { DataContext: SuiteValidationRow row }) vm.DuplicateCommand.Execute(row); }
    private void OnDeleteValidation(object sender, RoutedEventArgs e) { if (DataContext is SuiteDesignerViewModel vm && sender is MenuItem { DataContext: SuiteValidationRow row }) vm.DeleteCommand.Execute(row); }
    private void UpdateLayoutMode()
    {
        var wide = ActualWidth >= 720;
        SettingsColumn.Width = new GridLength(wide ? 215 : 0);
        SettingsRail.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        SettingsTab.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
    }
}
