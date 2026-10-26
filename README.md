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
2. From the **Network Connection** drop-down, select the wired adapter the cable is plugged into.
3. Click **Get Link Data**.
4. LDWin listens for CDP/LLDP announcements for up to 60 seconds. Each switch appears in
   the results panel as soon as its announcement arrives, with its name, port, management
   address, VLAN, platform and more; listening continues in case more than one switch is
   on the link. Click **Stop** once you have what you need.
5. Use **Save Link Data** to write the results to a text file, or **Copy** to put them on
   the clipboard.

Switches typically announce every 30 (LLDP) to 60 (CDP) seconds. If nothing turns up, tick
**Keep listening until found** and LDWin will keep retrying until a switch announces itself
or you click Stop. A valid TCP/IP address is not required to receive link information.

### Wi-Fi

Link discovery does not work over Wi-Fi. A switch sends CDP/LLDP only to whatever is directly
plugged into its port — the access point, not your laptop — and access points do not relay
those announcements over the air. Wi-Fi adapters are labelled **(Wi-Fi)** in the drop-down;
selecting one shows your current access point connection (network name, access point BSSID,
signal and link speed) instead.

On Windows 11 24H2 and later, Windows only shares those Wi-Fi details with apps allowed to
use location. If LDWin says access was blocked, turn on **Settings → Privacy & security →
Location → Location services** and **Let desktop apps access your location**.

## How it captures

LDWin uses Windows Packet Monitor (`pktmon`), which ships with Windows and works with Core
Isolation / Memory Integrity enabled. For each capture it tells Packet Monitor to log only
LLDP and CDP frames on the selected adapter, reads them live as they arrive, and stops the
capture when done.

Packet Monitor runs one capture at a time system-wide, so while LDWin is listening it takes
over Packet Monitor: it replaces any `pktmon` filters you have set and clears them afterwards.
If another Packet Monitor capture is already running, LDWin says so rather than stopping it;
end it with `pktmon stop` and try again.

### Credits

LDWin was originally created by **Chris Hall** (2010–2014). Original repository: <https://github.com/chall32/LDWin>

[CDP]: http://en.wikipedia.org/wiki/Cisco_Discovery_Protocol
[LLDP]: http://en.wikipedia.org/wiki/Link_Layer_Discovery_Protocol
