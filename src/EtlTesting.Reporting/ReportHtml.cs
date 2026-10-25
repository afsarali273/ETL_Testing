using System.Net;
using System.Text;
using EtlTesting.Core;

namespace EtlTesting.Reporting;

public static class ReportHtml
{
    private static string E(object? value) => WebUtility.HtmlEncode(value?.ToString() ?? "—");
    public static string Render(RunResult run)
    {
        var failed = run.Tests.Count(t => t.Status is "Failed" or "Error");
        var passed = run.Tests.Count(t => t.Status == "Passed");
        var violations = run.Tests.Sum(t => t.Violations);
        var html = new StringBuilder();
        html.Append("""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>ETL Test Studio · Run report</title><style>
:root{font:14px/1.45 "Segoe UI",Arial,sans-serif;color:#132641;background:#f2f6fa}
*{box-sizing:border-box}body{margin:0}.hero{background:#102b49;color:#fff;padding:22px max(24px,calc((100vw - 1180px)/2));}
.brand{display:flex;align-items:center;gap:9px;color:#b7d9ff;font-weight:700;letter-spacing:.03em}.brand-icon{display:grid;place-items:center;width:30px;height:30px;border-radius:7px;background:#1175dc;color:white;font-size:17px}
.hero-main{display:flex;justify-content:space-between;align-items:flex-end;gap:16px;margin-top:18px}h1{font-size:30px;line-height:1.2;margin:0 0 4px}.hero p{margin:0;color:#c4d5e7}.status{padding:7px 12px;border-radius:5px;font-weight:700;white-space:nowrap}
.status.Passed{background:#dff7e9;color:#087c4b}.status.Failed,.status.Error{background:#fde7e8;color:#ad2631}.status.Cancelled,.status.Skipped{background:#fff0ce;color:#8b5b00}
main{max-width:1228px;margin:auto;padding:21px 24px 40px}.panel{background:#fff;border:1px solid #d8e3ee;border-radius:8px;box-shadow:0 1px 3px #153c6510;margin-bottom:16px}
.meta{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));padding:16px 20px;gap:12px}.label{display:block;color:#6b7c91;text-transform:uppercase;font-size:10px;letter-spacing:.07em;font-weight:700;margin-bottom:4px}
.value{font-weight:650;overflow-wrap:anywhere}.stats{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;margin-bottom:16px}.stat{padding:16px 18px;border-radius:8px;border:1px solid #d4e3f3;background:#f0f6ff}
.stat:nth-child(2){background:#effaf5;border-color:#ceeadc}.stat:nth-child(3){background:#fff1f1;border-color:#f2d3d6}.stat:nth-child(4){background:#fff8eb;border-color:#f3e2bd}.stat strong{display:block;font-size:25px;line-height:1.2;margin-top:4px}.stat:nth-child(2) strong{color:#078852}.stat:nth-child(3) strong{color:#c7343b}.stat:nth-child(4) strong{color:#a66a09}
.panel-head{padding:15px 20px;border-bottom:1px solid #e4ebf3;display:flex;justify-content:space-between;align-items:center;gap:12px}h2{font-size:17px;margin:0}h3{font-size:14px;margin:0}.muted{color:#61748d}.body{padding:17px 20px}.summary{font-size:15px;margin:0 0 8px}.notice{padding:11px 13px;border-radius:6px;background:#fff4df;color:#77530b;margin-top:11px}
.validation{break-inside:avoid-page}.validation .panel-head{align-items:flex-start}.outcome{margin:6px 0 0;color:#526b84}.metrics{display:flex;gap:14px;flex-wrap:wrap;color:#4f6680;font-size:12px;margin:0 0 12px}.metrics b{color:#173654}
.evidence-list{display:grid;gap:10px}.evidence{border:1px solid #dce6ef;border-radius:6px;overflow:hidden;break-inside:avoid-page}.evidence-head{display:flex;justify-content:space-between;gap:12px;background:#f4f8fc;padding:9px 12px;border-bottom:1px solid #e0e9f2;font-weight:650}.pill{font-size:11px;padding:3px 8px;border-radius:100px;background:#fce4e5;color:#b52d37;white-space:nowrap}.evidence-body{padding:11px 12px}.evidence-body p{margin:0 0 8px}.facts{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}.fact{border-left:2px solid #d6e4f2;padding-left:9px;overflow-wrap:anywhere}.fact .label{margin-bottom:2px}.technical{margin-top:9px;border-top:1px solid #e6edf4;padding-top:8px}.technical summary{color:#0968ca;cursor:pointer;font-weight:600}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f6f8fb;border:1px solid #e5ebf2;border-radius:5px;padding:9px;font:11px/1.4 Consolas,monospace;max-height:250px;overflow:auto}
.empty{padding:15px;color:#0d8054;background:#f0faf4;border-radius:5px}.context{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}.context>div{overflow-wrap:anywhere}footer{color:#73849a;font-size:11px;padding:5px 1px}
@media(max-width:750px){.hero-main{display:block}.hero-main .status{display:inline-block;margin-top:12px}.meta{grid-template-columns:repeat(2,1fr)}.stats{grid-template-columns:repeat(2,1fr)}.facts,.context{grid-template-columns:1fr}}
@media print{@page{size:A4;margin:13mm}body{background:#fff;font-size:10pt}.hero{padding:15px 18px;print-color-adjust:exact;-webkit-print-color-adjust:exact}.hero h1{font-size:21pt}main{padding:12px 0}.panel{box-shadow:none;border-color:#cbd7e3}.stats,.stat,.evidence-head,.pill,.status{print-color-adjust:exact;-webkit-print-color-adjust:exact}.stat strong{font-size:19pt}pre{max-height:none;overflow:visible}details:not([open])>*:not(summary){display:block}.validation{break-inside:auto}.evidence{break-inside:avoid}.panel-head{break-after:avoid}}
</style></head><body><header class='hero'><div class='brand'><span class='brand-icon'>▤</span> ETL TEST STUDIO</div><div class='hero-main'><div><h1>
""");
        html.Append(E(run.SuiteName)).Append("</h1><p>Run report · validation results and saved evidence</p></div><span class='status ")
            .Append(E(run.Status)).Append("'>").Append(E(run.Status)).Append("</span></div></header><main>");
        html.Append("<section class='panel meta'>");
        Meta(html, "Run ID", run.RunId); Meta(html, "Environment", run.Environment);
        Meta(html, "Started", run.Started.ToLocalTime().ToString("dd MMM yyyy · HH:mm"));
        Meta(html, "Duration", (run.Ended - run.Started).ToString(@"mm\:ss\.fff"));
        Meta(html, "Scope", run.Scope);
        html.Append("</section><div class='stats'>");
        Stat(html, "Validations", run.Tests.Count); Stat(html, "Passed", passed); Stat(html, "Failed or error", failed); Stat(html, "Violations", violations);
        html.Append("</div><section class='panel'><div class='panel-head'><h2>At a glance</h2></div><div class='body'><p class='summary'>");
        html.Append(E(!run.Complete ? "This run is incomplete. Review errors or cancellation before relying on the results." :
            failed == 0 ? "All required validations completed without blocking failures." :
            $"{failed} validation{(failed == 1 ? "" : "s")} need attention. The sections below show what was checked, what happened, and the retained examples."));
        html.Append("</p><p class='muted'>Counts are per validation. A violation may represent a record, a field, or a group depending on its rule.</p>");
        if (run.Demo) html.Append("<div class='notice'>Demo data: synthetic fixture. No database was queried.</div>");
        if (!run.Complete) html.Append("<div class='notice'>Incomplete validation: some results may not reflect the full scope.</div>");
        html.Append("</div></section>");
        html.Append("<section class='panel'><div class='panel-head'><h2>Validation outcomes</h2><span class='muted'>").Append(run.Tests.Count).Append(" checks</span></div><div class='body'>");
        foreach (var t in run.Tests)
            html.Append("<div style='padding:8px 0;border-bottom:1px solid #e7eef5'><strong>").Append(E(t.Name)).Append("</strong> · <span class='status ").Append(E(t.Status)).Append("'>").Append(E(t.Status)).Append("</span><div class='muted' style='margin-top:5px'>").Append(E(ReportPresentation.Outcome(t))).Append("</div></div>");
        html.Append("</div></section>");
        foreach (var t in run.Tests)
        {
            html.Append("<section class='panel validation'><div class='panel-head'><div><h2>").Append(E(t.Name)).Append("</h2><p class='outcome'>").Append(E(ReportPresentation.Outcome(t))).Append("</p></div><span class='status ").Append(E(t.Status)).Append("'>").Append(E(t.Status)).Append("</span></div><div class='body'><div class='metrics'>");
            html.Append("<span>Severity <b>").Append(E(t.Severity)).Append("</b></span><span>Violations <b>").Append(t.Violations).Append("</b></span><span>Saved examples <b>").Append(t.Evidence.Count).Append("</b></span><span>Duration <b>").Append(t.DurationSeconds.ToString("F2")).Append(" s</b></span>");
            if (t.SourceRecords > 0 || t.TargetRecords > 0) html.Append("<span>Source rows <b>").Append(t.SourceRecords).Append("</b></span><span>Target rows <b>").Append(t.TargetRecords).Append("</b></span>");
            html.Append("</div>");
            if (t.Error != null) html.Append("<div class='notice'><b>Execution error:</b> ").Append(E(t.Error)).Append("</div>");
            if (t.EvidenceTruncated) html.Append("<div class='notice'>Evidence limit reached. All violations were counted; only a sample was retained.</div>");
            if (t.Evidence.Count == 0) html.Append("<div class='empty'>No evidence rows were saved for this validation.</div>");
            else
            {
                html.Append("<div class='evidence-list'>");
                foreach (var e in t.Evidence)
                {
                    html.Append("<article class='evidence'><div class='evidence-head'><span>").Append(E(ReportPresentation.DisplayKey(e))).Append("</span><span class='pill'>").Append(E(ReportPresentation.Category(e.Category))).Append("</span></div><div class='evidence-body'><p>").Append(E(ReportPresentation.Explanation(e))).Append("</p><div class='facts'>");
                    Fact(html, "Table / column", e.Table + (string.IsNullOrWhiteSpace(e.Column) ? "" : " · " + e.Column));
                    Fact(html, "Expected / source", ReportPresentation.Expected(e));
                    Fact(html, "Actual / target", ReportPresentation.Actual(e));
                    html.Append("</div><details class='technical'><summary>Technical rule and captured evidence</summary><p><b>Rule:</b> ").Append(E(e.Rule)).Append("</p><p><b>Recorded key:</b></p><pre>").Append(E(e.Key)).Append("</pre><p><b>Reason:</b></p><pre>").Append(E(e.Reason)).Append("</pre></details></div></article>");
                }
                html.Append("</div>");
            }
            html.Append("</div></section>");
        }
        html.Append("<section class='panel'><div class='panel-head'><h2>Execution context</h2></div><div class='body context'>");
        Fact(html, "Strategy", run.Strategy); Fact(html, "Suite hash", run.SuiteHash);
        Fact(html, "Run complete", run.Complete ? "Yes" : "No"); Fact(html, "Ended", run.Ended.ToLocalTime().ToString("dd MMM yyyy · HH:mm"));
        html.Append("</div></section><footer>Generated by ETL Test Studio. Warning-level violations may be nonblocking. Exact captured values and rules are available in each technical evidence section.</footer></main></body></html>");
        return html.ToString();
    }
    private static void Meta(StringBuilder b, string label, object? value) => b.Append("<div><span class='label'>").Append(E(label)).Append("</span><span class='value'>").Append(E(value)).Append("</span></div>");
    private static void Stat(StringBuilder b, string label, long value) => b.Append("<div class='stat'><span class='muted'>").Append(E(label)).Append("</span><strong>").Append(value.ToString("N0")).Append("</strong></div>");
    private static void Fact(StringBuilder b, string label, object? value) => b.Append("<div class='fact'><span class='label'>").Append(E(label)).Append("</span>").Append(E(value)).Append("</div>");
}


