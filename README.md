# Unified Save Path for Slay the Spire 2

This repository is a fork of [`luojiesi/SLS2Mods`](https://github.com/luojiesi/SLS2Mods), the original Slay the Spire 2 mod collection. It has been trimmed down to keep only the `UnifiedSavePath` mod.

## About

`UnifiedSavePath` is a Slay the Spire 2 mod that makes the game use the same save directory for vanilla and modded play.

By default, Slay the Spire 2 can separate normal saves and modded saves into different profile directories. This mod forces the game to resolve both paths to the same profile directory, so save data and mod save data are shared instead of being split.

## Current Status

This fork was migrated for Slay the Spire 2 beta `v0.108.0`. Release `v1.0.6` was rebuilt
against the `v0.113.1` hotfix (game commit `d09d48e5`). The save-path API and first-time
save-copy hook remain compatible; no runtime code changes were needed for this hotfix.

`v1.0.2` fixes the *"Assembly DLL for mod UnifiedSavePath failed to initialize!"* error reported on
other players' installs. Despite the wording, that message does not mean the DLL failed to load: the
game shows it whenever a mod's initializer throws. `v1.0.1` called Harmony's `PatchAll`, which is
all-or-nothing — on a game build where any one patch target was missing or renamed, it threw and
took down the four patches that would have worked. The mod was then marked failed while the game
still counted itself as modded, so saves silently split.

`v1.0.2` resolves each target by name at runtime and applies patches one at a time, logging and
skipping anything the current build does not have, and never lets the initializer throw.

## How It Works

The mod uses Harmony patches against `MegaCrit.Sts2.Core.Saves.UserDataPathProvider`.

It currently:

- Forces `UserDataPathProvider.IsRunningModded` to stay `false`.
- Patches the `IsRunningModded` getter and setter.
- Patches `GetProfileDir` so modded and unmodded runs resolve to the same `profile{id}` directory.
- Forces beta `forceModState` save path calls back to the unmodded path.
- Skips the game's first-time copy of vanilla saves into `modded/` when both paths are already the
  same directory. Without this, the first launch with mods copies each save file onto itself and
  shows a popup claiming the saves were moved to a separate modded location. If the path patches
  did not apply, the copy still runs as normal.

Every patch is optional. On a game build that is missing one, the mod applies the rest and writes a
line such as `[UnifiedSavePath] Skipping GetAccountDir(bool?): not present in this game build.` to
the game log.

## Troubleshooting

All messages from the mod are prefixed with `[UnifiedSavePath]` in the game log. On a successful
start you should see `Applied 6 of 6 patches.`

If the mod reports an error:

- **`There are two or more mods with the same id UnifiedSavePath`** — the mod is installed twice.
  Mod discovery scans every `.json` under `mods/` recursively, so a nested extra copy counts.
- **A declared min game version higher than yours** — check for an older installed copy. The current
  manifest has no minimum game version; unavailable patches are skipped individually.
- **`failed to initialize`** — the log line right before it names the cause; please include it when
  reporting.

This mod does not migrate existing files. It only changes where the game reads and writes, so saves
already written to a `modded/` directory need to be moved by hand.

## Repository Layout

```text
UnifiedSavePath/
  UnifiedSavePath.csproj
  UnifiedSavePathMod.cs
  UnifiedSavePathForked.json
  UnifiedSavePath.json      inert placeholder, see "Upgrading from v1.0.1"
  mod_manifest.json
```

## Upgrading from v1.0.1

`v1.0.1` shipped as `UnifiedSavePath.dll` + `UnifiedSavePath.json`; `v1.0.3` renamed both to
`UnifiedSavePathForked.*`. Steam does not always delete a file that a Workshop item stops shipping,
so the old manifest can survive the update and be loaded as a second save-path mod - which then
fails, and a failed mod still counts as modded.

The mod ships an inert `UnifiedSavePath.json` at the old name so Steam overwrites the leftover
instead. The game skips it with `does not look like a mod manifest; skipping it.` in the log. If you
ever see `Unified Save Path` (v1.0.1, by `JiesiLuo`) listed *alongside* `Unified Save Path Forked`,
delete `UnifiedSavePath.dll` and `UnifiedSavePath.json` from
`steamapps/workshop/content/2868840/3762072145/`.

## Build

The project targets .NET 9.0 and references assemblies from the local Slay the Spire 2 install.

```powershell
dotnet build UnifiedSavePath/UnifiedSavePath.csproj -c Release -p:STS2GameDir="E:/My Games/steamapps/common/Slay the Spire 2"
```

Adjust `STS2GameDir` if your game is installed elsewhere. Install the resulting
`UnifiedSavePathForked.dll` and `UnifiedSavePathForked.json` pair from `bin/Release/net9.0/` into the
game's `mods` directory. The DLL must keep its name, since the loader looks for `{manifest id}.dll`.

## Fork Source

This fork is based on [`luojiesi/SLS2Mods`](https://github.com/luojiesi/SLS2Mods), originally by
`JiesiLuo`.

It ships under the id `UnifiedSavePathForked` rather than upstream's `UnifiedSavePath` on purpose.
The loader only de-duplicates a mod id between the local `mods/` directory and the Workshop; two
Workshop mods sharing an id are not reconciled, so one of them fails with `MOD_ERROR.DUPLICATE_ID`.
A failed mod still counts toward `ModManager.IsRunningModded()`, which would put the game back on the
`modded/` save path - the exact problem this mod exists to prevent. Separate ids let a player have
both installed without either one failing.

## License

No explicit license file is present in the upstream repository or this fork at the time of migration. Do not assume an open-source license beyond the permissions granted by the original author.
