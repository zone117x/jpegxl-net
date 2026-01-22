#!/bin/bash

# Run tests with code coverage
# Usage: ./run-coverage.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$(dirname "$SCRIPT_DIR")")"
RESULTS_DIR="$SCRIPT_DIR/TestResults"

# Clean previous results
rm -rf "$RESULTS_DIR"
mkdir -p "$RESULTS_DIR"

echo "Running tests with coverage..."
dotnet test "$SCRIPT_DIR" \
    --configuration Release \
    -- \
    --coverage \
    --coverage-output-format cobertura \
    --coverage-output "$RESULTS_DIR/coverage.cobertura.xml"

echo ""
echo "Coverage report generated: $RESULTS_DIR/coverage.cobertura.xml"
