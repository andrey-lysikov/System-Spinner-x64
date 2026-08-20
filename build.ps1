#Requires -Version 5.1
<#
    Building System Spinner x64 on a clean Windows 11 x64.

        .\build.ps1              build
        .\build.ps1 -Clean       remove everything the compiler produced, the exe included
        .\build.ps1 -Install     build and install into the system

    The result is a single build\SystemSpinnerX64.exe, about 16 MB, without .NET packed inside.
    Everything intermediate stays in build (bin and obj) as well; nothing appears next to the
    sources — that is set in Directory.Build.props.
#>

[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Install,

    # Skip the tests. By default they run before the build and take less than a second.
    [switch]$NoTests,

    # Where to install with -Install. Empty means asking, with a default path offered.
    [string]$InstallPath
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is empty in a default parameter value under PowerShell 5.1 — paths are set here.
$repo    = $PSScriptRoot
$source  = Join-Path $repo 'src'
$project = Join-Path $source 'SystemSpinnerX64.csproj'
$tests   = Join-Path $repo 'test\SystemSpinnerX64.Tests.csproj'
$output  = Join-Path $repo 'build'
$exeName = 'SystemSpinnerX64.exe'
$exe     = Join-Path $output $exeName
$icon    = Join-Path $output 'icon.ico'

$defaultInstallPath = Join-Path $env:ProgramFiles 'System Spinner x64'

function Write-Step($text) { Write-Host "`n=== $text" -ForegroundColor Cyan }
function Write-Warn($text) { Write-Host "  ! $text" -ForegroundColor Yellow }
function Write-Ok($text)   { Write-Host "  + $text" -ForegroundColor Green }

# --- Icon --------------------------------------------------------------------

<#
    Builds a multi-size .ico from icon.png.

    The icon has one source: icon.png in the project root. The finished .ico is not kept next to
    the sources: it is a build result and belongs in build, together with the exe. Two pictures
    of one icon would sooner or later diverge, leaving the exe with the old one.

    The entries inside the .ico are compressed png, eight sizes from 16 to 256. Windows has read
    those since Vista, while an uncompressed 256-pixel DIB would take a quarter of a megabyte.
#>
function Build-Icon {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Target
    )

    if (-not (Test-Path $Source)) {
        throw "Не найден $Source. Значок exe собирается из него; верните файл или положите свой png."
    }

    Add-Type -AssemblyName System.Drawing

    # 20 and 24 are the tray icon sizes at 125 % and 150 % scaling; without them Windows would
    # stretch the sixteen-pixel entry and the icon would blur exactly where it is looked at.
    $sizes = @(16, 20, 24, 32, 48, 64, 128, 256)

    $image = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
    $blobs = New-Object System.Collections.ArrayList

    try {
        foreach ($size in $sizes) {
            $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $graphics.InterpolationMode = 'HighQualityBicubic'
            $graphics.PixelOffsetMode = 'HighQuality'
            $graphics.CompositingQuality = 'HighQuality'
            $graphics.DrawImage($image,
                (New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
                0, 0, $image.Width, $image.Height, 'Pixel')
            $graphics.Dispose()

            $stream = New-Object System.IO.MemoryStream
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $bitmap.Dispose()

            [void]$blobs.Add([PSCustomObject]@{ Size = $size; Data = $stream.ToArray() })
            $stream.Dispose()
        }
    }
    finally {
        $image.Dispose()
    }

    $out = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($out)

    try {
        # ICONDIR: two zeros, type 1 (icon), the number of entries.
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$blobs.Count)

        $offset = 6 + 16 * $blobs.Count
        foreach ($blob in $blobs) {
            # 256 is written as zero: one byte is reserved for the size and 256 does not fit.
            $dimension = if ($blob.Size -ge 256) { 0 } else { $blob.Size }

            $writer.Write([byte]$dimension)          # width
            $writer.Write([byte]$dimension)          # height
            $writer.Write([byte]0)                   # palette colours: 0 means no palette
            $writer.Write([byte]0)                   # reserved
            $writer.Write([UInt16]1)                 # planes
            $writer.Write([UInt16]32)                # bits per pixel
            $writer.Write([UInt32]$blob.Data.Length)
            $writer.Write([UInt32]$offset)

            $offset += $blob.Data.Length
        }

        foreach ($blob in $blobs) { $writer.Write($blob.Data) }
        $writer.Flush()

        [System.IO.File]::WriteAllBytes($Target, $out.ToArray())
    }
    finally {
        $writer.Dispose()
        $out.Dispose()
    }
}

# --- Clean -------------------------------------------------------------------

if ($Clean) {
    Write-Step 'Очищаю результаты сборки'

    # config.conf, the log and the dumps are left alone: the app creates them, not the build.
    foreach ($item in (Join-Path $output 'bin'), (Join-Path $output 'obj'), $exe, $icon) {
        if (Test-Path $item) {
            Remove-Item $item -Recurse -Force
            Write-Ok ("удалено: " + $item.Replace("$repo\", ''))
        }
    }

    Write-Host "`nГотово. Файлы самого приложения (config.conf, журнал) оставлены нетронутыми."
    return
}

# --- Build -------------------------------------------------------------------

if (-not (Test-Path $project)) {
    throw "Не найден $project. Скрипт должен лежать в корне проекта, рядом с папкой src."
}

Write-Step 'Проверяю .NET SDK'
$sdkOk = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    # Lines like "10.0.100 [C:\Program Files\dotnet\sdk]" — the major must be 10 or higher.
    $sdks = @(& dotnet --list-sdks 2>$null)
    $found = $sdks | Where-Object { (($_ -split '\.')[0] -as [int]) -ge 10 }
    $sdkOk = [bool]$found
    if ($sdkOk) { Write-Ok ('найден SDK ' + ((($found | Select-Object -Last 1) -split ' ')[0])) }
}

if (-not $sdkOk) {
    Write-Warn 'подходящий .NET SDK не найден'
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        Write-Host '  Ставлю .NET 10 SDK через winget...'
        & winget install --id Microsoft.DotNet.SDK.10 --accept-package-agreements --accept-source-agreements
        Write-Warn 'закройте это окно PowerShell, откройте новое и запустите build.ps1 снова — обновится PATH'
    }
    else {
        Write-Warn 'winget недоступен — скачайте SDK: https://dotnet.microsoft.com/download/dotnet/10.0'
    }
    return
}

Write-Step 'Снимаю блокировку со скачанных файлов'
Get-ChildItem -Path $repo -Recurse -File -Include *.cs, *.xaml, *.csproj, *.sln, *.json, *.manifest, *.ps1 |
    Unblock-File -ErrorAction SilentlyContinue
Write-Ok 'готово'

Write-Step 'Собираю значок'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Build-Icon -Source (Join-Path $repo 'icon.png') -Target $icon
Write-Ok ("готово: " + $icon.Replace("$repo\", ''))

Write-Step 'Восстанавливаю пакеты (нужен интернет)'
& dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore не прошёл. Проверьте интернет и доступ к nuget.org.' }

if (-not $NoTests) {
    Write-Step 'Прогоняю тесты'
    & dotnet test $tests -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Тесты не прошли. Сборку не делаю: чинить надо сначала их.' }
    Write-Ok 'тесты прошли'
}

# --- Version -----------------------------------------------------------------

# The version comes from the last tag of the form v1.2.3, by the same rules as release.yml, or
# a local build and a release would show different numbers. With no git, repository or tags the
# value from the csproj quietly remains.
$versionArgs = @()
$describe = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    try {
        # ErrorActionPreference=Stop turns git output on stderr (no tags, not a repository) into
        # an exception — an ordinary case here rather than an error, so both channels are muted.
        $ErrorActionPreference = 'Continue'

        # The output is taken whole, without "| Select-Object -First 1": that cuts the pipeline
        # short without waiting for git to finish, and $LASTEXITCODE ends up -1 on valid output.
        # The race was intermittent — the tag version was substituted every other time.
        $out = & git -C $repo describe --tags --long --dirty --match 'v[0-9]*' 2>$null
        if ($LASTEXITCODE -eq 0 -and $out) { $describe = @($out)[0] }
    }
    catch { $describe = $null }
    finally { $ErrorActionPreference = 'Stop' }
}

if ($describe) {
    # v1.2.3-4-gabc1234[-dirty] gives the numeric part up to the first hyphen
    $number = ($describe -replace '^v', '') -split '-' | Select-Object -First 1

    if ($number -match '^\d+(\.\d+){0,3}$') {
        $versionArgs = @("-p:Version=$number", "-p:InformationalVersion=$describe")
        Write-Ok "версия из тега: $number ($describe)"
    }
    else {
        Write-Warn "из «$describe» не вышло номера версии — беру значение из csproj"
    }
}
else {
    Write-Warn 'тега вида v1.2.3 нет — версия остаётся такой, как записана в csproj'
}

# --- Building the exe --------------------------------------------------------

Write-Step 'Собираю exe'

# The app puts its own files next to the exe, that is, here. The folder must not be removed
# wholesale — that would wipe the user settings and log along with the build folders.
if (Test-Path $exe) { Remove-Item $exe -Force }

# Every publish switch is already in the csproj: single-file, framework-dependent, win-x64.
& dotnet publish $project -c Release -o $output @versionArgs
if ($LASTEXITCODE -ne 0) { throw 'Сборка не прошла. Полный лог выше.' }
if (-not (Test-Path $exe)) { throw "Ожидал $exe, но его нет." }

# Apart from the exe itself the build must leave nothing extra in build. bin and obj are the
# compiler folders; the app files are created while it runs.
$expected = $exeName, 'icon.ico', 'config.conf', 'SystemSpinnerX64.log', 'SystemSpinnerX64.log.old',
            'sensors-found.txt'
$extra = Get-ChildItem $output -File | Where-Object { $expected -notcontains $_.Name }
if ($extra) {
    Write-Warn 'в build появились лишние файлы (для работы они не нужны):'
    $extra | ForEach-Object { Write-Host "    $($_.Name)" }
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Ok "$exe ($sizeMb МБ, .NET внутрь не упакован)"

# --- Install -----------------------------------------------------------------

if ($Install) {
    Write-Step 'Установка'

    if (-not $InstallPath) {
        $answer = Read-Host "  Куда установить? [Enter — $defaultInstallPath]"
        $InstallPath = if ([string]::IsNullOrWhiteSpace($answer)) { $defaultInstallPath } else { $answer.Trim('"') }
    }

    $target = Join-Path $InstallPath $exeName
    Write-Host "  Ставлю в $target"

    # Copying is tried directly first. Program Files and folders like it need administrator
    # rights — then the same operation is repeated through a separate process with a UAC prompt.
    $copied = $false
    try {
        if (-not (Test-Path $InstallPath)) { New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null }
        Copy-Item $exe $target -Force
        $copied = $true
    }
    catch [System.UnauthorizedAccessException] {
        Write-Warn 'нет прав на запись — запрашиваю их у Windows'
    }
    catch {
        throw "Не удалось скопировать: $($_.Exception.Message)"
    }

    if (-not $copied) {
        $command = "New-Item -ItemType Directory -Path '$InstallPath' -Force | Out-Null; " +
                   "Copy-Item '$exe' '$target' -Force"
        $process = Start-Process powershell -Verb RunAs -Wait -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $command
        )

        if ($process.ExitCode -ne 0) { throw 'Копирование с правами администратора не удалось.' }
        if (-not (Test-Path $target)) { throw "Файл так и не появился: $target" }
    }

    Write-Ok "установлено: $target"

    # The shortcut goes into the current user Start menu without a subfolder: there is one
    # program, and hiding it in a folder of one item is pointless. That folder is writable
    # without administrator rights even when the exe sits in Program Files.
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    $link = Join-Path $startMenu 'System Spinner x64.lnk'
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($link)
        $shortcut.TargetPath = $target
        $shortcut.WorkingDirectory = $InstallPath
        $shortcut.IconLocation = $target
        $shortcut.Description = 'Мониторинг системы: значок в трее и панель поверх полноэкранных приложений'
        $shortcut.Save()
        Write-Ok "ярлык в меню «Пуск»: $link"
    }
    catch {
        Write-Warn "ярлык создать не удалось: $($_.Exception.Message)"
    }

    # The same rule as in the app itself: a system folder is recognised by a path segment rather
    # than by matching a known folder — Windows and Program Files are not only on C.
    if ($InstallPath -match '(^|\\)(Program Files( \(x86\))?|ProgramData|Windows)(\\|$)') {
        Write-Host "  Настройки и журнал будут в $env:LOCALAPPDATA\SystemSpinnerX64:"
        Write-Host '  системному каталогу пользовательские данные не место.'
    }
    else {
        Write-Host '  Настройки и журнал приложение хранит рядом с exe.'
    }
}

# --- What next ---------------------------------------------------------------

Write-Step 'Дальше'
Write-Host '  Запуск: двойной клик по SystemSpinnerX64.exe - UAC запросится сам.'
Write-Host '  На целевой машине нужен .NET Desktop Runtime 10 или новее и Windows 11 x64.'
Write-Host '  Рантайма нет - .NET сам покажет окно со ссылкой на загрузку (по-английски).'
Write-Host '  Нужен драйвер PawnIO (https://pawnio.eu) - без него приложение не запустится:'
Write-Host '  через него читаются температуры, мощность и обороты.'
Write-Host '  Управление - значок в трее: левая кнопка открывает статистику, правая - меню.'
Write-Host '  Панель поверх игры видна, только когда активное окно занимает экран целиком.'
Write-Host '  Описание параметров: sample.conf'
