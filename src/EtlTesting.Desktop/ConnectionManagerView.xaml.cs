using System.Data;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace EtlTesting.Desktop;

public partial class ConnectionManagerView : UserControl
{
    private PropertyChangedEventHandler? passwordStateChanged;
    public ConnectionManagerView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var compact = ActualWidth < 1150;
            LibraryColumn.Width = new GridLength(compact ? 210 : 240);
            HealthColumn.Width = new GridLength(compact ? 250 : 285);
            HealthRow.Height = new GridLength(ActualHeight < 610 ? 250 : 305);
        };
    }
    private void PasswordContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not PasswordBox box) return;
        if (args.OldValue is ConnectionProfileEditor old && passwordStateChanged != null) old.PropertyChanged -= passwordStateChanged;
        if (box.DataContext is not ConnectionProfileEditor editor) { box.Clear(); return; }
        if (box.Password != editor.PendingPassword) box.Password = editor.PendingPassword;
        passwordStateChanged = (_, changed) => { if (changed.PropertyName == nameof(editor.PendingPassword) && box.Password != editor.PendingPassword) box.Password = editor.PendingPassword; };
        editor.PropertyChanged += passwordStateChanged;
    }
    private void PasswordChanged(object sender, RoutedEventArgs args)
    {
        if (sender is PasswordBox box && box.DataContext is ConnectionProfileEditor editor) editor.PendingPassword = box.Password;
    }
    private void PreviewColumnGenerating(object sender, DataGridAutoGeneratingColumnEventArgs args)
    {
        if (args.Column is DataGridBoundColumn bound && bound.Binding is Binding binding)
        {
            binding.Converter = new SqlCellDisplayConverter(); binding.Mode = BindingMode.OneWay;
            args.Column = new DataGridTextColumn { Header = args.Column.Header, Binding = binding, Width = 150, MinWidth = 80, MaxWidth = 300, IsReadOnly = true };
        }
    }
}
public sealed class SqlCellDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        null or DBNull => "NULL", DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture), decimal amount => amount.ToString("G29", CultureInfo.InvariantCulture), _ => value.ToString() ?? "NULL"
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
