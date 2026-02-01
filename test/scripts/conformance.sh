#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
CONFORMANCE_DIR="$REPO_ROOT/external/conformance"
CONFORMANCE_REPO="git@github.com:libjxl/conformance.git"

if [ -d "$CONFORMANCE_DIR/.git" ]; then
    echo "Updating conformance repo..."
    git -C "$CONFORMANCE_DIR" pull
else
    echo "Cloning conformance repo..."
    mkdir -p "$REPO_ROOT/external"
    git clone "$CONFORMANCE_REPO" "$CONFORMANCE_DIR"
fi

echo "Conformance repo ready at: $CONFORMANCE_DIR"
