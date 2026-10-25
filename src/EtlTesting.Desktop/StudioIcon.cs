using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EtlTesting.Desktop;

// All icons share a 24-unit vector grid; Windows DPI scaling never enlarges a bitmap or emoji.
public sealed class StudioIcon : Control
{
    private static readonly IReadOnlyDictionary<string, Geometry> FluentShapes = LoadFluentShapes();
    private static IReadOnlyDictionary<string, Geometry> LoadFluentShapes()
    {
        using var stream = typeof(StudioIcon).Assembly.GetManifestResourceStream("EtlTesting.Desktop.FluentIcons.json")!;
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!.ToDictionary(p => p.Key, p => { var g = Geometry.Parse(p.Value); g.Freeze(); return g; });
    }
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(StudioIcon), new FrameworkPropertyMetadata("database", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    private static readonly IReadOnlyDictionary<string, Geometry> Shapes = new Dictionary<string, string>
    {
        ["database"] = "M3,5 C3,1 21,1 21,5 C21,9 3,9 3,5 M3,5 L3,19 C3,23 21,23 21,19 L21,5 M3,10 C3,14 21,14 21,10 M3,15 C3,19 21,19 21,15",
        ["home"] = "M2,11 L12,2 22,11 M5,9 L5,22 10,22 10,15 14,15 14,22 19,22 19,9",
        ["suites"] = "M3,3 L9,3 9,9 3,9 Z M15,3 L21,3 21,9 15,9 Z M3,15 L9,15 9,21 3,21 Z M15,15 L21,15 21,21 15,21 Z",
        ["history"] = "M12,2 A10,10 0 1 1 11.99,2 M12,6 L12,12 17,15",
        ["reports"] = "M3,22 L3,14 7,14 7,22 Z M10,22 L10,8 14,8 14,22 Z M17,22 L17,2 21,2 21,22 Z",
        ["settings"] = "M9,2 L15,2 16,6 20,7 22,12 19,15 19,19 14,22 11,19 7,20 3,16 5,12 2,8 6,5 9,6 Z M16,12 A4,4 0 1 1 15.99,12",
        ["sql"] = "M8,5 L2,12 8,19 M16,5 L22,12 16,19 M14,3 L10,21",
        ["save"] = "M3,2 L18,2 22,6 22,22 2,22 2,2 Z M7,2 L7,9 17,9 17,2 M7,22 L7,14 17,14 17,22",
        ["check"] = "M3,12 L9,18 21,5",
        ["play"] = "M6,3 L21,12 6,21 Z",
        ["plus"] = "M12,3 L12,21 M3,12 L21,12",
        ["edit"] = "M3,17 L3,22 8,21 22,7 17,2 Z M14,5 L19,10",
        ["arrow"] = "M2,12 L22,12 M14,4 L22,12 14,20",
        ["back"] = "M22,12 L2,12 M10,4 L2,12 10,20",
        ["search"] = "M17,10 A7,7 0 1 1 16.99,10 M15,15 L22,22",
        ["file"] = "M5,2 L15,2 20,7 20,22 5,22 Z M15,2 L15,7 20,7 M8,12 L17,12 M8,16 L17,16",
        ["folder"] = "M2,6 L9,6 11,9 22,9 22,20 2,20 Z",
        ["undo"] = "M9,4 L3,10 9,16 M3,10 L14,10 C20,10 22,13 22,20",
        ["close"] = "M5,5 L19,19 M19,5 L5,19",
        ["copy"] = "M8,3 L21,3 21,17 8,17 Z M4,7 L3,7 3,21 17,21 17,20",
        ["test"] = "M8,3 L8,1 16,1 16,3 M6,3 L3,3 3,23 21,23 21,3 18,3 M8,9 L10,11 14,7 M8,16 L16,16"
    }.ToDictionary(p => p.Key, p => { var g = Geometry.Parse(p.Value); g.Freeze(); return g; });
    protected override Size MeasureOverride(Size constraint) => new(double.IsNaN(Width) ? 22 : Width, double.IsNaN(Height) ? 22 : Height);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); var size = Math.Min(ActualWidth, ActualHeight);
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 24, size / 24));
        var pen = new Pen(Foreground, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (FluentShapes.TryGetValue(Kind, out var fluent)) dc.DrawGeometry(Foreground, null, fluent);
        else dc.DrawGeometry(Kind == "play" ? Foreground : null, pen, Shapes.GetValueOrDefault(Kind, Shapes["database"]));
        dc.Pop(); dc.Pop();
    }
}
public sealed record StudioNavigationItem(string Label, string Icon);
