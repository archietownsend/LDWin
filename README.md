LDWin
=====

## Link Discovery Client for Windows — Windows 11 Compatible

LDWin tells you **which switch and switch port a network cable is plugged into**, by
listening for the link-discovery announcements (CDP / LLDP) that the directly-connected
switch broadcasts. No more tracing cables under floors or guessing at patch panels.

This is an updated fork, rewritten in C#, that works on Windows 11 with Core Isolation >
Memory Integrity (HVCI) enabled. It captures with Windows' built-in Packet Monitor
(pktmon), so there is no capture driver to install.

<p align="center">
<img src="LDWin.png" width="560" alt="LDWin - Link Discovery for Windows"/>
</p>

### Supported protocols

+ [CDP] — Cisco Discovery Protocol
+ [LLDP] — Link Layer Discovery Protocol

## Requirements

- **Windows 10 version 2004 or later, or Windows 11** — these include Packet Monitor
  (`pktmon.exe`), which LDWin uses to capture. Nothing else needs installing.
- **Administrative rights** — packet capture requires elevation (LDWin requests it).

## Download

Grab the latest **`LDWin-win-x64.zip`** from the [Releases page](../../releases/latest),
extract it, and run **`LDWin.exe`**. It is a self-contained build, so you do not need to
install .NET or a capture driver — just run `LDWin.exe` as administrator.

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

> **Wi-Fi is not supported for link discovery.** A switch sends CDP/LLDP only to
> whatever is directly plugged into its port - the access point, not your laptop -
> and access points do not relay those announcements over the air. Selecting a
> Wi-Fi adapter shows your current access point connection (SSID, BSSID, signal)
> instead; for the switch and port a cable is plugged into, use a wired connection.

## A note on antivirus / Windows Defender

Because LDWin captures network traffic, Windows Defender may occasionally false-flag it.
**It is not malware.** It captures with Windows' own Packet Monitor, never drops a
separate sniffer binary, and builds carry proper version/publisher metadata.

While a capture runs, LDWin takes over Packet Monitor, which allows one capture at a
time system-wide: it replaces any `pktmon` filters and clears them afterwards.

### Credits

LDWin was originally created by **Chris Hall** (2010–2014). Original repository: <https://github.com/chall32/LDWin>

[CDP]: http://en.wikipedia.org/wiki/Cisco_Discovery_Protocol
[LLDP]: http://en.wikipedia.org/wiki/Link_Layer_Discovery_Protocol
