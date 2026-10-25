using System.Windows;
using System.Windows.Controls;
namespace EtlTesting.Desktop;
public partial class ValidationTemplatePickerView : UserControl
{
    public ValidationTemplatePickerView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => CategoryColumn.Width = new GridLength(ActualWidth < 1100 ? 200 : 215);
    }
    private void OnMoreActions(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void OnSelectCheck(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SuiteDesignerViewModel vm && sender is CheckBox { DataContext: BatchTemplateItem item }) vm.ActiveBatchItem = item;
    }
}
