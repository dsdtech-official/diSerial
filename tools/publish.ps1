#requires -Version 7.0

param(
    [string[]]$Rid = @('win-x86', 'win-x64', 'win-arm64'),
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$proj = Join-Path $root 'src/diSerial.App/diSerial.App.csproj'
$devJson = Join-Path $root 'src/diSerial.App/diserial.dev.json'
$notesDir = Join-Path $PSScriptRoot 'package-notes'

$dropSymbols = @('libSkiaSharp.pdb', 'libHarfBuzzSharp.pdb')

# Check Windows debug paths.
function Assert-WindowsDebugPaths {
    param([string]$PublishDir, [string]$RepositoryRoot)

    $pdbs = @(Get-ChildItem -LiteralPath $PublishDir -Filter '*.pdb' -File)
    if ($pdbs.Count -ne 3) { throw "expected three managed PDBs in $PublishDir" }
    foreach ($pdb in $pdbs) {
        $stream = [System.IO.File]::OpenRead($pdb.FullName)
        $provider = $null
        try {
            $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
            $reader = $provider.GetMetadataReader()
            if ($reader.Documents.Count -eq 0) { throw "no source documents in $($pdb.Name)" }
            foreach ($handle in $reader.Documents) {
                $name = $reader.GetString($reader.GetDocument($handle).Name).Replace('\', '/')
                if (-not $name.StartsWith('/_/', [StringComparison]::Ordinal)) {
                    throw "unmapped source document in $($pdb.Name): $name"
                }
            }
        }
        finally {
            if ($null -ne $provider) { $provider.Dispose() }
            $stream.Dispose()
        }
    }

    $exe = Join-Path $PublishDir 'diSerial.exe'
    $text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($exe))
    $rootPath = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
    foreach ($path in @($rootPath, $rootPath.Replace('\', '/'))) {
        if ($text.IndexOf($path, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw 'the Windows executable still contains the build repository path'
        }
    }
    $homePath = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile).TrimEnd('\', '/')
    if ([string]::IsNullOrWhiteSpace($homePath)) { throw 'cannot determine the build user home directory' }
    foreach ($path in @($homePath, $homePath.Replace('\', '/'))) {
        if ($text.IndexOf($path, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw 'the Windows executable contains the build user home path'
        }
    }
    Write-Output 'debug path checks passed: all managed PDB documents mapped; no private build paths in executable'
}

# Detect a macOS target.
function Test-IsMacRid {
    param([string]$Rid)
    return $Rid.StartsWith('osx-', [System.StringComparison]::OrdinalIgnoreCase)
}

$OfficialDotnetRoot = '/usr/local/share/dotnet'

# Require the official .NET SDK.
function Assert-OfficialDotnetSdk {
    $muxer = Join-Path $OfficialDotnetRoot 'dotnet'
    if (-not (Test-Path $muxer)) {
        throw "the official .NET SDK is not at $OfficialDotnetRoot. A macOS package published with Homebrew's dotnet links Homebrew's brotli and does not start on other Macs -- install Microsoft's SDK there; do not fall back."
    }
    $sep = [System.IO.Path]::PathSeparator
    $rest = @($env:PATH -split [regex]::Escape($sep) | Where-Object { $_ -and $_ -ne $OfficialDotnetRoot })
    $env:PATH = (@($OfficialDotnetRoot) + $rest) -join $sep

    $resolved = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
    $line = (& dotnet --info) | Select-String -Pattern '^\s*Base Path:\s*(.+)$' | Select-Object -First 1
    $basePath = if ($line) { $line.Matches[0].Groups[1].Value.Trim() } else { '' }
    $official = $basePath.StartsWith($OfficialDotnetRoot + '/sdk/', [System.StringComparison]::Ordinal)
    if (-not $official -or $basePath -match 'homebrew|/Cellar/') {
        throw "refusing to publish a macOS package with the SDK at '$basePath' (dotnet resolved to '$resolved'). Only the official SDK under $OfficialDotnetRoot is allowed."
    }
    Write-Output "publishing with the official .NET SDK -- $basePath (dotnet = $resolved)"
}

# Detect a Windows target.
function Test-IsWindowsRid {
    param([string]$Rid)
    return $Rid.StartsWith('win-', [System.StringComparison]::OrdinalIgnoreCase)
}

# Choose the application host.
function Get-AppHostName {
    param([string]$Rid)
    if (Test-IsWindowsRid $Rid) { return 'diSerial.exe' }
    return 'diSerial'
}

# Read package version details.
function Get-PackageVersionInfo {
    param([string]$Rid, [string]$PublishDir)

    if (-not (Test-IsWindowsRid $Rid)) {
        $managed = Join-Path (Split-Path $PublishDir -Parent) 'diSerial.dll'
        if (-not (Test-Path $managed)) {
            throw "cannot read the version for ${Rid}: expected the managed assembly at $managed. A Mach-O or ELF apphost carries no version resource, so there is no fallback -- and an empty version would pass the agreement check below on two blanks."
        }
        return [System.Diagnostics.FileVersionInfo]::GetVersionInfo($managed)
    }

    return [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        (Join-Path $PublishDir (Get-AppHostName $Rid)))
}

# Create a macOS app bundle.
function New-MacAppBundle {
    param([string]$PublishDir, [string]$ShortVersion, [string]$FullVersion)

    $keepBeside = @('LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES', 'COMPATIBILITY.txt')

    $appDir = Join-Path $PublishDir 'diSerial.app'
    $contents = Join-Path $appDir 'Contents'
    $macOsDir = Join-Path $contents 'MacOS'

    if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
    New-Item -ItemType Directory -Path $macOsDir -Force | Out-Null

    foreach ($item in Get-ChildItem $PublishDir -Force) {
        if ($item.Name -eq 'diSerial.app') { continue }
        if ($keepBeside -contains $item.Name) { continue }
        Move-Item -LiteralPath $item.FullName -Destination (Join-Path $macOsDir $item.Name)
    }

    $realHost = Join-Path $macOsDir 'diSerial'
    if (-not (Test-Path $realHost)) { throw "no apphost at $realHost -- refusing to build a bundle around nothing" }
    Move-Item -LiteralPath $realHost -Destination (Join-Path $macOsDir 'diSerial-bin')

    $launcher = @(
        '#!/bin/sh'
        'if [ -z "$LANG" ]; then'
        '    _loc=$(defaults read -g AppleLocale 2>/dev/null)'
        '    if [ -n "$_loc" ]; then'
        '        LANG="$(printf ''%s'' "$_loc" | tr ''-'' ''_'').UTF-8"'
        '        export LANG'
        '    fi'
        'fi'
        'exec "$(dirname "$0")/diSerial-bin" "$@"'
    ) -join "`n"

    $launcherPath = Join-Path $macOsDir 'diSerial'
    [System.IO.File]::WriteAllText($launcherPath, $launcher + "`n",
        (New-Object System.Text.UTF8Encoding($false)))
    & chmod '+x' $launcherPath
    if ($LASTEXITCODE -ne 0) { throw "chmod +x failed for $launcherPath" }

    $plist = @(
        '<?xml version="1.0" encoding="UTF-8"?>'
        '<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">'
        '<plist version="1.0">'
        '<dict>'
        '    <key>CFBundleName</key><string>diSerial</string>'
        '    <key>CFBundleDisplayName</key><string>diSerial</string>'
        '    <key>CFBundleExecutable</key><string>diSerial</string>'
        '    <key>CFBundleIdentifier</key><string>com.deshide.diserial</string>'
        '    <key>CFBundlePackageType</key><string>APPL</string>'
        "    <key>CFBundleShortVersionString</key><string>$ShortVersion</string>"
        "    <key>CFBundleVersion</key><string>$ShortVersion</string>"
        '    <key>CFBundleIconFile</key><string>diserial</string>'
        '    <key>NSHighResolutionCapable</key><true/>'
        '    <key>LSMinimumSystemVersion</key><string>12.0</string>'
        "    <key>DiSerialInformationalVersion</key><string>$FullVersion</string>"
        '</dict>'
        '</plist>'
    ) -join "`n"

    [System.IO.File]::WriteAllText((Join-Path $contents 'Info.plist'), $plist + "`n",
        (New-Object System.Text.UTF8Encoding($false)))

    $iconSource = Join-Path $root 'src/diSerial.App/Assets/diserial.icns'
    if (-not (Test-Path $iconSource)) { throw "no icon at $iconSource -- refusing to build a bundle that silently falls back to the generic system icon (E-3)" }

    $resourcesDir = Join-Path $contents 'Resources'
    New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null
    Copy-Item -LiteralPath $iconSource -Destination (Join-Path $resourcesDir 'diserial.icns')

    $iconLanded = Join-Path $resourcesDir 'diserial.icns'
    if (-not (Test-Path $iconLanded)) { throw "icon did not land at $iconLanded" }
    if ((Get-Item $iconLanded).Length -lt 1024) { throw "icon at $iconLanded is suspiciously small ($((Get-Item $iconLanded).Length) bytes)" }

    if (-not (Test-Path $launcherPath)) { throw "launcher missing after write: $launcherPath" }
    $mode = (& stat -f '%Lp' $launcherPath)
    if ($mode -notmatch '[1357]$') { throw "launcher at $launcherPath is not executable (mode $mode)" }

    return $appDir
}

# Read a developer option.
function Read-DevSwitch {
    param([string]$Name)
    $text = [System.IO.File]::ReadAllText($devJson)
    $packed = [System.Text.RegularExpressions.Regex]::Replace($text, '\s', '')
    if ($packed -match "`"$Name`":`"?([A-Za-z0-9]+)`"?") { return $Matches[1] }
    return '<not found>'
}

$debugMode = Read-DevSwitch 'debugMode'
$logLevel = Read-DevSwitch 'logLevel'
$replay = Read-DevSwitch 'replay'

Write-Output "diserial.dev.json as it stands:"
Write-Output "  debugMode = $debugMode"
Write-Output "  logLevel  = $logLevel"
Write-Output "  replay    = $replay"
Write-Output ""

if ($debugMode -ne 'false') {
    Write-Output "REFUSING: debugMode is '$debugMode'."
    Write-Output "  Set it to false in src/diSerial.App/diserial.dev.json, publish, then set it back."
    Write-Output "  This script does NOT flip it for you: the csproj gate"
    Write-Output "  (CheckDeveloperSwitchesBeforePublish) exists so that shipping developer form"
    Write-Output "  has to be a deliberate act. Automating the flip would defeat it."
    exit 1
}

if ($logLevel -ne 'info') {
    Write-Output "NOTE: the source file says logLevel = '$logLevel'."
    Write-Output "  The package will ship 'info' -- the published copy is rewritten below."
    Write-Output "  The source file is NOT modified."
    Write-Output ""
}

$licenseFiles = @('LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES')
foreach ($name in $licenseFiles) {
    if (-not (Test-Path (Join-Path $root $name))) {
        throw "$name is missing from the repository root -- refusing to ship without it"
    }
}

# Locate dependency notices.
function Get-NoticesSource {
    param([string]$ForRid)
    if (Test-IsMacRid $ForRid) { return Join-Path $root 'THIRD-PARTY-NOTICES.macos' }
    return Join-Path $root 'THIRD-PARTY-NOTICES'
}
foreach ($r in $Rid) {
    $noticesSrc = Get-NoticesSource $r
    if (-not (Test-Path $noticesSrc)) {
        throw "$(Split-Path $noticesSrc -Leaf) is missing from the repository root -- the '$r' package needs it and would otherwise ship a notice describing another platform's runtime"
    }
}
foreach ($r in $Rid) {
    $note = Join-Path $notesDir "COMPATIBILITY.$r.txt"
    if (-not (Test-Path $note)) {
        throw "no compatibility note for '$r' at $note -- every package has to say which machines it is for, and with several downloads a user can pick the wrong one"
    }
}

if ($WhatIf) {
    Write-Output ""
    Write-Output "-WhatIf: for each of $($Rid -join ', ') would run"
    Write-Output "  dotnet publish -c Release -r <rid> --self-contained true \"
    Write-Output "    -p:PublishSingleFile=true -p:PublishTrimmed=false \"
    Write-Output "    -p:IncludeNativeLibrariesForSelfExtract=true \"
    Write-Output "    -p:DiSerialPackagedBuild=true"
    Write-Output "  then delete: $($dropSymbols -join ', ')"
    Write-Output "  then write diserial.dev.json into the package with logLevel = info"
    Write-Output "    (source stays '$logLevel'; the source file is never modified)"
    Write-Output "  then copy LICENSE, NOTICE, THIRD-PARTY-NOTICES and COMPATIBILITY.txt in"
    exit 0
}

if ($IsMacOS -and @($Rid | Where-Object { Test-IsMacRid $_ }).Count -gt 0) {
    Assert-OfficialDotnetSdk
}

$summary = @()

foreach ($currentRid in $Rid) {

$outDir = Join-Path $root "src/diSerial.App/bin/Release/net10.0/$currentRid/publish"

Write-Output ""
Write-Output "=== publishing $currentRid ==="
& dotnet publish $proj -c Release -r $currentRid --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DiSerialPackagedBuild=true

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $currentRid with exit code $LASTEXITCODE" }
if (-not (Test-Path $outDir)) { throw "publish reported success but $outDir does not exist" }

Write-Output ""
foreach ($name in $dropSymbols) {
    $p = Join-Path $outDir $name
    if (Test-Path $p) {
        $mb = [math]::Round((Get-Item $p).Length / 1MB, 1)
        Remove-Item $p -Force
        Write-Output "dropped $name ($mb MB of third-party native symbols)"
    }
}

$devJsonRegex = [regex]'("logLevel"\s*:\s*")[^"]*(")'
$srcText = [System.IO.File]::ReadAllText($devJson)
if (-not $devJsonRegex.IsMatch($srcText)) {
    throw "no logLevel key found in $devJson -- refusing to guess what to ship"
}
$shippedDevJson = Join-Path $outDir 'diserial.dev.json'
[System.IO.File]::WriteAllText(
    $shippedDevJson,
    $devJsonRegex.Replace($srcText, '${1}info${2}', 1),
    (New-Object System.Text.UTF8Encoding($false)))

$shippedText = [System.IO.File]::ReadAllText($shippedDevJson)
if ($shippedText -notmatch '"logLevel"\s*:\s*"info"') {
    throw "wrote $shippedDevJson but it does not read back as logLevel: info"
}
if ($shippedText -notmatch '"debugMode"\s*:\s*false') {
    throw "wrote $shippedDevJson but debugMode is not false in it"
}
Write-Output "wrote diserial.dev.json into the package: logLevel = info, debugMode = false"
Write-Output "  (source file untouched -- it still says logLevel = '$logLevel')"

foreach ($name in $licenseFiles) {
    $src = Join-Path $root $name
    if ($name -eq 'THIRD-PARTY-NOTICES') { $src = Get-NoticesSource $currentRid }
    Copy-Item $src (Join-Path $outDir $name) -Force
    Write-Output "copied $name into the package from $(Split-Path $src -Leaf) ($((Get-Item $src).Length) bytes)"
}

$noteSrc = Join-Path $notesDir "COMPATIBILITY.$currentRid.txt"
Copy-Item $noteSrc (Join-Path $outDir 'COMPATIBILITY.txt') -Force
Write-Output "copied COMPATIBILITY.txt into the package ($((Get-Item $noteSrc).Length) bytes)"

$info = Get-PackageVersionInfo -Rid $currentRid -PublishDir $outDir

if ($currentRid.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase)) {
    Assert-WindowsDebugPaths -PublishDir $outDir -RepositoryRoot $root
}

if (Test-IsMacRid $currentRid) {
    $shortVersion = ($info.ProductVersion -split '\+')[0]
    $bundle = New-MacAppBundle -PublishDir $outDir -ShortVersion $shortVersion `
                               -FullVersion $info.ProductVersion
    Write-Output ""
    Write-Output "built $([System.IO.Path]::GetFileName($bundle)) (CFBundleShortVersionString = $shortVersion)"
    Write-Output "  the launcher inside it exports LANG from AppleLocale"
}

$files = Get-ChildItem $outDir -Recurse -File

Write-Output ""
Write-Output "output: $outDir"
Get-ChildItem $outDir | Sort-Object Length -Descending |
    Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } |
    Format-Table -AutoSize | Out-String | Write-Output

$totalMb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 2)
"{0} files, {1} MB total" -f $files.Count, $totalMb | Write-Output
Write-Output ""
Write-Output "Product   : $($info.ProductName)"
Write-Output "Company   : $($info.CompanyName)"
Write-Output "Copyright : $($info.LegalCopyright)"
Write-Output "Version   : $($info.ProductVersion)"

$summary += [pscustomobject]@{
    Rid     = $currentRid
    Files   = $files.Count
    MB      = $totalMb
    Version = $info.ProductVersion
    Output  = $outDir
}

}

Write-Output ""
Write-Output "=== all packages ==="
$summary | Format-Table -AutoSize | Out-String | Write-Output

$versions = $summary.Version | Sort-Object -Unique
if ($versions.Count -ne 1) {
    throw "packages disagree on version: $($versions -join ', ')"
}
Write-Output "Version must carry a +<git sha>. A bare 1.0.0.0 means the banner is back to"
Write-Output "being unable to identify the build."
