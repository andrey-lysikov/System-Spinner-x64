#Requires -Version 5.1

param(
    [switch] $Installer,

    [string] $Version = '',
    [switch] $SkipTests
)
$ErrorActionPreference = 'Stop'

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

    if (Test-Path $exe) { Remove-Item $exe -Force }

    & dotnet publish $project -c Release -o $output
    if ($LASTEXITCODE -ne 0) { throw 'The build failed. The full log is above.' }
    if (-not (Test-Path $exe)) { throw "Expected $exe, but it is not there." }
    Write-Ok "$exe ($(Show-Size $exe), .NET not bundled)"
    if (-not $Installer) { return }

    $properties = ([xml](Get-Content $project)).Project.PropertyGroup
    if (-not $Version) {
        $Version = "$($properties.Version | Where-Object { $_ })".Trim()
        if (-not $Version) { throw "No <Version> in $project" }
    }
    $manufacturer = "$($properties.Company | Where-Object { $_ })".Trim()
    if (-not $manufacturer) { throw "No <Company> in $project" }

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
