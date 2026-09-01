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

## Excluded (typeface)

`src/OptiGames/Assets/Fonts/Excluded.ttf`

By Foyezes — <https://www.fontspace.com/excluded-font-f43356>
Distributed there as **Freeware**. The `info.txt` shipped in the download states
`license: Freeware` and nothing further; the listing does not spell out terms for
commercial use or redistribution.

Used for the wordmark on the app's welcome screen and nowhere else. If those
terms ever need to be exact, replace this file with an SIL OFL face — the font is
referenced from a single resource key, `F.Display` in `Theme.xaml`.

---

Everything else in this repository is original work covered by the [MIT
licence](LICENSE) at the repository root.
