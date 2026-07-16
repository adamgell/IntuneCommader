//! Brand-aligned configuration detail renderer.
//!
//! Replaces the raw-JSON dump in the list detail pane with a **dense two-column**
//! reading of an Intune object: a header band (name + badges + the one little
//! `☁ synced` cloud pill), a key/value field list (signal first; Graph noise greyed
//! behind a "System fields" toggle), nested objects/arrays as indented sub-rows, and
//! a Copy + Raw-JSON footer. All color/type comes from [`crate::theme`] — teal/neutral
//! chrome, blue ONLY on the cloud pill (see `docs/brand/BRAND.md`).
//!
//! This is a plain render helper (not a `component()`): its show/hide toggles live in
//! the parent `list_workspace` hook context and are threaded in, the same way the
//! assignment editor works (nested `component()` children don't re-render on their own
//! state in the reactor).

use api_types::{Assignment, CaSummary};
use serde_json::Value;
use windows_reactor::*;

use crate::theme;

/// Render the object `json` for surface `path`. `height` bounds the scroll area
/// (an explicit height is required for the inner scroll viewer to actually
/// scroll); the two toggles (system-fields, raw-json) are owned by the parent.
#[allow(clippy::too_many_arguments)]
pub fn render_config(
    path: &str,
    json: &str,
    height: f64,
    show_system: bool,
    set_show_system: &SetState<bool>,
    show_raw: bool,
    set_show_raw: &SetState<bool>,
    settings_json: Option<&str>,
    ca_summary: Option<&CaSummary>,
    assignments: Option<&[Assignment]>,
) -> Element {
    let value: Value = match serde_json::from_str(json) {
        Ok(v) => v,
        Err(_) => return raw_fallback(json, height),
    };
    let Some(obj) = value.as_object() else {
        return raw_fallback(json, height);
    };

    // ── Header band: ▣ motif + name + badge pills (incl. the one cloud pill) ──
    let name = obj
        .get("displayName")
        .or_else(|| obj.get("name"))
        .and_then(|v| v.as_str())
        .unwrap_or("(unnamed)")
        .to_string();
    // Logo: Intune shows the app icon. largeIcon/icon is a mimeContent object
    // { type, value(base64) }; decode it to a cached temp file and show it,
    // falling back to the ▣ brand motif when there's no usable icon.
    let icon_el: Element = obj
        .get("largeIcon")
        .or_else(|| obj.get("icon"))
        .or_else(|| obj.get("largeCover"))
        .and_then(|v| v.get("value").or(Some(v)))
        .and_then(|v| v.as_str())
        .and_then(icon_file_uri)
        .map(|uri| {
            Element::from(
                Image::new_with_uri(uri)
                    .stretch(Stretch::Uniform)
                    .width(40.0)
                    .height(40.0),
            )
        })
        .unwrap_or_else(|| {
            Element::from(body_strong("▣").font_size(16.0).foreground(theme::BRAND_BRIGHT))
        });
    let title_row = hstack((
        icon_el,
        Element::from(
            body_strong(name)
                .font_size(20.0)
                .font_family(theme::FONT_DISPLAY)
                .foreground(theme::TEXT),
        ),
    ))
    .spacing(8.0);

    let mut pills: Vec<Element> = Vec::new();
    if let Some(p) = str_field(obj, "platforms").or_else(|| str_field(obj, "platform")) {
        pills.push(pill(p, theme::BRAND_BRIGHT, theme::SURFACE_2));
    }
    if let Some(s) = str_field(obj, "state").or_else(|| str_field(obj, "status")) {
        pills.push(pill(s, theme::TEXT_2, theme::SURFACE_2));
    }
    if obj.get("isAssigned").and_then(|v| v.as_bool()) == Some(true) {
        pills.push(pill("assigned".into(), theme::BRAND_BRIGHT, theme::SURFACE_2));
    }
    if let Some(v) = obj.get("version").filter(|v| !v.is_null()) {
        pills.push(pill(format!("v{}", fmt_scalar(v)), theme::TEXT_3, theme::SURFACE_2));
    }
    // The single permitted blue: cloud-origin freshness.
    if let Some(d) = str_field(obj, "lastModifiedDateTime") {
        pills.push(pill(
            format!("☁ synced · {}", short_date(&d)),
            theme::CLOUD_BRIGHT,
            theme::CLOUD_DEEP,
        ));
    }
    let header = vstack((
        Element::from(title_row),
        if pills.is_empty() { Element::Empty } else { Element::from(hstack(pills).spacing(6.0)) },
    ))
    .spacing(6.0);

    // ── Fields: typed curation for shallow surfaces, else generic signal/noise ──
    let (field_rows, sys_el): (Vec<Element>, Element) = if path == "/conditional-access" {
        // Conditional Access → readable Conditions/Grant/Session from the resolved
        // summary (falls back to generic until it loads).
        match ca_summary {
            Some(ca) => (ca_rows(ca), Element::Empty),
            None => {
                let (signal, noise, noise_count) = generic_rows(obj);
                (signal, system_toggle(noise, noise_count, show_system, set_show_system))
            }
        }
    } else if let Some(spec) = typed_spec(path) {
        (curated_rows(obj, spec), Element::Empty)
    } else {
        let (signal, noise, noise_count) = generic_rows(obj);
        (signal, system_toggle(noise, noise_count, show_system, set_show_system))
    };

    // ── Footer: Copy + Raw-JSON toggle (and the raw textbox when open) ──
    let copy_btn = button("Copy").on_click({
        let j = json.to_string();
        move || {
            let _ = copy_to_clipboard(&j);
        }
    });
    let raw_btn = button(if show_raw { "Hide raw JSON" } else { "Raw JSON" }).on_click({
        let set_show_raw = set_show_raw.clone();
        move || set_show_raw.call(!show_raw)
    });
    let footer = hstack((Element::from(copy_btn), Element::from(raw_btn))).spacing(8.0);
    let raw_el: Element = if show_raw {
        border(text_box(json.to_string()).multiline().enabled(false))
            .corner_radius(4.0)
            .border_brush(theme::LINE)
            .padding(Thickness::uniform(4.0))
            .into()
    } else {
        Element::Empty
    };

    // Settings Catalog's deep settingInstance tree (fetched separately, only present
    // for that surface) — a collapsible tree under the metadata.
    let settings_el: Element = if path == "/settings-catalog" {
        match settings_json {
            Some(s) if !s.trim().is_empty() => settings_section(s),
            _ => Element::Empty,
        }
    } else {
        Element::Empty
    };

    // ── Status grid + resolved Assignments tables (assignable surfaces only) ──
    // `assignments = None` → surface isn't assignable, render nothing; `Some([])` →
    // assignable but unassigned, render the empty-state. Group GUIDs are resolved to
    // names server-side (DirectoryObjectResolver), like Conditional Access.
    let scope_tags = obj
        .get("roleScopeTagIds")
        .and_then(|v| v.as_array())
        .map(|a| a.len())
        .unwrap_or(0);
    let status_el: Element = match assignments {
        Some(items) => {
            let total = items.len();
            let inc = items.iter().filter(|a| a.kind != "exclusionGroup").count();
            let exc = total - inc;
            status_grid(vec![
                ("Assignments", total.to_string()),
                ("Included", inc.to_string()),
                ("Excluded", exc.to_string()),
                ("Scope tags", scope_tags.to_string()),
            ])
        }
        None => Element::Empty,
    };
    let assignments_el: Element = match assignments {
        Some(items) if !items.is_empty() => {
            let included: Vec<&Assignment> =
                items.iter().filter(|a| a.kind != "exclusionGroup").collect();
            let excluded: Vec<&Assignment> =
                items.iter().filter(|a| a.kind == "exclusionGroup").collect();
            let mut secs: Vec<Element> =
                vec![section_header(&format!("Assignments ({})", items.len()))];
            if !included.is_empty() {
                secs.push(assignment_table("Included", &included));
            }
            if !excluded.is_empty() {
                secs.push(assignment_table("Excluded", &excluded));
            }
            vstack(secs).spacing(6.0).into()
        }
        Some(_) => vstack((
            section_header("Assignments (0)"),
            Element::from(
                caption("No assignments configured")
                    .foreground(theme::TEXT_3)
                    .font_family(theme::FONT_UI),
            ),
        ))
        .spacing(4.0)
        .into(),
        None => Element::Empty,
    };

    // Information hierarchy: header → status cards → assignments → a named section
    // for the configured fields → deep settings tree → footer/raw. Each major group
    // gets a section_header (bold title + hairline divider) so the panel reads as
    // Intune-style sections rather than one flat field dump.
    let content = vstack(vec![
        Element::from(header),
        status_el,
        assignments_el,
        section_header(detail_section_title(path)),
        Element::from(vstack(field_rows).spacing(2.0)),
        sys_el,
        settings_el,
        Element::from(footer),
        raw_el,
    ])
    .spacing(10.0)
    .margin(Thickness::uniform(4.0));

    scroll_viewer(content).height(height).into()
}

fn raw_fallback(json: &str, height: f64) -> Element {
    scroll_viewer(text_box(json.to_string()).multiline().enabled(false))
        .height(height)
        .into()
}

// ── Conditional Access typed detail ─────────────────────────────────────────

// Render a resolved CA summary as Conditions / Grant / Session sections.
fn ca_rows(ca: &CaSummary) -> Vec<Element> {
    let mut rows: Vec<Element> = Vec::new();
    rows.push(sub_header("Conditions".to_string(), 0.0));
    rows.push(kv_row("Users", ca.conditions.users.clone(), 14.0, false));
    rows.push(kv_row("Applications", ca.conditions.applications.clone(), 14.0, false));
    rows.push(kv_row("Platforms", ca.conditions.platforms.clone(), 14.0, false));
    rows.push(kv_row("Locations", ca.conditions.locations.clone(), 14.0, false));
    rows.push(kv_row("Client apps", ca.conditions.client_apps.clone(), 14.0, false));
    rows.push(kv_row("Sign-in risk", ca.conditions.sign_in_risk.clone(), 14.0, false));
    rows.push(kv_row("User risk", ca.conditions.user_risk.clone(), 14.0, false));

    rows.push(sub_header("Grant controls".to_string(), 0.0));
    let op = if ca.grant_operator.is_empty() { "—".to_string() } else { ca.grant_operator.clone() };
    rows.push(kv_row("Operator", op, 14.0, false));
    let gc = if ca.grant_controls.is_empty() { "none".to_string() } else { ca.grant_controls.join(", ") };
    rows.push(kv_row("Controls", gc, 14.0, false));

    rows.push(sub_header("Session controls".to_string(), 0.0));
    let sc = if ca.session_controls.is_empty() { "none".to_string() } else { ca.session_controls.join(", ") };
    rows.push(kv_row("Controls", sc, 14.0, false));
    rows
}

// ── Settings Catalog deep tree ──────────────────────────────────────────────

// Render the fetched settings[] array as a collapsible tree: one top node per
// setting (labelled by its settingDefinitionId), children = the settingInstance
// value hierarchy.
fn settings_section(settings_json: &str) -> Element {
    let parsed: Value = match serde_json::from_str(settings_json) {
        Ok(v) => v,
        Err(_) => return Element::Empty,
    };
    let Some(arr) = parsed.as_array() else {
        return Element::Empty;
    };
    if arr.is_empty() {
        return Element::Empty;
    }

    let nodes: Vec<TreeNodeDef> = arr
        .iter()
        .enumerate()
        .map(|(i, s)| {
            let label = s
                .get("settingInstance")
                .and_then(|si| si.get("settingDefinitionId"))
                .and_then(|v| v.as_str())
                .map(str::to_string)
                .unwrap_or_else(|| format!("Setting {}", i + 1));
            let mut node = tree_node(label);
            if let Some(Value::Object(si)) = s.get("settingInstance") {
                for (k, child) in si {
                    if k == "settingDefinitionId" {
                        continue; // already the node label
                    }
                    node = node.child(json_tree(k, child));
                }
            }
            node
        })
        .collect();

    let header = caption(format!("Settings ({})", arr.len()))
        .font_family(theme::FONT_UI)
        .foreground(theme::BRAND_BRIGHT);
    vstack((
        Element::from(header),
        Element::from(tree_view(nodes).height(300.0)),
    ))
    .spacing(6.0)
    .into()
}

// Recursively turn a JSON value into a tree node (objects/arrays branch; scalars
// become "key: value" leaves).
fn json_tree(key: &str, v: &Value) -> TreeNodeDef {
    match v {
        Value::Object(map) => {
            let mut node = tree_node(key.to_string());
            for (k, child) in map {
                node = node.child(json_tree(k, child));
            }
            node
        }
        Value::Array(arr) => {
            let mut node = tree_node(format!("{key} [{}]", arr.len()));
            for (i, child) in arr.iter().enumerate() {
                node = node.child(json_tree(&i.to_string(), child));
            }
            node
        }
        _ => tree_node(format!("{key}: {}", fmt_scalar(v))),
    }
}

// ── Field rendering ──────────────────────────────────────────────────────────

fn generic_rows(
    obj: &serde_json::Map<String, Value>,
) -> (Vec<Element>, Vec<Element>, usize) {
    let mut signal: Vec<Element> = Vec::new();
    let mut noise: Vec<Element> = Vec::new();
    let mut noise_count = 0usize;

    // Hoist description right under the header — in a bordered, fixed-height box
    // (Intune-style) so a long description can't dominate the panel.
    if let Some(d) = obj.get("description").filter(|v| !v.is_null()) {
        match d.as_str() {
            Some(s) => signal.push(boxed_value("Description", s, 0.0)),
            None => push_value(&mut signal, "description", d, 0, false),
        }
    }
    for (k, v) in obj {
        if k == "displayName" || k == "name" || k == "description" {
            continue; // shown in header / hoisted
        }
        if is_noise(k) {
            noise_count += 1;
            push_value(&mut noise, k, v, 0, true);
        } else {
            push_value(&mut signal, k, v, 0, false);
        }
    }
    (signal, noise, noise_count)
}

fn curated_rows(
    obj: &serde_json::Map<String, Value>,
    spec: &[(&str, &str)],
) -> Vec<Element> {
    let mut rows: Vec<Element> = Vec::new();
    for &(label, key) in spec {
        if let Some(v) = obj.get(key).filter(|v| !v.is_null()) {
            match (key, v.as_str()) {
                ("description", Some(s)) => rows.push(boxed_value(label, s, 0.0)),
                _ => push_value(&mut rows, label, v, 0, false),
            }
        }
    }
    rows
}

fn system_toggle(
    noise: Vec<Element>,
    noise_count: usize,
    show_system: bool,
    set_show_system: &SetState<bool>,
) -> Element {
    if noise_count == 0 {
        return Element::Empty;
    }
    let label = format!(
        "{} System fields ({noise_count})",
        if show_system { "▾" } else { "▸" }
    );
    let btn = button(label).on_click({
        let set_show_system = set_show_system.clone();
        move || set_show_system.call(!show_system)
    });
    if show_system {
        vstack((
            Element::from(btn),
            Element::from(vstack(noise).spacing(2.0)),
        ))
        .spacing(4.0)
        .into()
    } else {
        Element::from(btn)
    }
}

// Recursively push field rows. Scalars → one kv row; objects/arrays → an indented
// sub-header then their children (depth-capped); scalar arrays inline.
fn push_value(out: &mut Vec<Element>, key: &str, v: &Value, depth: usize, muted: bool) {
    let indent = depth as f64 * 14.0;
    match v {
        Value::Object(map) => {
            if depth >= 4 || map.is_empty() {
                out.push(kv_row(&humanize(key), "—".into(), indent, muted));
                return;
            }
            out.push(sub_header(humanize(key), indent));
            for (k, child) in map {
                push_value(out, k, child, depth + 1, muted);
            }
        }
        Value::Array(arr) => {
            if arr.is_empty() {
                out.push(kv_row(&humanize(key), "—".into(), indent, muted));
            } else if arr.iter().all(is_scalar) {
                let joined = arr.iter().map(fmt_value).collect::<Vec<_>>().join(", ");
                out.push(kv_row(&humanize(key), joined, indent, muted));
            } else {
                out.push(sub_header(format!("{} ({})", humanize(key), arr.len()), indent));
                if depth >= 4 {
                    out.push(kv_row("", "[…]".into(), indent + 14.0, muted));
                    return;
                }
                for (i, child) in arr.iter().take(20).enumerate() {
                    push_value(out, &format!("[{i}]"), child, depth + 1, muted);
                }
                if arr.len() > 20 {
                    out.push(sub_header(format!("… {} more", arr.len() - 20), indent + 14.0));
                }
            }
        }
        // Long blobs (scripts, base64, multi-paragraph text) go in a contained,
        // scrollable box. If the blob is base64 that decodes to readable text
        // (e.g. an encoded detection/remediation script), show the decoded text.
        Value::String(s) if s.chars().count() > 160 => {
            let shown = maybe_decode_b64_text(s).unwrap_or_else(|| s.clone());
            out.push(boxed_value(&humanize(key), &shown, indent));
        }
        _ => out.push(kv_row(&humanize(key), fmt_value(v), indent, muted)),
    }
}

// One key/value line (Intune Properties style): a muted label in a fixed-width
// left column, a prominent value on the right. Both use the UI face (not mono) so
// prose, URLs and flags read cleanly; the value wraps in its Star column.
fn kv_row(key: &str, value: String, indent: f64, muted: bool) -> Element {
    let key_color = if muted { theme::TEXT_4 } else { theme::TEXT_3 };
    let val_color = if muted { theme::TEXT_4 } else { theme::TEXT };
    let key_el = caption(key.to_string())
        .font_family(theme::FONT_UI)
        .foreground(key_color)
        .wrap();
    let val_el = caption(value)
        .font_family(theme::FONT_UI)
        .foreground(val_color)
        .wrap();
    grid((key_el.grid_column(0), val_el.grid_column(1)))
        .columns([GridLength::Pixel(200.0), GridLength::Star(1.0)])
        .column_spacing(16.0)
        .margin(Thickness::xy(indent, 5.0))
        .into()
}

/// A long value rendered like Intune's read-only boxes: the text sits in a
/// bordered, fixed-height box that scrolls, so prose, scripts and base64 blobs
/// stay contained instead of dominating the panel. Used for `description` and
/// any value too long for a one-line kv row.
fn boxed_value(label: &str, value: &str, indent: f64) -> Element {
    let key_el = caption(label.to_string())
        .font_family(theme::FONT_UI)
        .foreground(theme::TEXT_3)
        .wrap();
    let boxed = border(
        scroll_viewer(
            caption(value.to_string())
                .font_family(theme::FONT_MONO)
                .foreground(theme::TEXT_2)
                .wrap(),
        )
        .height(88.0),
    )
    .border_brush(theme::LINE)
    .background(theme::SURFACE_2)
    .corner_radius(4.0)
    .padding(Thickness::uniform(8.0));
    grid((key_el.grid_column(0), Element::from(boxed).grid_column(1)))
        .columns([GridLength::Pixel(200.0), GridLength::Star(1.0)])
        .column_spacing(16.0)
        .margin(Thickness::xy(indent, 4.0))
        .into()
}

// A nested-group label (one level down from a section): emphasized over the plain
// kv rows so grouped/array data reads as its own sub-section.
fn sub_header(label: String, indent: f64) -> Element {
    body_strong(label)
        .font_family(theme::FONT_UI)
        .font_size(13.0)
        .foreground(theme::TEXT_2)
        .margin(Thickness::xy(indent, 6.0))
        .into()
}

// A top-level section heading in the Intune Properties style: a bold title over a
// full-width hairline divider, with space above to separate it from the prior
// group. This is what turns the flat field dump into named, scannable sections.
fn section_header(title: &str) -> Element {
    vstack((
        Element::from(
            body_strong(title.to_string())
                .font_family(theme::FONT_UI)
                .font_size(15.0)
                .foreground(theme::TEXT),
        ),
        Element::from(
            border(Element::Empty)
                .background(theme::LINE)
                .height(1.0),
        ),
    ))
    .spacing(6.0)
    .margin(Thickness::xy(0.0, 12.0))
    .into()
}

// Title for the main field section, mirroring Intune's wording where we can.
fn detail_section_title(path: &str) -> &'static str {
    match path {
        "/apps" => "App information",
        "/conditional-access" => "Policy",
        "/groups" => "Group information",
        _ => "Details",
    }
}

fn pill(text: String, fg: Color, bg: Color) -> Element {
    border(caption(text).foreground(fg).font_family(theme::FONT_UI))
        .background(bg)
        .corner_radius(9.0)
        .padding(Thickness::xy(8.0, 2.0))
        .into()
}

// ── Status grid + Assignments table (shared rich-detail kit) ─────────────────

// One status card: small uppercase label over a large display number. Mirrors the
// posture/CA stat cards so every detail panel reads with the same visual weight.
fn stat_card(label: &str, value: String) -> Element {
    border(
        vstack((
            Element::from(
                caption(label.to_uppercase())
                    .font_family(theme::FONT_UI)
                    .foreground(theme::TEXT_4),
            ),
            Element::from(
                body_strong(value)
                    .font_size(24.0)
                    .font_family(theme::FONT_DISPLAY)
                    .foreground(theme::TEXT),
            ),
        ))
        .spacing(2.0),
    )
    .background(theme::SURFACE_2)
    .border_brush(theme::LINE)
    .corner_radius(10.0)
    .padding(Thickness::uniform(12.0))
    .into()
}

// A row of equal-width status cards (the IC "status grid").
fn status_grid(cards: Vec<(&str, String)>) -> Element {
    let cols: Vec<GridLength> = cards.iter().map(|_| GridLength::Star(1.0)).collect();
    let cells: Vec<Element> = cards
        .into_iter()
        .enumerate()
        .map(|(i, (label, value))| stat_card(label, value).grid_column(i as i32))
        .collect();
    grid(cells).columns(cols).column_spacing(10.0).into()
}

// Display label for one assignment target: resolved group name, the all-users /
// all-devices virtual target, or the raw id as a last resort.
fn assign_label(a: &Assignment) -> String {
    match a.kind.as_str() {
        "allUsers" => "All users".to_string(),
        "allDevices" => "All devices".to_string(),
        _ => a
            .group_name
            .clone()
            .filter(|s| !s.is_empty())
            .or_else(|| a.group_id.clone().filter(|s| !s.is_empty()))
            .unwrap_or_else(|| "(group)".to_string()),
    }
}

// Intent / inclusion mode for the middle column (apps carry an install intent;
// policies are plain include/exclude).
fn assign_intent(a: &Assignment) -> String {
    if let Some(i) = a.intent.as_ref().filter(|s| !s.is_empty()) {
        return i.clone();
    }
    match a.kind.as_str() {
        "exclusionGroup" => "Exclude".to_string(),
        "allUsers" | "allDevices" => "—".to_string(),
        _ => "Include".to_string(),
    }
}

// Assignment filter mode (the filter id is an unresolved GUID, so show the mode).
fn assign_filter(a: &Assignment) -> String {
    match (&a.filter_id, &a.filter_mode) {
        (Some(id), Some(m)) if !id.is_empty() => {
            let mut c = m.chars();
            c.next()
                .map(|f| f.to_uppercase().collect::<String>() + c.as_str())
                .unwrap_or_else(|| m.clone())
        }
        _ => "None".to_string(),
    }
}

// A 3-column assignment table (Group | Intent | Filter) under a sub-header.
fn assignment_table(title: &str, items: &[&Assignment]) -> Element {
    let head = |t: &str, col: i32| {
        caption(t.to_string())
            .foreground(theme::TEXT_4)
            .font_family(theme::FONT_UI)
            .grid_column(col)
    };
    let header = grid((head("GROUP", 0), head("INTENT", 1), head("FILTER", 2)))
        .columns([GridLength::Star(2.0), GridLength::Star(1.0), GridLength::Star(1.0)])
        .column_spacing(8.0);
    let rows: Vec<Element> = items
        .iter()
        .map(|a| {
            grid((
                caption(assign_label(a))
                    .foreground(theme::TEXT)
                    .font_family(theme::FONT_UI)
                    .wrap()
                    .grid_column(0),
                caption(assign_intent(a))
                    .foreground(theme::TEXT_2)
                    .font_family(theme::FONT_UI)
                    .grid_column(1),
                caption(assign_filter(a))
                    .foreground(theme::TEXT_3)
                    .font_family(theme::FONT_MONO)
                    .grid_column(2),
            ))
            .columns([GridLength::Star(2.0), GridLength::Star(1.0), GridLength::Star(1.0)])
            .column_spacing(8.0)
            .margin(Thickness::xy(0.0, 2.0))
            .into()
        })
        .collect();
    vstack((
        Element::from(sub_header(title.to_string(), 0.0)),
        Element::from(header),
        Element::from(vstack(rows).spacing(1.0)),
    ))
    .spacing(4.0)
    .into()
}

// ── value helpers ──────────────────────────────────────────────────────────

fn is_scalar(v: &Value) -> bool {
    !matches!(v, Value::Object(_) | Value::Array(_))
}

fn fmt_scalar(v: &Value) -> String {
    match v {
        Value::String(s) => s.clone(),
        Value::Bool(b) => b.to_string(),
        Value::Number(n) => n.to_string(),
        Value::Null => "—".to_string(),
        other => other.to_string(),
    }
}

/// Human-facing value: booleans → Yes/No, nulls/empties → em-dash, ISO dates →
/// short date, everything else as-is. Makes the panel read like a product UI,
/// not a JSON dump.
fn fmt_value(v: &Value) -> String {
    match v {
        Value::Bool(true) => "Yes".to_string(),
        Value::Bool(false) => "No".to_string(),
        Value::Null => "—".to_string(),
        Value::String(s) if s.trim().is_empty() => "—".to_string(),
        Value::String(s) if looks_like_date(s) => short_date(s),
        _ => fmt_scalar(v),
    }
}

/// Minimal standard-base64 decoder (no external crate). Ignores whitespace and
/// `=` padding; returns `None` on any non-base64 byte.
fn b64_decode(s: &str) -> Option<Vec<u8>> {
    fn val(c: u8) -> Option<u8> {
        match c {
            b'A'..=b'Z' => Some(c - b'A'),
            b'a'..=b'z' => Some(c - b'a' + 26),
            b'0'..=b'9' => Some(c - b'0' + 52),
            b'+' => Some(62),
            b'/' => Some(63),
            _ => None,
        }
    }
    let mut out = Vec::new();
    let mut buf = 0u32;
    let mut bits = 0u32;
    for &c in s.as_bytes() {
        if c == b'=' || c.is_ascii_whitespace() {
            continue;
        }
        buf = (buf << 6) | val(c)? as u32;
        bits += 6;
        if bits >= 8 {
            bits -= 8;
            out.push((buf >> bits) as u8);
        }
    }
    Some(out)
}

/// If `s` is one big base64 blob that decodes to readable UTF-8 text (e.g. a
/// base64-encoded detection/remediation script), return the decoded text so it
/// can be shown verbatim instead of an opaque blob. Returns `None` otherwise
/// (mixed strings, binary payloads like icons, etc.).
fn maybe_decode_b64_text(s: &str) -> Option<String> {
    let t = s.trim();
    if t.len() < 24 || t.len() % 4 != 0 {
        return None;
    }
    if !t
        .bytes()
        .all(|c| c.is_ascii_alphanumeric() || matches!(c, b'+' | b'/' | b'='))
    {
        return None;
    }
    let text = String::from_utf8(b64_decode(t)?).ok()?;
    // Reject binary-ish payloads — keep only mostly-printable text (scripts/XML).
    let total = text.chars().count();
    if total == 0 {
        return None;
    }
    let printable = text
        .chars()
        .filter(|c| !c.is_control() || matches!(c, '\n' | '\r' | '\t'))
        .count();
    (printable as f64 >= total as f64 * 0.95).then_some(text)
}

/// Decode a base64 image payload (PNG/JPEG/GIF/BMP) to a content-addressed temp
/// file and return a `file://` URI the reactor's Image/BitmapImage can display.
/// Returns None for non-image or invalid data. Cached by content hash so each
/// unique icon is written at most once.
fn icon_file_uri(b64: &str) -> Option<String> {
    use std::hash::{Hash, Hasher};
    let bytes = b64_decode(b64.trim())?;
    if bytes.len() < 16 {
        return None;
    }
    let ext = if bytes.starts_with(&[0x89, b'P', b'N', b'G']) {
        "png"
    } else if bytes.starts_with(&[0xFF, 0xD8, 0xFF]) {
        "jpg"
    } else if bytes.starts_with(b"GIF8") {
        "gif"
    } else if bytes.starts_with(b"BM") {
        "bmp"
    } else {
        return None;
    };
    let mut h = std::collections::hash_map::DefaultHasher::new();
    bytes.hash(&mut h);
    let mut path = std::env::temp_dir();
    path.push("cmprojectx-icons");
    let _ = std::fs::create_dir_all(&path);
    path.push(format!("cmx-icon-{:016x}.{ext}", h.finish()));
    if !path.exists() {
        std::fs::write(&path, &bytes).ok()?;
    }
    Some(format!("file:///{}", path.to_string_lossy().replace('\\', "/")))
}

/// `2026-06-11T…` / `2026-06-11 …` heuristic — `YYYY-MM-DD` prefix.
fn looks_like_date(s: &str) -> bool {
    let b = s.as_bytes();
    b.len() >= 10
        && b[0..4].iter().all(u8::is_ascii_digit)
        && b[4] == b'-'
        && b[7] == b'-'
}

/// CANONICAL label formatter — use this EVERYWHERE a raw Graph/JSON key is shown
/// to a human, instead of hand-written label strings or `match key { … }` tables.
/// It is purely algorithmic (no word lookup): it inserts a space at each camelCase
/// boundary (and at the end of an acronym run, `URLValue` → `URL Value`) and
/// capitalizes the first letter, preserving the source casing of the rest so
/// acronyms already capitalized in the key stay capitalized
/// (`minimumFreeDiskSpaceInMB` → "Minimum Free Disk Space In MB",
/// `@odata.type` → "Type", `informationUrl` → "Information Url"). New surfaces
/// that project Graph fields should route their labels through here.
pub(crate) fn humanize(key: &str) -> String {
    // Array indices stay literal; `@odata.context` → last dotted segment.
    if key.starts_with('[') {
        return key.to_string();
    }
    let base = key.trim_start_matches('@');
    let base = base.rsplit('.').next().unwrap_or(base);

    let chars: Vec<char> = base.chars().collect();
    let mut out = String::new();
    for i in 0..chars.len() {
        let ch = chars[i];
        // Separators (`_`, `-`, space) collapse to a single word break.
        if matches!(ch, '_' | '-' | ' ') {
            if !out.is_empty() && !out.ends_with(' ') {
                out.push(' ');
            }
            continue;
        }
        let boundary = if let Some(p) = i.checked_sub(1).map(|j| chars[j]) {
            let next_lower = chars.get(i + 1).is_some_and(|n| n.is_lowercase());
            // lower/digit → Upper, or the last cap of an acronym run before a word
            (ch.is_uppercase() && (p.is_lowercase() || p.is_ascii_digit()))
                || (ch.is_uppercase() && p.is_uppercase() && next_lower)
        } else {
            false
        };
        if boundary {
            out.push(' ');
        }
        if i == 0 {
            out.extend(ch.to_uppercase());
        } else {
            out.push(ch);
        }
    }
    out
}

fn str_field(obj: &serde_json::Map<String, Value>, key: &str) -> Option<String> {
    obj.get(key).and_then(|v| v.as_str()).map(str::to_string)
}

/// Trim an ISO-8601 timestamp to its date (`2026-06-09`).
fn short_date(s: &str) -> String {
    s.split(['T', ' ']).next().unwrap_or(s).to_string()
}

// Pipe text through clip.exe — no clipboard API in the reactor bindings, and this
// avoids a `windows`-crate dep for one call. (Mirrors signin.rs's helper; kept
// local so the config view doesn't couple to the sign-in module.)
fn copy_to_clipboard(text: &str) -> std::io::Result<()> {
    use std::io::Write;
    use std::os::windows::process::CommandExt;
    use std::process::{Command, Stdio};
    const CREATE_NO_WINDOW: u32 = 0x0800_0000;
    let mut child = Command::new("clip.exe")
        .creation_flags(CREATE_NO_WINDOW)
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()?;
    child.stdin.take().expect("piped stdin").write_all(text.as_bytes())?;
    child.wait()?;
    Ok(())
}

/// Graph metadata / housekeeping fields, greyed behind the System-fields toggle.
/// Includes embedded binary blobs (`largeIcon` etc.) whose base64 payload would
/// otherwise dump a multi-kilobyte string into the field list.
fn is_noise(key: &str) -> bool {
    key.starts_with('@')
        || matches!(
            key,
            "id" | "version"
                | "createdDateTime"
                | "lastModifiedDateTime"
                | "roleScopeTagIds"
                | "supportsScopeTags"
                | "isAssigned"
                | "largeIcon"
                | "largeCover"
                | "icon"
                | "committedContentVersion"
                | "uploadState"
                | "packageId"
        )
}

/// Curated (label, json-key) field specs for the shallow surfaces — a clean card
/// over the same renderer. `None` → use the generic signal/noise path.
fn typed_spec(path: &str) -> Option<&'static [(&'static str, &'static str)]> {
    Some(match path {
        "/scope-tags" => &[
            ("Name", "displayName"),
            ("Description", "description"),
            ("Built-in", "isBuiltIn"),
        ],
        "/device-categories" => &[("Name", "displayName"), ("Description", "description")],
        "/named-locations" => &[
            ("Name", "displayName"),
            ("Type", "@odata.type"),
            ("Trusted", "isTrusted"),
            ("IP ranges", "ipRanges"),
            ("Countries", "countriesAndRegions"),
        ],
        "/assignment-filters" => &[
            ("Name", "displayName"),
            ("Platform", "platform"),
            ("Management type", "assignmentFilterManagementType"),
            ("Rule", "rule"),
        ],
        "/notification-templates" => &[
            ("Name", "displayName"),
            ("Default locale", "defaultLocale"),
            ("Branding", "brandingOptions"),
        ],
        _ => return None,
    })
}

// ── M7 typed quick-edit forms (progressive enhancement over the raw JSON editor) ──────
// The input kind per field.
#[derive(Clone, Copy, PartialEq, Debug)]
pub enum FieldKind {
    Text,
    Multiline,
    Bool,
    /// Integer field — edits a JSON number (e.g. termsAndConditions.version). Blank clears
    /// it to null; a non-integer keystroke is rejected (keeps the last valid value).
    Number,
}

/// Parse a Number field's text into the JSON value to store: empty ⇒ `Null` (clear the
/// field), a valid integer ⇒ `Number`, anything else ⇒ `None` (reject — don't write garbage
/// into the draft). Pure; unit-tested.
fn number_value(text: &str) -> Option<Value> {
    let t = text.trim();
    if t.is_empty() {
        Some(Value::Null)
    } else {
        t.parse::<i64>().ok().map(|n| Value::Number(n.into()))
    }
}

// Typed EDIT fields for the WRITABLE shallow surfaces (assignment-filters is read-only, so
// it has no form). Parallel to typed_spec (read-only cards) but carries an input kind.
fn typed_edit_fields(path: &str) -> Option<&'static [(&'static str, &'static str, FieldKind)]> {
    use FieldKind::*;
    Some(match path {
        "/scope-tags" => &[("Name", "displayName", Text), ("Description", "description", Multiline)],
        "/device-categories" => &[("Name", "displayName", Text), ("Description", "description", Multiline)],
        "/named-locations" => &[("Name", "displayName", Text), ("Trusted network", "isTrusted", Bool)],
        "/notification-templates" => &[("Name", "displayName", Text), ("Default locale", "defaultLocale", Text)],
        "/role-definitions" => &[("Name", "displayName", Text), ("Description", "description", Multiline)],
        "/auth-contexts" => &[
            ("Name", "displayName", Text),
            ("Description", "description", Multiline),
            ("Available to users", "isAvailable", Bool),
        ],
        "/terms-conditions" => &[
            ("Name", "displayName", Text),
            ("Title", "title", Text),
            ("Description", "description", Multiline),
            ("Body text", "bodyText", Multiline),
            ("Acceptance statement", "acceptanceStatement", Multiline),
            ("Version", "version", Number),
        ],
        _ => return None,
    })
}

// True if `path` has a typed quick-edit form (a writable shallow surface).
pub fn has_typed_edit_form(path: &str) -> bool {
    typed_edit_fields(path).is_some()
}

// A typed quick-edit form for a shallow surface. It edits the SAME `draft` JSON string the
// raw editor edits (each input rewrites one field and pushes the whole object back), so the
// existing preview-diff → confirm → PATCH save pipeline in list_workspace is untouched.
// Returns None when the surface has no typed spec (caller falls back to the raw editor).
// The bool is client-side validity: the Name (displayName) field must be non-empty — the
// caller gates Save on it. State lives in the parent (set_draft/set_review), per the reactor
// rules (this is a render helper, not a nested component()).
pub fn typed_edit_form(
    draft: &str,
    path: &str,
    set_draft: &SetState<String>,
    set_review: &SetState<bool>,
) -> Option<(Element, bool)> {
    let fields = typed_edit_fields(path)?;
    let root: Value = serde_json::from_str(draft).unwrap_or(Value::Object(Default::default()));
    let obj = root.as_object().cloned().unwrap_or_default();

    let mut valid = true;
    let mut rows: Vec<Element> = Vec::new();
    for (label, key, kind) in fields.iter().copied() {
        let cur = obj.get(key).cloned().unwrap_or(Value::Null);
        let input: Element = match kind {
            FieldKind::Text | FieldKind::Multiline => {
                let s = cur.as_str().unwrap_or("").to_string();
                if key == "displayName" && s.trim().is_empty() {
                    valid = false;
                }
                let tb = text_box(s);
                let tb = if kind == FieldKind::Multiline { tb.multiline() } else { tb };
                tb.on_text_changed({
                    let (sd, sr, o) = (set_draft.clone(), set_review.clone(), obj.clone());
                    move |t| push_field(&o, key, Value::String(t), &sd, &sr)
                })
                .into()
            }
            FieldKind::Bool => {
                let on = cur.as_bool().unwrap_or(false);
                button(if on { "Yes" } else { "No" })
                    .on_click({
                        let (sd, sr, o) = (set_draft.clone(), set_review.clone(), obj.clone());
                        move || push_field(&o, key, Value::Bool(!on), &sd, &sr)
                    })
                    .into()
            }
            FieldKind::Number => {
                // Show the current integer (or blank); reject non-integer input so the draft
                // never carries a malformed number into the PATCH.
                let s = cur.as_i64().map(|n| n.to_string()).unwrap_or_default();
                text_box(s)
                    .on_text_changed({
                        let (sd, sr, o) = (set_draft.clone(), set_review.clone(), obj.clone());
                        move |t: String| {
                            if let Some(v) = number_value(&t) {
                                push_field(&o, key, v, &sd, &sr);
                            }
                        }
                    })
                    .into()
            }
        };
        rows.push(
            vstack((Element::from(caption(label.to_string()).foreground(theme::TEXT_4)), input))
                .spacing(2.0)
                .into(),
        );
    }
    if !valid {
        rows.push(caption("Name is required.".to_string()).foreground(theme::ERROR).into());
    }
    Some((vstack(rows).spacing(10.0).into(), valid))
}

// Rewrite one field in the object and push the whole pretty-printed object back into the
// shared draft (invalidating any pending review so a stale diff can't be confirmed).
fn push_field(
    obj: &serde_json::Map<String, Value>,
    key: &str,
    val: Value,
    set_draft: &SetState<String>,
    set_review: &SetState<bool>,
) {
    let mut m = obj.clone();
    m.insert(key.to_string(), val);
    set_review.call(false);
    set_draft.call(serde_json::to_string_pretty(&Value::Object(m)).unwrap_or_default());
}

#[cfg(test)]
mod tests {
    use super::{has_typed_edit_form, number_value, typed_edit_fields, FieldKind};
    use serde_json::Value;

    #[test]
    fn number_value_parses_clears_and_rejects() {
        assert_eq!(number_value(""), Some(Value::Null)); // blank clears the field
        assert_eq!(number_value("   "), Some(Value::Null)); // whitespace-only clears too
        assert_eq!(number_value("42"), Some(Value::Number(42.into())));
        assert_eq!(number_value("  -3 "), Some(Value::Number((-3).into())));
        assert_eq!(number_value("1.5"), None); // non-integer rejected (no draft write)
        assert_eq!(number_value("abc"), None);
        assert_eq!(number_value("0x10"), None);
    }

    #[test]
    fn new_writable_surfaces_have_typed_forms() {
        for p in ["/role-definitions", "/auth-contexts", "/terms-conditions"] {
            assert!(has_typed_edit_form(p), "{p} should have a typed quick-edit form");
        }
        // A read-only / unknown surface still falls back to the raw editor.
        assert!(!has_typed_edit_form("/assignment-filters"));
        assert!(!has_typed_edit_form("/device-configs"));
    }

    #[test]
    fn terms_conditions_version_is_a_number_field() {
        let fields = typed_edit_fields("/terms-conditions").expect("form exists");
        let version = fields.iter().find(|f| f.1 == "version").expect("version field");
        assert_eq!(version.2, FieldKind::Number);
        // displayName stays Text — the Name-required gate keys off it.
        let name = fields.iter().find(|f| f.1 == "displayName").expect("name field");
        assert_eq!(name.2, FieldKind::Text);
    }
}
