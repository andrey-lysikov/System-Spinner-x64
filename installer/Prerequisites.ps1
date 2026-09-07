#  Copyright © AndreyLysikov
#  SPDX-License-Identifier: Apache-2.0

# What the msi cannot do from inside itself, done on the installing machine once its own files are
# in place. Three things:
#
#   * Copies of the app that some other installer left behind are taken out. Windows Installer
#     replaces only what was installed under its own UpgradeCode — a copy from a fork's build, from
#     an Inno Setup or NSIS setup, or from an msi carrying an UpgradeCode of its own is invisible to
#     it. Left alone, it stays where it is: two entries in the list of installed programs, two tray
#     icons polling the same sensors, and the older of the two starting with Windows.
#
#   * The .NET Desktop Runtime is fetched when it is missing or behind. The app is published
#     framework-dependent — one exe, and .NET on the machine — and without it nothing of it runs:
#     the window with the download link is put up by the apphost itself, before the first line of
#     C#, which is why this cannot be checked from inside the app.
#
#   * PawnIO is fetched from its author's releases and installed, when it is missing or behind. The
#     sensors are read through this driver and the app refuses to start without it; a version behind
#     is a version without whatever hardware arrived since. The msi used to carry a copy of that
#     setup — it no longer does: the copy was as old as the build that made the msi, and a driver
#     signed by somebody else is not ours to hand out.
#
# Nothing here stops the install. The app is installed by the time this runs; every failure is
# written down and stepped over, and the two that leave the app unable to start — no runtime, no
# driver — are said again at the end, in the window somebody is looking at.
#
#     %TEMP%\System-Spinner-prerequisites.log
#
# Rights are asked for once, and only when there is something to do — a machine with nothing to
# remove and both the runtime and the driver already in place sees no prompt at all. A refused prompt changes nothing,
# and the same file can be run again by hand:
#
#     powershell -ExecutionPolicy Bypass -File Prerequisites.ps1 -Exe "C:\...\System-Spinner.exe"

param(
    # Where the app was just installed. The shortcuts and the autostart task are pointed back at it
    # when the uninstaller of an older copy takes them along: the names are the same, and it cannot
    # tell whose they are.
    [string] $Exe = '',

    # The ProductCode of this very install, handed over by the msi. What the repair below puts back
    # is what this package installed and nothing else.
    [string] $Product = '',

    # The checkbox on the shortcuts page, cleared: whatever else is on the machine is left alone.
    [switch] $NoPrevious,

    # Set on the second run of this file — the one that has the rights. Everything above has
    # already been decided by then; this run only carries it out.
    [switch] $Elevated
)

# Not Stop: curl writes its progress to the error stream, which under Stop would end the run in the
# middle of a download. Every step below is checked for itself instead.
$ErrorActionPreference = 'Continue'

# The UpgradeCode of the package this file ships in — installer/Package.wxs, and build.ps1 refuses
# to build the msi when the two have drifted apart. Everything registered under it belongs to
# Windows Installer: it replaces those itself, and they are stepped over here.
$ownUpgradeCode = '{D993C858-1615-4986-B456-173BEFEDA37C}'

# What an installed copy of this app calls itself: "System Spinner x64" from the msi, "System-Spinner"
# from most anything else. The path is the second sign, for an entry named something else entirely.
$namePattern = '^system[ _-]?spinner'
$pathMark    = 'System-Spinner'
$exeName     = 'System-Spinner.exe'
$taskName    = 'System-Spinner'

# Where the newest build of the runtime lives, and where the ones already on the machine do.
# ProgramW6432 and not ProgramFiles: run by hand from a 32-bit shell, "Program Files" is the (x86)
# one, where no .NET runtime has ever been.
$dotNetUrl    = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe'
$programFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
$dotNetShared = Join-Path $programFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'

# The driver's own entry in the list of installed programs — the same key the app looks at in
# Platform/SensorDriver.cs — and where its releases are.
$pawnIoKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO'
$pawnIoApi = 'https://api.github.com/repos/namazso/PawnIO.Setup/releases/latest'
$pawnIoSys = Join-Path $env:SystemRoot 'System32\drivers\PawnIO.sys'

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

# curl.exe, which Windows ships, rather than Invoke-WebRequest: present under any execution policy
# and fast. Its progress meter goes to the error stream, hence the silence.
function Read-Text($url) {
    $answer = & curl.exe -sL --fail -H 'User-Agent: System-Spinner-setup' $url
    if ($LASTEXITCODE -ne 0) { return $null }
    return $answer
}

# The address a redirect ends at, which is how the newest build of the runtime names its version
# without anything being downloaded yet.
function Read-Address($url) {
    $answer = & curl.exe -sIL -o NUL -w '%{url_effective}' $url
    if ($LASTEXITCODE -ne 0) { return $null }
    return $answer
}

function Get-File($url, $path) {
    & curl.exe -L --fail --silent --show-error -o $path $url
    return ($LASTEXITCODE -eq 0) -and (Test-Path -LiteralPath $path)
}

# An uninstall string is a command line, and what has to be started is its first word. A path with
# spaces in it is quoted; one without spaces and without quotes is taken up to the first space.
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

# Runs something and waits for it. This script already has the rights by the time anything here is
# called, so nothing raises a prompt of its own.
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

# ------------------------------------------------------------------ what else is installed

# The product codes Windows Installer already knows as ours. RelatedProducts throws when there are
# none, which is the answer as much as a list would be.
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

# Everything in the three uninstall branches that calls itself this app, minus what the msi handles
# on its own. Both bitnesses of the machine-wide branch: an installer built for 32 bits writes to
# the other one.
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
                Uninstall = "$($entry.UninstallString)"
                Quiet     = "$($entry.QuietUninstallString)"
            }
        }
    }
}

# The command that takes one of them out, without asking questions of its own. An msi is removed by
# product code; everything else is trusted to have written down how it wants to be called, and the
# two setups anybody actually builds with are recognised by the name of their uninstaller.
function Get-RemovalCommand($copy) {
    if ($copy.Code -match '^\{[0-9A-Fa-f-]{36}\}$' -and $copy.Uninstall -match 'msiexec') {
        return @{
            File      = (Join-Path $env:SystemRoot 'System32\msiexec.exe')
            Arguments = "/x $($copy.Code) /qn /norestart"
        }
    }

    if ($copy.Quiet) { return (Split-Command $copy.Quiet) }
    if (-not $copy.Uninstall) { return $null }

    $command = Split-Command $copy.Uninstall
    if (-not $command) { return $null }

    if ($command.File -match 'unins\d*\.exe$') {
        # Inno Setup
        $command.Arguments = "$($command.Arguments) /VERYSILENT /NORESTART /SUPPRESSMSGBOXES".Trim()
    }
    elseif ($command.File -match 'uninst.*\.exe$') {
        # NSIS
        $command.Arguments = "$($command.Arguments) /S".Trim()
    }

    return $command
}

# ------------------------------------------------------------------ what is ours

# The Start menu entry, the desktop shortcut and the autostart task, as they are before anything is
# removed. All three carry the same names an older copy would have used, and its uninstaller will
# take them for its own.
function Get-OurTraces {
    $menu = Join-Path ([Environment]::GetFolderPath('CommonStartMenu')) "Programs\$taskName.lnk"
    $desk = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) "$taskName.lnk"

    $target = $null
    try {
        $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($task) { $target = "$($task.Actions[0].Execute)".Trim('"') }
    }
    catch { }

    return @{
        Menu       = $menu
        Desk       = $desk
        HadMenu    = (Test-Path -LiteralPath $menu)
        HadDesk    = (Test-Path -LiteralPath $desk)
        TaskTarget = $target
    }
}

# Puts back what the uninstaller of an older copy took with it. The shortcuts come from the msi
# itself — /fs reinstalls the shortcuts of this product and touches nothing else — and the task is
# made again by hand, because it belongs to the app rather than to the package.
function Restore-Ours($before) {
    if ($Product -and (($before.HadMenu -and -not (Test-Path -LiteralPath $before.Menu)) -or
                       ($before.HadDesk -and -not (Test-Path -LiteralPath $before.Desk)))) {
        Write-Note 'the shortcuts went with it: putting them back'
        Invoke-Program (Join-Path $env:SystemRoot 'System32\msiexec.exe') "/fs $Product /qn /norestart" | Out-Null
    }

    if (-not $Exe) { return }

    $now = (Get-OurTraces).TaskTarget

    # Gone with the older copy, or still pointing at the exe that has just been removed: either way
    # the machine would come up without the app. /RL HIGHEST and ONLOGON are what the app itself
    # writes in Startup/AutoStart.cs.
    $lost    = $before.TaskTarget -and -not $now
    $strayed = $now -and ($now -ne $Exe)

    if (-not ($lost -or $strayed)) { return }

    Write-Note $(if ($lost) { 'the autostart task went with it: making it again' }
                 else { "the autostart task still points at $now, and is pointed here instead" })

    $code = Invoke-Program (Join-Path $env:SystemRoot 'System32\schtasks.exe') `
                           @('/Create', '/TN', $taskName, '/TR', "`"$Exe`"",
                             '/SC', 'ONLOGON', '/RL', 'HIGHEST', '/F')

    if ($code -ne 0) { Write-Bad "schtasks returned $code; turn autostart on again from the tray menu" }
}

# ------------------------------------------------------------------ the runtime

# The frameworks a .NET application actually runs on are folders named by version. The newest of
# them is what the app will get; nothing there means nothing installed.
function Get-DotNetInstalled {
    if (-not (Test-Path -LiteralPath $dotNetShared)) { return $null }

    $versions = Get-ChildItem -LiteralPath $dotNetShared -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { $parsed = $null; if ([version]::TryParse($_.Name, [ref] $parsed)) { $parsed } }

    if (-not $versions) { return $null }
    return ($versions | Sort-Object -Descending)[0]
}

# The one address Microsoft keeps pointed at the newest build of the band, and it lands on a file
# named after the version: windowsdesktop-runtime-10.0.11-win-x64.exe.
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

    # Said against the folders again rather than against the setup's word for it.
    $now = Get-DotNetInstalled
    if ($now) { Write-Note "the runtime is now $now"; return $true }

    Write-Bad 'the runtime is still not there'
    return $false
}

# ------------------------------------------------------------------ the driver

# What the driver's own entry says, and failing that the file it installs: an entry without a
# version is still an answer to whether it is there at all.
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

# The newest release and the setup hanging on it. A machine with no way out to the internet gets
# nothing here: what is on it already stays as it is, and if that is nothing at all the end of this
# run says so.
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

# 3010 is the way a setup says "in, but not until a restart".
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

    # Said against the registry again rather than against the setup's word for it.
    $now = Get-PawnIoInstalled
    if ($now) { Write-Note "the driver is now $now" }
    else      { Write-Bad 'the driver is still not there; the app will say so at its next start' }
}

# ------------------------------------------------------------------ what has to be done
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

# The one failure worth making a noise about: the app does not start at all without the runtime,
# and the apphost says so in a window of its own rather than in a log anybody would look at.
$stopped = (-not $netInstalled) -and (-not $netOffered)
if ($stopped) { Write-Bad 'it is missing, and its address could not be read either' }

# Set when the prompt for administrator rights below is refused. Nothing was done then, and this
# file is the way to try again, so it is not deleted at the end.
$refused = $false

Write-Step 'Looking at the PawnIO driver'

$pawnInstalled = Get-PawnIoInstalled
$pawnOffered   = Get-PawnIoOffered

if ($pawnInstalled) { Write-Note "installed: $pawnInstalled" } else { Write-Note 'installed: none' }
if ($pawnOffered)   { Write-Note "newest:    $($pawnOffered.Version)" } else { Write-Note 'newest:    could not be asked for' }

$driver = $pawnOffered -and ((-not $pawnInstalled) -or ($pawnInstalled -lt $pawnOffered.Version))
if (-not $driver) { Write-Note 'nothing to do' }

function Complete-Run {
    Write-Host "`nWhat happened here is also in $transcript"
    try { Stop-Transcript | Out-Null } catch { }

    # Nothing of this belongs on the machine once it has run. Only the first run clears it away:
    # the second one is started from this same file and would pull it out from under itself. A
    # refused prompt is the one case where it stays: there is nothing to show for the run, and the
    # advice it just gave was to start this same file again.
    if (-not $Elevated -and -not $refused) {
        Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
    }

    # A window that vanishes takes its message with it, and the messages worth reading here are the
    # two that say the app will not start at all. Said by the first run, whose window is the one
    # somebody is looking at; the second one closes with the rest of the installer.
    if ($Elevated) { exit 0 }

    # Asked of the machine rather than taken from what was tried above: the driver may have been
    # there all along, and the elevated run may have just put it in. Since the msi stopped carrying
    # the setup, this is the only place it comes from — a machine with no way out to the internet
    # ends up without it and has to be told.
    $driverless = -not (Get-PawnIoInstalled)

    if ($stopped -or $driverless) {
        if ($stopped) {
            Write-Bad 'The app will not start until the .NET Desktop Runtime is on this machine:'
            Write-Bad 'https://dotnet.microsoft.com/download/dotnet/10.0'
        }

        if ($driverless) {
            Write-Bad 'The app will not start until the PawnIO driver is on this machine:'
            Write-Bad 'https://pawnio.eu'
        }

        Write-Host "`nThis window closes in a minute." -ForegroundColor Red
        Start-Sleep -Seconds 60
    }

    exit 0
}

if (-not $copies -and -not $runtime -and -not $driver) { Complete-Run }

# Removing a program, installing a runtime and installing a driver are all machine-wide, and this
# half of the installer runs as whoever started it. The rights are asked for once, here, and the
# work is done by a second run of this same file — rather than one prompt per setup.
if (-not (Test-Elevated)) {
    Write-Step 'This needs administrator rights: Windows will ask for them'

    # Quoted here rather than left to Start-Process: it joins the list with spaces and quotes
    # nothing, and both of these paths go through a folder named after whoever is installing.
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Elevated')
    if ($Exe)        { $arguments += @('-Exe', "`"$Exe`"") }
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

    # What the second run made of it, asked of the machine rather than taken on trust.
    if (-not (Get-DotNetInstalled)) { $stopped = $true }
    Complete-Run
}

# ------------------------------------------------------------------ doing it

if ($copies) {
    Write-Step 'Taking out what was installed some other way'

    # A running copy holds its own exe, and every uninstaller here would leave the file behind for
    # the next restart. It has no window to close and it runs elevated, so it is simply ended.
    Invoke-Program (Join-Path $env:SystemRoot 'System32\taskkill.exe') "/f /im $exeName" | Out-Null

    $before = Get-OurTraces

    foreach ($copy in $copies) {
        $command = Get-RemovalCommand $copy
        if (-not $command) {
            Write-Bad "$($copy.Name): it registered no way of removing itself; take it out by hand"
            continue
        }

        Write-Note "$($copy.Name): $($command.File) $($command.Arguments)"
        $code = Invoke-Program $command.File $command.Arguments

        if ($code -eq 0 -or $code -eq 3010) { Write-Note 'removed' }
        else { Write-Bad "its uninstaller ended with $code; what is left of it is in the list of installed programs" }
    }

    Restore-Ours $before
}

if ($runtime) {
    Write-Step 'Installing the .NET Desktop Runtime'
    if (-not (Install-DotNet $netOffered) -and -not $netInstalled) { $stopped = $true }
}

if ($driver) {
    Write-Step $(if ($pawnInstalled) { 'Bringing the PawnIO driver up to date' }
                 else { 'Installing the PawnIO driver' })
    Install-PawnIo $pawnOffered
}

Complete-Run
