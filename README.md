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
<img src="LDWin.png" width="560" alt="LDWin - Link Discovery for Windows"/>
</p>

### Supported protocols

+ [CDP] — Cisco Discovery Protocol
+ [LLDP] — Link Layer Discovery Protocol

LDWin is written in C# (.NET 8, WinForms) and captures + decodes CDP/LLDP **in process**
via [Npcap]. There is no bundled `tcpdump.exe`, no temp-file extraction, and nothing to
keep alongside the program — it ships as a single self-contained `LDWin.exe`.

---

## Requirements

- **Windows 10 / 11** (including with Core Isolation / HVCI enabled)
- **[Npcap]** installed — the free installer from <https://npcap.com/> is fine. LDWin
  detects whether it is present and points you to the download if it is missing.
  Installing in "WinPcap API-compatible Mode" is optional; LDWin finds Npcap's libraries
  automatically.
- **Administrative rights** — packet capture requires elevation (LDWin requests it).

## Download

Grab the latest **`LDWin-win-x64.zip`** from the [Releases page](../../releases/latest),
extract it, and run **`LDWin.exe`**. It is a self-contained build, so you do not need to
install .NET — just install [Npcap] and run `LDWin.exe` as administrator.

> The build is currently unsigned, so Windows SmartScreen may warn on first run — choose
> **More info → Run anyway** (or right-click the exe → **Properties → Unblock**). A
> publicly-trusted signature is planned to remove this.

## How to use

1. Start the program (it will request administrator rights).
2. From the **Network Connection** drop-down, select the network adapter you want to listen on.
3. Click **Get Link Data**.
4. LDWin listens for a CDP/LLDP announcement — this can take up to 60 seconds.
5. The decoded switch name, port, management address, VLAN, platform, etc. appear in the results panel.
6. Use **Save Link Data** to write the results to a text file.

A valid TCP/IP address is not required to receive link information.

## A note on antivirus / Windows Defender

Because LDWin captures network traffic, Windows Defender may occasionally false-flag it.
**It is not malware.** It captures in process and never drops or executes a separate
sniffer binary, and builds carry proper version/publisher metadata. If you still hit a
block, the most reliable fix is to
[submit the binary to Microsoft as a false positive](https://www.microsoft.com/en-us/wdsi/filesubmission).
On your own machine you can allow it via **Windows Security → Protection history**.

## Building from source

LDWin builds on Windows via GitHub Actions — run [`Build LDWin (.NET)`](.github/workflows/build-dotnet.yml)
from the repository's **Actions** tab ("Run workflow") to produce a single self-contained
`LDWin.exe`. Locally:

```
dotnet publish dotnet/LDWin/LDWin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The CDP/LLDP decoders are unit-tested ([`Test`](.github/workflows/test.yml) workflow):

```
dotnet test dotnet/LDWin.Tests/LDWin.Tests.csproj
```

Releases are published by the [`Release`](.github/workflows/release.yml) workflow: push a
tag (`git tag v3.0.0 && git push origin v3.0.0`) or run it manually with a tag, and it
builds the self-contained exe and attaches it to a GitHub Release.

### Project layout

| Project | Target | Purpose |
|---|---|---|
| `dotnet/LDWin.Core` | `net8.0` | Capture engine + CDP/LLDP decoders (no UI; unit-testable) |
| `dotnet/LDWin` | `net8.0-windows` | WinForms GUI; published as the single-file exe |
| `dotnet/LDWin.Tests` | `net8.0` | xUnit decoder tests |

## What's new?

See the [changelog] for the most recent changes.

---

### Credits

LDWin was originally created by **Chris Hall** (2010–2014) and is based on his
[WinCDP] project. Original repository: <https://github.com/chall32/LDWin>

[Npcap]: https://npcap.com/
[changelog]: ChangeLog.txt
[CDP]: http://en.wikipedia.org/wiki/Cisco_Discovery_Protocol
[LLDP]: http://en.wikipedia.org/wiki/Link_Layer_Discovery_Protocol
[WinCDP]: http://github.com/chall32/WinCDP
