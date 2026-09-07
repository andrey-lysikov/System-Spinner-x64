#Requires -Version 5.1

<#
    Builds the app. Run it plainly and the exe alone comes out:

        ./build.ps1

    With -Installer the msi follows it, built around the same exe:

        ./build.ps1 -Installer

    Building the msi needs the WiX toolset, installed here when it is missing. Nothing else is
    fetched: the msi carries the exe and installer/Prerequisites.ps1, and that script is what runs
    when the install is over — it takes out copies of the app that came from another installer,
    fetches the .NET Desktop Runtime when the machine has none, and fetches the PawnIO driver,
    without which there are no temperatures, no power and no fan speeds and the app refuses to
    start. A driver is not ours to keep a copy of, and one carried inside the msi would be as old
    as the build.
#>

param(
    # Package the installer as well as the exe.
    [switch] $Installer,

    # The version the installer announces. Taken from the csproj when not given, so it cannot
    # drift from the app; the workflow passes the one on the release page.
    [string] $Version = '',

    # For CI only. Both workflows run tests.yml as a job of their own and only reach the build
    # once it has passed, so running the same tests here again would be paying twice for one
    # answer. Left alone locally: a build that skipped the tests is not one to hand to anybody.
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is empty in a default parameter value under PowerShell 5.1 — paths are set here.
$repo    = $PSScriptRoot
$source  = Join-Path $repo 'src'
$tests   = Join-Path $repo 'test'
$project = Join-Path $source 'SystemSpinnerX64.csproj'
$testProj = Join-Path $tests 'SystemSpinnerX64.Tests.csproj'
$output  = Join-Path $repo 'build'
$exe     = Join-Path $output 'System-Spinner.exe'

$wixDir  = Join-Path $repo 'installer'
$msi     = Join-Path $output 'System-Spinner.msi'
$wixWork = Join-Path $output 'obj\wix'

$wixVersion = '6.0.2'

# What MSBuild and WiX leave behind. Both now write under build\ — see Directory.Build.props —
# so it is these two folders and nothing else. Removed when the script ends, whether it succeeded
# or not: the release build happens in a GitHub Action, and a local run should leave only what was
# asked for. The exe, the msi and the config and log the app keeps beside itself are all directly
# in build\ and are not touched.
$leftovers = @(
    (Join-Path $output 'obj'), (Join-Path $output 'bin'),
    [IO.Path]::ChangeExtension($msi, '.wixpdb')
)

function Write-Step($text) { Write-Host "`n=== $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "  + $text" -ForegroundColor Green }
function Show-Size($path)  { "$([math]::Round((Get-Item $path).Length / 1MB, 1)) MB" }

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

    if ($SkipTests) {
        Write-Step 'Tests skipped: they passed in a job of their own'
    }
    else {
        Write-Step 'Running tests'
        & dotnet test $testProj -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Nothing is built: those come first.' }
    }

    Write-Step 'Building the exe'

    # The app puts its own files next to the exe, that is, here. The folder must not be removed
    # wholesale — that would wipe the user settings and log along with the build.
    if (Test-Path $exe) { Remove-Item $exe -Force }

    # Every publish switch is already in the csproj: single-file, framework-dependent, win-x64.
    & dotnet publish $project -c Release -o $output
    if ($LASTEXITCODE -ne 0) { throw 'The build failed. The full log is above.' }
    if (-not (Test-Path $exe)) { throw "Expected $exe, but it is not there." }

    Write-Ok "$exe ($(Show-Size $exe), .NET not bundled)"

    if (-not $Installer) { return }

    # --- The installer ---

    $properties = ([xml](Get-Content $project)).Project.PropertyGroup

    if (-not $Version) {
        $Version = "$($properties.Version | Where-Object { $_ })".Trim()
        if (-not $Version) { throw "No <Version> in $project" }
    }

    # The publisher shown in the list of installed programs. Taken from the csproj, where the exe
    # gets its own company name: two places to write it down is one place to forget.
    $manufacturer = "$($properties.Company | Where-Object { $_ })".Trim()
    if (-not $manufacturer) { throw "No <Company> in $project" }

    # The script the msi carries names the UpgradeCode of this package: it tells the copies
    # Windows Installer replaces by itself from the ones it has to take out with their own
    # uninstaller. One number in two files, so they are held against each other here rather than
    # found to disagree on somebody's machine.
    $packageWxs    = Join-Path $wixDir 'Package.wxs'
    $prerequisites = Join-Path $wixDir 'Prerequisites.ps1'

    $upgradeCode = [regex]::Match((Get-Content $packageWxs -Raw),
                                  'UpgradeCode="\{?([0-9A-Fa-f-]{36})\}?"').Groups[1].Value
    if (-not $upgradeCode) { throw "No UpgradeCode in $packageWxs" }

    if ((Get-Content $prerequisites -Raw) -notmatch [regex]::Escape($upgradeCode)) {
        throw "Prerequisites.ps1 names an UpgradeCode other than the {$upgradeCode} in $packageWxs"
    }

    New-Item -ItemType Directory -Force -Path $wixWork | Out-Null

    $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"

    if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
        Write-Step 'Installing the WiX toolset'
        & dotnet tool install --global wix --version $wixVersion
        if ($LASTEXITCODE -ne 0) { throw 'The WiX toolset was not installed.' }
        $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
    }

    # Idempotent: an extension already added is reported and nothing changes.
    foreach ($extension in 'WixToolset.UI.wixext', 'WixToolset.Util.wixext') {
        & wix extension add -g "$extension/$wixVersion" | Out-Null
    }

    Write-Step "Building the msi for $Version, published by $manufacturer"

    & wix build -arch x64 `
        -d "Version=$Version" `
        -d "Manufacturer=$manufacturer" `
        -d "Exe=$exe" `
        -d "Icon=$(Join-Path $repo 'icon.ico')" `
        -d "License=$(Join-Path $wixDir 'License.rtf')" `
        -d "Prerequisites=$prerequisites" `
        -ext WixToolset.UI.wixext `
        -ext WixToolset.Util.wixext `
        (Join-Path $wixDir 'Package.wxs') (Join-Path $wixDir 'ShortcutsDlg.wxs') `
        -o $msi
    if ($LASTEXITCODE -ne 0) { throw 'The msi was not built.' }

    Write-Ok "$msi ($(Show-Size $msi))"
}
finally {
    Remove-Leftovers
}
