<div align="center">

<img src="src/OptiGames/Assets/logo.png" width="96" alt="OptiGames">

# OptiGames

**Windows tuning for games, with an undo button.**

[![Release](https://img.shields.io/github/v/release/OptiGamesHQ/optigames-cli?color=e11d2f&label=release)](https://github.com/OptiGamesHQ/optigames-cli/releases/latest)
[![License](https://img.shields.io/badge/licence-MIT-e11d2f)](LICENSE)
[![Build](https://github.com/OptiGamesHQ/optigames-cli/actions/workflows/release.yml/badge.svg)](https://github.com/OptiGamesHQ/optigames-cli/actions/workflows/release.yml)

</div>

---

## Install

Open PowerShell and run:

```powershell
irm optigames.gg/cli | iex
```

That downloads the current release, checks it against the published SHA-256, and
launches it. You will see a UAC prompt — the tool writes to `HKLM` and calls
`powercfg`, so it needs administrator rights.

Prefer not to pipe a script into your shell? [Download the exe from
Releases](https://github.com/OptiGamesHQ/optigames-cli/releases/latest) instead.
Both paths give you the same binary.

## Why this exists

Most Windows "optimisers" are a wall of switches that write to your registry and
give you no way back. OptiGames is built the other way round:

- **Every tweak is reversible.** Each one carries an explicit off-state — the
  actual Windows default, authored per value — not "whatever was there when we
  first looked". Flipping a switch back restores the default even on a machine
  that was already modified before the tool ever ran.
- **It shows its work.** Every tweak lists the exact registry paths it writes and
  what each value becomes in both directions, *before* you apply it.
- **Nothing happens until you say so.** Switches stage a change. Reviewing the
  batch and pressing Apply is what commits it, and Undo reverses the whole batch.
- **Restore points first.** First run makes you take a System Restore point
  before it will let you at anything else.

## What it changes

17 tweaks across two groups. The Optimize page shows the precise registry values
for each one; this is the summary.

**General** — Xbox Game Bar and Game DVR, Store auto-update, shell animation and
input delay, Windows Game Mode, Microsoft telemetry, a high-performance power
plan, and a tuned NVIDIA driver profile.

**Advanced** — browser debloat via enterprise policy (Brave, Chrome, Edge),
fullscreen optimisations, notification centre, the Windows 10 right-click menu,
background apps, hibernation, Storage Sense, hardware-accelerated GPU scheduling,
paused Windows Updates, and virtualisation-based security.

The last two weaken your security posture and are labelled as such in the app.
Pausing updates stops security patches. Disabling VBS removes a kernel-level
exploit defence and breaks some anti-cheats. They are off by default and behind a
warning badge.

## Other pages

- **Clean Drive** — sizes the caches Windows never clears (temp, update cache,
  delivery optimisation, crash dumps, thumbnails, prefetch) and removes only what
  you tick. It does not touch documents, downloads or game installs.
- **Restore Point** — create, list, roll back to, or delete System Restore points
  without going through Control Panel.

## Reverting

Three independent layers, weakest to strongest:

1. **Per-tweak** — flip the switch off and apply.
2. **Undo** — reverses the batch you just committed, in one click.
3. **System Restore** — rolls the whole machine back.

The NVIDIA profile is a special case. NVIDIA Profile Inspector has no export
switch, so rather than invent "NVIDIA defaults" the tool byte-copies the driver's
own profile database (`nvdrsdb0.bin`, `nvdrsdb1.bin`, `nvdrssel.bin`) aside before
its first import and restores those exact files on revert. If your driver version
has changed since that backup it refuses to restore, and tells you to use NVIDIA
Control Panel → Manage 3D Settings → Restore Defaults instead — pushing a profile
database from a different driver build can corrupt the store.

## Building from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on
Windows. WPF means this does not cross-compile.

```powershell
git clone https://github.com/OptiGamesHQ/optigames-cli
cd optigames-cli
dotnet build
```

To produce the single-file executable the installer downloads:

```powershell
dotnet publish src/OptiGames/OptiGames.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

## Layout

```
src/OptiGames.Core/     Engine. No UI dependency.
  Tweaks/               Tweak model, the catalog, the apply/revert engine
  Services/             Restore points, drive cleanup, power plan, NVIDIA profile
  Payloads/             Files embedded in the exe and extracted at runtime
src/OptiGames/          WPF app
  Views/ ViewModels/    One view per page
  Theme.xaml            Palette, control templates, motion
  Icons.xaml            Icon geometry set
bootstrap/optigames.ps1 What `irm optigames.gg/cli | iex` runs
```

Adding a tweak means adding one entry to `TweakCatalog.cs` with its on-state and
its off-state. The engine, the status detection and the disclosure panel all
follow from that — there is no UI to write.

## Contributing

Issues and pull requests welcome. If you are adding a tweak, the off-state has to
be the genuine Windows default rather than a guess — if you are not sure what the
default is, say so in the PR and we will work it out together. A wrong off-state
is worse than no tweak, because it leaves people in a state they believe is stock
when it is not.

## Licence

[MIT](LICENSE). Third-party components are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
