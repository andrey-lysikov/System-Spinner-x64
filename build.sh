#!/usr/bin/env bash
#
# Builds the Windows exe on macOS or Linux — the counterpart of build.ps1 for machines
# without PowerShell. The result is the same file: build/System-Spinner.exe (win-x64, needs
# .NET Desktop Runtime 10 on the target PC).
#
#     ./build.sh
#
# What stays on Windows: the tests (they load WPF, which the Mac runtime does not have) and the
# msi. Every commit is still tested in GitHub Actions — see .github/workflows/tests.yml, which
# the release and rebuild workflows call as a job of their own; the msi is packaged on every
# release, see .github/workflows/release.yml.

set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$repo/src/SystemSpinnerX64.csproj"
output="$repo/build"
exe="$output/System-Spinner.exe"

step() { printf '\n\033[36m=== %s\033[0m\n' "$1"; }
ok()   { printf '\033[32m  + %s\033[0m\n' "$1"; }

# What MSBuild leaves behind. It writes under build/ — see Directory.Build.props — so these two
# folders are all of it; the exe beside them stays, along with the settings and log the app keeps
# in the same place.
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

# Every publish switch is already in the csproj: single-file, framework-dependent, win-x64.
# Directory.Build.props turns on EnableWindowsTargeting, so no flag is needed here.
dotnet publish "$project" -c Release -o "$output"

[[ -f "$exe" ]] || { echo "Expected $exe, but it is not there." >&2; exit 1; }

size=$(du -m "$exe" | cut -f1)
ok "$exe (${size} MB, .NET not bundled)"
