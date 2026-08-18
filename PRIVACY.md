# Privacy Policy

**diSerial** — DongGuan DESHIDE TECHNOLOGY CO.,LTD
Last updated: 2026-08-18

## The short version

diSerial collects nothing, sends nothing, and shares nothing.

It makes no network connections at all. Everything it records stays on the computer it runs on,
in files you can open, copy, or delete yourself.

## What the app stores, and where

diSerial writes only inside its own application data directory:

| Platform | Directory |
|---|---|
| macOS (Mac App Store) | `~/Library/Containers/com.deshide.diserial/Data/Library/Application Support/diSerial` |
| macOS (downloaded from our website) | `~/Library/Application Support/diSerial` |
| Windows | `%APPDATA%\diSerial` |

What lives there:

| File | What it holds |
|---|---|
| `recordings.db` | The serial traffic you chose to record, and nothing else |
| `settings.db`, `settings.json` | Your own preferences: window layout, port aliases, interface language |
| `diserial-*.log` | Diagnostic logs, written locally so you can send them to us **if you choose to** |

Files you export go wherever you tell the export dialog to put them. The app never moves them
anywhere else.

## Network access: none

There is no HTTP client, no socket, no telemetry, no analytics, no crash reporter, and no
update check anywhere in the product. This is not a policy promise on top of a program that
could do those things — the code to do them is not present.

## Serial ports

The app opens the serial ports you select, for the sole purpose of exchanging data with the
devices you connect. Port names and device identifiers are used on your machine to show you
which port is which. They are never transmitted.

## The Mac App Store version keeps its own data

The Mac App Store version runs inside Apple's App Sandbox, which gives it a private container
of its own. It cannot see recordings or settings written by the version downloaded from our
website, and that version cannot see its. The two installations are independent, and nothing
is copied between them. If you have been using the downloaded version and want to keep those
recordings, keep using that version, or export what you need before switching.

## Children

diSerial is a developer tool. It is not directed at children, and it collects no data from
anyone, of any age.

## Deleting your data

Deleting the application data directory listed above removes everything the app has stored.
Uninstalling the Mac App Store version removes its container with it.

## Changes to this policy

If this policy changes, the revised version will be published at this address with a new date
at the top.

## Contact

**dsd_tech@outlook.com** — DongGuan DESHIDE TECHNOLOGY CO.,LTD, www.deshide.com
