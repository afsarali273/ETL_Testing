using System.Windows.Controls;
namespace EtlTesting.Desktop;
public partial class TemplateCategoriesView : UserControl
{
    private bool? shortLayout;
    public TemplateCategoriesView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var isShort = ActualHeight < 510;
            if (shortLayout == isShort) return;
            shortLayout = isShort;
            ContextExpander.IsExpanded = !isShort;
        };
    }
}
