//! Native OS integration — file/folder/save pickers (via `rfd`, i.e. the Windows
//! `IFileDialog`) and shell open / reveal-in-Explorer. Centralized so every screen
//! shares one implementation instead of pasting a path into a text box.
//!
//! The pickers are SYNCHRONOUS and must be called from a UI-thread event handler
//! (a button `on_click`), where the thread is STA + COM-initialized — `rfd` pumps
//! its own modal loop there, like any native dialog.

use std::os::windows::process::CommandExt;

const CREATE_NO_WINDOW: u32 = 0x0800_0000;

/// `(label, extensions)` filter pairs, e.g. `("Backup", &["zip"])`. Use `&["*"]`
/// for an all-files filter.
pub(crate) type Filters<'a> = &'a [(&'a str, &'a [&'a str])];

fn apply_filters(mut d: rfd::FileDialog, filters: Filters) -> rfd::FileDialog {
    for (name, exts) in filters {
        d = d.add_filter(*name, exts);
    }
    d
}

/// Pick one existing file. Returns its path, or `None` if cancelled.
pub(crate) fn pick_open_file(title: &str, filters: Filters) -> Option<String> {
    apply_filters(rfd::FileDialog::new().set_title(title), filters)
        .pick_file()
        .map(|p| p.to_string_lossy().into_owned())
}

/// Pick a folder. Returns its path, or `None` if cancelled.
#[allow(dead_code)] // part of the dialog API; wired by the folder-export / open-folder work
pub(crate) fn pick_folder(title: &str) -> Option<String> {
    rfd::FileDialog::new()
        .set_title(title)
        .pick_folder()
        .map(|p| p.to_string_lossy().into_owned())
}

/// Pick a save destination (with a default file name). Returns the chosen path,
/// or `None` if cancelled.
pub(crate) fn pick_save_file(title: &str, default_name: &str, filters: Filters) -> Option<String> {
    apply_filters(
        rfd::FileDialog::new()
            .set_title(title)
            .set_file_name(default_name),
        filters,
    )
    .save_file()
    .map(|p| p.to_string_lossy().into_owned())
}

/// Open a file with its default handler (Explorer's `start`), no console flash.
/// The empty `""` is `start`'s window-title arg — without it `start` treats the
/// path as the title and opens nothing.
pub(crate) fn open_with_default(path: &str) -> std::io::Result<()> {
    std::process::Command::new("cmd")
        .args(["/C", "start", "", path])
        .creation_flags(CREATE_NO_WINDOW)
        .spawn()
        .map(|_| ())
}

/// Reveal a file in Explorer with it selected.
pub(crate) fn reveal_in_explorer(path: &str) -> std::io::Result<()> {
    std::process::Command::new("explorer")
        .arg(format!("/select,{path}"))
        .spawn()
        .map(|_| ())
}
