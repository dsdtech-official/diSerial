# publish.ps1 -- produce the Windows release build. THIS IS THE SUPPORTED WAY TO SHIP.
#
#   tools\publish.ps1            build it
#   tools\publish.ps1 -WhatIf    just report what would happen, touch nothing
#
# Running "dotnet publish" by hand gives a DIFFERENT artifact: framework-dependent,
# multi-file, and 100 MB heavier. The flags that matter are baked in here on purpose.
#
# Shape (user decision 2026-08-03): self-contained, single file, NO trimming.
# Trimming is off deliberately: ViewLocator resolves views by reflection (P2-3),
# CommunityToolkit uses source generators, and SQLitePCLRaw carries a native library --
# all three are exactly where trimming breaks things, and it breaks them ONLY in the
# published build. That is the worst possible place to find a defect before a release.
#
# THREE packages: win-x86, win-x64 and win-arm64. One command builds all of them.
#
#   package    | 64-bit x86 | 32-bit x86 | Win11 Arm64 | Win10 Arm64
#   -----------|------------|------------|-------------|-------------
#   win-x86    | WOW64      | native     | (see below) | (see below)
#   win-x64    | native     | -          | emulated    | -
#   win-arm64  | -          | -          | native      | native
#
# Every target machine gets a package that runs NATIVELY, except 32-bit x86 (which only
# win-x86 can serve, natively) -- no machine is left out.
#
# HISTORY, because the shape changed twice and each change had a different reason.
#
# Until 2026-08-06 this shipped win-x86 ALONE, chosen (2026-08-04) because it reaches
# strictly more machines than win-x64: Win10 on Arm only ever emulated x86, so win-x64
# silently loses both 32-bit machines and every Win10 Arm64 machine.
#
# That coverage argument still holds -- but it counted whether a machine could START the
# program, not what it looked like once running. P2-75: the win-x86 package renders at 100%
# scale under Arm64 emulation, so on a scaled display the whole UI comes out 20-33% too
# small (67% on a 150% display, which is what a 4K laptop ships with). Adding a native
# arm64 package (2026-08-06) fixes exactly the machines that were affected and nothing else.
# Dropping win-x86 was rejected: it is the only package 32-bit machines can run.
#
# win-x64 was added 2026-08-17 (user decision), and the reason is NOT that the two-package
# coverage argument broke -- it did not. It is a NEW distribution channel: the Microsoft
# Store picks the architecture for the customer, so "the user reads COMPATIBILITY.txt and
# picks" -- the whole basis of the 2026-08-06 shape -- does not exist there. On the Store a
# 64-bit x86 customer would silently receive the 32-bit package because it is the only one
# their machine can run. The user decided both channels ship the same three packages rather
# than letting the download channel and the Store diverge (06-public 2.8).
#
# NOTE the x86-on-x64 case is NOT the P2-75 defect: measured by the user 2026-08-06, the
# win-x86 package on a real x64 machine renders correctly. What win-x64 buys those machines
# is a native 64-bit process, not a fix for a visible bug.
#
# NOT a side effect, measured 2026-08-06 and worth stating because the opposite was
# assumed first: the native arm64 package STILL takes the Adreno software-rendering
# fallback (P1-21). That blocklist matches the Adreno GPU driver, not emulation, so going
# native does nothing for it. What changed is the scope of P1-21 -- it used to read as a
# dev-machine environment note, and it now reaches every Arm64 customer.
#
# The cost of win-x86 being a 32-bit process (2-4 GB address space) is irrelevant here --
# this tool is bounded by serial line rate, not by CPU or memory.
#
# WARNING: this does NOT lower the Windows floor. .NET 10 requires Windows 10 1607 or
# later regardless of architecture, so Windows 7 / 8 / 8.1 are out either way
# (user decision 2026-08-04: Windows 7 is not a target).
#
# Each package carries a COMPATIBILITY.txt saying which machines it is for, because with
# several downloads a user can now pick the wrong one -- and for the x86 package on an Arm64
# machine the symptom (P2-75, an undersized UI) does not look like "wrong download" at all.
#
# WARNING: that note is the download channel's safety net and the Microsoft Store has NO
# equivalent step -- nobody reads a text file the Store never shows them. On that channel the
# only thing standing between a customer and the wrong architecture is which packages we
# submit, which is why win-x64 exists (06-public 2.8).
#
# The text lives in tools/package-notes/ rather than in here: it is user-facing prose that
# will be edited far more often than this script, and keeping it out means a wording change
# never risks a syntax error in the one supported way to ship. English only
# (user decision 2026-08-06), same rule as the READMEs (03-conventions 2.0).
#
# All text in this file is ASCII on purpose (03-conventions 10.5).

param(
    [string[]]$Rid = @('win-x86', 'win-x64', 'win-arm64'),
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# Forward slashes, not backslashes (2026-08-14). Windows accepts '/' everywhere, and
# PowerShell on macOS does NOT translate '\' -- it is a legal filename character there, so
# 'src\diSerial.App\x.csproj' became a single file named "src\diSerial.App\x.csproj" that
# never existed. The failure is a plain "not found", which reads like a missing file rather
# than a malformed path.
$proj = Join-Path $root 'src/diSerial.App/diSerial.App.csproj'
$devJson = Join-Path $root 'src/diSerial.App/diserial.dev.json'
$notesDir = Join-Path $PSScriptRoot 'package-notes'

# Third-party NATIVE symbol files. Skia alone is 84 MB and HarfBuzz 20 MB -- together
# they are bigger than the application. We never symbolicate into them.
#
# WARNING - do NOT extend this to *.pdb. The managed ones (DiSerial.*.pdb, ~200 KB total)
# must ship: without them stack traces lose line numbers, and 01-spec 4.7 clause 6 promises
# "exception records carry a full call stack". PathMap already strips the build machine's
# paths out of them (02-architecture 11.1.6), so shipping them leaks nothing.
$dropSymbols = @('libSkiaSharp.pdb', 'libHarfBuzzSharp.pdb')

# ---- macOS support (E-4, 2026-08-14) -------------------------------------------------
#
# osx-* RIDs are NOT in the default set on purpose. The shipped form is the three Windows
# packages above and that is a spec-level statement, not a detail this script may widen on
# its own. Naming -Rid osx-arm64 opts in.
#
# What an osx RID needs that a win RID does not:
#
#   1. The apphost has no file extension: diSerial, not diSerial.exe.
#   2. The apphost is Mach-O and carries NO version resource, so
#      FileVersionInfo.GetVersionInfo returns a record of nulls -- not an error, a silently
#      empty answer, which would have made the "all packages agree on version" check below
#      pass on two empty strings. The managed assembly IS a PE file even when the target is
#      macOS, so the version is read from the build output's diSerial.dll instead.
#      Measured 2026-08-14: it reports 1.0.0+<git sha>, the same value Windows reports.
#   3. A bare executable is not an application on macOS: no Dock name, no Finder name, and
#      NSWorkspace cannot see it. It has to be wrapped in a .app bundle.
#   4. The bundle carries a launcher script, and that is P2-110 rather than packaging
#      taste -- see New-MacAppBundle.
#
# The icon (E-3) IS done here as of 2026-08-15: Contents/Resources/diserial.icns plus
# CFBundleIconFile. The .icns is committed rather than converted at publish time, because
# sips and iconutil are macOS-only and this script also runs on Windows.
#
# ⛔ This comment previously read "NOT done here ... the bundle gets the system's generic
# application icon". Left as a note of what the failure mode was: nothing errors when the
# icon is missing, the app just wears the generic icon -- which is how it stayed open.
function Test-IsMacRid {
    param([string]$Rid)
    return $Rid.StartsWith('osx-', [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-AppHostName {
    param([string]$Rid)
    if (Test-IsMacRid $Rid) { return 'diSerial' }
    return 'diSerial.exe'
}

# The version a package reports.
#
# Windows: the apphost .exe carries the version resource, and that is what shipped for
# months -- unchanged.
#
# macOS: the Mach-O apphost carries nothing, and FileVersionInfo answers with a record of
# NULLS rather than failing. So it is read from the managed assembly in the BUILD output
# (one level above publish/), which is a PE file on every target. Measured 2026-08-14:
# 1.0.0+<git sha>, byte for byte what Windows reports.
function Get-PackageVersionInfo {
    param([string]$Rid, [string]$PublishDir)

    if (Test-IsMacRid $Rid) {
        $managed = Join-Path (Split-Path $PublishDir -Parent) 'diSerial.dll'
        if (-not (Test-Path $managed)) {
            throw "cannot read the version for ${Rid}: expected the managed assembly at $managed. The Mach-O apphost carries no version resource, so there is no fallback -- and an empty version would pass the agreement check below on two blanks."
        }
        return [System.Diagnostics.FileVersionInfo]::GetVersionInfo($managed)
    }

    return [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        (Join-Path $PublishDir (Get-AppHostName $Rid)))
}

# Wrap the published payload in a .app bundle (E-4).
#
# SHAPE. Everything the program needs moves into Contents/MacOS; the licence texts and
# COMPATIBILITY.txt stay BESIDE the bundle, because a recipient has to be able to read them
# without knowing about "Show Package Contents".
#
# THE LAUNCHER IS NOT PACKAGING TASTE -- it is 00-STATUS P2-110.
# On macOS .NET derives its culture from CFLocale, and that mapping DROPS the script
# subtag: a Mac set to zh-Hans_HK (Simplified) arrives in the process as zh-HK, whose
# default script is Traditional, so a Simplified user reads the whole UI in Traditional.
# Measured 2026-08-14, and measured again the other way: when LANG IS set, .NET parses it
# and the subtag survives (LANG=zh_Hans_HK.UTF-8 -> CurrentUICulture zh-Hans-HK).
#
# So the launcher hands .NET the value the OS actually holds. Three things about it are
# deliberate:
#
#   * AppleLocale, not AppleLanguages[0]. AppleLocale is the very setting .NET's macOS path
#     is trying to read; feeding it back REPAIRS that mapping rather than substituting a
#     different preference. (AppleLanguages[0] is the better answer to "which UI language",
#     and if the two ever disagree on a real machine this is the line to revisit.)
#   * An existing LANG is never overwritten. Someone who exported LANG meant it.
#   * A failure here is silent and harmless: no AppleLocale, no export, and the app behaves
#     exactly as it did before this script existed.
#
# WARNING: the bundle identifier below is a placeholder derived from the company domain in the
# product's own About text. It has to match whatever the Developer ID certificate is issued
# against BEFORE anything is signed -- confirm it, do not inherit it.
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

    # The real apphost steps aside so the launcher can own the bundle's executable name.
    $realHost = Join-Path $macOsDir 'diSerial'
    if (-not (Test-Path $realHost)) { throw "no apphost at $realHost -- refusing to build a bundle around nothing" }
    Move-Item -LiteralPath $realHost -Destination (Join-Path $macOsDir 'diSerial-bin')

    $launcher = @(
        '#!/bin/sh'
        '# Give .NET the locale macOS actually holds (00-STATUS P2-110).'
        '# Without this a Simplified Chinese Mac reads the whole UI in Traditional.'
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
        '    <!-- CONFIRMED by the user 2026-08-15, after being carried as "to be decided"'
        '         because signing was thought to require a match against the certificate.'
        '         It does not, for Developer ID: codesign accepts any bundle id, and only the'
        '         Mac App Store / certain entitlements need one registered in the portal.'
        '         What does bite is that this string is effectively PERMANENT once shipped --'
        '         change it and macOS treats the result as a different app: preferences gone,'
        '         TCC grants re-prompted, and the user Gatekeeper approval reset. -->'
        '    <key>CFBundleIdentifier</key><string>com.deshide.diserial</string>'
        '    <key>CFBundlePackageType</key><string>APPL</string>'
        "    <key>CFBundleShortVersionString</key><string>$ShortVersion</string>"
        "    <key>CFBundleVersion</key><string>$ShortVersion</string>"
        '    <key>CFBundleIconFile</key><string>diserial</string>'
        '    <key>NSHighResolutionCapable</key><true/>'
        '    <!-- MEASURED, not guessed (2026-08-15). otool -l on the published apphost reports'
        '         LC_BUILD_VERSION minos 12.0, and the package is a single-file publish, so that'
        '         one binary is the whole native surface -- nothing inside raises the floor.'
        '         Below 12.0 dyld refuses to load it, so this is the honest lower bound.'
        '         WITHOUT this key macOS does not block launch at all: an old system gets a'
        '         vague failure instead of "requires macOS 12.0 or later".'
        '         NOT verified by running on a real macOS 12 -- that needs a machine nobody'
        '         here has. The claim is "matches what the binary declares", not "tested". -->'
        '    <key>LSMinimumSystemVersion</key><string>12.0</string>'
        '    <!-- Custom key. CFBundleShortVersionString must be numeric, so the +<git sha>'
        '         has nowhere to live in the standard keys; without this the bundle cannot'
        '         say WHICH build it is without being launched (P1-16). verify-publish.ps1'
        '         reports this value. -->'
        "    <key>DiSerialInformationalVersion</key><string>$FullVersion</string>"
        '</dict>'
        '</plist>'
    ) -join "`n"

    [System.IO.File]::WriteAllText((Join-Path $contents 'Info.plist'), $plist + "`n",
        (New-Object System.Text.UTF8Encoding($false)))

    # The icon (E-3). Committed as .icns beside the .ico rather than converted here: the
    # conversion needs sips + iconutil, which exist only on macOS, and publish.ps1 has to run
    # on Windows too. The .ico is committed for the same reason on that side.
    #
    # Contents/Resources is where CFBundleIconFile is resolved from, and the key carries the
    # base name with no extension.
    $iconSource = Join-Path $root 'src/diSerial.App/Assets/diserial.icns'
    if (-not (Test-Path $iconSource)) { throw "no icon at $iconSource -- refusing to build a bundle that silently falls back to the generic system icon (E-3)" }

    $resourcesDir = Join-Path $contents 'Resources'
    New-Item -ItemType Directory -Path $resourcesDir -Force | Out-Null
    Copy-Item -LiteralPath $iconSource -Destination (Join-Path $resourcesDir 'diserial.icns')

    # Self-check, same reason as the launcher's below: a missing icon does not fail anything,
    # it just quietly shows the generic app icon -- which is exactly how E-3 stayed open
    # without anyone noticing.
    $iconLanded = Join-Path $resourcesDir 'diserial.icns'
    if (-not (Test-Path $iconLanded)) { throw "icon did not land at $iconLanded" }
    if ((Get-Item $iconLanded).Length -lt 1024) { throw "icon at $iconLanded is suspiciously small ($((Get-Item $iconLanded).Length) bytes)" }

    # Read the launcher back and confirm it is executable. An unreadable or non-executable
    # launcher produces an app that simply does not open, and macOS reports that as a
    # generic "the application cannot be opened" with nothing pointing here.
    if (-not (Test-Path $launcherPath)) { throw "launcher missing after write: $launcherPath" }
    $mode = (& stat -f '%Lp' $launcherPath)
    if ($mode -notmatch '[1357]$') { throw "launcher at $launcherPath is not executable (mode $mode)" }

    return $appDir
}

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

# The csproj gate checks debugMode ONLY. logLevel is a separate knob (03-conventions 8.5:
# volume and form are orthogonal), so a debug-volume build used to pass the gate silently.
#
# Since 2026-08-03 (user decision, P2-47 clause 1) this script no longer merely warns: the
# copy of diserial.dev.json that goes INTO the package is rewritten to 'info' further down.
# The source file is never touched, so daily development keeps the debug volume the user
# asked for -- logLevel: debug is what puts ReadChunk (bytes per read + GapMs) in the log,
# and that is the observation point for framing.
if ($logLevel -ne 'info') {
    Write-Output "NOTE: the source file says logLevel = '$logLevel'."
    Write-Output "  The package will ship 'info' -- the published copy is rewritten below."
    Write-Output "  The source file is NOT modified."
    Write-Output ""
}

# Fail fast, BEFORE spending minutes publishing: every file the packages must carry has to
# exist now. Discovering a missing LICENSE after two 100 MB publishes wastes the whole run,
# and the licence obligation is not something to leave to the end.
$licenseFiles = @('LICENSE', 'NOTICE', 'THIRD-PARTY-NOTICES')
foreach ($name in $licenseFiles) {
    if (-not (Test-Path (Join-Path $root $name))) {
        throw "$name is missing from the repository root -- refusing to ship without it"
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
    Write-Output "    -p:IncludeNativeLibrariesForSelfExtract=true"
    Write-Output "  then delete: $($dropSymbols -join ', ')"
    Write-Output "  then write diserial.dev.json into the package with logLevel = info"
    Write-Output "    (source stays '$logLevel'; the source file is never modified)"
    Write-Output "  then copy LICENSE, NOTICE, THIRD-PARTY-NOTICES and COMPATIBILITY.txt in"
    exit 0
}

$summary = @()

foreach ($currentRid in $Rid) {

$outDir = Join-Path $root "src/diSerial.App/bin/Release/net10.0/$currentRid/publish"

Write-Output ""
Write-Output "=== publishing $currentRid ==="
& dotnet publish $proj -c Release -r $currentRid --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true

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

# Ship 'info' volume regardless of what the source file says (user decision 2026-08-03).
#
# Why this script may flip THIS knob while it flatly refuses to flip debugMode: the two
# protect different things. debugMode decides whether the user can be shown SIMULATED data
# -- shipping that by accident is the worst error a measuring tool can make, so the csproj
# gate deliberately makes it a manual act. logLevel is only volume: no payload can reach
# the log without three gates. Leaving volume to human memory had no gate at all, and this
# project's own rule is that a discipline nothing enforces does not hold.
#
# WARNING -- written FROM SOURCE rather than edited in place, on purpose:
# CopyToPublishDirectory="PreserveNewest" would skip re-copying a file this script made
# newer than the source, so a later edit to any OTHER key could silently miss the package.
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

# Read it back. A check that cannot fail is not a check -- the same reason the ASCII
# self-check once reported a green it had not earned (03-conventions 10.3).
$shippedText = [System.IO.File]::ReadAllText($shippedDevJson)
if ($shippedText -notmatch '"logLevel"\s*:\s*"info"') {
    throw "wrote $shippedDevJson but it does not read back as logLevel: info"
}
if ($shippedText -notmatch '"debugMode"\s*:\s*false') {
    throw "wrote $shippedDevJson but debugMode is not false in it"
}
Write-Output "wrote diserial.dev.json into the package: logLevel = info, debugMode = false"
Write-Output "  (source file untouched -- it still says logLevel = '$logLevel')"

# Ship the licence texts next to the executable.
#
# This is not decoration. Apache-2.0 section 4(a) requires giving recipients of the
# Work a copy of the License, and the bundled MIT components require their copyright
# notices to travel with the binary. The repository copies do not satisfy that -- the
# obligation is on the side that hands someone an executable.
#
# NOTICE ships for a second reason: section 4(d) makes anyone who redistributes a
# derivative work reproduce its attribution notices. That only bites if the NOTICE file
# is actually part of what we distribute.
foreach ($name in $licenseFiles) {
    $src = Join-Path $root $name
    Copy-Item $src (Join-Path $outDir $name) -Force
    Write-Output "copied $name into the package ($((Get-Item $src).Length) bytes)"
}

# The compatibility note (user decision 2026-08-06). Several downloads exist now, so each
# one has to be able to say what it is -- a user who took the wrong one has no other way to
# find out, and for the x86 package on an Arm64 machine the symptom (P2-75, an undersized
# UI) does not look like "wrong download" at all.
#
# Named COMPATIBILITY.txt in the package rather than by RID: inside the package the RID is
# not the question, "is this the right one for my machine" is.
$noteSrc = Join-Path $notesDir "COMPATIBILITY.$currentRid.txt"
Copy-Item $noteSrc (Join-Path $outDir 'COMPATIBILITY.txt') -Force
Write-Output "copied COMPATIBILITY.txt into the package ($((Get-Item $noteSrc).Length) bytes)"

# Read the version BEFORE the bundle step: on macOS it comes from the build output, and
# building the bundle moves the publish payload around underneath us.
$info = Get-PackageVersionInfo -Rid $currentRid -PublishDir $outDir

if (Test-IsMacRid $currentRid) {
    # CFBundleShortVersionString has to be numeric-only, so the +<git sha> is dropped here
    # and here alone. The full value still travels: it is compiled into the assembly and is
    # what the startup banner prints (P1-16).
    $shortVersion = ($info.ProductVersion -split '\+')[0]
    $bundle = New-MacAppBundle -PublishDir $outDir -ShortVersion $shortVersion `
                               -FullVersion $info.ProductVersion
    Write-Output ""
    Write-Output "built $([System.IO.Path]::GetFileName($bundle)) (CFBundleShortVersionString = $shortVersion)"
    Write-Output "  the launcher inside it exports LANG from AppleLocale -- that is P2-110, not packaging"
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

}   # foreach rid

Write-Output ""
Write-Output "=== all packages ==="
$summary | Format-Table -AutoSize | Out-String | Write-Output

# Every package must carry the same version, and it must carry a +<git sha>. Two packages
# built from one run that disagree would mean the working tree moved underneath the loop.
$versions = $summary.Version | Sort-Object -Unique
if ($versions.Count -ne 1) {
    throw "packages disagree on version: $($versions -join ', ')"
}
Write-Output "Version must carry a +<git sha>. A bare 1.0.0.0 means the banner is back to"
Write-Output "being unable to identify the build (P1-16)."
