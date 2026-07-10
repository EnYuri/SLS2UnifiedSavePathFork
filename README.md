# Unified Save Path for Slay the Spire 2

This repository is a fork of [`luojiesi/SLS2Mods`](https://github.com/luojiesi/SLS2Mods), the original Slay the Spire 2 mod collection. It has been trimmed down to keep only the `UnifiedSavePath` mod.

## About

`UnifiedSavePath` is a Slay the Spire 2 mod that makes the game use the same save directory for vanilla and modded play.

By default, Slay the Spire 2 can separate normal saves and modded saves into different profile directories. This mod forces the game to resolve both paths to the same profile directory, so save data and mod save data are shared instead of being split.

## Current Status

This fork has an initial migration for Slay the Spire 2 beta `v0.108.0`.

The project builds against the current beta game assemblies, but the save-path behavior still needs in-game verification before release.

## How It Works

The mod uses Harmony patches against `MegaCrit.Sts2.Core.Saves.UserDataPathProvider`.

It currently:

- Forces `UserDataPathProvider.IsRunningModded` to stay `false`.
- Patches the `IsRunningModded` getter and setter.
- Patches `GetProfileDir` so modded and unmodded runs resolve to the same `profile{id}` directory.
- Forces beta `forceModState` save path calls back to the unmodded path.

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

Adjust `STS2GameDir` if your game is installed elsewhere.

## Migration Notes

The next step is to install the built DLL/JSON pair into the game's `mods` directory and verify that vanilla and modded profiles read and write the same save files.

## Fork Source

This fork is based on [`luojiesi/SLS2Mods`](https://github.com/luojiesi/SLS2Mods). The retained mod metadata lists `JiesiLuo` as the original author.

## License

No explicit license file is present in the upstream repository or this fork at the time of migration. Do not assume an open-source license beyond the permissions granted by the original author.
