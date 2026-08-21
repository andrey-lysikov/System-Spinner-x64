#Requires -Version 5.1

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is empty in a default parameter value under PowerShell 5.1 — paths are set here.
$repo    = $PSScriptRoot
$source  = Join-Path $repo 'src'
$tests   = Join-Path $repo 'test'
$project = Join-Path $source 'SystemSpinnerX64.csproj'
$testProj = Join-Path $tests 'SystemSpinnerX64.Tests.csproj'
$output  = Join-Path $repo 'build'
$exe     = Join-Path $output 'System-Spinner.exe'

# What MSBuild leaves next to the sources. Removed when the script ends, whether it succeeded or
# not: the release build happens in a GitHub Action, and a local run should leave only the exe.
$leftovers = @(
    (Join-Path $source 'bin'), (Join-Path $source 'obj'),
    (Join-Path $tests  'bin'), (Join-Path $tests  'obj')
)

function Write-Step($text) { Write-Host "`n=== $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "  + $text" -ForegroundColor Green }

function Remove-Leftovers {
    foreach ($path in $leftovers) {
        if (Test-Path $path) { Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

if (-not (Test-Path $project)) {
    throw "$project not found. This script belongs in the project root, next to the src folder."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK is required: https://dotnet.microsoft.com/download/dotnet/10.0'
}

try {
    Write-Step 'Restoring packages'
    & dotnet restore $project
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed. Check the connection and access to nuget.org.' }

    Write-Step 'Running tests'
    & dotnet test $testProj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Nothing is built: those come first.' }

    Write-Step 'Building the exe'

    # The app puts its own files next to the exe, that is, here. The folder must not be removed
    # wholesale — that would wipe the user settings and log along with the build.
    if (Test-Path $exe) { Remove-Item $exe -Force }

    # Every publish switch is already in the csproj: single-file, framework-dependent, win-x64.
    & dotnet publish $project -c Release -o $output
    if ($LASTEXITCODE -ne 0) { throw 'The build failed. The full log is above.' }
    if (-not (Test-Path $exe)) { throw "Expected $exe, but it is not there." }

    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Ok "$exe ($sizeMb MB, .NET not bundled)"
}
finally {
    Remove-Leftovers
}
