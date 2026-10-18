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
- A second matrix leg tracks the current unstable linux server from `https://api.vintagestory.at/unstable.json` when that version is newer than stable. It uses the cache and a checksum-verified download only (no GHCR image) and is marked `continue-on-error`, so it does not block the stable checks. If the feed has no linux server entry, or the unstable version is not newer than stable, that job is skipped and the resolve step records a notice.

[`.github/workflows/server-image.yml`](.github/workflows/server-image.yml) publishes `ghcr.io/deenski/vs-wayfinder-server:<version>` only from a manual `workflow_dispatch`. It does not run on pushes to `main`, and it does not run when `ci/vs-version.txt` changes. That trigger stays off until the package is confirmed private. Before it downloads or pushes the server, the job requires the package to exist and be private (`ci/check-ghcr-visibility.py`). `GITHUB_TOKEN` is an installation token. `GET /users/deenski/packages/container/vs-wayfinder-server` is documented for a public package, and `GET /user/packages/container/vs-wayfinder-server` is the authenticated-user route, so a private package often cannot be proved from Actions. A public reading fails the job. If neither call can prove the package is private, the job fails unless the repository variable `GHCR_PACKAGE_CONFIRMED_PRIVATE` is `true`. That variable never overrides a public reading. The image does not set `org.opencontainers.image.source`, so that label does not link the package to this public repo or inherit its access. The workflow uses `GITHUB_TOKEN` only (`packages: write`, `contents: read`). The first run has to be the bootstrap input, which pushes a `FROM scratch` image tagged `bootstrap` and does not download the game. After that image exists, set the package to Private and grant `deenski/vs-wayfinder` access under Manage Actions access (role Write) so later publishes and CI pulls work. Game files are never committed, never redistributed, and never uploaded as workflow or release artifacts. The Actions cache and that private image are the only copies CI keeps. Until the image exists or the pull fails, `build-and-load` still uses the cache and then the checksum-verified download.

[`.github/workflows/release.yml`](.github/workflows/release.yml) runs on `v*` tags. It builds Release against the same server and attaches only `Releases/wayfinding_<version>.zip` to the GitHub Release.

### Publishing to the Mod DB

Uploading to the Vintage Story Mod DB is a manual step. The tag workflow does not do it.

1. Open the GitHub Release for the tag and download `wayfinding_<version>.zip`. Leave the server archive, the container image, and every game DLL where they are.
2. Sign in at <https://mods.vintagestory.at/>.
3. Open your mods. If Wayfinding does not exist yet, create it with mod id `wayfinding`.
4. Add a release on that mod and upload the zip from the GitHub Release.
5. Set the release version to the `version` in `modinfo.json`, select game compatibility 1.22, and paste the changelog.
6. Publish the Mod DB release.

## Teams

A team is a vanilla player group. By default that is every group the local player is in. `.nav team <group>` limits who you see to one group, and `.nav team` with no argument clears that limit.

Create a group and invite someone in game chat:

```text
/group create scouts
/group invite Maili
```

Maili accepts the invite from the group chat. Only members can post in that channel.

Share commands:

```text
.nav share on
.nav share off
.nav team
.nav team scouts
```

Sharing is on until you turn it off. That flag, and the `.nav team` limit, are saved on this client for the current world. `.nav share` posts one line to every group you belong to, for example `[Wayfinding] Maili is sharing location` or `[Wayfinding] Maili stopped sharing location`. The same message carries `wayfinding:v1:share:on:<playerUid>` or `wayfinding:v1:share:off:<playerUid>`. Joining a world, or a change to your group list, posts the current choice again so someone who was offline can catch up. The client log lists who is currently visible (`Wayfinding teammates visible: ...`) until the map and tether draw them.

The client cannot see another player's group membership. Position packets carry only the uid, X/Z, yaw, and despawn, so the only teammate evidence is a Wayfinding announcement in a group channel you are in. A remote player is shown only when both of these are true: you are in that group (and it is the group selected with `.nav team`, if you set one), and this session has seen a share-on announcement from that player in that channel. Anything unknown is hidden. Every later draw and target resolve has to ask `ITeamService.IsVisibleTeammate`. No line, marker, distance, or tether is shown otherwise.

This is stricter than design v2 line 145. Players who do not run the mod cannot announce, and they are never shown. The visibility invariant wins over treating every group member as a teammate.

The opt-out is client-only and best-effort:

- Online teammates see a share change on the next chat message, which is immediate once the server accepts it. The next draw or resolve hides them. There is no server-side guarantee.
- Vintage Story 1.22.7 does not relay the `data` argument of a client chat message. The server replaces it with `from: <entityId>,withoutPrefix:<message>`. The payload is therefore also written into the message text, and it is still passed in `data` for a server that keeps it. `ChatMessage` does not include a sender uid. Sender checks are fail-closed. The `from:` entity id must resolve to an online player whose entity is loaded, and that player's uid must match the uid in the payload. An unknown entity id, a known entity that is not loaded, a uid mismatch, a missing `from:` id, or a line on a group this client is not in is dropped. Share-on and share-off are treated the same: nothing is shown and no state changes. A later verified line, or the re-announce when someone joins, repairs it. The unverified drop is logged once per sender, at debug level only.
- Evidence about other players is kept only for this session. After a restart they stay hidden until they announce again. Leaving a group drops evidence for that channel, including if you rejoin, until a new announcement arrives.
- Several groups are announced one channel at a time so the server chat rate limit does not drop them. One group is immediate. A long group list can take more than 5 seconds to finish.
