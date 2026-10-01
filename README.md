# diSerial

> **A free, open-source serial terminal and two-port bus monitor.** Send and receive data through one serial port, or observe both directions of a conversation through two ports on a single merged timeline. View HEX and ASCII data and save local recordings for later export. No account or subscription required.
>
> The companion application for DSD TECH serial hardware, also compatible with other serial ports exposed by your operating system. Two-port monitoring requires suitable monitoring hardware and connections. Use it for serial communication testing, PLC troubleshooting, UART and embedded development.

| | |
|---|---|
| **Stack** | .NET 10 / Avalonia 12.1.0 / CommunityToolkit.Mvvm 8.4.2 / Microsoft.Extensions.DependencyInjection |
| **UI languages** | Nine: English, Simplified Chinese, Traditional Chinese, Japanese, French, German, Spanish, Italian and Portuguese. **The first launch follows the operating system's UI language** (a Chinese system starts in Chinese, a Japanese one in Japanese; anything else falls back to English), and the `Language` menu switches **live**, no restart. Once you pick one it is remembered and the OS no longer has a say.<br>Two regional notes, both deliberate: Portuguese is **European**, not Brazilian, and Traditional Chinese uses **Taiwanese** terminology |
| **Platform** | **Standalone downloads:** Windows 10 version 1607 or later and macOS 12 or later on Apple Silicon. **Four builds:** `win-x86` (32-bit), `win-x64` (64-bit Intel/AMD), `win-arm64` (Windows on Arm) and `osx-arm64`. Each package carries a `COMPATIBILITY.txt`. **Microsoft Store:** Windows 10 version 1809 or later; Windows selects the architecture. No Intel Mac or Linux package is published. macOS 12 is the declared minimum; it has not been tested on that version |
| **License** | Apache-2.0 — see [LICENSE](LICENSE), [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES) (Windows) and [THIRD-PARTY-NOTICES.macos](THIRD-PARTY-NOTICES.macos) (macOS) |

## Download diSerial

| Option | Download |
|---|---|
| Windows — Microsoft Store | [Get diSerial from Microsoft Store](https://apps.microsoft.com/detail/9PLJCLPGNZCW) |
| macOS — Mac App Store (Apple Silicon) | [Get diSerial from Mac App Store](https://apps.apple.com/app/id6802570917) |
| Windows or macOS — standalone ZIP | [Download v1.1.0](https://github.com/dsdtech-official/diSerial/releases/tag/v1.1.0) · [Latest release](https://github.com/dsdtech-official/diSerial/releases/latest) |

For a standalone download, open **Assets** on the release page and choose the ZIP for your
machine. Package choices, installation notes and SHA-256 verification are below.

> **About this repository:** it holds public source snapshots for released versions of diSerial.
> The v1.1.0 Windows downloads were rebuilt from the cleaned application source and build
> inputs prepared for this public release. The retained macOS download was built on a separate
> Mac from a different source revision; it was not rebuilt from this public source snapshot.
> Some comments, diagnostic text and developer notes therefore differ from its build inputs.
> Day-to-day development happens in a separate repository, so you will not find in-progress
> work or intermediate history here.

> 🔍 **Attaching this to a live bus?** [SECURITY.md](SECURITY.md) answers the two questions
> that matter — whether it sends anything anywhere (it has no network code at all) and
> whether it can write to your bus — and shows you how to verify both yourself.

---

## Features

### Serial terminal

Connect to one available serial port and view sent and received data with timestamps and byte
counts. Choose from standard baud rates and serial framing options. Use send history, timed
sending, DTR/RTS controls and serial signal status.

### Two-port monitor

With suitable serial monitoring hardware and connections, capture both directions of a
conversation through two ports. View them on one timeline, coloured by channel, with the time
gap between frames.

Sending is disabled by default. Manual sending requires explicit confirmation and can affect
connected devices. **Timed sending, shortcut buttons and auto-answer rules are unavailable in
monitor sessions.**

### Shortcut buttons

Save frequently used commands as named buttons and send them with one click. Saved shortcuts
remain available after restarting the app. **Terminal sessions only.**

### Auto-answer rules

Send a predefined reply automatically when a received frame contains a specified byte sequence.
Use this to test a device or simulate its responses. **Terminal sessions only; all rules start
disabled each time you launch the app.** If more than one enabled rule matches, only the first
replies. Matching is per received frame, so the receive framing setting can affect a trigger.

### Recording and export

Record serial data to a local database and export to TSV or CSV. Manage past recordings in one
list: filter by date, export a recording again, or select multiple recordings for deletion.
The list shows port names and aliases to help identify each recording.

**Optional automatic reconnect** retries an unexpected disconnect with a configurable retry count
and interval. It is off by default. After successful reconnection, an active recording continues
in the same batch. The original port must be available under the same name; a renamed port is
not automatically substituted.

Recordings, settings and logs stay on your computer, and export files go to the location you
choose. The application has no network code, telemetry, analytics, crash uploads or built-in
update checks.

## Standalone package details

There are **four** package types, all self-contained — there is no .NET runtime to install:

| Package | For |
|---|---|
| `diSerial-<version>-win-x64.zip` | 64-bit Windows on Intel or AMD |
| `diSerial-<version>-win-x86.zip` | 32-bit Windows |
| `diSerial-<version>-win-arm64.zip` | Windows on Arm |
| `diSerial-<version>-osx-arm64.zip` | macOS on Apple Silicon |

Unpack the package, then run `diSerial.exe` (Windows) or open `diSerial.app` (macOS). Every package also
carries `COMPATIBILITY.txt` (which machines that build is for), `LICENSE`, `NOTICE` and
`THIRD-PARTY-NOTICES` — **keep them with the executable if you pass the package on**; the licence
requires it.

In the macOS download, those four text files sit **beside** `diSerial.app`, outside the bundle.
Moving the app to `/Applications` does not move the text files. Keep them, and include them if
you redistribute the app. The packaged `THIRD-PARTY-NOTICES` comes from this repository's
`THIRD-PARTY-NOTICES.macos`; the Windows package uses `THIRD-PARTY-NOTICES`.

The Mac App Store edition uses a separate sandbox container for recordings, settings and logs.
It does not copy data from the standalone download.

**Every release publishes the SHA-256 of every package.** Check what you downloaded:

```powershell
(Get-FileHash .\diSerial-<version>-win-x64.zip -Algorithm SHA256).Hash   # Windows
```

```bash
shasum -a 256 diSerial-<version>-osx-arm64.zip                           # macOS
```

You can also check the other direction: the release notes quote the exact version string the
application reports, so compare it against the binary — right-click `diSerial.exe` →
`Properties` → `Details` on Windows, or read `DiSerialInformationalVersion` in
`diSerial.app/Contents/Info.plist` on macOS. That string identifies the package's internal build;
the v1.1.0 Windows suffix identifies the frozen source tree used for those builds, not a public
commit. The macOS suffix identifies its separate Mac build. Neither suffix is a ZIP checksum;
see the release notes for build provenance and the SHA-256 of each download.

> ⚠️ **Windows downloads: the executables are not code-signed**, so SmartScreen may show a warning
> ("Windows protected your PC"). Getting past it is `More info` → `Run anyway`. An unsigned
> download is exactly the case where the checksum above is worth the ten seconds.
>
> ✅ **macOS: the app is signed and notarized**, so it opens normally — no warning and no
> right-click-Open dance. You can confirm that yourself before running it:
>
> ```bash
> spctl -a -vvv -t exec diSerial.app     # expect: accepted / source=Notarized Developer ID
> ```
>
> Drag `diSerial.app` into `/Applications` before opening it. Running it straight from
> `~/Downloads` also works, but macOS then launches it from a randomized read-only path
> (App Translocation), which makes the app's own file paths in logs harder to read.

## Quick start

```bash
dotnet build diSerial.sln
```

```bash
dotnet run --project src/diSerial.App
```

Build on Windows or Apple Silicon macOS with the **.NET 10 SDK**. No specific IDE is required.

You can also open the solution in your preferred C# editor.

The DI container runs with `ValidateOnBuild` and `ValidateScopes` — a bad service registration **throws at startup**, not later.

> ⚠️ **Close any running instance before building.** A running `DiSerial.App` locks
> `DiSerial.Infrastructure.dll` and the build fails with `MSB3027`:
>
> ```powershell
> Get-Process diSerial -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit() }
> ```

### Building a release

⛔ **Do not run `dotnet publish` by hand** — that produces a *different* artifact: framework-dependent,
multi-file, and about 100 MB larger. The supported way is:

```powershell
pwsh -NoProfile -File tools/publish.ps1 -WhatIf   # report the planned build
pwsh -NoProfile -File tools/publish.ps1           # build it
```

The release script requires **PowerShell 7 or later**; Windows PowerShell 5.1 is not supported.

It produces **three** self-contained, single-file Windows builds — `win-x86`, `win-x64` and
`win-arm64` — with trimming **off**, drops the two third-party native symbol files
(`libSkiaSharp.pdb`, `libHarfBuzzSharp.pdb` — together about 100 MB, larger than the application),
keeps the managed PDBs so log stack traces retain line numbers, writes `diserial.dev.json` into each
package with `logLevel` set to `info`, and copies in the licence texts plus a `COMPATIBILITY.txt`
naming the machines that package is for.

Windows packaging normalizes the application's debug paths after XAML compilation, before
creating the single-file executable. It checks the managed PDB documents and executable
for this build machine's repository and user-directory paths while preserving source line
numbers. This does not rewrite the bundled third-party libraries.

The macOS build is not in the default set; ask for it explicitly:

```bash
pwsh -NoProfile -File tools/publish.ps1 -Rid osx-arm64
```

For macOS packaging, install Microsoft's official .NET 10 SDK at `/usr/local/share/dotnet`.
The script selects and checks that SDK explicitly; it refuses Homebrew's .NET SDK to avoid
introducing dependencies on Homebrew libraries into the download.

That produces a `diSerial.app` bundle. **Signing and notarization are not part of this script** —
they need an Apple developer certificate, so the bundle you build yourself is unsigned. That is
expected: the published macOS download is the signed one, and the point of this script is that you
can build from this public source and compare its behaviour with the release, subject to the
macOS source differences noted above.

---

## Verifying without serial hardware

> ⚠️ **On a machine with no real serial ports, the port dropdown in "New session…" is empty and "Connect" is disabled. That is expected, not a defect.**
> The port list comes from `IPortEnumerator`, which enumerates ports the operating system actually has.

For a source build, set `"replay": "on"` in `src/diSerial.App/diserial.dev.json` and launch with
developer mode enabled for that run:

```bash
dotnet run --project src/diSerial.App -- --dev --log-level=debug
```

Leave the checked-in `debugMode: false` and `logLevel: info` values unchanged. Replay requires
both developer mode and a replay setting other than `off`:

| Path | How to enable | Ports it adds | Pipeline it uses |
|---|---|---|---|
| **Replay ports** | Developer mode (`--dev` for a source build, or `"debugMode": true` in the file) **and** `"replay": "on"` | `REPLAY-MODBUS` / `REPLAY-AT` / `REPLAY-BURST` / `REPLAY-FAULTS` | The **real capture pipeline** (`ISerialPort → IFrameSplitter → SerialFrame`); scripts loop by default — except `REPLAY-FAULTS`, which ends on a scripted fatal error and must not restart |

`replay` also accepts `coalesced` (simulates driver-side frame batching) and `fragmented`
(one frame split across several chunks).
The replay port names identify test data sets; they do not imply protocol decoding support.

> ⚠️ **Replay is gated by `debugMode`** — when it is `false`, the value of `replay` is ignored entirely.
> Replay ports run the real capture pipeline, so the frames they produce are indistinguishable from
> real hardware in the logs. Without the gate, "the user form never produces simulated data" would be
> an empty promise.
>
> If you set it and nothing happens, check the log — it says so explicitly:
> `replay=coalesced (ignored in user form)`.

> **One easily-missed consequence**: with `debugMode: true` but `replay: "off"`
> (this file's default), the port list contains **no fake ports at all**.

### Development-time configuration (`diserial.dev.json`)

**This file is version-controlled and ships with the package**, in both Debug and Release.
Both builds read it through the same code path.
There is no `#if DEBUG` anywhere in the project.

Configuration precedence is **command line → `diserial.dev.json` → built-in defaults**.
The command line overrides only developer mode (`--dev`) and log level (`--log-level=debug`).
Replay, payload logging and diagnostic framing overrides remain in the file. **`--dev` is ignored
in packaged builds**; `--log-level` works there too. Avalonia DevTools are not included.

> ⚠️ **The block below shows the shape of the file and what each key accepts — it is not what
> your copy says.** In a **released package** `debugMode` is `false` and `logLevel` is `info`
> (the publish gate requires user mode and `tools/publish.ps1` sets the package's log level), so nothing here is
> switched on unless you turn it on yourself. The **source tree** also keeps `debugMode: false`
> and `logLevel: info`; use command-line overrides for development runs.

```jsonc
{
  "schemaVersion": 2,
  "debugMode": false,               // false = user form, true = developer form
  "logLevel": "info",               // off|error|warning|info|debug|trace
  "logPayload": false,              // payload hex; requires debugMode AND logLevel=trace
  "replay": "off",                  // off|on|coalesced|fragmented (gated by form)
  "replayWindowMs": 0,              // batching window, only meaningful for coalesced
  "idleGapMs": null                 // null=follow Options, 0=one row per chunk, positive=override
}
```

> ⚠️ `0` means **opposite things** for `idleGapMs` and `replayWindowMs`: for the former it means
> "one row per chunk" (i.e. no framing), for the latter it means "use the built-in default".
> `idleGapMs: null` leaves the user's Options setting in control. A fresh install uses a fixed
> **10 ms**, with automatic selection off. A non-negative diagnostic override takes precedence.

You can tell it is on because the window title shows **`⚠ DEVELOPER MODE — SIMULATED DATA`**.
If the title did not change, check the log — it says `Running in USER FORM: no simulated data`.

> ⚠️ **Set `debugMode` back to `false` before publishing.**
> Forgetting is fine: `dotnet publish` is blocked by the csproj target
> `CheckDeveloperSwitchesBeforePublish`, which fails the build — shipping a release with simulated
> ports is the most damaging class of mistake for a measurement tool.
> **It blocks publish only, never build**, since day-to-day development is meant to run with it on.

### Full manual verification path

1. Launch the app → empty state is shown. **The language follows the operating system on a
   machine that has never had one chosen** — English system, English UI; Chinese system,
   Chinese UI; Japanese system, Japanese UI. (Delete `settings.db` from the configuration directory
   below to get back to that state.)
2. Menu `Language → 简体中文` → **the whole UI switches to Chinese immediately, no restart**;
   close and reopen and the choice is remembered
3. `File → New session` → click "Serial monitor" → select two distinct ports for the two directions.
   On macOS, prefilling prefers adapter ports over built-in Bluetooth and debug-console ports;
   confirm both choices before connecting. Without real ports, enable developer mode and replay
   as described above, then select two `REPLAY-*` ports
   - Good moment to also check port hot-plug: plug or unplug a port **while the dialog is open** and
     the dropdown follows along
4. Click "Connect" → the merged timeline starts scrolling: each channel in its own colour, and
   lines you sent yourself on a tinted background so they read apart from received traffic
5. The send area at the bottom shows a disabled warning → click "Enable sending" → an injection risk
   confirmation appears

> ⚠️ **One class of problem only human eyes can judge**: whether wording misleads, whether a warning
> is actually scary enough, whether the layout looks bad.
> Several of the defects fixed so far were found exactly this way and no automated test caught any
> of them (a typical one: a `ControlTheme` missing `BasedOn` made dropdown items invisible, while
> keyboard navigation still worked — so the tests happily "passed").

For standalone downloads and source builds, user settings are stored in `settings.db` in the
configuration directory:

```
%AppData%\diSerial\        Windows
~/Library/Application Support/diSerial/    macOS
```

One directory holds everything the application writes: `settings.db`, `recordings.db` and the `logs`
folder. Delete `settings.db` to return to all defaults, or the whole directory for a clean state.
These are the standalone paths; store installations can redirect them into an application
container. The Mac App Store edition uses its own sandbox container as noted above.

---

## Why one message sometimes shows as two lines

Serial ports carry bytes rather than message boundaries. diSerial groups received bytes into
frames using the idle gap observed between reads. **A fresh install uses a fixed 10 ms gap;
automatic selection is off.**

Change the gap under **Tools → Options → Reception**; the setting applies the next time you
connect. Increase it if one device message is split across rows. A larger gap can also combine
two separate messages into one row. You can optionally enable automatic selection, which derives
the threshold from the serial port settings.

USB adapters and drivers deliver data in chunks on their own schedule. They can split one
message across reads or combine several messages into one read. No idle-gap value can recover
a boundary the driver has already hidden. The framing setting changes how bytes are grouped;
it does not decode a protocol or add Modbus-specific support.

Recordings retain the received bytes. Auto-answer rules match each received frame separately,
so a trigger sequence split across frames will not match.

---

## Logging

Logging starts on its own, no configuration needed. Files sit next to the settings database, in a
`logs` subdirectory of the configuration directory shown above:

```
diserial-<date>.log      human-readable
diserial-<date>.jsonl    machine-readable (one event per line)
```

To turn up the volume for one run, pass a command-line override:

```powershell
.\diSerial.exe --log-level=debug
```

For a source build, use `dotnet run --project src/diSerial.App -- --log-level=debug`.
You can also set `logLevel` in `diserial.dev.json`:

```jsonc
"logLevel": "debug"   // off | error | warning | info (default) | debug | trace
```

**Volume and form are orthogonal**: investigating on site often calls for "user form + debug volume",
and neither constrains the other.
An invalid file value falls back to `info`, **never to `off`** — "nothing was recorded because the
config had a typo" is the worst possible failure mode.
An invalid command-line log level is reported and ignored, leaving the file or default in control.

Diagnostic payload hex logging requires **three gates**, all enabled: developer mode,
`"logLevel": "trace"` and `"logPayload": true`. Developer mode alone does not enable it.
This concerns diagnostic logs; a recording you explicitly start still stores serial data.

**The log directory is not configurable**; it is fixed next to the settings file.
Developer switches are configured through the file and the two command-line overrides described
above, rather than environment variables.

⚠️ Review log files yourself before sharing them — they can contain information about your bus.

---

## Code layout

```
diSerial.sln
Directory.Build.props          compile properties and NuGet versions shared solution-wide
│
└── src/
    ├── diSerial.Core             domain layer — models + service contracts
    ├── diSerial.Infrastructure   infrastructure layer — concrete implementations of Core interfaces
    └── diSerial.App              presentation layer — Avalonia + MVVM
```

**Dependencies flow strictly one way**: `App → Infrastructure → Core`.

This repository contains the application source and public build resources.

---

## Adding a translation

1. Copy `src/diSerial.App/Resources/Strings.resx` to `Strings.<culture>.resx`
2. Translate the `<value>` entries, preserving their formatting placeholders
3. Add one line to `LocalizationService.AvailableLanguages`, **writing the language name in that
   language itself**
4. Done — menu entries are driven by the collection, so **no XAML changes are needed**, and MSBuild
   picks the file up by name, so **no project file changes either**

Check these two requirements before using a translation:

- **Key parity** — every key in `Strings.resx` must exist in yours, and yours must add none.
  A missing key does not crash: .NET falls back to English for that key alone, so the UI shows
  one English line and nothing is logged
- **Placeholder parity** — the set of `{0}`, `{1}`… in each value must match the English one.
  Dropping one silently loses that value; inventing one throws at format time

Five things to know before starting:

- **Menus carry no mnemonics** in any language — do not add `_` or `(&X)` accelerators
- **Placeholders such as `{0}` must survive translation** — the set of them, not the order:
  reordering to suit the target language's grammar is expected and fine
- **Some strings are deliberately not translated**: `ASCII`, `HEX`, `TX` / `RX`, the serial
  signal names (`RTS / CTS`, `XON / XOFF`, `CR` / `LF`), and the product name `diDatatracker`
- **Length affects layout** — the send area wraps rather than clips, so a much longer translation
  silently becomes a second row
- ⚠️ **XML comments (`<!-- … -->`) in a `.resx` cannot contain two consecutive hyphens** — an XML
  rule, not a project one; the build fails with `MSB3103`. Use an em dash. Text inside
  `<comment>` elements is unaffected

---

## The three conventions easiest to get wrong

1. **No user-visible hardcoded text in code** — use localized resources
2. **Device identification must not depend on VID/PID** — a chip change would make the software stop
   recognizing the hardware
3. **Always format numbers with `InvariantCulture`** — German locales render `4.1` as `4,1`, which
   breaks CSV export outright

---

## How this software was built

This software was written with substantial AI assistance.

DSD TECH is responsible for maintaining the software and its published behavior.

We say this because we would rather you heard it from us. It does not change what you can check
for yourself — see [SECURITY.md](SECURITY.md).

---

## License

Apache License 2.0 — see [LICENSE](LICENSE) and the attribution notices in [NOTICE](NOTICE).

Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES)
for Windows downloads and [THIRD-PARTY-NOTICES.macos](THIRD-PARTY-NOTICES.macos) for macOS downloads.
Each download ships the appropriate list under the common name `THIRD-PARTY-NOTICES`.

`diSerial` and `diDatatracker` are product names of DongGuan DESHIDE TECHNOLOGY CO.,LTD. As
section 6 of the license states, it does not grant permission to use them — a fork must use a
different name.
