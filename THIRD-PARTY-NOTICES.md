# Third-party notices

OptiGames bundles the following third-party components inside the published
executable. They are extracted to `%LOCALAPPDATA%\OptiGamesTool` at runtime.

## NVIDIA Profile Inspector

`src/OptiGames.Core/Payloads/inspector.exe`

Copyright (c) Orbmu2k — <https://github.com/Orbmu2k/nvidiaProfileInspector>
Licensed under the MIT License.

Used to import the tuned driver profile shipped as
`src/OptiGames.Core/Payloads/optigames.nip`. OptiGames does not modify the
tool; it invokes its documented `-silentImport` switch.

---

Everything else in this repository is original work covered by the [MIT
licence](LICENSE) at the repository root.
