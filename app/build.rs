fn main() {
    // Link against the installed Windows App SDK runtime (2.0.1+) rather than
    // bundling it. Mirrors crates/samples/reactor/framework-dependent and the
    // Phase 0 reactor-gate.
    windows_reactor_setup::as_framework_dependent();
}
