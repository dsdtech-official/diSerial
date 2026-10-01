# What diSerial does with your data

diSerial is attached to live industrial buses. Two questions decide whether it is safe to
put it there, and both have short answers you can verify yourself in this repository:

1. **Does it send anything anywhere?** No. It contains no networking code at all.
2. **Does it write to my bus?** Only after you explicitly enable sending, twice.

The rest of this page tells you how to check both without taking our word for it.

---

## 1. It has no network code

There is no HTTP client, no socket, no telemetry, no crash reporter, no update check.
Not disabled by a setting — **absent from the source**.

Check it yourself:

```bash
grep -rE --include='*.cs' --include='*.axaml' --exclude-dir=bin --exclude-dir=obj \
  "System\.Net|HttpClient|WebClient|Socket|TcpClient|UdpClient|WebRequest|Dns\." src/
```

That returns nothing.

> ⚠️ **Two exclusions in that command are load-bearing, and it is fairer to tell you why than to
> let you find them.** Drop them and you *will* get hits — none of them from code we wrote:
>
> - `obj/…/GlobalUsings.g.cs` contains `global using System.Net.Http;`. The .NET SDK adds that
>   line to every project it builds; it imports a namespace, and importing a namespace is not
>   using it. **No type from it appears anywhere in the source** — which is what the command
>   above shows.
> - `bin/` holds the compiled third-party libraries (Avalonia and friends). Those are binaries,
>   and some of them do contain networking code that this application never calls.
>
> If you would rather not take that on trust, the honest check is the runtime one below: block it
> at the firewall and watch. Source greps can be argued with; a process that never opens a socket
> cannot.

You can also confirm from the outside: the dependency list in
[THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES) contains no networking library, and the
published build is a single self-contained executable whose contents are exactly what
those dependencies produce.

If you want to be certain at runtime rather than in the source, run the released binary
behind a firewall that blocks it, or watch it with any connection monitor. It never opens
one.

---

## 2. It cannot write to your bus by accident

A serial monitor that transmits is dangerous: injecting bytes into a running line can
disturb equipment that is in production. diSerial treats sending in a monitor session as
something you must ask for, deliberately:

| Layer | What it does |
|---|---|
| **1. Disabled by default** | In a monitor session the send area is switched off and shows a warning bar; nothing you type can reach the line |
| **2. Explicit confirmation** | Enabling it opens a dialog that states the consequence — that data is really injected into the bus and may disturb running equipment — and requires you to confirm |
| **3. Stays visible** | Once enabled, the send area keeps an orange border for the rest of the session, so an armed window never looks like a passive one |
| **4. Injected frames are marked** | Anything diSerial itself sends is labelled `TX` in the timeline and is **not** counted as received traffic — a byte you injected can never be mistaken for a byte the bus produced |

The relevant code is in `src/diSerial.App/ViewModels/Panels/SendPanelViewModel.cs` and
`src/diSerial.App/ViewModels/Sessions/MonitorSessionViewModel.cs`.

A terminal session is different by design: it exists to transmit, so sending is available
without the extra gate.

---

## 3. Your bus traffic is not written to the log

diSerial keeps a diagnostic log. **The content of the frames it captures does not go into
it** unless three separate switches are all turned on:

```
logPayload: true      in diserial.dev.json
logLevel:   trace     in diserial.dev.json
debugMode:  true      in diserial.dev.json
```

The shipped values are `logPayload=false`, `logLevel=info` and `debugMode=false`.
Payload logging requires all three gates to be satisfied. Developer form is visible: when it is on, the window title permanently reads `⚠ DEVELOPER MODE — SIMULATED
DATA`, so a machine in that state is visible at a glance and in any screenshot.

The reason for the third gate is specific: serial payloads are **your** production data,
so a build in normal user form never records them, no matter what the other two switches
say.

Relevant code: `src/diSerial.Infrastructure/Diagnostics/LoggingOptions.cs`.

---

## 4. Where diSerial puts files, and nowhere else

Everything the application writes lives in **one** directory:

```
%AppData%\diSerial\        Windows
~/Library/Application Support/diSerial/    macOS
```

| What | Where, inside that directory |
|---|---|
| Settings | `settings.db` (a local SQLite file) |
| Logs | `logs\` |
| Recordings, the terminal send history, saved shortcuts, and auto-answer rules | `recordings.db` (a local SQLite file) |
| Exports | Only where you choose, when you ask to export |

There is nowhere else: the location is fixed in one class (`AppPaths`), the same code on both
platforms, and **the application reads no environment variables at all**, so nothing can redirect it.

Recordings stay on your machine. Nothing is uploaded, because there is nothing to upload
with (see section 1). Delete the folder and diSerial returns to a clean state.

**One thing in that file is worth stating plainly**, because it is the only content diSerial
stores without you asking: a **terminal** session remembers the commands you send, so you can
pick them again later. That list lives in the same database, each entry can be deleted from
the dropdown, and the dropdown also has a "clear all" that really empties the table.

**A monitor session does not do this.** What you send from a monitor session is an injection
into a live bus, so its history is kept **in memory only and is gone when you close diSerial**
— nothing about it is ever written to disk. This mirrors the rule in section 2 that enabling
sending never persists either: both the gate and the payloads reset every time you start.

Review a log before sending it to anyone: it records port names, session settings and byte
counts. It does not record frame contents unless all three gates above are open.

---

## 5. Reviewing the public source and released binaries

The public source includes [`tools/publish.ps1`](tools/publish.ps1), the packaging script.
You can build from this snapshot and compare behaviour. The v1.1.0 Windows downloads were
rebuilt from the cleaned application source and build inputs prepared for this public release.
The retained macOS download was built on a separate Mac from a different source revision;
it was not rebuilt from this public source snapshot. Some comments, diagnostic text and
developer notes differ from its build inputs. Build environment and signing can also change
binary bytes, so a local rebuild need not be byte-for-byte identical to either download.

```powershell
tools\publish.ps1                             # Windows: all three Windows packages
```

```bash
pwsh -File tools/publish.ps1 -Rid osx-arm64   # macOS
```

> On macOS the bundle you build is **unsigned** — signing and notarization need an Apple developer
> certificate and are not part of this script. The published macOS download is the signed one.

The startup banner records the full informational version. For v1.1.0, the Windows suffix
identifies the frozen source tree used for its builds; that tree ID is not a public commit ID.
The macOS suffix identifies a separate Mac build and does not establish source identity with
this repository. Use the release notes for the exact package hashes and build provenance, and
the public version tag for the released source snapshot. A local build can have a different
suffix; a source archive without Git history can produce a version without one.

> Note on scope: this repository contains public application source snapshots. The automated
> test suite and internal design documents are not published. For v1.1.0, the public snapshot
> differs from the retained macOS build inputs as described above.

---

## 6. How the claims above are kept true

Everything on this page describes behaviour that could be undone by a future change. Most of
it is therefore not a policy we remember — it is checked automatically, and the build fails
when a check does.

The test suite itself is not published (see the note in section 5), so here is what it
guards. The relevant checks include:

| What is guarded | Why it needs a machine, not a habit |
|---|---|
| **No hardcoded user-facing text** | A scanner reads the source and fails the build on literal UI strings, in `.cs` and in `.axaml`. Without it, a string typed straight into markup would silently escape translation and review |
| **No silently swallowed exceptions** | An empty `catch` is a defect that looks like working code. Every allowed exception is on a written allowlist, and a second check fails if an allowlist entry no longer matches anything |
| **Injected frames stay distinguishable** | The `TX` labelling and the "not counted as received" rule from section 2 each have their own cases. A byte you injected must never be able to read as a byte the bus produced |
| **The three logging gates from section 3** | Each gate is asserted separately, so removing any one of them turns the suite red rather than quietly widening what gets written to disk |
| **The developer-mode switch cannot ship enabled** | Checked in the test suite *and* by a build target that refuses to publish a package with it on |
| **The binary states its true identity** | The version, product and company recorded in the executable are asserted against the build's real values, so the startup banner cannot report a version that was never built |
| **Removed features stay removed** | Several checks exist only to fail if a deleted behaviour is reintroduced — the cheapest way to bring back a defect is to re-add the thing that caused it |

Two properties of these checks matter more than their number:

- **They fail the build, not a report.** A broken guarantee stops the release; it does not
  produce a warning somebody has to notice.
- ⭐ **The scanners check themselves.** Any check that works by reading source text can fail
  in the worst possible way — by reading nothing and passing. **Dedicated cases
  prove the scanners are not blind**: each plants a violation it must catch, so a scanner
  that has stopped seeing its input turns red instead of green.
- ⭐ **And one check watches those.** A scanner shipped with a weak self-check is the same
  hole one level up, so a further case requires **every** self-check to carry a marker saying
  it has been reviewed against the known ways they fail. It enforces *that someone looked*,
  not *that they judged correctly* — which is the part a machine can actually hold.

> Release notes record the official package versions and their relationship to the public
> source tag. The startup banner identifies the build installed on your machine (section 5).

---

## Reporting a problem

If you believe you have found a security problem, please contact the vendor directly
rather than opening a public issue:

**dsd_tech@outlook.com** — DongGuan DESHIDE TECHNOLOGY CO.,LTD, www.deshide.com

Please say which package and version you were running. The version string is in the startup banner
in the log, and also on the binary itself: `Properties` → `Details` on Windows, or
`DiSerialInformationalVersion` in `diSerial.app/Contents/Info.plist` on macOS.
