using System.Windows.Controls;
using System.Windows.Data;

namespace EtlTesting.Desktop;

public partial class DatabaseWorkspaceView : UserControl
{
    public DatabaseWorkspaceView() => InitializeComponent();
    private void PreviewColumnGenerating(object sender, DataGridAutoGeneratingColumnEventArgs args)
    {
        if (args.Column is DataGridBoundColumn bound && bound.Binding is Binding binding)
        {
            binding.Converter = new SqlCellDisplayConverter(); binding.Mode = BindingMode.OneWay;
            args.Column = new DataGridTextColumn { Header = args.Column.Header, Binding = binding, Width = 150, MinWidth = 80, MaxWidth = 300, IsReadOnly = true };
        }
    }
}
