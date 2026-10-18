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

The pathing tests do not need a game install. `Wayfinding.Pathing` is a game-free library. Building that project fails if it references a game assembly (`Vintagestory`, `VintagestoryAPI`, `VSEssentials`, `VSSurvivalMod`, `0Harmony`, and the other game assemblies), a strong name such as `VintagestoryAPI, Version=...`, or any HintPath under the `VINTAGE_STORY` install. Each rejected assembly is named once.

## CI

Pull requests and pushes to `main` run [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

- `guard-and-unit-tests` runs `dotnet test` from the repo root with `VINTAGE_STORY` unset. That covers the Wayfinding.Pathing tests, including the game-reference guard.
- `build-and-load` builds Release against the Vintage Story headless server in [`ci/vs-version.txt`](ci/vs-version.txt) (1.22.7) and loads the mod on that server. It expects the log line `[wayfinding] loaded on Server` and no server errors, then shuts the server down. The server comes from the private image `ghcr.io/deenski/vs-wayfinder-server:1.22.7` when this repo's `GITHUB_TOKEN` can pull it (`packages: read`). Otherwise CI uses the Actions cache, keyed on the version and the tarball MD5, or downloads the linux server listed for that version in `https://api.vintagestory.at/stable.json` and checks the MD5 (`3031212903b1c283084d5fe0cda6bce6` for 1.22.7). Fork pull requests cannot read the private package, so they take the cache or download path.
- A second matrix leg tracks the current unstable linux server from `https://api.vintagestory.at/unstable.json`. It uses the cache and a checksum-verified download only (no GHCR image) and is marked `continue-on-error`, so it does not block the stable checks. If that feed has no linux server entry, the leg is skipped.

[`.github/workflows/server-image.yml`](.github/workflows/server-image.yml) rebuilds `ghcr.io/deenski/vs-wayfinder-server:<version>` when `ci/vs-version.txt` changes on `main`, or when the workflow is run manually. It downloads that stable server, verifies the MD5 from `stable.json`, and bakes the extracted tree into an image based on the .NET 10 runtime (`/vs`). The workflow uses `GITHUB_TOKEN` with `packages: write` and `contents: read`. The package is private. Game files are never committed, never redistributed, and never uploaded as workflow or release artifacts. The Actions cache and that private image are the only copies CI keeps.

[`.github/workflows/release.yml`](.github/workflows/release.yml) runs on `v*` tags. It builds Release against the same server and attaches only `Releases/wayfinding_<version>.zip` to the GitHub Release.

### Publishing to the Mod DB

Uploading to the Vintage Story Mod DB is a manual step. The tag workflow does not do it.

1. Open the GitHub Release for the tag and download `wayfinding_<version>.zip`. Leave the server archive, the container image, and every game DLL where they are.
2. Sign in at <https://mods.vintagestory.at/>.
3. Open your mods. If Wayfinding does not exist yet, create it with mod id `wayfinding`.
4. Add a release on that mod and upload the zip from the GitHub Release.
5. Set the release version to the `version` in `modinfo.json`, select game compatibility 1.22, and paste the changelog.
6. Publish the Mod DB release.
