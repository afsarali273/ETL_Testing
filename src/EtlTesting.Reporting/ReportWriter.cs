using System.Text;
using System.Xml.Linq;
using EtlTesting.Core;

namespace EtlTesting.Reporting;

public sealed class ReportWriter : IReportWriter
{
    public static readonly string[] Supported = ["json", "html", "pdf", "csv", "junit"];
    public async Task WriteAsync(RunResult result, string directory, IEnumerable<string> formats, CancellationToken cancellationToken = default)
    {
        var selected = formats.Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Any(f => !Supported.Contains(f))) throw new InvalidDataException("Supported formats: json,html,pdf,csv,junit");
        Directory.CreateDirectory(directory);
        foreach (var format in selected)
        {
            if (format == "pdf")
            {
                ReportPdf.Write(result, Path.Combine(directory, "report.pdf"));
                continue;
            }
            var (file, content) = format switch
            {
                "json" => ("run.json", StudioJson.Write(result)),
                "html" => ("report.html", Html(result)),
                "csv" => ("mismatches.csv", Csv(result)),
                "junit" => ("junit.xml", JUnit(result)),
                _ => throw new InvalidDataException("Unknown report format")
            };
            await File.WriteAllTextAsync(Path.Combine(directory, file), content, Encoding.UTF8, cancellationToken);
        }
    }
    public static string EscapeCsv(string? value)
    {
        value ??= "";
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@\t\r\n".Contains(trimmed[0]) || value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    public static string Csv(RunResult run)
    {
        var text = new StringBuilder("RunId,Demo,Test,Table,Category,Key,Column,ExpectedType,Expected,ActualType,Actual,Rule,Reason,EvidenceTruncated\r\n");
        foreach (var test in run.Tests) foreach (var e in test.Evidence)
            text.AppendLine(string.Join(",", new[] { run.RunId, run.Demo.ToString(), e.TestId, e.Table, e.Category, e.Key, e.Column, e.Expected.Kind, e.Expected.Value, e.Actual.Kind, e.Actual.Value, e.Rule, e.Reason, test.EvidenceTruncated.ToString() }.Select(EscapeCsv)));
        return text.ToString();
    }
    public static string JUnit(RunResult run)
    {
        var root = new XElement("testsuite", new XAttribute("name", run.SuiteName), new XAttribute("tests", run.Tests.Count), new XAttribute("failures", run.Tests.Count(t => t.Status == "Failed" && t.Severity == "error")), new XAttribute("errors", run.Tests.Count(t => t.Status == "Error")), new XAttribute("skipped", run.Tests.Count(t => t.Status is "Skipped" or "Cancelled")), new XAttribute("time", (run.Ended - run.Started).TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
        root.Add(new XElement("properties", new XElement("property", new XAttribute("name", "demo"), new XAttribute("value", run.Demo)), new XElement("property", new XAttribute("name", "runId"), new XAttribute("value", run.RunId))));
        foreach (var test in run.Tests)
        {
            var item = new XElement("testcase", new XAttribute("name", test.Name), new XAttribute("classname", run.SuiteId), new XAttribute("time", test.DurationSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
            if (test.Status == "Error") item.Add(new XElement("error", new XAttribute("message", test.Error ?? "Runtime error")));
            else if (test.Status == "Failed" && test.Severity == "error") item.Add(new XElement("failure", new XAttribute("message", $"{test.Violations} violations"), StudioJson.Write(test.Evidence)));
            else if (test.Status is "Skipped" or "Cancelled") item.Add(new XElement("skipped", new XAttribute("message", test.Status)));
            if (test.Severity == "warning" && test.Status == "Failed") item.Add(new XElement("system-out", $"Warning: {test.Violations} violations (non-blocking)"));
            root.Add(item);
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }
    public static string Html(RunResult run) => ReportHtml.Render(run);
}
