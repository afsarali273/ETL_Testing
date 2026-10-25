using System.Windows;
using System.Windows.Controls;
namespace EtlTesting.Desktop;
public sealed class SuiteDetailsDialog : Window
{
    private readonly TextBox nameInput, descriptionInput;
    public string SuiteName => nameInput.Text.Trim();
    public string Description => descriptionInput.Text.Trim();
    public SuiteDetailsDialog(string title, string name, string description)
    {
        Title = title; Width = 470; Height = 300; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; FontSize = 13; Owner = Application.Current?.MainWindow;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Suite name", Margin = new Thickness(0, 0, 0, 6) });
        nameInput = new TextBox { Text = name }; panel.Children.Add(nameInput);
        panel.Children.Add(new TextBlock { Text = "Description", Margin = new Thickness(0, 12, 0, 6) });
        descriptionInput = new TextBox { Text = description, Height = 70, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top }; panel.Children.Add(descriptionInput);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; panel.Children.Add(actions);
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true });
        var save = new Button { Content = "Save", IsDefault = true }; actions.Children.Add(save);
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(SuiteName)) { nameInput.Focus(); return; } DialogResult = true; };
        Loaded += (_, _) => { nameInput.Focus(); nameInput.SelectAll(); };
    }
}
