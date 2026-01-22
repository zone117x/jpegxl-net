#!/bin/bash
# Run the viewer with an animated JXL test file
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
dotnet run --project "$SCRIPT_DIR/JpegXL.Viewer.csproj" -- "$SCRIPT_DIR/../../test/TestData/animation_spline.jxl"
