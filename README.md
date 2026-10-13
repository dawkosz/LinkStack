# LinkStack

**Use Ethernet and Wi-Fi together for parallel downloads on Windows.**

LinkStack is a portable Windows desktop application with a Polish interface. It distributes new TCP connections across independent internet connections, shows traffic per adapter, and includes a transparent Steam download mode.

No account, subscription, transfer quota, telemetry, or relay server. Your mobile provider's data allowance still applies.

**Experimental release: 1.0.2.** This is connection-level load balancing, not single-stream bonding. One TCP stream uses one link; applications need multiple concurrent connections to benefit.

## Download and run

Download the portable ZIP from this repository's **Releases** page and extract all files to the same folder. Start `LinkStack.exe`.

1. Disconnect your VPN before starting aggregation.
2. Select the Ethernet and Wi-Fi adapters you want to use. They must have independent internet uplinks.
3. For Steam, select **Tryb Steam (administrator)** and click **Włącz łączenie**. Accept the Windows UAC prompt. The elevated window starts automatically and preserves the selected adapters and weights.
4. Pause and resume the Steam download to create new connections. If Steam keeps its existing connections, fully exit Steam and restart it while LinkStack stays active.
5. Watch the **Steam** connection counter and both adapter download rates.
6. Click **Zatrzymaj** before reconnecting your VPN.

Windows 10/11 x64 with .NET Framework 4.8 is the supported target. The app has not been validated on every Windows version or Steam CDN.

## Features

- **Steam mode:** redirects new IPv4 TCP connections on ports 80/443 from `steam`, `steamservice`, and `steamwebhelper`. Uses WinDivert and requires administrator privileges. Steam itself does not need to honor a proxy setting.
- **HTTP/HTTPS proxy:** `127.0.0.1:17881`, including HTTPS CONNECT tunnels.
- **SOCKS5 TCP proxy:** `127.0.0.1:17880`. Domain-name and IPv4 CONNECT are supported. UDP ASSOCIATE and IPv6 are explicitly rejected.
- **Optional Windows proxy:** uses WinINet, with a saved snapshot and a separate crash-recovery watcher. Orphaned LinkStack proxy settings are also cleaned up when the listener is absent.
- **Built-in file download:** downloads direct HTTP/HTTPS URLs using up to eight workers and 4 MiB ranges when the server supplies reliable range support and a validator. Otherwise it uses a single stream. Checks range boundaries, file validators, and length; it does not replace verification against a publisher's independent checksum.
- **Per-link diagnostics:** the explicit **Sprawdź oba łącza** button checks the public IP over HTTPS through each selected adapter using `api.ipify.org`.
- **Connection weights:** weights 1–10 control the distribution of new connections. They are not byte-level bandwidth guarantees. Failed connection attempts can fall back to another selected adapter.
- **Local status:** `%LOCALAPPDATA%\LinkStack\status.txt` and `events.log`. These files are not uploaded by LinkStack.

## What has been verified

On one Windows PC, an actual Steam download used both physical adapters simultaneously, with 48 redirected connections. One observed interval showed approximately **17.5 MiB/s on Ethernet + 22.9 MiB/s on Wi-Fi**. This demonstrates concurrent use of both links, not a controlled benchmark or a guaranteed speed increase.

Local tests cover HTTP, CONNECT, fragmented SOCKS handshakes, half-close behavior, range-download integrity, fallback, changed validators, cancellation, malformed requests, and stopping the listeners. Separate Windows tests cover native WinINet proxy recovery, including orphaned settings without a backup. Real-network tests have also exercised HTTPS via both adapters and transparent redirection of a test process.

## Limitations

- Existing Steam connections are not taken over. Pause/resume or restart Steam after enabling the mode.
- Steam interception covers IPv4 TCP 80/443 only. IPv6, UDP/QUIC, other ports, and actual game processes stay outside this mode.
- HTTPS is tunneled without decryption or certificate replacement.
- Direct socket selection bypasses the ordinary default-route preference for proxied traffic. Disconnect VPN first; the GUI refuses to start with a detected active VPN.
- Disconnecting a link does not migrate an established TCP connection; clients must reconnect. Only new connection attempts have fallback.
- A shared router/ISP uplink will not gain capacity just because the PC connects to it over both Ethernet and Wi-Fi.
- Throughput can be limited by the CDN, the weaker link, disk writes/unpacking, CPU, and the number of concurrent transfers. The rate display uses 1,048,576-byte units despite its `MB/s` label.
- Steam mode installs/loads the WinDivert driver on demand. Closing LinkStack removes its interception handle; it does not globally stop a driver that another application may also be using.

## Build from source

No NuGet packages, Node.js, or separate SDK download is required on a Windows machine with the .NET Framework compiler.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\fetch-windivert.ps1
.\dist\LinkStack.exe
```

The first command builds the application and its generated icon. The second downloads **WinDivert 2.2.2-A** from the official GitHub release, verifies its pinned SHA-256, and copies the x64 runtime files alongside the EXE. WinDivert is only needed for Steam mode.

To create a portable bundle:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
```

To run the local protocol tests, stop any running LinkStack instance first because the tests use the same proxy ports:

```bat
test.cmd
```

Optional integration tests:

```bat
dist\LinkStack.Tests.exe --live
dist\LinkStack.Tests.exe --live --capture
dist\LinkStack.Tests.exe --proxy-recovery
```

`--live` makes small HTTPS requests to the IP-check service. `--capture` requires administrator privileges and redirects only the test process. `--proxy-recovery` briefly changes the current user's LAN proxy and verifies recovery after the test owner exits; run it only after stopping LinkStack.

## Dependencies and license

LinkStack's own source is **MIT licensed**. WinDivert is a separately distributed, unmodified dependency under its own dual-license terms; see [third-party notices](third_party/README.md) and [WinDivert's license](third_party/WinDivert-LICENSE.txt).

The app's implementation was written independently. Public HypoMuxPlus source was examined to understand the `IP_UNICAST_IF` routing technique and proxy limitations; no code or artwork from that application is included.

[Polska instrukcja](README.pl.md) · [Changes](CHANGELOG.md)
