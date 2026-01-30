# Claude Code Instructions

This file contains instructions for Claude Code when working on this project.

## Project Overview

JpegXL.Net is a .NET library for decoding JPEG XL images. It wraps the jxl-rs Rust decoder with a C FFI layer.

## Build Commands

- **Full rebuild**: Use `./rebuild-all.sh` for building the native library, regenerating bindings, building .NET, and running tests
- **Example app**: Run `examples/JpegXL.Viewer/run-animation-test.sh` to test the viewer
- **Do NOT** use `cargo build` directly - the rebuild script handles cross-platform targets and copies libraries correctly

## Architecture

### Native Layer (native/jxl-ffi/)
- Rust FFI wrapper around jxl-rs library
- `build.rs` uses csbindgen to auto-generate C# bindings
- Generated bindings go to `src/JpegXL.Net/Native/NativeMethods.g.cs`

### C# Layer (src/JpegXL.Net/)
- `JxlDecoder` - Low-level streaming decoder API
- `JxlImage` - High-level one-shot decode API
- Source generator in `src/JpegXL.Net.Generators/` creates public wrapper types from native types

### Key Design Principles
- The native FFI layer should be minimal - just thin wrappers
- Prefer streaming API (`Process()`, `ReadPixels()`) over one-shot functions
- Options are immutable after decoder creation
- Native bindings are auto-generated - edit Rust code, not NativeMethods.g.cs

## Testing

Tests are in `test/JpegXL.Net.Tests/`. The rebuild script runs them automatically.

## Common Patterns

### Adding New FFI Functions
1. Add function to `native/jxl-ffi/src/decoder.rs`
2. Run `./rebuild-all.sh` to regenerate bindings
3. Add C# wrapper method to `JxlDecoder.cs`

### Modifying Types
1. Edit structs/enums in `native/jxl-ffi/src/types.rs`
2. Run `./rebuild-all.sh` to regenerate
3. Source generator will create public wrapper types automatically
