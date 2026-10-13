# Third-party dependency

## WinDivert 2.2.2-A

- Project: https://github.com/basil00/WinDivert
- Corresponding source: https://github.com/basil00/WinDivert/tree/v2.2.2
- Official binary release: https://github.com/basil00/WinDivert/releases/tag/v2.2.2
- Documentation: https://www.reqrypt.org/windivert-doc.html
- License: [WinDivert-LICENSE.txt](WinDivert-LICENSE.txt), dual LGPL v3 / GPL v2.
- Runtime files used without modification: x64 `WinDivert.dll` and `WinDivert64.sys`.

LinkStack dynamically calls the separate WinDivert DLL for optional Steam mode. The portable bundle includes the unmodified upstream DLL, signed driver, and upstream license. These files can be replaced independently of LinkStack. The build fetches the pinned official package; WinDivert source is not part of LinkStack's MIT-licensed code.

SHA-256 of `WinDivert-2.2.2-A.zip`:

```
63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15
```

The driver uses an upstream signing sponsor's certificate; it is not signed by the LinkStack author. Steam itself does not use this dependency for LinkStack's feature.
