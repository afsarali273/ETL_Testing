using System.Windows;
using System.Windows.Controls;
namespace EtlTesting.Desktop;
public sealed class RuleFieldsTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is ValidationEditor editor && container is FrameworkElement element ? element.TryFindResource("Rule_" + editor.Kind) as DataTemplate : null;
}
