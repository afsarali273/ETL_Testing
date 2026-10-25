using System.Windows;
namespace EtlTesting.Desktop;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Keep the restored window inside the current monitor's usable desktop.
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(620, workArea.Height);
        Width = Math.Min(1440, workArea.Width);
        Height = Math.Min(900, workArea.Height);
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        SizeChanged += (_, _) => UpdateDensity();
        Loaded += (_, _) => UpdateDensity();
        var arguments = System.Environment.GetCommandLineArgs();
        var preview = arguments.Contains("--suite-designer-preview") || System.AppContext.BaseDirectory.Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar], System.StringSplitOptions.RemoveEmptyEntries).Contains("SuiteDesignerPreview", System.StringComparer.OrdinalIgnoreCase);
        if (preview) Title = "ETL Test Studio — Suite Designer Preview";
        var vm = new StudioViewModel(preview ? System.IO.Path.Combine(System.AppContext.BaseDirectory, "preview-data") : null);
        DataContext = vm;
        Loaded += async (_, _) => { await vm.InitializeAsync(); if (arguments.Contains("--workspace")) vm.Section = 3; else if (arguments.Contains("--connections")) vm.Section = 2; else if (arguments.Contains("--suites")) vm.Section = 1; else if (arguments.Contains("--reports")) vm.Section = 5; if (preview) vm.Section = 1; };
        Closing += (_, _) => vm.Cancel();
    }
    private bool? compactDensity;
    private void UpdateDensity()
    {
        // PerMonitorV2 DPI awareness keeps WPF units tied to the active monitor.
        // The usable window size, in those units, determines how densely controls fit.
        var compact = (ActualWidth > 0 ? ActualWidth : Width) < 1450 || (ActualHeight > 0 ? ActualHeight : Height) < 840;
        if (compactDensity == compact) return;
        compactDensity = compact;
        var resources = Application.Current.Resources;
        resources["StudioBodyFontSize"] = compact ? 12d : 13d;
        resources["StudioGridFontSize"] = compact ? 12d : 13d;
        resources["StudioSuiteHeadingSize"] = compact ? 21d : 23d;
        resources["StudioCaseHeadingSize"] = compact ? 17d : 19d;
        resources["StudioTestRailWidth"] = compact ? 220d : 240d;
        resources["StudioButtonHeight"] = compact ? 29d : 32d;
        resources["StudioGridRowHeight"] = compact ? 30d : 34d;
        resources["StudioButtonPadding"] = compact ? new Thickness(9, 5, 9, 5) : new Thickness(12, 7, 12, 7);
        resources["StudioTextBoxPadding"] = compact ? new Thickness(7, 4, 7, 4) : new Thickness(8, 5, 8, 5);
    }
}
