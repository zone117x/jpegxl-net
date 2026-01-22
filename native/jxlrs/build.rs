fn main() {
    let crate_dir = std::env::var("CARGO_MANIFEST_DIR").unwrap();

    // Generate C header file
    let config = cbindgen::Config::from_file("cbindgen.toml")
        .expect("Failed to read cbindgen.toml");

    cbindgen::Builder::new()
        .with_crate(&crate_dir)
        .with_config(config)
        .generate()
        .expect("Unable to generate C bindings")
        .write_to_file("include/jxlrs.h");

    println!("cargo:rerun-if-changed=src/");
    println!("cargo:rerun-if-changed=cbindgen.toml");
}
