#  Copyright © AndreyLysikov
#  SPDX-License-Identifier: Apache-2.0

param(
    [string] $Exe = '',

    [string] $Product = '',

    [switch] $NoPrevious,

    [string] $Folder = '',

    [string] $UiLevel = '',

    [switch] $Elevated
)

$ErrorActionPreference = 'Continue'

$ownUpgradeCode = '{D993C858-1615-4986-B456-173BEFEDA37C}'

$namePattern = '^system[ _-]?spinner'
$pathMark    = 'System-Spinner'
$exeName     = 'System-Spinner.exe'
$taskName    = 'System-Spinner'

if ($Folder) {
    try { $Folder = [IO.Path]::GetFullPath($Folder) } catch { }
    if ($Folder.Length -gt 3) { $Folder = $Folder.TrimEnd('\') }
}

if (-not $Exe -and $Folder)  { $Exe    = Join-Path $Folder $exeName }
if (-not $Folder -and $Exe)  { $Folder = Split-Path -Parent $Exe }

$dotNetUrl    = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe'
$programFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
$dotNetShared = Join-Path $programFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'

$pawnIoKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO'
$pawnIoApi = 'https://api.github.com/repos/namazso/PawnIO.Setup/releases/latest'
$pawnIoSys = Join-Path $env:SystemRoot 'System32\drivers\PawnIO.sys'

$level       = $UiLevel -as [int]
$interactive = (-not $level) -or ($level -ge 4)

$protected = @($env:SystemRoot, $env:ProgramFiles, $env:ProgramW6432, ${env:ProgramFiles(x86)},
               $env:LocalAppData, $env:AppData, $env:UserProfile, $env:TEMP) |
    Where-Object { $_ } |
    ForEach-Object { try { [IO.Path]::GetFullPath($_).TrimEnd('\') } catch { } }

$transcript = Join-Path $env:TEMP 'System-Spinner-prerequisites.log'
try { Start-Transcript -Path $transcript -Append -Force | Out-Null } catch { }

function Write-Step($text) { Write-Host "`n$text" -ForegroundColor Cyan }
function Write-Note($text) { Write-Host "  $text" }
function Write-Bad($text)  { Write-Host "  $text" -ForegroundColor Red }

function Test-Elevated {
    $me = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal $me).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Read-Text($url) {
    $answer = & curl.exe -sL --fail -H 'User-Agent: System-Spinner-setup' $url
    if ($LASTEXITCODE -ne 0) { return $null }
    return $answer
}

function Read-Address($url) {
    $answer = & curl.exe -sIL -o NUL -w '%{url_effective}' $url
    if ($LASTEXITCODE -ne 0) { return $null }
    return $answer
}

function Get-File($url, $path) {
    & curl.exe -L --fail --silent --show-error -o $path $url
    return ($LASTEXITCODE -eq 0) -and (Test-Path -LiteralPath $path)
}

function Split-Command([string] $line) {
    $line = "$line".Trim()
    if (-not $line) { return $null }

    if ($line.StartsWith('"')) {
        $end = $line.IndexOf('"', 1)
        if ($end -lt 0) { return $null }
        return @{ File = $line.Substring(1, $end - 1); Arguments = $line.Substring($end + 1).Trim() }
    }

    $space = $line.IndexOf(' ')
    if ($space -lt 0) { return @{ File = $line; Arguments = '' } }
    return @{ File = $line.Substring(0, $space); Arguments = $line.Substring($space + 1).Trim() }
}

function Test-Ours([string] $path) {
    if (-not $Folder -or -not $path) { return $false }

    try {
        $full = [IO.Path]::GetFullPath($path.Trim('"').Trim())
        $root = [IO.Path]::GetFullPath($Folder).TrimEnd('\')
    }
    catch { return $false }

    return ($full -eq $root) -or $full.StartsWith("$root\", [StringComparison]::OrdinalIgnoreCase)
}

function Invoke-Program($file, $arguments) {
    try {
        $start = @{ FilePath = $file; Wait = $true; PassThru = $true }
        if ($arguments) { $start.ArgumentList = $arguments }
        return (Start-Process @start).ExitCode
    }
    catch {
        Write-Bad "it did not run: $($_.Exception.Message)"
        return $null
    }
}

function Get-OwnProducts {
    $codes = @()
    try {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        foreach ($code in $installer.RelatedProducts($ownUpgradeCode)) { $codes += "$code".ToUpperInvariant() }
    }
    catch { }

    if ($Product) { $codes += "$Product".ToUpperInvariant() }
    return $codes
}

function Get-OtherCopies {
    $own = Get-OwnProducts

    $roots = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
               'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
               'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')

    foreach ($root in $roots) {
        foreach ($key in (Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue)) {
            $entry = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            if (-not $entry) { continue }

            $name  = "$($entry.DisplayName)"
            $where = "$($entry.InstallLocation)$($entry.DisplayIcon)$($entry.UninstallString)"

            if (($name -notmatch $namePattern) -and ($where -notlike "*$pathMark*")) { continue }
            if ($own -contains $key.PSChildName.ToUpperInvariant()) { continue }

            [pscustomobject]@{
                Name      = $(if ($name) { $name } else { $key.PSChildName })
                Version   = "$($entry.DisplayVersion)"
                Code      = $key.PSChildName
                Path      = $key.PSPath
                Location  = "$($entry.InstallLocation)"
                Icon      = "$($entry.DisplayIcon)"
                Uninstall = "$($entry.UninstallString)"
            }
        }
    }
}

function Get-CopyFiles($copy) {
    $files = @()

    $icon = "$($copy.Icon)".Split(',')[0].Trim('"').Trim()
    if ($icon) { $files += $icon }

    if ($copy.Location) { $files += (Join-Path "$($copy.Location)".Trim('"').Trim() $exeName) }

    $command = Split-Command $copy.Uninstall
    if ($command -and $command.File -notmatch 'msiexec') { $files += $command.File }

    return ($files | Where-Object { $_ } | Select-Object -Unique)
}

function Get-CopyFolders($copy) {
    $folders = @()

    if ($copy.Location) { $folders += "$($copy.Location)".Trim('"').Trim() }

    $icon = "$($copy.Icon)".Split(',')[0].Trim('"').Trim()
    if ($icon) { $folders += (Split-Path -Parent $icon) }

    $command = Split-Command $copy.Uninstall
    if ($command -and $command.File -notmatch 'msiexec') { $folders += (Split-Path -Parent $command.File) }

    return ($folders | Where-Object { $_ } | Select-Object -Unique)
}

function Test-CopyFolder([string] $path) {
    if (-not $path) { return $false }

    try { $full = [IO.Path]::GetFullPath($path.Trim('"').Trim()).TrimEnd('\') } catch { return $false }

    if ($full.Length -le 3) { return $false }
    if (Test-Ours $full) { return $false }
    if ($protected -contains $full) { return $false }
    if (-not (Test-Path -LiteralPath $full)) { return $false }

    return (Test-Path -LiteralPath (Join-Path $full $exeName))
}

function Clear-CopyFolder([string] $folder) {
    foreach ($item in (Get-ChildItem -LiteralPath $folder -Force -ErrorAction SilentlyContinue)) {
        if (-not $item.PSIsContainer -and $item.Name -match '\.(conf|log)(\.\d+)?$') { continue }
        Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }

    Remove-Item -LiteralPath $folder -Force -ErrorAction SilentlyContinue
}

function Clear-Copy($copy) {
    $clean = $true
    $folders = @(Get-CopyFolders $copy | Where-Object { Test-CopyFolder $_ })

    foreach ($file in (Get-CopyFiles $copy)) {
        if (Test-Ours $file) { Write-Note "in our folder, so it is ours now: $file"; continue }
        if (-not (Test-Path -LiteralPath $file)) { continue }

        Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue

        if (Test-Path -LiteralPath $file) { Write-Bad "it could not be removed: $file"; $clean = $false }
        else { Write-Note "removed: $file" }
    }

    foreach ($folder in $folders) {
        Clear-CopyFolder $folder

        if (-not (Test-Path -LiteralPath $folder)) { Write-Note "removed: $folder" }
        elseif (Get-ChildItem -LiteralPath $folder -Force -ErrorAction SilentlyContinue) {
            Write-Note "emptied, its settings and log left in place: $folder"
        }
        else { Write-Bad "it could not be removed: $folder"; $clean = $false }
    }

    Remove-Item -LiteralPath $copy.Path -Recurse -Force -ErrorAction SilentlyContinue

    if (Test-Path -LiteralPath $copy.Path) {
        Write-Bad "it is still in the list of installed programs: $($copy.Name)"
        $clean = $false
    }

    return $clean
}

function Clear-RunEntries($roots) {
    foreach ($root in $roots) {
        $entry = Get-ItemProperty -LiteralPath $root -ErrorAction SilentlyContinue
        if (-not $entry) { continue }

        foreach ($name in $entry.PSObject.Properties.Name) {
            if ($name -like 'PS*') { continue }

            $value = "$($entry.$name)"
            if ($value -notlike "*$pathMark*") { continue }

            $command = Split-Command $value
            if ($command -and (Test-Ours $command.File)) { continue }

            Write-Note "no longer started with Windows: $root\$name"
            Remove-ItemProperty -LiteralPath $root -Name $name -Force -ErrorAction SilentlyContinue
        }
    }
}

function Clear-Shortcuts($roots) {
    $shell = $null
    try { $shell = New-Object -ComObject WScript.Shell } catch { return }

    foreach ($root in $roots) {
        if (-not $root -or -not (Test-Path -LiteralPath $root)) { continue }

        foreach ($link in (Get-ChildItem -LiteralPath $root -Filter '*.lnk' -Recurse -Force -ErrorAction SilentlyContinue)) {
            $target = ''
            try { $target = "$($shell.CreateShortcut($link.FullName).TargetPath)" } catch { continue }

            if (-not $target) { continue }
            if ($target -notlike "*$exeName") { continue }
            if (Test-Ours $target) { continue }

            Remove-Item -LiteralPath $link.FullName -Force -ErrorAction SilentlyContinue
            if (-not (Test-Path -LiteralPath $link.FullName)) { Write-Note "removed: $($link.FullName)" }
        }
    }
}

function Get-TaskTarget {
    try {
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($task) { return "$($task.Actions[0].Execute)".Trim('"') }
    }
    catch { }

    return $null
}

function Restore-Ours($before) {
    if ($Product -and $Exe -and -not (Test-Path -LiteralPath $Exe)) {
        Write-Bad 'the app is not where the msi put it: putting it back'
        Invoke-Program (Join-Path $env:SystemRoot 'System32\msiexec.exe') "/fas $Product /qn /norestart" | Out-Null

        if (-not (Test-Path -LiteralPath $Exe)) {
            Write-Bad "$Exe is gone and could not be put back: install the app again"
        }
    }

    if (-not $Exe) { return }

    $now = Get-TaskTarget

    $lost    = $before -and -not $now
    $strayed = $now -and ($now -ne $Exe)

    if (-not ($lost -or $strayed)) { return }

    Write-Note $(if ($lost) { 'the autostart task went with it: making it again' }
                 else { "the autostart task still points at $now, and is pointed here instead" })

    $code = Invoke-Program (Join-Path $env:SystemRoot 'System32\schtasks.exe') `
                           @('/Create', '/TN', $taskName, '/TR', "`"$Exe`"",
                             '/SC', 'ONLOGON', '/RL', 'HIGHEST', '/F')

    if ($code -ne 0) { Write-Bad "schtasks returned $code; turn autostart on again from the tray menu" }
}

function Get-DotNetInstalled {
    if (-not (Test-Path -LiteralPath $dotNetShared)) { return $null }

    $versions = Get-ChildItem -LiteralPath $dotNetShared -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { $parsed = $null; if ([version]::TryParse($_.Name, [ref] $parsed)) { $parsed } }

    if (-not $versions) { return $null }
    return ($versions | Sort-Object -Descending)[0]
}

function Get-DotNetOffered {
    $href = Read-Address $dotNetUrl
    if (-not $href) { return $null }
    if ($href -notmatch 'windowsdesktop-runtime-([0-9]+(\.[0-9]+)+)-win') { return $null }

    $parsed = $null
    if (-not [version]::TryParse($Matches[1], [ref] $parsed)) { return $null }

    return @{ Version = $parsed; Url = $href }
}

function Install-DotNet($offered) {
    $file = Join-Path $env:TEMP 'windowsdesktop-runtime.exe'
    Write-Note "fetching $($offered.Url)"

    if (-not (Get-File $offered.Url $file)) {
        Write-Bad 'it could not be fetched'
        return $false
    }

    $code = Invoke-Program $file '/install /passive /norestart'
    Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue

    if ($code -eq 3010) { Write-Host '  installed, and Windows needs a restart to finish it' -ForegroundColor Yellow }
    elseif ($code -ne 0) { Write-Bad "its setup ended with $code" }

    $now = Get-DotNetInstalled
    if ($now) { Write-Note "the runtime is now $now"; return $true }

    Write-Bad 'the runtime is still not there'
    return $false
}

function Get-PawnIoInstalled {
    $entry = Get-ItemProperty -LiteralPath $pawnIoKey -ErrorAction SilentlyContinue
    $parsed = $null

    if ($entry -and [version]::TryParse("$($entry.DisplayVersion)", [ref] $parsed)) { return $parsed }

    if (Test-Path -LiteralPath $pawnIoSys) {
        $file = (Get-Item -LiteralPath $pawnIoSys).VersionInfo.FileVersion
        if ([version]::TryParse("$file", [ref] $parsed)) { return $parsed }
    }

    if ($entry) { return [version]'0.0' }
    return $null
}

function Get-PawnIoOffered {
    $release = Read-Text $pawnIoApi
    if (-not $release) { return $null }

    try { $json = $release | ConvertFrom-Json } catch { return $null }

    $asset = $json.assets | Where-Object { $_.name -eq 'PawnIO_setup.exe' } | Select-Object -First 1
    if (-not $asset) { return $null }

    $parsed = $null
    if (-not [version]::TryParse("$($json.tag_name)".TrimStart('v'), [ref] $parsed)) { return $null }

    return @{ Version = $parsed; Url = $asset.browser_download_url }
}

function Install-PawnIo($offered) {
    $file = Join-Path $env:TEMP 'PawnIO_setup.exe'
    Write-Note "fetching $($offered.Url)"

    if (-not (Get-File $offered.Url $file)) {
        Write-Bad 'it could not be fetched; whatever is on the machine stays as it is'
        return
    }

    $code = Invoke-Program $file '-install -silent'
    Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue

    if ($code -eq 3010) { Write-Host '  installed, and Windows needs a restart to finish it' -ForegroundColor Yellow }
    elseif ($code -ne 0) { Write-Bad "its setup ended with $code" }

    $now = Get-PawnIoInstalled
    if ($now) { Write-Note "the driver is now $now" }
    else      { Write-Bad 'the driver is still not there; the app will say so at its next start' }
}

$refused = $false

function Complete-Run {
    if (-not $Elevated) {
        $missing = @()

        if ($Exe -and -not (Test-Path -LiteralPath $Exe)) {
            $missing += [pscustomobject]@{ What  = 'The app is not where it was installed'
                                           Where = $Exe }
        }
        if (-not (Get-DotNetInstalled)) {
            $missing += [pscustomobject]@{ What  = 'The .NET Desktop Runtime is not on this machine'
                                           Where = 'https://dotnet.microsoft.com/download/dotnet/10.0' }
        }
        if (-not (Get-PawnIoInstalled)) {
            $missing += [pscustomobject]@{ What  = 'The PawnIO driver is not on this machine'
                                           Where = 'https://pawnio.eu' }
        }

        foreach ($one in $missing) { Write-Bad "$($one.What): $($one.Where)" }
    }

    Write-Host "`nWhat happened here is also in $transcript"
    try { Stop-Transcript | Out-Null } catch { }

    if ($Elevated) { exit 0 }

    if (-not $refused) {
        Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
    }

    if ($missing -and $interactive) {
        $lines = $missing | ForEach-Object { "$($_.What)`n$($_.Where)" }
        $text  = "System Spinner x64 is installed, but it will not start until this is put " +
                 "right:`n`n" + ($lines -join "`n`n")

        try { (New-Object -ComObject WScript.Shell).Popup($text, 60, 'System Spinner x64', 48) | Out-Null }
        catch { }
    }

    exit 0
}

if (-not (Test-Elevated)) {
    Write-Step 'This needs administrator rights: Windows will ask for them'

    $arguments = @('-NoProfile', '-WindowStyle', 'Minimized', '-ExecutionPolicy', 'Bypass',
                   '-File', "`"$PSCommandPath`"", '-Elevated')
    if ($Exe)        { $arguments += @('-Exe', "`"$Exe`"") }
    if ($Folder)     { $arguments += @('-Folder', "`"$Folder`"") }
    if ($Product)    { $arguments += @('-Product', "`"$Product`"") }
    if ($NoPrevious) { $arguments += '-NoPrevious' }

    try {
        Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait
    }
    catch {
        $refused = $true
        Write-Bad 'the prompt was refused: nothing was changed.'
        Write-Bad "Run this file again to try once more: $PSCommandPath"
    }

    if (-not $refused -and -not $NoPrevious) {
        Write-Step 'Clearing away what an older copy left in this account'

        Clear-RunEntries @('HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run')

        Clear-Shortcuts @([Environment]::GetFolderPath('StartMenu'),
                          [Environment]::GetFolderPath('Desktop'))
    }

    Complete-Run
}

Write-Step 'Looking for copies installed some other way'

$copies = @()
if ($NoPrevious) {
    Write-Note 'not asked for: whatever else is on this machine is left alone'
}
else {
    $copies = @(Get-OtherCopies)
    if ($copies) { foreach ($copy in $copies) { Write-Note "found: $($copy.Name) $($copy.Version)" } }
    else         { Write-Note 'none: this is the only one' }
}

Write-Step 'Looking for the .NET Desktop Runtime'

$netInstalled = Get-DotNetInstalled
$netOffered   = Get-DotNetOffered

if ($netInstalled) { Write-Note "installed: $netInstalled" } else { Write-Note 'installed: none' }
if ($netOffered)   { Write-Note "newest:    $($netOffered.Version)" } else { Write-Note 'newest:    could not be asked for' }

$runtime = $netOffered -and ((-not $netInstalled) -or ($netInstalled -lt $netOffered.Version))
if (-not $runtime) { Write-Note 'nothing to do' }
if ((-not $netInstalled) -and (-not $netOffered)) { Write-Bad 'it is missing, and its address could not be read either' }

Write-Step 'Looking at the PawnIO driver'

$pawnInstalled = Get-PawnIoInstalled
$pawnOffered   = Get-PawnIoOffered

if ($pawnInstalled) { Write-Note "installed: $pawnInstalled" } else { Write-Note 'installed: none' }
if ($pawnOffered)   { Write-Note "newest:    $($pawnOffered.Version)" } else { Write-Note 'newest:    could not be asked for' }

$driver = $pawnOffered -and ((-not $pawnInstalled) -or ($pawnInstalled -lt $pawnOffered.Version))
if (-not $driver) { Write-Note 'nothing to do' }

if ($copies) {
    Write-Step 'Clearing away what was installed some other way'

    Invoke-Program (Join-Path $env:SystemRoot 'System32\taskkill.exe') "/f /im $exeName" | Out-Null

    $before = Get-TaskTarget

    foreach ($copy in $copies) {
        Write-Note "$($copy.Name) $($copy.Version):"
        if (Clear-Copy $copy) { Write-Note 'cleared away' }
    }

    Clear-RunEntries @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
                       'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run')

    Clear-Shortcuts @([Environment]::GetFolderPath('CommonStartMenu'),
                      [Environment]::GetFolderPath('CommonDesktopDirectory'))

    Restore-Ours $before
}

if ($runtime) {
    Write-Step 'Installing the .NET Desktop Runtime'
    Install-DotNet $netOffered | Out-Null
}

if ($driver) {
    Write-Step $(if ($pawnInstalled) { 'Bringing the PawnIO driver up to date' }
                 else { 'Installing the PawnIO driver' })
    Install-PawnIo $pawnOffered
}

Complete-Run
