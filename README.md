LDWin
=====

## Link Discovery Client for Windows
Chris Hall 2010-2014 - [chall32.blogspot.com]

<p align="center"> 
<img src="https://github.com/chall32/LDWin/blob/master/LDWin.png?raw=true" alt="LDWin is a Link Discovery Protocol Client for Windows"/>
</p>

### What is Link Discovery?
Link discovery is the process of ascertaining information from directly connected networking devices, such as network switches.  This can be helpful when diagnosing suspected network connectivity issues.

LDWin supports the following methods of link discovery:

+   [CDP] - Cisco Discovery Protocol
+   [LLDP] - Link Layer Discovery Protocol

LDWin is based on [WinCDP] also by Chris Hall

### Why?
Lets face it.  We have all been there: "where does this network cable / uplink / port go?"

Until now, it has been a matter of looking up cable numbers in databases, fiddling about in the back of server and network racks or worst case - manually tracing cables down the backs of server racks, under the computer room or office floor, in overhead cable trays etc etc...

There must be a better way to tell where a network cable goes to without having to go to all that trouble every time.  VMware ESXi has Link discovery built in. Why not also have link discovery in Windows?

### How to Use
**You must have administrative rights to run this program**

1.   Start the program
2.   From the "Network Connection:" drop down, select the network adaptor over which you wish to obtain network link information
3.   Click "Get Link Data"
4.   LDWin will then listen on the selected network adaptor for link protocol announcements.  It may take up to 60 seconds to receive an announcement
5.   Once an announcement has been received, the received information will be displayed in the results section
6.   Use the "Save Link Data" button to save the received information into a text file

NOTE: A valid TCP/IP address is not required to receive valid link information.

### Windows 11 / Core Isolation
On Windows 11, the "Core Isolation > Memory Integrity" (HVCI) security feature blocks the old WinPcap-era capture driver that earlier versions of LDWin relied on, which caused capture to silently fail. From v2.3, LDWin captures using [Npcap], the maintained successor to WinPcap. Npcap's driver is signed for and compatible with Core Isolation, so **Core Isolation does not need to be disabled**.

**Requirement:** [Npcap] must be installed (the free installer from https://npcap.com/ is fine). If it is not installed, LDWin will tell you and link you to the download. LDWin finds Npcap's libraries automatically; installing in "WinPcap API-compatible Mode" is optional.

> **Building from source:** the `tcpdump.exe` bundled with LDWin must be a build linked against Npcap's `wpcap.dll` (for example [WinDump], or a tcpdump-for-Windows Npcap build). The old self-contained Microolap build shipped with LDWin &le; 2.2 will not work, because it loads its own HVCI-blocked driver.

[Npcap]: https://npcap.com/
[WinDump]: https://www.winpcap.org/windump/

### What's New?
***See the [changelog] for what's new in the most recent release.***


### [Click here to download latest version](https://github.com/chall32/LDWin/blob/master/LDWin.exe?raw=true)

If LDWin helped you, how about buying me a beer? Use the donate button below. THANK YOU!

[![Donate](https://www.paypalobjects.com/en_US/i/btn/btn_donate_LG.gif)](https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=KT462HRW7XQ3J)


[changelog]: https://github.com/chall32/LDWin/blob/master/ChangeLog.txt
[chall32.blogspot.com]: http://chall32.blogspot.com
[CDP]:http://en.wikipedia.org/wiki/Cisco_Discovery_Protocol
[LLDP]:http://en.wikipedia.org/wiki/Link_Layer_Discovery_Protocol
[WinCDP]:http://github.com/chall32/WinCDP