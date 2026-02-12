#!/usr/bin/env bash
set -euo pipefail

if [ $# -lt 1 ]; then
    echo "Usage: $0 <input-file> [cjxl-args...]" >&2
    exit 1
fi

input="$1"
shift
output="${input%.*}.jxl"

ffmpeg -i "$input" -pix_fmt rgb48be -f image2pipe -vcodec png - 2>/dev/null | \
    cjxl - "$output" -d 0 --intensity_target 10000 "$@"

echo "Wrote $output"
