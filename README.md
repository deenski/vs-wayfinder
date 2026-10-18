# Wayfinding

Terrain-aware routes for Vintage Story 1.22. The mod id is `wayfinding`.

Licensed under the [MIT License](LICENSE).

## Game files

Vintage Story game files (DLLs, server archives) are never committed or redistributed by this repo. Builds reference a local install through the `VINTAGE_STORY` environment variable, or the MSBuild property of the same name.

## Prerequisites

- .NET 10 SDK. `global.json` pins the 10.0 SDK band.
- `VINTAGE_STORY` pointing at a Vintage Story 1.22 game or dedicated server install: the directory that contains `VintagestoryAPI.dll`.

```bash
export VINTAGE_STORY=/path/to/vintagestory
```

## Build

```bash
dotnet build -c Release
```

The Release build writes one mod zip to `Releases/wayfinding_0.1.0.zip` (the mod id and version from `modinfo.json`). The zip contains `modinfo.json`, `Wayfinding.dll`, and `Wayfinding.Pathing.dll`. Game assemblies are referenced with `Private=false` and are not copied into the build output or the zip.

## Test

```bash
dotnet test
```

The pathing tests do not need a game install. `Wayfinding.Pathing` is a game-free library. The build fails if that project references a game assembly (`VintagestoryAPI`, `VSEssentials`, `VSSurvivalMod`, `0Harmony`, and the other game assemblies).
