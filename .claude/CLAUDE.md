# Claude Code Instructions

This file contains instructions for Claude Code when working on this project.

## Project Overview

JpegXL.Net is a .NET library for decoding JPEG XL images. It wraps the jxl-rs Rust decoder with a C FFI layer.

## Build Commands

- **Full rebuild**: Use `./rebuild-all.sh` for building the native library, regenerating bindings, building .NET, and running tests
- **Example app**: Run `examples/JpegXL.Viewer/run-animation-test.sh` to test the viewer
- **Do NOT** use `cargo build` directly - the rebuild script handles cross-platform targets and copies libraries correctly

## macOS Viewer Development

Use `examples/JpegXL.MacOS/run.sh` for iterative development:

```bash
# Debug build (default)
./examples/JpegXL.MacOS/run.sh

# Release build
./examples/JpegXL.MacOS/run.sh --release
```

The script kills the previously running app (if any), builds, codesigns, and runs the app with a test animation file.

The script changes the current directory automatically, so do not use a `cd ...` command to run it.

**Optional flags:**
- `--file=<path>` - Open a specific image file instead of the default test animation
- `--log` - Log stdout to `examples/JpegXL.MacOS/logs/run.txt` (existing logs are renamed with unix timestamp)
- `--kill-after=<seconds>` - Auto-kill the app after N seconds (useful for automated testing)

**Sample files:** Test images of different types (HDR, animation, etc.) are available in `./examples/sample-files/`

```bash
# Open an HDR HLG test image
./examples/JpegXL.MacOS/run.sh --file=examples/sample-files/hdr_hlg_test.jxl

# Run for 5 seconds with logging
./examples/JpegXL.MacOS/run.sh --log --kill-after=5
```

**Development loop:**
1. Modify C# code
2. Run `./examples/JpegXL.MacOS/run.sh`
3. View app behavior (use `--log-file` to capture `Console.WriteLine` output)
4. Repeat

**Note:** If you modify Rust code in `native/jxl-ffi/`, you must run `./rebuild-all.sh` first to recompile the native library before using `run.sh`.

## Architecture

### jxl-rs (native/jxl-rs/)
- Third-party Rust JPEG XL decoder, included as a Git submodule (fork: `zone117x/jxl-rs`)
- Contains the actual JXL format parsing and pixel decoding logic
- Key paths: `jxl/src/` for decoder implementation, `jxl/resources/test/` for test images
- Look here for: decoder bugs, format parsing issues, understanding JXL spec behavior
- Changes here require `./rebuild-all.sh` to recompile

**Design goals for JpegXL.Net ↔ jxl-rs alignment:**
- JpegXL.Net should expose *all* functions, structs, enums, and fields available in jxl-rs
- Names and structure should match jxl-rs as closely as possible
- Only deviate from jxl-rs when idiomatic Rust must be adapted to idiomatic C#:
  - Rust generics → FFI state machine (Rust generics can't cross FFI boundary)
  - snake_case → PascalCase
  - Rust enums with data → C# patterns (see below)

**Rust enums with data → C# conversion pattern:**
Rust enums like `enum Foo { A, B { x: f32 } }` can't cross FFI directly. The pattern used:

1. **jxl-rs** (Rust): `enum JxlBitDepth { Int { bits }, Float { bits, exponent_bits } }`
2. **jxl-ffi** (FFI): Create a C-compatible struct with enum discriminator + all fields:
   ```rust
   pub enum JxlBitDepthType { Int = 0, Float = 1 }
   pub struct JxlBitDepth {
       pub Type: JxlBitDepthType,
       pub BitsPerSample: u32,
       pub ExponentBitsPerSample: u32,  // 0 for Int
   }
   ```
3. **JpegXL.Net** (C#): csbindgen auto-generates the struct; add convenience via extensions:
   ```csharp
   // In JxlBitDepth.Extensions.cs
   public partial struct JxlBitDepth {
       public bool IsInteger => Type == JxlBitDepthType.Int;
       public bool IsFloat => Type == JxlBitDepthType.Float;
   }
   ```

See `JxlBitDepth`, `JxlColorProfile` for examples. The FFI layer does type discrimination, not C#.

### Native FFI Layer (native/jxl-ffi/)
Bridges jxl-rs to C# via `extern "C"` functions. Contains only code necessary for C# interop:
- `decoder.rs` - FFI entry points and decoder state machine (required because Rust generics can't cross FFI)
- `types.rs` - C-compatible struct/enum definitions with PascalCase fields for C# idioms
- `conversions.rs` - Type conversions between jxl-rs Rust types and C-compatible FFI types
- `error.rs` - Thread-local error storage for FFI error reporting
- `build.rs` - Runs csbindgen to auto-generate `src/JpegXL.Net/NativeMethods.g.cs`

Look here for: FFI boundary issues, type conversion bugs, state machine errors.

Check `docs/csbindgen-README.md` for how the Rust↔C# interop works.

### C# Layer (src/JpegXL.Net/)
**Core APIs:**
- `JxlDecoder.cs` - Low-level streaming decoder API
- `JxlImage.cs` - High-level one-shot decode API

**Supporting types:**
- `JxlBasicInfo.cs` - Image metadata (dimensions, bit depth, animation, HDR tone mapping)
- `JxlColorProfile.cs` - Color profile support (ICC, transfer functions, primaries)
- `JxlBitDepth.cs` - Bit depth discriminated union (Int vs Float)
- `JxlAnimation.cs`, `JxlAnimationMetadata.cs` - Animation timing and frame info

**Infrastructure:**
- `NativeMethods.g.cs` - Auto-generated P/Invoke bindings (DO NOT EDIT)
- `NativeLibraryLoader.cs` - Platform-specific native library resolution
- `*.Extensions.cs` - Helper methods for generated types (e.g., `JxlPixelFormat.Rgba8`, `JxlBasicInfo.IsHdr`)

### Where to Look for Issues
| Issue Type | Location |
|------------|----------|
| Decoder/format bugs | `native/jxl-rs/jxl/src/` |
| FFI/interop issues | `native/jxl-ffi/src/decoder.rs` |
| Type conversion bugs | `native/jxl-ffi/src/conversions.rs` |
| C# API issues | `src/JpegXL.Net/*.cs` |
| Color/HDR issues | `conversions.rs` + `JxlColorProfile.cs` |

### Key Design Principles
- The native FFI layer should contain only code necessary for C# interop
- Options are immutable after decoder creation
- Native bindings are auto-generated - edit Rust code, not NativeMethods.g.cs

## Testing

Tests are in `test/JpegXL.Net.Tests/`. The rebuild script runs them automatically.

## Test Files

**Sample files for manual testing** (`examples/sample-files/`):
- `hdr_hlg_test.jxl`, `hdr_pq_test.jxl` - HDR images with HLG/PQ transfer functions
- `animation_icos4d.jxl`, `animation_spline.jxl` - Animated images
- `cmyk_layers.jxl` - CMYK with layers
- `progressive.jxl` - Progressive decode testing
- `with_icc.jxl` - ICC color profile

**Additional test resources:**
- `./external/conformance/**/*.jxl` - Optional conformance test suite
- `./native/jxl-rs/jxl/resources/test/**/*.jxl` - Test resources from the upstream jxl-rs decoder
- `./native/jxl-rs/**/*.rs` - Look here for unit tests related to JXL files/properties/behaviors

## Common Patterns

### Adding New FFI Functions
1. Add function to `native/jxl-ffi/src/decoder.rs`
2. Run `./rebuild-all.sh` to regenerate bindings
3. Add C# wrapper method to `JxlDecoder.cs`

### Modifying Types
1. Edit structs/enums in `native/jxl-ffi/src/types.rs`
2. Run `./rebuild-all.sh` to regenerate
3. Source generator will create public wrapper types automatically
