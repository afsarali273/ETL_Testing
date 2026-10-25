using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EtlTesting.Desktop;

public partial class SuiteDesignerView : UserControl
{
    public SuiteDesignerView() => InitializeComponent();
    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && DataContext is SuiteDesignerViewModel vm) vm.SyncFromHost();
    }
    private void OnValidationDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not SuiteDesignerViewModel vm || !vm.CanInteract) return;
        var source = e.OriginalSource as DependencyObject;
        while (source != null && source is not DataGridRow) source = VisualTreeHelper.GetParent(source);
        if (source is DataGridRow { Item: SuiteValidationRow row }) vm.EditCommand.Execute(row);
    }
}
