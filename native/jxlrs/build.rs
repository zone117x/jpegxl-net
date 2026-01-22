fn main() {
    // Generate C# bindings for .NET interop
    csbindgen::Builder::default()
        .input_extern_file("src/lib.rs")
        .input_extern_file("src/decoder.rs")
        .input_extern_file("src/error.rs")
        .input_extern_file("src/types.rs")
        .csharp_dll_name("jxlrs")
        .csharp_namespace("JpegXL.Net.Native")
        .csharp_class_name("NativeMethods")
        .csharp_class_accessibility("public") // Make types public for API consumers
        .csharp_use_nint_types(false) // Use UIntPtr/IntPtr for netstandard2.0 compatibility
        .generate_csharp_file("../../src/JpegXL.Net/Native/NativeMethods.g.cs")
        .expect("Failed to generate C# bindings");

    println!("cargo:rerun-if-changed=src/");
}
