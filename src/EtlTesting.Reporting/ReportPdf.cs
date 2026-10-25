using EtlTesting.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Fonts;

namespace EtlTesting.Reporting;

internal static class ReportPdf
{
    private static readonly XColor Navy = XColor.FromArgb(17, 43, 73);
    private static readonly XColor Blue = XColor.FromArgb(15, 104, 204);
    private static readonly XColor Gray = XColor.FromArgb(88, 106, 128);
    private static readonly XColor Border = XColor.FromArgb(216, 228, 238);
    private static readonly XColor Red = XColor.FromArgb(178, 43, 52);
    private static readonly XColor Green = XColor.FromArgb(8, 129, 78);
    private const double Left = 42, Right = 42, Top = 42, Bottom = 47;

    public static void Write(RunResult run, string path)
    {
        if (OperatingSystem.IsWindows()) GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        using var doc = new PdfDocument();
        doc.Info.Title = run.SuiteName + " - Run report";
        doc.Info.Author = "ETL Test Studio";
        var canvas = new Canvas(doc, run);
        canvas.Title(run);
        canvas.Summary(run);
        canvas.Section("Validation outcomes");
        foreach (var test in run.Tests)
        {
            canvas.Check(test.Name, test.Status, ReportPresentation.Outcome(test));
        }
        foreach (var test in run.Tests)
        {
            canvas.Section(test.Name + " - " + test.Status);
            canvas.Paragraph(ReportPresentation.Outcome(test), 10, Navy);
            canvas.Paragraph($"Severity: {test.Severity}   Violations: {test.Violations:N0}   Saved examples: {test.Evidence.Count:N0}   Duration: {test.DurationSeconds:F2} s", 8.5, Gray);
            if (test.SourceRecords > 0 || test.TargetRecords > 0)
                canvas.Paragraph($"Source rows: {test.SourceRecords:N0}   Target rows: {test.TargetRecords:N0}", 8.5, Gray);
            if (!string.IsNullOrWhiteSpace(test.Error)) canvas.Paragraph("Execution error: " + test.Error, 9, Red);
            if (test.EvidenceTruncated) canvas.Paragraph("Evidence limit reached: all violations were counted; only a sample was retained.", 9, Red);
            if (test.Evidence.Count == 0) canvas.Paragraph("No evidence rows were saved for this validation.", 9, Green);
            foreach (var evidence in test.Evidence)
            {
                canvas.Evidence(evidence);
            }
        }
        canvas.Section("Execution context");
        canvas.Paragraph($"Strategy: {run.Strategy}", 9, Navy);
        canvas.Paragraph($"Scope: {run.Scope}", 9, Navy);
        canvas.Paragraph($"Started: {run.Started:O}   Ended: {run.Ended:O}", 8.5, Gray);
        canvas.Paragraph($"Suite hash: {run.SuiteHash}", 8, Gray);
        canvas.Paragraph($"Run complete: {(run.Complete ? "Yes" : "No")}", 8.5, Gray);
        canvas.Finish();
        doc.Save(path);
    }

    private sealed class Canvas
    {
        private readonly PdfDocument doc;
        private readonly RunResult run;
        private PdfPage page = null!;
        private XGraphics g = null!;
        private double y;
        private readonly XFont regular = new("Arial", 9);
        private readonly XFont semibold = new("Arial", 9, XFontStyleEx.Bold);
        private readonly XFont small = new("Arial", 7.5);
        private readonly XFont title = new("Arial", 21, XFontStyleEx.Bold);
        private readonly XFont heading = new("Arial", 12, XFontStyleEx.Bold);
        private int pageNumber;
        private double Width => page.Width.Point - Left - Right;
        private double Limit => page.Height.Point - Bottom;
        public Canvas(PdfDocument doc, RunResult run) { this.doc = doc; this.run = run; NewPage(); }
        private void NewPage()
        {
            if (g != null) { Footer(); g.Dispose(); }
            page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            g = XGraphics.FromPdfPage(page);
            pageNumber++;
            y = Top;
            g.DrawString("ETL TEST STUDIO  /  RUN REPORT", small, new XSolidBrush(Blue), Left, 27);
            g.DrawLine(new XPen(Border), Left, 33, page.Width.Point - Right, 33);
        }
        private void Footer()
        {
            g.DrawLine(new XPen(Border), Left, page.Height.Point - 36, page.Width.Point - Right, page.Height.Point - 36);
            g.DrawString($"Run {run.RunId}   |   Page {pageNumber}", small, new XSolidBrush(Gray), Left, page.Height.Point - 24);
        }
        private void Need(double height) { if (y + height > Limit) NewPage(); }
        public void Finish() { Footer(); g.Dispose(); }
        public void Title(RunResult result)
        {
            Paragraph(result.SuiteName + " / Run report", 21, Navy, title, 30);
            Paragraph(result.Complete ? "Validation results and saved evidence" : "INCOMPLETE RUN - review errors before relying on results", 10, result.Complete ? Gray : Red);
            y += 10;
            var box = new XRect(Left, y, Width, 60);
            g.DrawRoundedRectangle(new XPen(Border), new XSolidBrush(XColor.FromArgb(242, 247, 252)), box, new XSize(5, 5));
            var cells = new[] { ("RUN ID", result.RunId), ("ENVIRONMENT", result.Environment), ("STATUS", result.Status), ("DURATION", (result.Ended - result.Started).ToString(@"mm\:ss\.fff")) };
            double cw = Width / cells.Length;
            for (var i = 0; i < cells.Length; i++)
            {
                var x = Left + 12 + i * cw;
                g.DrawString(cells[i].Item1, small, new XSolidBrush(Gray), x, y + 19);
                DrawClipped(cells[i].Item2, semibold, Navy, x, y + 39, cw - 22);
            }
            y += 72;
        }
        public void Summary(RunResult result)
        {
            var passed = result.Tests.Count(t => t.Status == "Passed");
            var failed = result.Tests.Count(t => t.Status is "Failed" or "Error");
            var violations = result.Tests.Sum(t => t.Violations);
            Section("At a glance");
            var data = new[] { ("VALIDATIONS", result.Tests.Count), ("PASSED", passed), ("FAILED / ERROR", failed), ("VIOLATIONS", violations) };
            Need(61);
            var cw = (Width - 18) / 4;
            for (var i = 0; i < 4; i++)
            {
                var x = Left + i * (cw + 6);
                g.DrawRoundedRectangle(new XPen(Border), new XSolidBrush(i switch { 1 => XColor.FromArgb(235, 248, 240), 2 => XColor.FromArgb(255, 240, 240), 3 => XColor.FromArgb(255, 247, 231), _ => XColor.FromArgb(237, 246, 255) }), new XRect(x, y, cw, 55), new XSize(4, 4));
                g.DrawString(data[i].Item1, small, new XSolidBrush(Gray), x + 10, y + 19);
                g.DrawString(data[i].Item2.ToString("N0"), new XFont("Arial", 17, XFontStyleEx.Bold), new XSolidBrush(i == 1 ? Green : i == 2 ? Red : Navy), x + 10, y + 43);
            }
            y += 68;
            Paragraph(!result.Complete ? "This run is incomplete. Review errors or cancellation before relying on the results." :
                failed == 0 ? "All required validations completed without blocking failures." :
                $"{failed} validation{(failed == 1 ? "" : "s")} need attention. Each section explains the outcome and retained examples.", 10, Navy);
            if (result.Demo) Paragraph("Demo data: synthetic fixture; no database was queried.", 9, Red);
            Paragraph("Counts are per validation. A violation may represent a record, field, or group depending on its rule.", 8, Gray);
        }
        public void Section(string name)
        {
            Need(95);
            y += 11;
            g.DrawString(Clean(name), heading, new XSolidBrush(Navy), Left, y + 13);
            y += 22;
            g.DrawLine(new XPen(Border), Left, y, page.Width.Point - Right, y);
            y += 8;
        }
        public void Check(string name, string status, string outcome)
        {
            Need(33);
            g.DrawString(Clean(name), semibold, new XSolidBrush(Navy), Left + 5, y + 10);
            var sw = g.MeasureString(Clean(status), semibold).Width;
            g.DrawString(Clean(status), semibold, new XSolidBrush(status == "Passed" ? Green : Red), page.Width.Point - Right - sw - 5, y + 10);
            y += 14;
            Paragraph(outcome, 8.5, Gray);
            g.DrawLine(new XPen(Border), Left, y, page.Width.Point - Right, y);
            y += 7;
        }
        public void Evidence(Evidence evidence)
        {
            Need(105);
            y += 8;
            var heading = ReportPresentation.DisplayKey(evidence) + "  |  " + ReportPresentation.Category(evidence.Category);
            Paragraph(heading, 9, Navy, semibold);
            Paragraph(ReportPresentation.Explanation(evidence), 8.5, Gray);
            Paragraph("Table: " + evidence.Table + (string.IsNullOrWhiteSpace(evidence.Column) ? "" : "   Column: " + evidence.Column), 8, Gray);
            Paragraph("Expected / source: " + ReportPresentation.Expected(evidence), 8, Navy);
            Paragraph("Actual / target: " + ReportPresentation.Actual(evidence), 8, Navy);
            Paragraph("Rule: " + evidence.Rule, 8, Blue);
            if (!string.IsNullOrWhiteSpace(evidence.Reason)) Paragraph("Captured reason: " + evidence.Reason, 7.5, Gray, small, 10, 900);
            g.DrawLine(new XPen(Border), Left, y, page.Width.Point - Right, y);
            y += 4;
        }
        public void Paragraph(string text, double size, XColor color, XFont? font = null, double after = 6, int maxChars = int.MaxValue)
        {
            font ??= size >= 20 ? title : size >= 11 ? heading : size < 8 ? small : regular;
            text = Clean(text);
            if (text.Length > maxChars) text = text[..maxChars] + "...";
            var lines = Wrap(text, font, Width);
            var lineHeight = size * 1.4;
            foreach (var line in lines)
            {
                Need(lineHeight);
                g.DrawString(line, font, new XSolidBrush(color), Left, y + lineHeight * 0.82);
                y += lineHeight;
            }
            y += after;
        }
        private List<string> Wrap(string text, XFont font, double width)
        {
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r", "").Split('\n'))
            {
                var current = "";
                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = current.Length == 0 ? word : current + " " + word;
                    if (g.MeasureString(candidate, font).Width <= width) { current = candidate; continue; }
                    if (current.Length > 0) { lines.Add(current); current = ""; }
                    foreach (var chunk in SplitLong(word, font, width)) { if (g.MeasureString(chunk, font).Width >= width - 2) lines.Add(chunk); else current = chunk; }
                }
                lines.Add(current);
            }
            return lines;
        }
        private IEnumerable<string> SplitLong(string word, XFont font, double width)
        {
            var part = "";
            foreach (var c in word)
            {
                if (g.MeasureString(part + c, font).Width > width - 2 && part.Length > 0) { yield return part; part = ""; }
                part += c;
            }
            if (part.Length > 0) yield return part;
        }
        private void DrawClipped(string value, XFont font, XColor color, double x, double baseline, double width)
        {
            value = Clean(value);
            while (value.Length > 4 && g.MeasureString(value, font).Width > width) value = value[..^4] + "...";
            g.DrawString(value, font, new XSolidBrush(color), x, baseline);
        }
        private static string Clean(string? value) => (value ?? "").Replace('—', '-').Replace('·', '|').Replace('≤', '<').Replace('≥', '>').Replace('→', '>').Replace('±', '+');
    }
}
