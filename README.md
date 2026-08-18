# diSerial

> **Free and open-source serial port tool.** Two kinds of session: a **serial terminal** for talking to one device, and **dual-port passive monitoring** — both directions of a conversation between two devices, merged onto one timeline.
>
> Written as the official companion software for the DSD TECH diDatatracker, and not tied to it: the terminal works with any serial port, monitoring with any two.

| | |
|---|---|
| **Stack** | .NET 10 / Avalonia 12.1.0 / CommunityToolkit.Mvvm 8.4.2 / Microsoft.Extensions.DependencyInjection |
| **UI languages** | Nine: English, Simplified Chinese, Traditional Chinese, Japanese, French, German, Spanish, Italian and Portuguese. **The first launch follows the operating system's UI language** (a Chinese system starts in Chinese, a Japanese one in Japanese; anything else falls back to English), and the `Language` menu switches **live**, no restart. Once you pick one it is remembered and the OS no longer has a say.<br>Two regional notes, both deliberate: Portuguese is **European**, not Brazilian, and Traditional Chinese uses **Taiwanese** terminology |
| **Platform** | **Windows** 10 version 1607 or later (7 / 8 / 8.1 are **not** supported) and **macOS 12 or later on Apple Silicon**. **Four builds**: `win-x86` (32-bit), `win-x64` (64-bit Intel/AMD), `win-arm64` (Windows on Arm) and `osx-arm64` — each package carries a `COMPATIBILITY.txt` saying which one to take. There is no Intel Mac build and no Linux build |
| **License** | Apache-2.0 — see [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES) |

> **About this repository:** it holds the source of **released** versions of diSerial. Each release is
> published here as the code that produced that release's binary, so you can check the two against
> each other. Day-to-day development happens in a separate repository, so you will not find
> in-progress work or intermediate history here.

> 🔍 **Attaching this to a live bus?** [SECURITY.md](SECURITY.md) answers the two questions
> that matter — whether it sends anything anywhere (it has no network code at all) and
> whether it can write to your bus — and shows you how to verify both yourself.

---

## Downloads

Each release ships **four** packages, all self-contained — there is no .NET runtime to install:

| Package | For |
|---|---|
| `diSerial-<version>-win-x64.zip` | 64-bit Windows on Intel or AMD |
| `diSerial-<version>-win-x86.zip` | 32-bit Windows |
| `diSerial-<version>-win-arm64.zip` | Windows on Arm |
| `diSerial-<version>-osx-arm64.zip` | macOS on Apple Silicon |

Unzip anywhere, then run `diSerial.exe` (Windows) or open `diSerial.app` (macOS). Every package also
carries `COMPATIBILITY.txt` (which machines that build is for), `LICENSE`, `NOTICE` and
`THIRD-PARTY-NOTICES` — **keep them with the executable if you pass the package on**; the licence
requires it.

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
`diSerial.app/Contents/Info.plist` on macOS. That string is what ties the binary to the source
published here.

> ⚠️ **Windows: the executables are not code-signed**, so SmartScreen will show a warning
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

All you need is the **.NET 10 SDK** — no IDE, on any platform it supports.

If you would rather read the code in an editor, **VS Code with the C# extension**
(`ms-dotnettools.csharp`) opens this solution and debugs it as-is. C# Dev Kit is **not**
required — the .NET debugger ships in the C# extension itself. That matters if you are at a
company: Dev Kit's licence excludes organisations over 250 users or 1M USD revenue from using
it on their own applications, and this repository is deliberately laid out so you never need
it to check what the software does.

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
tools\publish.ps1 -WhatIf   # report what it would do, change nothing
tools\publish.ps1           # build it
```

It produces **three** self-contained, single-file Windows builds — `win-x86`, `win-x64` and
`win-arm64` — with trimming **off**, drops the two third-party native symbol files
(`libSkiaSharp.pdb`, `libHarfBuzzSharp.pdb` — together about 100 MB, larger than the application),
keeps the managed PDBs so log stack traces retain line numbers, writes `diserial.dev.json` into each
package with `logLevel` set to `info`, and copies in the licence texts plus a `COMPATIBILITY.txt`
naming the machines that package is for.

The macOS build is not in the default set; ask for it explicitly:

```bash
pwsh -File tools/publish.ps1 -Rid osx-arm64
```

That produces a `diSerial.app` bundle. **Signing and notarization are not part of this script** —
they need an Apple developer certificate, so the bundle you build yourself is unsigned. That is
expected: the published macOS download is the signed one, and the point of this script is that you
can build the same application from this source and compare its behaviour.

---

## Verifying without serial hardware

> ⚠️ **On a machine with no real serial ports, the port dropdown in "New session…" is empty and "Connect" is disabled. That is expected, not a defect.**
> The port list comes from `IPortEnumerator`, which enumerates ports the operating system actually has.

**Everything here is driven by `src/diSerial.App/diserial.dev.json`.** There is exactly one path:

| Path | How to enable | Ports it adds | Pipeline it uses |
|---|---|---|---|
| **Replay ports** | `"debugMode": true` **and** `"replay": "on"` — **both switches required** | `REPLAY-MODBUS` / `REPLAY-AT` / `REPLAY-BURST` / `REPLAY-FAULTS` | The **real capture pipeline** (`ISerialPort → IFrameSplitter → SerialFrame`); scripts loop by default — except `REPLAY-FAULTS`, which ends on a scripted fatal error and must not restart |

`replay` also accepts `coalesced` (simulates driver-side frame batching) and `fragmented`
(one frame split across several chunks).

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

**This file is version-controlled and ships with the package**, in both Debug and Release — both builds
read the same file through the same code path, and differ only in the values inside it.
There is no `#if DEBUG` anywhere in the project.

It is the **single source** of development-time configuration: form selection, log level and payload
switch, replay, and DevTools all live here.

> ⚠️ **The block below shows the shape of the file and what each key accepts — it is not what
> your copy says.** In a **released package** `debugMode` is `false` and `logLevel` is `info`
> (`tools\publish.ps1` writes them, and the build refuses to publish otherwise), so nothing here is
> switched on unless you turn it on yourself. In the **source tree** the checked-in values are the
> ones the developers work with day to day, and they change.

```jsonc
{
  "debugMode": true,  // form: false = user form, true = developer form
  "logLevel": "info",               // off|error|warning|info|debug|trace
  "logPayload": false,              // payload hex; also requires logLevel=trace (two gates)
  "replay": "off",                  // off|on|coalesced|fragmented (gated by form)
  "replayWindowMs": 0,              // batching window, only meaningful for coalesced
  "idleGapMs": null                 // C-07 framing threshold override: null=auto, 0=one row per chunk, positive=override
}
```

> ⚠️ `0` means **opposite things** for `idleGapMs` and `replayWindowMs`: for the former it means
> "one row per chunk" (i.e. no framing), for the latter it means "use the built-in default".
> Full explanations for both live in the comments inside `diserial.dev.json`.

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
3. `File → New session` → click "Serial monitor" → both channels are pre-filled with the first two
   ports in the list
   (without real serial ports this requires `debugMode` **and** `replay` both on, in which case the
   pre-filled values are two `REPLAY-*` ports)
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

User settings are stored in `settings.db` in the configuration directory:

```
%AppData%\diSerial\        Windows
~/Library/Application Support/diSerial/    macOS
```

One directory holds everything the application writes: `settings.db`, `recordings.db` and the `logs`
folder. Delete `settings.db` to return to all defaults, or the whole directory for a clean state.

---

## Why one message sometimes shows as two lines

diSerial has no way to know where one message ends and the next begins — a serial line carries
bytes, not messages. It infers the boundary from **idle time**: a gap longer than the threshold
for the current port settings ends the line. The threshold is derived from the Modbus RTU 3.5
character time (3.646 ms at 9600 8N1) and is not user-adjustable.

**The limit is your USB-to-serial driver, not the threshold.** Drivers report received bytes on
their own schedule, and that schedule puts a floor under the shortest gap diSerial can possibly
observe. On an FTDI adapter the default Latency Timer measured **16 ms** — more than four times
the 9600 8N1 threshold. So a pause *inside* one message can look like the end of it.

Measured on a real diDatatracker (Prolific, `usbser.sys`): a single message was split across two
lines in **0.7–5%** of cases with traffic in one direction, rising to roughly **14%** with both
devices talking. Timestamps and byte counts stay correct — only the line break is in the wrong
place, and a recording (which stores raw bytes) is unaffected.

> **Worth trying if it bothers you, but unverified:** FTDI adapters expose *Latency Timer*
> under Device Manager → the port → Port Settings → Advanced, adjustable from 1 to 255 ms.
> Lowering it should lower the floor. **We have not measured this**, so treat it as a lead
> rather than a fix.

There is no threshold you can set that solves this in general: once the driver hands over two
messages in a single read — with the boundary falling mid-message, which we have observed —
no timing rule can separate them. Doing so requires understanding the protocol, which is
planned for a later version (Modbus decoding).

---

## Logging

Logging starts on its own, no configuration needed. Files sit next to the settings database, in a
`logs` subdirectory of the configuration directory shown above:

```
diserial-<date>.log      human-readable
diserial-<date>.jsonl    machine-readable (one event per line)
```

To turn up the volume while investigating, change `logLevel` in `diserial.dev.json`:

```jsonc
"logLevel": "debug"   // off | error | warning | info (default) | debug | trace
```

**Volume and form are orthogonal**: investigating on site often calls for "user form + debug volume",
and neither constrains the other.
An invalid value always falls back to `info`, **never to `off`** — "nothing was recorded because the
config had a typo" is the worst possible failure mode.

Recording payload hex requires **two gates**: `"logLevel": "trace"` **and** `"logPayload": true`.
Serial payloads are the customer's live bus data, so **the developer form does not open these gates
automatically**.

**The log directory is not configurable**; it is fixed next to the settings file.
**The project reads no environment variables at all**; configuration lives only in `diserial.dev.json`.

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

**Dependencies flow strictly one way**: `App → Infrastructure → Core`, and in the full
engineering repository that rule is asserted by reflection in a test that fails the build.

### ⚠️ What this repository does and does not contain

It contains the **application source of released versions** — what the shipped binary is
built from. It is not the whole engineering repository:

| Not published | Why |
|---|---|
| The automated test suite | It is our regression and review apparatus rather than part of the product |
| Internal design documents | Design rationale, measurement records and working conventions |

Two consequences worth stating plainly rather than letting you discover them:

- Some code comments reference those documents by section number (for example `01-spec 4.8`
  or `03-conventions 8.5`). **Those references are left exactly as they are in the source
  that produced the release**, rather than rewritten for this repository — the code here is
  the same code, not a cleaned-up variant of it.
- Claims in comments about tests ("a violation fails the build") refer to that suite. You
  cannot run it from this repository; you can still read every line the product is made of.

---

## Adding a translation

1. Copy `src/diSerial.App/Resources/Strings.resx` to `Strings.<culture>.resx`
2. Translate the `<value>` entries; each resource's `<comment>` explains what the placeholders mean
3. Add one line to `LocalizationService.AvailableLanguages`, **writing the language name in that
   language itself**
4. Done — menu entries are driven by the collection, so **no XAML changes are needed**, and MSBuild
   picks the file up by name, so **no project file changes either**

Two build-time guards check the result, so a partial or drifted translation fails rather than
degrading quietly:

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

1. **No user-visible hardcoded text in code** — a test scans the source and fails the build
2. **Device identification must not depend on VID/PID** — a chip change would make the software stop
   recognizing the hardware
3. **Always format numbers with `InvariantCulture`** — German locales render `4.1` as `4,1`, which
   breaks CSV export outright

---

## How this software was built

This software was written with substantial AI assistance.

DSD TECH is responsible for what it does and for whether it is correct. Our team defined the
product requirements, decided every open question and parameter value, and set the acceptance
criteria. The behaviour was verified on real diDatatracker hardware and on a virtual-port test
bench, not only by unit tests.

We say this because we would rather you heard it from us. It does not change what you can check
for yourself — see [SECURITY.md](SECURITY.md).

---

## License

Apache License 2.0 — see [LICENSE](LICENSE) and the attribution notices in [NOTICE](NOTICE).

Third-party components bundled into the released executable, and their licenses, are listed in
[THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES).

`diSerial` and `diDatatracker` are product names of DongGuan DESHIDE TECHNOLOGY CO.,LTD. As
section 6 of the license states, it does not grant permission to use them — a fork must use a
different name.
