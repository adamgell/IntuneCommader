fn main() {
    // Link against the installed Windows App SDK runtime (2.0.1+), rather than
    // bundling it. Mirrors crates/samples/reactor/framework-dependent.
    windows_reactor_setup::as_framework_dependent();
}
