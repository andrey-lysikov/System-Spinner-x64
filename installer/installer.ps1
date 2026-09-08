#Requires -Version 5.1

param(
    [string] $Version = '',

    [string] $PawnIoSetup = ''
)

$ErrorActionPreference = 'Stop'

$ProgressPreference = 'SilentlyContinue'

[Net.ServicePointManager]::SecurityProtocol =
    [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$wixDir  = $PSScriptRoot
$repo    = Split-Path -Parent $PSScriptRoot
$source  = Join-Path $repo 'src'
$project = Join-Path $source 'SystemSpinnerX64.csproj'
$output  = Join-Path $repo 'build'
$exe     = Join-Path $output 'System-Spinner.exe'

$msi     = Join-Path $output 'System-Spinner.msi'

$packageWxs   = Join-Path $wixDir 'Package.wxs'
$shortcutsWxs = Join-Path $wixDir 'ShortcutsDlg.wxs'

$pawnDir  = Join-Path $output 'pawnio'
$pawnExe  = Join-Path $pawnDir 'PawnIO_setup.exe'
$pawnApi  = 'https://api.github.com/repos/namazso/PawnIO.Setup/releases/latest'
$pawnName = 'PawnIO_setup.exe'

$wixVersion = '6.0.2'

$projects  = @($source, (Join-Path $repo 'test'), $wixDir)
$leftovers = @((Join-Path $output 'obj'), (Join-Path $output 'bin'),
               [IO.Path]::ChangeExtension($msi, '.wixpdb'))

function Write-Step($text) { Write-Host "`n=== $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "  + $text" -ForegroundColor Green }
function Write-Note($text) { Write-Host "  $text" -ForegroundColor Gray }
function Write-Kept($text) { Write-Host "  ! $text" -ForegroundColor Yellow }
function Show-Size($path)  {
    $bytes = (Get-Item $path).Length
    if ($bytes -lt 1MB) { "$([math]::Round($bytes / 1KB)) KB" }
    else                { "$([math]::Round($bytes / 1MB, 1)) MB" }
}

function Get-Sha256($path) {
    (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Remove-Leftovers {
    $paths = @()
    foreach ($one in $projects) {
        $paths += (Join-Path $one 'bin'), (Join-Path $one 'obj')
    }
    $paths += $leftovers

    $kept = @()

    foreach ($path in $paths) {
        if (-not (Test-Path $path)) { continue }

        for ($attempt = 0; $attempt -lt 2 -and (Test-Path $path); $attempt++) {
            if ($attempt) { Start-Sleep -Milliseconds 400 }
            Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
        }

        if (Test-Path $path) { $kept += $path }
    }

    if ($kept) { Write-Kept "could not be removed: $($kept -join ', ')" }
}

function Get-PawnIo {
    if ($PawnIoSetup) {
        if (-not (Test-Path $PawnIoSetup)) { throw "$PawnIoSetup is not there." }

        Write-Note "given on the command line, so nothing is fetched"
        return (Resolve-Path $PawnIoSetup).Path
    }

    $headers = @{
        'User-Agent' = 'System-Spinner-build'
        'Accept'     = 'application/vnd.github+json'
    }

    $token = if ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { $env:GH_TOKEN }
    if ($token) { $headers['Authorization'] = "Bearer $token" }

    $release = Invoke-RestMethod -Uri $pawnApi -Headers $headers
    $asset = $release.assets | Where-Object { $_.name -eq $pawnName } | Select-Object -First 1
    if (-not $asset) { throw "The newest PawnIO release carries no $pawnName." }

    $wanted = ''
    if ($asset.PSObject.Properties['digest'] -and $asset.digest -match '^sha256:([0-9a-f]{64})$') {
        $wanted = $Matches[1]
    }

    Write-Note "PawnIO $($release.tag_name), $([math]::Round($asset.size / 1MB, 1)) MB"

    if ((Test-Path $pawnExe) -and $wanted -and (Get-Sha256 $pawnExe) -eq $wanted) {
        Write-Note 'already fetched, and it is the same file'
        return $pawnExe
    }

    if (-not (Test-Path $pawnDir)) { New-Item -ItemType Directory -Path $pawnDir | Out-Null }

    Write-Note "fetching $($asset.browser_download_url)"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $pawnExe -Headers @{
        'User-Agent' = 'System-Spinner-build'
    }

    if (-not (Test-Path $pawnExe)) { throw 'The PawnIO setup was not fetched.' }

    if ($wanted) {
        $got = Get-Sha256 $pawnExe
        if ($got -ne $wanted) {
            Remove-Item $pawnExe -Force -ErrorAction SilentlyContinue
            throw "The PawnIO setup does not hash to what the release says: $got, expected $wanted"
        }

        Write-Note "sha256 $got, as the release says"
    }
    else {
        Write-Kept 'the release names no hash for it, so nothing could be checked'
    }

    return $pawnExe
}

if (-not (Test-Path $packageWxs)) {
    throw "$packageWxs not found. This script belongs in the installer folder, beside the sources it packages."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK is required: https://dotnet.microsoft.com/download/dotnet/10.0'
}

if (-not (Test-Path $exe)) {
    throw "$exe is not there. Run build.ps1 first - this packages what that produces."
}

try {
    $properties = [xml](Get-Content $project -Raw)

    if (-not $Version) {
        $Version = "$($properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText)".Trim()
        if (-not $Version) { throw "No <Version> in $project" }
    }

    $manufacturer = "$($properties.SelectSingleNode('/Project/PropertyGroup/Company').InnerText)".Trim()
    if (-not $manufacturer) { throw "No <Company> in $project" }

    $msiVersion = if ($Version -match '^\d+\.\d+$') { "$Version.0" } else { $Version }

    Write-Step 'Fetching the PawnIO driver setup'

    $pawn = Get-PawnIo
    Write-Ok "$pawn ($(Show-Size $pawn))"

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
        -d "Version=$msiVersion" `
        -d "Manufacturer=$manufacturer" `
        -d "Exe=$exe" `
        -d "Icon=$(Join-Path $repo 'pictures\icon.ico')" `
        -d "License=$(Join-Path $wixDir 'License.rtf')" `
        -d "PawnIO=$pawn" `
        -ext WixToolset.UI.wixext `
        -ext WixToolset.Util.wixext `
        $packageWxs $shortcutsWxs `
        -o $msi
    if ($LASTEXITCODE -ne 0) { throw 'The msi was not built.' }

    Write-Ok "$msi ($(Show-Size $msi))"
}
finally {
    Remove-Leftovers
}
