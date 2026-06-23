LDWin
=====

## Link Discovery Client for Windows — Windows 11 Compatible Fork

This is an updated fork of LDWin that works on Windows 11 with **Core Isolation > Memory Integrity** (HVCI) enabled. The original WinPcap-era capture driver is blocked by HVCI; this version replaces it with [Npcap], whose driver is signed and fully compatible with Core Isolation.

<p align="center"> 
<img src="https://github.com/chall32/LDWin/blob/master/LDWin.png?raw=true" alt="LDWin is a Link Discovery Protocol Client for Windows"/>
</p>

### What is Link Discovery?
Link discovery lets you find out what network switch port (and switch name) a cable is plugged into by listening for announcements from the directly connected switch. Useful when tracing cables or diagnosing connectivity.

LDWin supports:

+   [CDP] - Cisco Discovery Protocol
+   [LLDP] - Link Layer Discovery Protocol

### Requirements

- **Windows 10 / 11** (including with Core Isolation / HVCI enabled)
- **[Npcap]** must be installed — the free installer from https://npcap.com/ is fine. LDWin will tell you if it isn't found.
- **Keep `LDWin.exe` and `tcpdump.exe` in the same folder.** LDWin runs `tcpdump.exe` from its own directory. LDWin will prompt you if `tcpdump.exe` is missing.

Installing Npcap in "WinPcap API-compatible Mode" is optional — LDWin finds Npcap's libraries automatically.

### How to Use

**You must have administrative rights to run this program.**

1. Start the program
2. From the **Network Connection:** drop-down, select the network adapter you want to listen on
3. Click **Get Link Data**
4. LDWin listens for link protocol announcements — it may take up to 60 seconds to receive one
5. Once an announcement arrives the information is displayed in the results panel
6. Use **Save Link Data** to save the results to a text file

A valid TCP/IP address is not required to receive link information.

### A note on antivirus / Windows Defender

Because LDWin is an unsigned AutoIt program that ships a packet-capture tool (`tcpdump`), Windows Defender may false-flag it. It is not malware. Steps taken to reduce this:

- `tcpdump.exe` is run directly from the program folder — it is never dropped into `%TEMP%`
- The build does not compress the executable (compressed payloads look like packers to AV)
- Proper version/publisher metadata is stamped onto both binaries at build time

The most reliable fix remains [submitting the binary to Microsoft as a false positive](https://www.microsoft.com/en-us/wdsi/filesubmission). On your own machine you can also allow it via **Windows Security → Protection history**.

Optional code-signing (see below) will stop Defender from flagging a signed binary.

### Building from Source

Both binaries are built automatically on Windows by the [`Build LDWin`](.github/workflows/build.yml) GitHub Actions workflow — run it from the repository's **Actions** tab ("Run workflow").

The workflow:
1. Builds `tcpdump.exe` from source against the [Npcap] SDK (so it loads Npcap's `wpcap.dll` at runtime)
2. Compiles `LDWin.exe` with AutoIt's Aut2Exe
3. Optionally code-signs both binaries
4. Optionally commits the rebuilt binaries back to the branch

**Code-signing (recommended):** To sign the binaries, add two repository secrets and the workflow will `signtool sign` both executables:

- `CODE_SIGN_PFX_BASE64` — your code-signing certificate (`.pfx`), base64-encoded  
  (e.g. `certutil -encode cert.pfx cert.b64`)
- `CODE_SIGN_PASSWORD` — the `.pfx` password

Without these secrets the build still succeeds but the binaries are left unsigned.

### What's New?
***See the [changelog] for what's new in the most recent release.***

---

### Credits

LDWin was originally created by **Chris Hall** (2010–2014).  
Original project: [github.com/chall32/LDWin](https://github.com/chall32/LDWin) · Blog: [chall32.blogspot.com]

[Npcap]: https://npcap.com/
[changelog]: ChangeLog.txt
[chall32.blogspot.com]: http://chall32.blogspot.com
[CDP]: http://en.wikipedia.org/wiki/Cisco_Discovery_Protocol
[LLDP]: http://en.wikipedia.org/wiki/Link_Layer_Discovery_Protocol
