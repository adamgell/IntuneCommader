//! Report rendering — turn a list of `ListItem` rows (the same rows any LIST screen
//! shows) into a downloadable HTML / CSV / Markdown report. Pure string builders, no I/O;
//! the caller saves the result via `dialogs::pick_save_file`. Columns mirror the rich list:
//! Name · Detail · Platform · Modified · Status.

use api_types::ListItem;

fn esc_html(s: &str) -> String {
    s.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;").replace('"', "&quot;")
}

// CSV field: quote when it contains a comma, quote, CR or LF; double embedded quotes.
fn csv_field(s: &str) -> String {
    if s.contains([',', '"', '\n', '\r']) {
        format!("\"{}\"", s.replace('"', "\"\""))
    } else {
        s.to_string()
    }
}

fn esc_md(s: &str) -> String {
    // Escape pipes (column sep) and collapse newlines so a cell stays on one row.
    s.replace('|', "\\|").replace('\n', " ").replace('\r', " ")
}

fn cell<'a>(v: &'a Option<String>) -> &'a str {
    v.as_deref().unwrap_or("")
}

/// A self-contained, styled HTML report (inline CSS, no external assets).
pub fn to_html(title: &str, rows: &[ListItem]) -> String {
    let mut s = String::new();
    s.push_str("<!doctype html>\n<html><head><meta charset=\"utf-8\">\n<title>");
    s.push_str(&esc_html(title));
    s.push_str("</title>\n<style>\n");
    s.push_str(
        "body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1a1a1a}\
         h1{font-size:20px;margin:0 0 2px}\
         .meta{color:#666;font-size:13px;margin-bottom:16px}\
         table{border-collapse:collapse;width:100%;font-size:13px}\
         th,td{text-align:left;padding:6px 10px;border-bottom:1px solid #e2e2e2;vertical-align:top}\
         th{background:#f4f4f4;font-weight:600}\
         tr:hover td{background:#fafafa}\
         .badge{display:inline-block;background:#e6f4f1;color:#0b7a6b;border-radius:9px;padding:1px 8px;font-size:12px}\
         .mono{font-family:Consolas,monospace;color:#555}\n",
    );
    s.push_str("</style></head><body>\n<h1>");
    s.push_str(&esc_html(title));
    s.push_str("</h1>\n<div class=\"meta\">");
    s.push_str(&format!("{} item(s)", rows.len()));
    s.push_str("</div>\n<table>\n<thead><tr>\
        <th>Name</th><th>Detail</th><th>Platform</th><th>Modified</th><th>Status</th></tr></thead>\n<tbody>\n");
    for r in rows {
        s.push_str("<tr><td>");
        s.push_str(&esc_html(&r.title));
        s.push_str("</td><td>");
        s.push_str(&esc_html(&r.subtitle));
        s.push_str("</td><td>");
        s.push_str(&esc_html(cell(&r.platform)));
        s.push_str("</td><td class=\"mono\">");
        s.push_str(&esc_html(cell(&r.modified)));
        s.push_str("</td><td>");
        if let Some(b) = &r.badge {
            s.push_str("<span class=\"badge\">");
            s.push_str(&esc_html(b));
            s.push_str("</span>");
        }
        s.push_str("</td></tr>\n");
    }
    s.push_str("</tbody></table>\n</body></html>\n");
    s
}

/// CSV with a header row (RFC-4180-ish quoting).
pub fn to_csv(rows: &[ListItem]) -> String {
    let mut s = String::from("Name,Detail,Platform,Modified,Status\n");
    for r in rows {
        s.push_str(&csv_field(&r.title));
        s.push(',');
        s.push_str(&csv_field(&r.subtitle));
        s.push(',');
        s.push_str(&csv_field(cell(&r.platform)));
        s.push(',');
        s.push_str(&csv_field(cell(&r.modified)));
        s.push(',');
        s.push_str(&csv_field(cell(&r.badge)));
        s.push('\n');
    }
    s
}

/// A GitHub-flavored Markdown table.
pub fn to_markdown(title: &str, rows: &[ListItem]) -> String {
    let mut s = format!("# {}\n\n_{} item(s)_\n\n", title, rows.len());
    s.push_str("| Name | Detail | Platform | Modified | Status |\n");
    s.push_str("| --- | --- | --- | --- | --- |\n");
    for r in rows {
        s.push_str(&format!(
            "| {} | {} | {} | {} | {} |\n",
            esc_md(&r.title),
            esc_md(&r.subtitle),
            esc_md(cell(&r.platform)),
            esc_md(cell(&r.modified)),
            esc_md(cell(&r.badge)),
        ));
    }
    s
}
