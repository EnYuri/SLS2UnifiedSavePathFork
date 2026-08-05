# Unified Save Path for Slay the Spire 2

This repository is a fork of [`luojiesi/SLS2Mods`](https://github.com/luojiesi/SLS2Mods), the original Slay the Spire 2 mod collection. It has been trimmed down to keep only the `UnifiedSavePath` mod.

## About

`UnifiedSavePath` is a Slay the Spire 2 mod that makes the game use the same save directory for vanilla and modded play.

By default, Slay the Spire 2 can separate normal saves and modded saves into different profile directories. This mod forces the game to resolve both paths to the same profile directory, so save data and mod save data are shared instead of being split.

## Current Status

This fork was migrated for Slay the Spire 2 beta `v0.108.0` and verified against `v0.110.1`.

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

Every patch is optional. On a game build that is missing one, the mod applies the rest and writes a
line such as `[UnifiedSavePath] Skipping GetAccountDir(bool?): not present in this game build.` to
the game log.

## Troubleshooting

All messages from the mod are prefixed with `[UnifiedSavePath]` in the game log. On a successful
start you should see `Applied 5 of 5 patches.`

If the mod reports an error:

- **`There are two or more mods with the same id UnifiedSavePath`** — the mod is installed twice.
  Mod discovery scans every `.json` under `mods/` recursively, so a nested extra copy counts.
- **A declared min game version higher than yours** — the manifest requires game `0.108.0` or newer.
- **`failed to initialize`** — the log line right before it names the cause; please include it when
  reporting.

This mod does not migrate existing files. It only changes where the game reads and writes, so saves
already written to a `modded/` directory need to be moved by hand.

## Repository Layout

```text
UnifiedSavePath/
  UnifiedSavePath.csproj
  UnifiedSavePathMod.cs
  UnifiedSavePath.json
  mod_manifest.json
```

## Build

The project targets .NET 9.0 and references assemblies from the local Slay the Spire 2 install.

```powershell
dotnet build UnifiedSavePath/UnifiedSavePath.csproj -c Release -p:STS2GameDir="E:/My Games/steamapps/common/Slay the Spire 2"
```

Adjust `STS2GameDir` if your game is installed elsewhere. Install the resulting `UnifiedSavePath.dll`
and `UnifiedSavePath.json` pair from `bin/Release/net9.0/` into the game's `mods` directory. The DLL
must keep its name, since the loader looks for `{manifest id}.dll`.

## Fork Source

This fork is based on [`luojiesi/SLS2Mods`](https://github.com/luojiesi/SLS2Mods). The retained mod metadata lists `JiesiLuo` as the original author.

## License

No explicit license file is present in the upstream repository or this fork at the time of migration. Do not assume an open-source license beyond the permissions granted by the original author.
