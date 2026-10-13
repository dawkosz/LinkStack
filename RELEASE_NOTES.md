# LinkStack 1.0.2 — experimental

Portable Windows x64 app that spreads parallel downloads over independent Ethernet and Wi-Fi internet connections. Includes a Polish desktop interface, per-link counters, HTTP/SOCKS5 proxies, range downloads, and transparent Steam IPv4 TCP 80/443 interception.

Extract the entire ZIP, disconnect VPN, select your adapters, enable Steam mode, and accept UAC. Pause/resume or restart Steam to create new connections. Existing TCP sessions are not intercepted.

An actual Steam download was observed using both physical links. Speed gains depend on parallel connections, both uplinks, the CDN, disk, and CPU; this does not bond a single TCP stream.

The ZIP includes unmodified WinDivert 2.2.2 runtime files and its license. Steam mode needs administrator privileges. LinkStack's own EXE is unsigned. This is an experimental release tested on one Windows PC.
