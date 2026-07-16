fn main() {
    // With `--features self-contained` (release builds), bundle the entire Windows
    // App SDK runtime next to the exe so the client runs on a clean machine with no
    // runtime pre-installed. main.rs must skip the bootstrapper in that mode.
    // Without the feature (dev builds), stay framework-dependent: link against the
    // installed Windows App SDK runtime (mirrors the Phase 0 reactor-gate).
    if std::env::var_os("CARGO_FEATURE_SELF_CONTAINED").is_some() {
        windows_reactor_setup::as_self_contained();
    } else {
        windows_reactor_setup::as_framework_dependent();
    }
}
