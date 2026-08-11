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
# TWO packages: win-x86 and win-arm64 (user decision 2026-08-06). One command builds both.
#
#   package    | 64-bit x86 | 32-bit x86 | Win11 Arm64 | Win10 Arm64
#   -----------|------------|------------|-------------|-------------
#   win-x86    | WOW64      | native     | (see below) | (see below)
#   win-arm64  | -          | -          | native      | native
#
# Between them every target machine gets a package that runs NATIVELY or under WOW64, and
# no machine is left out.
#
# Why not one package. Until 2026-08-06 this shipped win-x86 alone, chosen (2026-08-04)
# because it reaches strictly more machines than win-x64: Win10 on Arm only ever emulated
# x86, so win-x64 silently loses both 32-bit machines and every Win10 Arm64 machine.
#
# That coverage argument still holds -- but it counted whether a machine could START the
# program, not what it looked like once running. P2-75: the win-x86 package renders at 100%
# scale under Arm64 emulation, so on a scaled display the whole UI comes out 20-33% too
# small (67% on a 150% display, which is what a 4K laptop ships with). Adding a native
# arm64 package fixes exactly the machines that were affected and nothing else.
# Dropping win-x86 was rejected: it is the only package 32-bit machines can run.
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
# two downloads a user can now pick the wrong one -- and for the x86 package on an Arm64
# machine the symptom (P2-75, an undersized UI) does not look like "wrong download" at all.
#
# The text lives in tools/package-notes/ rather than in here: it is user-facing prose that
# will be edited far more often than this script, and keeping it out means a wording change
# never risks a syntax error in the one supported way to ship. English only
# (user decision 2026-08-06), same rule as the READMEs (03-conventions 2.0).
#
# All text in this file is ASCII on purpose (03-conventions 10.5).

param(
    [string[]]$Rid = @('win-x86', 'win-arm64'),
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\diSerial.App\diSerial.App.csproj'
$devJson = Join-Path $root 'src\diSerial.App\diserial.dev.json'
$notesDir = Join-Path $PSScriptRoot 'package-notes'

# Third-party NATIVE symbol files. Skia alone is 84 MB and HarfBuzz 20 MB -- together
# they are bigger than the application. We never symbolicate into them.
#
# WARNING - do NOT extend this to *.pdb. The managed ones (DiSerial.*.pdb, ~200 KB total)
# must ship: without them stack traces lose line numbers, and 01-spec 4.7 clause 6 promises
# "exception records carry a full call stack". PathMap already strips the build machine's
# paths out of them (02-architecture 11.1.6), so shipping them leaks nothing.
$dropSymbols = @('libSkiaSharp.pdb', 'libHarfBuzzSharp.pdb')

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
    Write-Output "  Set it to false in src\diSerial.App\diserial.dev.json, publish, then set it back."
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
        throw "no compatibility note for '$r' at $note -- every package has to say which machines it is for, and with two downloads a user can pick the wrong one"
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

$outDir = Join-Path $root "src\diSerial.App\bin\Release\net10.0\$currentRid\publish"

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

# The compatibility note (user decision 2026-08-06). Two downloads exist now, so each one
# has to be able to say what it is -- a user who took the wrong one has no other way to
# find out, and for the x86 package on an Arm64 machine the symptom (P2-75, an undersized
# UI) does not look like "wrong download" at all.
#
# Named COMPATIBILITY.txt in the package rather than by RID: inside the package the RID is
# not the question, "is this the right one for my machine" is.
$noteSrc = Join-Path $notesDir "COMPATIBILITY.$currentRid.txt"
Copy-Item $noteSrc (Join-Path $outDir 'COMPATIBILITY.txt') -Force
Write-Output "copied COMPATIBILITY.txt into the package ($((Get-Item $noteSrc).Length) bytes)"

$files = Get-ChildItem $outDir -Recurse -File
$exe = Join-Path $outDir 'diSerial.exe'
$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)

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
