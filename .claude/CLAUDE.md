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
- `--log` - Log stdout to `examples/JpegXL.MacOS/logs/run.txt` (existing logs are renamed with unix timestamp)
- `--kill-after=<seconds>` - Auto-kill the app after N seconds (useful for automated testing)

```bash
# Run for 5 seconds with logging
./examples/JpegXL.MacOS/run.sh --log-file --kill-after=5
```

**Development loop:**
1. Modify C# code
2. Run `./examples/JpegXL.MacOS/run.sh`
3. View app behavior (use `--log-file` to capture `Console.WriteLine` output)
4. Repeat

**Note:** If you modify Rust code in `native/jxl-ffi/`, you must run `./rebuild-all.sh` first to recompile the native library before using `run.sh`.

## Architecture

### Native Layer (native/jxl-ffi/)
- Rust FFI wrapper around jxl-rs library
- `build.rs` uses csbindgen to auto-generate C# bindings
- Generated bindings go to `src/JpegXL.Net/NativeMethods.g.cs`
- Check csbindgen docs at `docs/csbindgen-README.md` for how the Rust<->C# interop works

### C# Layer (src/JpegXL.Net/)
- `JxlDecoder` - Low-level streaming decoder API
- `JxlImage` - High-level one-shot decode API
- `*.Extensions.cs` files add helper methods to generated types (e.g., `JxlBasicInfo.IsHdr`)

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
