<#
.SYNOPSIS
    Builds Lumenotepad's self-contained Windows x64 setup executable, and optionally the launcher.

.DESCRIPTION
    Three passes, in this order, because each one needs the previous:

      1. dotnet publish Lumenotepad          -> the app being installed, staged as the payload directory
      2. dotnet publish Lumenotepad.Setup    -> a payload-LESS installer. It is not thrown away: it becomes
         (no LumenotepadPayload)                uninstall.exe inside the payload, so removing Lumenotepad
                                                never depends on keeping setup.exe around.
      3. dotnet publish Lumenotepad.Setup    -> the real setup.exe, single-file, with the archive embedded
         (LumenotepadPayload=<archive>)
      4. dotnet publish Lumenotepad.Setup    -> OPTIONAL, -Launcher only. The same GUI with no payload, which
         (LumenotepadLauncher=true)             downloads the published portable zip at install time instead
                                                of carrying one. A few megabytes rather than eighty.

    The staging directory IS the payload: whatever ends up in it is what gets installed, so new runtime files
    are picked up without editing a file list.

    The portable zip is NOT built here; tools/publish-windows.sh owns it, and the launcher downloads whatever
    that script published (through the latest.json update manifest).

.EXAMPLE
    pwsh .\installer\build-setup.ps1 -OpenOutputFolder

.EXAMPLE
    pwsh .\installer\build-setup.ps1 -Version 1.2.8 -Launcher
#>

[CmdletBinding()]
param(
    [string] $Version,

    # Reuse whatever a previous run left in the staging directory instead of republishing the app. Handy
    # while iterating on the installer itself, where the app hasn't changed.
    [switch] $SkipAppPublish,

    # Also publish the launcher: the same GUI with no payload, which downloads the published portable archive
    # instead of carrying it.
    [switch] $Launcher,

    [switch] $OpenOutputFolder,

    # Brotli quality 11 instead of the default. Worth it for a build you actually publish; not worth the
    # extra minutes while iterating.
    [switch] $MaxCompression
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string] $Text)
    Write-Host ''
    Write-Host "== $Text ==" -ForegroundColor Cyan
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)] [string] $FilePath,
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [Parameter(Mandatory = $true)] [string] $FailureMessage,
        [string] $WorkingDirectory
    )

    if ($WorkingDirectory) { Push-Location -LiteralPath $WorkingDirectory }
    try {
        & $FilePath @Arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($WorkingDirectory) { Pop-Location }
    }
    if ($exitCode -ne 0) { throw "$FailureMessage Exit code: $exitCode" }
}

function New-PayloadArchive {
    # tar + Brotli, the exact pair Payload.ExtractAsync reads back: TarFile.CreateFromDirectory with no base
    # directory, wrapped in a BrotliStream. Compressed here, in-process, so packing needs no elevation and no
    # extra tool.
    param(
        [Parameter(Mandatory = $true)] [string] $SourceDirectory,
        [Parameter(Mandatory = $true)] [string] $DestinationFile,
        [Parameter(Mandatory = $true)] [bool] $Maximum
    )

    $level = if ($Maximum) {
        [System.IO.Compression.CompressionLevel]::SmallestSize
    } else {
        [System.IO.Compression.CompressionLevel]::Optimal
    }

    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $DestinationFile) -Force)
    if (Test-Path -LiteralPath $DestinationFile) { Remove-Item -LiteralPath $DestinationFile -Force }

    $outFile = [System.IO.File]::Create($DestinationFile)
    try {
        $brotli = [System.IO.Compression.BrotliStream]::new($outFile, $level)
        try {
            [System.Formats.Tar.TarFile]::CreateFromDirectory($SourceDirectory, $brotli, $false)
        }
        finally { $brotli.Dispose() }
    }
    finally { $outFile.Dispose() }
}

function Find-DotNet10 {
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (-not $command) { $command = Get-Command dotnet -ErrorAction SilentlyContinue }
    if (-not $command) { throw '.NET SDK was not found. Install the .NET 10 SDK first.' }

    $sdkList = & $command.Source --list-sdks
    if ($LASTEXITCODE -ne 0) { throw 'dotnet --list-sdks failed.' }
    if (-not ($sdkList -match '(?m)^10\.')) {
        throw ".NET 10 SDK was not found.`nInstalled SDKs:`n$($sdkList -join "`n")"
    }
    return $command.Source
}

function Get-VersionFromCsproj {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    $text = Get-Content -LiteralPath $Path -Raw
    $match = [regex]::Match($text, '<Version>([^<]+)</Version>')
    if ($match.Success) { return $match.Groups[1].Value.Trim() }
    return $null
}

function Convert-ToFileVersion {
    param([string] $SemanticVersion)
    $base = ($SemanticVersion -split '[-+]')[0]
    $parts = @()
    foreach ($part in @($base -split '\.')) {
        if ($part -match '^\d+$') {
            $number = [int]$part
            if ($number -lt 0) { $number = 0 }
            if ($number -gt 65535) { $number = 65535 }
            $parts += $number
        }
        else { $parts += 0 }
    }
    while ($parts.Count -lt 4) { $parts += 0 }
    return ($parts[0..3] -join '.')
}

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

$installerDir = $PSScriptRoot
$root = Split-Path -Parent $installerDir
$setupProject = Join-Path $installerDir 'Lumenotepad.Setup\Lumenotepad.Setup.csproj'
$appProject = Join-Path $root 'src\Lumenotepad\Lumenotepad.csproj'

if (-not (Test-Path -LiteralPath $setupProject -PathType Leaf)) {
    throw "Setup project not found: $setupProject"
}

if (-not $Version) { $Version = Get-VersionFromCsproj -Path $appProject }
if (-not $Version) { throw "Could not read a version from $appProject. Pass -Version." }
if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z._+\-]*$') { throw "Invalid version '$Version'." }

$fileVersion = Convert-ToFileVersion -SemanticVersion $Version

$buildRoot = Join-Path $installerDir '.build'
$configRoot = Join-Path $buildRoot 'Release'
$payloadDir = Join-Path $configRoot 'payload'      # <- this directory IS what gets installed
$bareDir = Join-Path $configRoot 'bare'            # <- payload-less installer / uninstall.exe
$setupDir = Join-Path $configRoot 'setup'
$launcherDir = Join-Path $configRoot 'launcher'
$archive = Join-Path $configRoot 'Lumenotepad.payload'
$distDir = Join-Path $root 'dist'
$setupPath = Join-Path $distDir "Lumenotepad-Setup-$Version-win-x64.exe"
$launcherPath = Join-Path $distDir "Lumenotepad-Launcher-$Version-win-x64.exe"
$hashPath = Join-Path $distDir 'SHA256SUMS.txt'

Write-Host ''
Write-Host 'Lumenotepad Setup Builder' -ForegroundColor Green
Write-Host "  Root:         $root"
Write-Host "  Version:      $Version"
Write-Host "  File version: $fileVersion"
Write-Host "  Staging:      $payloadDir"
Write-Host "  Output:       $distDir"

$dotnet = Find-DotNet10

Write-Step 'Cleaning staging directory'
if (Test-Path -LiteralPath $configRoot) {
    if ($SkipAppPublish) {
        # Keep the staged app; clear everything derived from it, including the uninstall.exe a previous run
        # placed in the payload, so this run's bare build replaces it rather than shipping a stale one.
        foreach ($sub in @('bare', 'setup', 'launcher')) {
            $p = Join-Path $configRoot $sub
            if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force }
        }
        if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
        $stale = Join-Path $payloadDir 'uninstall.exe'
        if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force }
    }
    else {
        # Per child, tolerating a directory that is empty but still has a stale handle on it (an antivirus
        # scan of a just-deleted installer does this). Empty-but-stuck is harmless: publishing into it works;
        # only leftover CONTENT would poison the payload, so that is the only thing treated as fatal.
        foreach ($child in @(Get-ChildItem -LiteralPath $configRoot -Force)) {
            try { Remove-Item -LiteralPath $child.FullName -Recurse -Force -ErrorAction Stop }
            catch {
                if (Test-Path -LiteralPath $child.FullName) {
                    $left = @(Get-ChildItem -LiteralPath $child.FullName -Recurse -File -Force -ErrorAction SilentlyContinue)
                    if ($left.Count -gt 0) { throw }
                }
            }
        }
    }
}
[void](New-Item -ItemType Directory -Path $payloadDir -Force)
[void](New-Item -ItemType Directory -Path $distDir -Force)

# ---------------------------------------------------------------------------
# Pass 1 — the app
# ---------------------------------------------------------------------------

if (-not $SkipAppPublish) {
    Write-Step "Publishing Lumenotepad ($Version, win-x64, self-contained)"
    Invoke-NativeCommand -FilePath $dotnet -Arguments @(
        'publish', $appProject,
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:UseAppHost=true', '-o', $payloadDir,
        '-v', 'q', '--nologo'
    ) -FailureMessage 'Publishing Lumenotepad failed.'
}
else {
    Write-Step 'Skipping the app publish (-SkipAppPublish)'
}

$appExe = Join-Path $payloadDir 'Lumenotepad.exe'
if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
    throw "Lumenotepad.exe was not found at $appExe."
}

foreach ($doc in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    $src = Join-Path $root $doc
    if (Test-Path -LiteralPath $src -PathType Leaf) {
        Copy-Item -LiteralPath $src -Destination (Join-Path $payloadDir $doc) -Force
    }
}

# ---------------------------------------------------------------------------
# Pass 2 — the payload-less installer: uninstall.exe
# ---------------------------------------------------------------------------

Write-Step 'Publishing the bare installer (becomes uninstall.exe)'
Invoke-NativeCommand -FilePath $dotnet -Arguments @(
    'publish', $setupProject,
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-o', $bareDir,
    '-p:PublishSingleFile=true',
    '-p:EnableCompressionInSingleFile=true',
    # NOT optional. Without it a single-file publish still drops libSkiaSharp/av_libglesv2/libHarfBuzzSharp
    # NEXT TO the exe, and since only the .exe is copied the installer starts with no renderer and dies
    # instantly, with no console to say why.
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:PublishTrimmed=false', '-p:PublishReadyToRun=false',
    '-p:DebugType=none', '-p:DebugSymbols=false',
    "-p:Version=$Version", "-p:AssemblyVersion=$fileVersion",
    "-p:FileVersion=$fileVersion", "-p:InformationalVersion=$Version"
) -FailureMessage 'Publishing the bare installer failed.'

$bareExe = Join-Path $bareDir 'Lumenotepad.Setup.exe'
if (-not (Test-Path -LiteralPath $bareExe -PathType Leaf)) {
    throw "Expected $bareExe after publishing the bare installer."
}

Copy-Item -LiteralPath $bareExe -Destination (Join-Path $payloadDir 'uninstall.exe') -Force

$payloadFiles = @(Get-ChildItem -LiteralPath $payloadDir -File -Recurse)
$payloadBytes = ($payloadFiles | Measure-Object -Property Length -Sum).Sum
Write-Host ("Payload: {0} file(s), {1:0.0} MiB uncompressed" -f $payloadFiles.Count, ($payloadBytes / 1MB))

Write-Step ('Compressing the payload ({0})' -f $(if ($MaxCompression) { 'maximum' } else { 'balanced' }))
New-PayloadArchive -SourceDirectory $payloadDir -DestinationFile $archive -Maximum $MaxCompression.IsPresent

if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
    throw "Packing reported success but $archive is not there."
}
$archiveBytes = (Get-Item -LiteralPath $archive).Length
if ($archiveBytes -le 0) { throw "The payload archive is empty: $archive" }
Write-Host ("Archive: {0:0.0} MiB compressed ({1:0}% of {2:0.0} MiB)" -f `
    ($archiveBytes / 1MB), (100 * $archiveBytes / $payloadBytes), ($payloadBytes / 1MB))

# ---------------------------------------------------------------------------
# Pass 3 — the real setup
# ---------------------------------------------------------------------------

Write-Step 'Publishing setup.exe with the payload embedded'
Invoke-NativeCommand -FilePath $dotnet -Arguments @(
    'publish', $setupProject,
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-o', $setupDir,
    '-p:PublishSingleFile=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:PublishTrimmed=false', '-p:PublishReadyToRun=false',
    '-p:DebugType=none', '-p:DebugSymbols=false',
    "-p:Version=$Version", "-p:AssemblyVersion=$fileVersion",
    "-p:FileVersion=$fileVersion", "-p:InformationalVersion=$Version",
    "-p:LumenotepadPayload=$archive"
) -FailureMessage 'Publishing setup.exe failed.'

$builtSetup = Join-Path $setupDir 'Lumenotepad.Setup.exe'
if (-not (Test-Path -LiteralPath $builtSetup -PathType Leaf)) {
    throw "Expected $builtSetup after publishing setup.exe."
}

if (Test-Path -LiteralPath $setupPath) { Remove-Item -LiteralPath $setupPath -Force }
Copy-Item -LiteralPath $builtSetup -Destination $setupPath -Force

# ---------------------------------------------------------------------------
# Pass 4 (optional) — the launcher
# ---------------------------------------------------------------------------

# The same GUI as setup.exe, with no payload and the launcher flag set, so it fetches the published portable
# archive at install time instead of carrying one. It has to be published alongside the payload passes and
# never instead of them: the launcher installs whatever latest.json is currently offering, so the portable zip
# for this version has to be published before a launcher built alongside it can install this version.
if ($Launcher) {
    Write-Step 'Publishing the launcher (downloads instead of carrying a payload)'
    Invoke-NativeCommand -FilePath $dotnet -Arguments @(
        'publish', $setupProject,
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-o', $launcherDir,
        '-p:PublishSingleFile=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false', '-p:PublishReadyToRun=false',
        '-p:DebugType=none', '-p:DebugSymbols=false',
        "-p:Version=$Version", "-p:AssemblyVersion=$fileVersion",
        "-p:FileVersion=$fileVersion", "-p:InformationalVersion=$Version",
        '-p:LumenotepadLauncher=true'
    ) -FailureMessage 'Publishing the launcher failed.'

    $builtLauncher = Join-Path $launcherDir 'Lumenotepad.Setup.exe'
    if (-not (Test-Path -LiteralPath $builtLauncher -PathType Leaf)) {
        throw "Expected $builtLauncher after publishing the launcher."
    }

    # A launcher the size of the full setup means the payload leaked into it, and it would then install the
    # embedded copy rather than the published one. Cheap to check, silent and confusing if it ever happens.
    $launcherBytes = (Get-Item -LiteralPath $builtLauncher).Length
    if ($launcherBytes -ge $archiveBytes) {
        throw ("The launcher is {0:0.0} MiB, which is no smaller than the payload it is supposed to omit. It was built with a payload embedded." -f ($launcherBytes / 1MB))
    }

    if (Test-Path -LiteralPath $launcherPath) { Remove-Item -LiteralPath $launcherPath -Force }
    Copy-Item -LiteralPath $builtLauncher -Destination $launcherPath -Force
    Write-Host ("  Launcher: {0:0.0} MiB against {1:0.0} MiB for the full setup" -f `
        ($launcherBytes / 1MB), ((Get-Item -LiteralPath $setupPath).Length / 1MB))
}

# ---------------------------------------------------------------------------
# Hashes
# ---------------------------------------------------------------------------

Write-Step 'Writing SHA256 checksums'
$hashTargets = @($setupPath)
if ($Launcher -and (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    $hashTargets += $launcherPath
}
$hashLines = foreach ($target in $hashTargets) {
    $hash = Get-FileHash -LiteralPath $target -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $target)"
}
$hashLines | Set-Content -LiteralPath $hashPath -Encoding ASCII

$setupMiB = (Get-Item -LiteralPath $setupPath).Length / 1MB

Write-Host ''
Write-Host 'BUILD SUCCEEDED' -ForegroundColor Green
Write-Host ("  Setup:    $setupPath ({0:0.0} MiB)" -f $setupMiB)
if ($Launcher) { Write-Host "  Launcher: $launcherPath" }
Write-Host "  SHA256:   $hashPath"
Write-Host ''

if ($OpenOutputFolder) { Start-Process explorer.exe "/select,`"$setupPath`"" }
