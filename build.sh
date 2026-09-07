#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$repo/src/SystemSpinnerX64.csproj"
output="$repo/build"
exe="$output/System-Spinner.exe"

step() { printf '\n\033[36m=== %s\033[0m\n' "$1"; }
ok()   { printf '\033[32m  + %s\033[0m\n' "$1"; }

cleanup() {
    rm -rf "$output/obj" "$output/bin"
}
trap cleanup EXIT

[[ -f "$project" ]] || { echo "$project not found. Run the script from the project root." >&2; exit 1; }

command -v dotnet >/dev/null 2>&1 || {
    echo 'The .NET 10 SDK is required: brew install --cask dotnet-sdk, or' >&2
    echo 'https://dotnet.microsoft.com/download/dotnet/10.0' >&2
    exit 1
}

step 'Restoring packages'
dotnet restore "$project"

step 'Building the exe'
rm -f "$exe"

dotnet publish "$project" -c Release -o "$output"

[[ -f "$exe" ]] || { echo "Expected $exe, but it is not there." >&2; exit 1; }

size=$(du -m "$exe" | cut -f1)
ok "$exe (${size} MB, .NET not bundled)"
