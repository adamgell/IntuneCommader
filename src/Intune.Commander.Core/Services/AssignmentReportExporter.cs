using System.Text;
using Intune.Commander.Core.Models;

namespace Intune.Commander.Core.Services;

/// <summary>
/// Generates HTML and CSV exports from <see cref="AssignmentReportRow"/> data.
/// Columns are determined dynamically from which fields are populated in the results.
/// </summary>
public static class AssignmentReportExporter
{
    // ── Public API ────────────────────────────────────────────────────────────────

    public static string GenerateHtml(string reportMode, IList<AssignmentReportRow> rows)
    {
        var cols = GetColumns(rows);

        var byType = rows
            .GroupBy(r => r.PolicyType)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        var byPlatform = rows
            .Where(r => !string.IsNullOrEmpty(r.Platform))
            .GroupBy(r => r.Platform)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        var typeChart = BuildBarChart(byType);
        var platformChart = BuildBarChart(byPlatform);

        var typeOptions = string.Join("",
            byType.Keys.Select(t => $"<option value=\"{H(t)}\">{H(t)}</option>"));
        var platformOptions = string.Join("",
            byPlatform.Keys.Select(p => $"<option value=\"{H(p)}\">{H(p)}</option>"));

        var headers = string.Join("",
            cols.Select((c, i) => $"<th onclick=\"sortTable({i})\" aria-sort=\"none\">{H(c.Header)}</th>"));
        var headersJson = "[" + string.Join(",",
            cols.Select(c => "\"" + c.Header.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) + "]";

        var rowsSb = new StringBuilder();
        foreach (var row in rows)
        {
            rowsSb.Append("<tr>");
            foreach (var col in cols)
                rowsSb.Append($"<td>{H(col.Value(row))}</td>");
            rowsSb.Append("</tr>");
        }

        var platformCard = byPlatform.Count > 0
            ? $"<div class=\"card\"><span class=\"card-value\">{byPlatform.Count}</span><span class=\"card-label\">Platforms</span></div>"
            : "";

        return HtmlTemplate
            .Replace("%%MODE%%", H(reportMode))
            .Replace("%%COUNT%%", rows.Count.ToString())
            .Replace("%%TYPE_COUNT%%", byType.Count.ToString())
            .Replace("%%PLATFORM_CARD%%", platformCard)
            .Replace("%%TYPE_CHART%%", typeChart)
            .Replace("%%PLATFORM_CHART%%", platformChart)
            .Replace("%%TYPE_OPTIONS%%", typeOptions)
            .Replace("%%PLATFORM_OPTIONS%%", platformOptions)
            .Replace("%%HEADERS%%", headers)
            .Replace("%%HEADERS_JSON%%", headersJson)
            .Replace("%%ROWS%%", rowsSb.ToString())
            .Replace("%%DATE%%", H(date));
    }

    public static string GenerateCsv(string reportMode, IList<AssignmentReportRow> rows)
    {
        var cols = GetColumns(rows);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", cols.Select(c => CsvQ(c.Header))));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", cols.Select(c => CsvQ(c.Value(row)))));
        return sb.ToString();
    }

    // ── Column selection ─────────────────────────────────────────────────────────

    private static List<(string Header, Func<AssignmentReportRow, string> Value)> GetColumns(
        IList<AssignmentReportRow> rows)
    {
        var cols = new List<(string, Func<AssignmentReportRow, string>)>
        {
            ("Policy Name",  r => r.PolicyName),
            ("Type",         r => r.PolicyType),
            ("Platform",     r => r.Platform),
        };

        if (rows.Any(r => !string.IsNullOrEmpty(r.AssignmentSummary)))
            cols.Add(("Assignments", r => r.AssignmentSummary));
        if (rows.Any(r => !string.IsNullOrEmpty(r.AssignmentReason)))
            cols.Add(("Assignment Reason", r => r.AssignmentReason));
        if (rows.Any(r => !string.IsNullOrEmpty(r.GroupName)))
            cols.Add(("Empty Group", r => r.GroupName));
        if (rows.Any(r => !string.IsNullOrEmpty(r.GroupId)))
            cols.Add(("Group ID", r => r.GroupId));
        if (rows.Any(r => !string.IsNullOrEmpty(r.Group1Status)))
            cols.Add(("Group 1 Status", r => r.Group1Status));
        if (rows.Any(r => !string.IsNullOrEmpty(r.Group2Status)))
            cols.Add(("Group 2 Status", r => r.Group2Status));
        if (rows.Any(r => !string.IsNullOrEmpty(r.TargetDevice)))
            cols.Add(("Device", r => r.TargetDevice));
        if (rows.Any(r => !string.IsNullOrEmpty(r.UserPrincipalName)))
            cols.Add(("User", r => r.UserPrincipalName));
        if (rows.Any(r => !string.IsNullOrEmpty(r.Status)))
            cols.Add(("Status", r => r.Status));
        if (rows.Any(r => !string.IsNullOrEmpty(r.LastReported)))
            cols.Add(("Last Reported", r => r.LastReported));

        return cols;
    }

    // ── SVG chart ────────────────────────────────────────────────────────────────

    private static string BuildBarChart(Dictionary<string, int> data)
    {
        if (data.Count == 0)
            return "<p style=\"color:var(--muted);font-size:12px;padding:8px\">No data available</p>";

        var ordered = data.Take(10).ToList();
        var max = ordered.Max(x => x.Value);
        var colors = new[]
        {
            "#2563eb", "#059669", "#d97706", "#7c3aed", "#dc2626",
            "#0891b2", "#be185d", "#065f46", "#92400e", "#374151"
        };

        const int labelW = 145, barMaxW = 165, countW = 40, barH = 22, gap = 6, padY = 8;
        var svgH = ordered.Count * (barH + gap) + padY * 2;
        const int svgW = labelW + barMaxW + countW;

        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {svgW} {svgH}\" xmlns=\"http://www.w3.org/2000/svg\" class=\"chart-svg\">");

        for (int i = 0; i < ordered.Count; i++)
        {
            var label = ordered[i].Key;
            var count = ordered[i].Value;
            var y = padY + i * (barH + gap);
            var barW = max > 0 ? Math.Max((int)((double)count / max * barMaxW), 3) : 3;
            var color = colors[i % colors.Length];
            var displayLabel = label.Length > 20 ? label[..17] + "..." : label;

            sb.Append($"<text x=\"{labelW - 8}\" y=\"{y + barH - 5}\" text-anchor=\"end\" " +
                      $"font-size=\"12\" fill=\"var(--chart-label)\">{H(displayLabel)}</text>");
            sb.Append($"<rect x=\"{labelW}\" y=\"{y}\" width=\"{barW}\" height=\"{barH}\" " +
                      $"fill=\"{color}\" rx=\"3\"/>");
            sb.Append($"<text x=\"{labelW + barW + 5}\" y=\"{y + barH - 5}\" " +
                      $"font-size=\"12\" fill=\"var(--muted)\">{count}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    // ── Encoding helpers ─────────────────────────────────────────────────────────

    private static string H(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");
    }

    private static string CsvQ(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // ── HTML template ─────────────────────────────────────────────────────────────

    private const string HtmlTemplate = """
        <!DOCTYPE html>
        <html lang="en" data-theme="light">
        <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Intune Assignment Report &mdash; %%MODE%%</title>
        <style>
        :root{
          --bg:#f3f4f6;--surface:#fff;--text:#111827;--muted:#6b7280;
          --accent:#2563eb;--border:#e5e7eb;
          --row-even:#f9fafb;--row-hover:#eff6ff;
          --hdr-bg:#1e40af;--hdr-text:#fff;
          --card-bg:#fff;--card-border:#e5e7eb;
          --btn:#2563eb;--btn-text:#fff;--btn-hover:#1d4ed8;
          --input-bg:#fff;--input-border:#d1d5db;
          --chart-label:#374151;--muted-str:#6b7280;
        }
        [data-theme=dark]{
          --bg:#111827;--surface:#1f2937;--text:#f9fafb;--muted:#9ca3af;
          --accent:#60a5fa;--border:#374151;
          --row-even:#1f2937;--row-hover:#1e3a5f;
          --hdr-bg:#1e3a5f;--hdr-text:#f9fafb;
          --card-bg:#1f2937;--card-border:#374151;
          --btn:#2563eb;--btn-text:#fff;--btn-hover:#1d4ed8;
          --input-bg:#374151;--input-border:#4b5563;
          --chart-label:#d1d5db;--muted-str:#9ca3af;
        }
        *{box-sizing:border-box;margin:0;padding:0}
        body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;
          background:var(--bg);color:var(--text);font-size:14px;line-height:1.5}
        .hdr{display:flex;align-items:center;justify-content:space-between;
          padding:14px 24px;background:var(--hdr-bg);color:var(--hdr-text);gap:12px}
        .hdr h1{font-size:19px;font-weight:700;letter-spacing:-.01em}
        .subtitle{font-size:12px;opacity:.75;margin-top:2px}
        .btn{padding:7px 14px;background:var(--btn);color:var(--btn-text);
          border:none;border-radius:6px;cursor:pointer;font-size:13px;font-weight:500;
          transition:background .15s;white-space:nowrap}
        .btn:hover{background:var(--btn-hover)}
        .btn-ghost{background:transparent;border:1px solid rgba(255,255,255,.45);color:inherit}
        .btn-ghost:hover{background:rgba(255,255,255,.12)}
        .cards{display:flex;gap:12px;padding:16px 24px;flex-wrap:wrap}
        .card{background:var(--card-bg);border:1px solid var(--card-border);border-radius:8px;
          padding:12px 18px;flex:1;min-width:110px;max-width:180px;display:flex;flex-direction:column;gap:2px}
        .card-value{font-size:26px;font-weight:700;color:var(--accent)}
        .card-label{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.05em}
        .charts{display:grid;grid-template-columns:1fr 1fr;gap:14px;padding:0 24px 14px}
        .chart-box{background:var(--card-bg);border:1px solid var(--card-border);
          border-radius:8px;padding:14px 16px}
        .chart-box h2{font-size:11px;font-weight:600;color:var(--muted);
          text-transform:uppercase;letter-spacing:.07em;margin-bottom:10px}
        .chart-svg{width:100%;height:auto}
        .controls{display:flex;gap:8px;padding:0 24px 12px;align-items:center;flex-wrap:wrap}
        .controls input,.controls select{
          padding:7px 10px;border:1px solid var(--input-border);border-radius:6px;
          background:var(--input-bg);color:var(--text);font-size:13px}
        .controls input{min-width:220px}
        .controls select{min-width:140px}
        .result-count{font-size:12px;color:var(--muted);margin-left:auto}
        .tbl-wrap{overflow-x:auto;padding:0 24px 24px}
        table{width:100%;border-collapse:collapse;background:var(--surface);
          border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,.1)}
        th{background:var(--hdr-bg);color:var(--hdr-text);padding:9px 11px;text-align:left;
          font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.06em;
          cursor:pointer;white-space:nowrap;user-select:none}
        th:hover{filter:brightness(1.15)}
        th[aria-sort=ascending]::after{content:" \2191"}
        th[aria-sort=descending]::after{content:" \2193"}
        td{padding:8px 11px;border-bottom:1px solid var(--border);font-size:13px;vertical-align:top}
        tr:nth-child(even) td{background:var(--row-even)}
        tbody tr:hover td{background:var(--row-hover)}
        .hidden{display:none!important}
        footer{text-align:center;padding:14px;color:var(--muted);font-size:12px;
          border-top:1px solid var(--border)}
        @media(max-width:640px){
          .charts{grid-template-columns:1fr}
          .hdr{flex-direction:column;align-items:flex-start}
        }
        </style>
        </head>
        <body>

        <div class="hdr">
          <div>
            <h1>Intune Assignment Report</h1>
            <div class="subtitle">%%MODE%% &bull; %%COUNT%% result(s) &bull; Generated %%DATE%%</div>
          </div>
          <button class="btn btn-ghost" onclick="toggleTheme()" id="themeBtn">&#127769; Dark Mode</button>
        </div>

        <div class="cards">
          <div class="card">
            <span class="card-value">%%COUNT%%</span>
            <span class="card-label">Total Results</span>
          </div>
          <div class="card">
            <span class="card-value">%%TYPE_COUNT%%</span>
            <span class="card-label">Policy Types</span>
          </div>
          %%PLATFORM_CARD%%
        </div>

        <div class="charts">
          <div class="chart-box"><h2>By Policy Type</h2>%%TYPE_CHART%%</div>
          <div class="chart-box"><h2>By Platform</h2>%%PLATFORM_CHART%%</div>
        </div>

        <div class="controls">
          <input type="search" id="searchInput" placeholder="&#128269; Filter results..." oninput="applyFilters()" autocomplete="off">
          <select id="typeFilter" onchange="applyFilters()">
            <option value="">All Types</option>%%TYPE_OPTIONS%%
          </select>
          <select id="platformFilter" onchange="applyFilters()">
            <option value="">All Platforms</option>%%PLATFORM_OPTIONS%%
          </select>
          <button class="btn" onclick="exportCsv()">&#11015; Export CSV</button>
          <span class="result-count" id="resultCount"></span>
        </div>

        <div class="tbl-wrap">
          <table id="dataTable">
            <thead><tr>%%HEADERS%%</tr></thead>
            <tbody>%%ROWS%%</tbody>
          </table>
        </div>

        <footer>Generated by Intune Commander &bull; %%DATE%%</footer>

        <script>
        const HEADERS = %%HEADERS_JSON%%;
        let sortCol = -1, sortAsc = true;

        function applyFilters() {
          const q  = document.getElementById('searchInput').value.toLowerCase();
          const t  = document.getElementById('typeFilter').value;
          const p  = document.getElementById('platformFilter').value;
          const rows = document.querySelectorAll('#dataTable tbody tr');
          let shown = 0;
          rows.forEach(row => {
            const typeOk = !t || (row.cells[1] && row.cells[1].textContent === t);
            const platOk = !p || (row.cells[2] && row.cells[2].textContent === p);
            const textOk = !q || row.textContent.toLowerCase().includes(q);
            const visible = typeOk && platOk && textOk;
            row.classList.toggle('hidden', !visible);
            if (visible) shown++;
          });
          document.getElementById('resultCount').textContent =
            shown + ' of ' + rows.length + ' shown';
        }

        function sortTable(idx) {
          if (sortCol === idx) sortAsc = !sortAsc;
          else { sortCol = idx; sortAsc = true; }
          const tbody = document.querySelector('#dataTable tbody');
          const rows = [...tbody.rows];
          rows.sort((a, b) => {
            const av = a.cells[idx] ? a.cells[idx].textContent : '';
            const bv = b.cells[idx] ? b.cells[idx].textContent : '';
            return sortAsc ? av.localeCompare(bv) : bv.localeCompare(av);
          });
          rows.forEach(r => tbody.appendChild(r));
          document.querySelectorAll('#dataTable th').forEach((th, i) => {
            th.setAttribute('aria-sort',
              i === idx ? (sortAsc ? 'ascending' : 'descending') : 'none');
          });
        }

        function exportCsv() {
          const visibleRows = document.querySelectorAll('#dataTable tbody tr:not(.hidden)');
          const lines = [HEADERS.map(h => '"' + h.replace(/"/g, '""') + '"').join(',')];
          visibleRows.forEach(row => {
            const cols = [...row.cells].map(c => '"' + c.textContent.replace(/"/g, '""') + '"');
            lines.push(cols.join(','));
          });
          const csv = '\uFEFF' + lines.join('\r\n');
          const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
          const a = document.createElement('a');
          a.href = URL.createObjectURL(blob);
          a.download = 'assignment-report.csv';
          document.body.appendChild(a);
          a.click();
          document.body.removeChild(a);
        }

        function toggleTheme() {
          const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
          const next = isDark ? 'light' : 'dark';
          document.documentElement.setAttribute('data-theme', next);
          document.getElementById('themeBtn').innerHTML =
            next === 'dark' ? '&#9728; Light Mode' : '&#127769; Dark Mode';
          try { localStorage.setItem('ica-theme', next); } catch(e) {}
        }

        (function init() {
          try {
            var saved = localStorage.getItem('ica-theme');
            if (saved === 'dark') {
              document.documentElement.setAttribute('data-theme', 'dark');
              document.getElementById('themeBtn').innerHTML = '&#9728; Light Mode';
            }
          } catch(e) {}
          applyFilters();
        })();
        </script>
        </body>
        </html>
        """;
}
