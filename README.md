LDWin
=====

## Link Discovery Client for Windows — Windows 11 Compatible

LDWin tells you **which switch and switch port a network cable is plugged into**, by
listening for the link-discovery announcements (CDP / LLDP) that the directly-connected
switch broadcasts. No more tracing cables under floors or guessing at patch panels.

This is an updated fork that works on **Windows 11 with Core Isolation > Memory
Integrity (HVCI) enabled**. The original WinPcap-era capture driver is blocked by HVCI;
this version captures through [Npcap], whose driver is signed for and compatible with
Core Isolation — so **Core Isolation does not need to be disabled**.

<p align="center">
<img src="https://github.com/chall32/LDWin/blob/master/LDWin.png?raw=true" alt="LDWin is a Link Discovery Protocol Client for Windows"/>
</p>

### Supported protocols

+ [CDP] — Cisco Discovery Protocol
+ [LLDP] — Link Layer Discovery Protocol

---

## Two editions

This repository contains two builds of LDWin. Both require [Npcap] and work with Core
Isolation enabled; pick whichever you prefer.

| | **LDWin .NET** (recommended) | **LDWin (AutoIt)** |
|---|---|---|
| Source | `dotnet/` (C# / .NET 8, WinForms) | `LDWin.au3` (AutoIt) |
| Packet capture | In-process via SharpPcap/Npcap | Shells out to `tcpdump.exe` |
| Files to ship | Single `LDWin.exe` | `LDWin.exe` **+** `tcpdump.exe` (keep together) |
| Build workflow | `Build LDWin (.NET)` | `Build LDWin` |

The **.NET** edition decodes CDP/LLDP itself, so there is no bundled `tcpdump.exe`, no
temp-file extraction, and nothing to keep alongside the exe — which also means fewer
antivirus false positives. New work targets this edition; the AutoIt build is kept for
continuity.

---

## Requirements

- **Windows 10 / 11** (including with Core Isolation / HVCI enabled)
- **[Npcap]** installed — the free installer from <https://npcap.com/> is fine. LDWin
  detects whether it is present and points you to the download if it is missing.
  Installing in "WinPcap API-compatible Mode" is optional; LDWin finds Npcap's libraries
  automatically.
- **Administrative rights** — packet capture requires elevation (both editions request it).

> **AutoIt edition only:** `LDWin.exe` runs `tcpdump.exe` from its own folder, so the two
> must stay in the same directory. The .NET edition has no such requirement.

## How to use

1. Start the program (it will request administrator rights).
2. From the **Network Connection** drop-down, select the network adapter you want to listen on.
3. Click **Get Link Data**.
4. LDWin listens for a CDP/LLDP announcement — this can take up to 60 seconds.
5. The decoded switch name, port, management address, VLAN, platform, etc. appear in the results panel.
6. Use **Save Link Data** to write the results to a text file.

A valid TCP/IP address is not required to receive link information.

## A note on antivirus / Windows Defender

Because LDWin captures network traffic and (in the AutoIt edition) ships a packet-capture
tool, Windows Defender may false-flag it. **It is not malware.** Mitigations already in place:

- The .NET edition captures in-process — it never drops or executes a separate sniffer binary.
- The AutoIt edition runs `tcpdump.exe` from its own folder rather than extracting it to `%TEMP%`.
- Builds are uncompressed and carry proper version/publisher metadata.

If you still hit a block, the most reliable fix is to
[submit the binary to Microsoft as a false positive](https://www.microsoft.com/en-us/wdsi/filesubmission).
On your own machine you can allow it via **Windows Security → Protection history**.

## Building from source

Both editions build on Windows via GitHub Actions — run them from the repository's
**Actions** tab ("Run workflow"). Neither requires a local toolchain.

- **LDWin .NET** — [`Build LDWin (.NET)`](.github/workflows/build-dotnet.yml) runs
  `dotnet publish` to produce a single self-contained `LDWin.exe`.
  Locally: `dotnet publish dotnet/LDWin/LDWin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
- **LDWin (AutoIt)** — [`Build LDWin`](.github/workflows/build.yml) builds an
  Npcap-linked `tcpdump.exe` from source and compiles `LDWin.au3` with AutoIt's Aut2Exe.

## What's new?

See the [changelog] for the most recent changes.

---

### Credits

LDWin was originally created by **Chris Hall** (2010–2014) and is based on his
[WinCDP] project.
Original repository: <https://github.com/chall32/LDWin> · Blog: [chall32.blogspot.com]

[Npcap]: https://npcap.com/
[changelog]: ChangeLog.txt
[chall32.blogspot.com]: http://chall32.blogspot.com
[CDP]: http://en.wikipedia.org/wiki/Cisco_Discovery_Protocol
[LLDP]: http://en.wikipedia.org/wiki/Link_Layer_Discovery_Protocol
[WinCDP]: http://github.com/chall32/WinCDP
