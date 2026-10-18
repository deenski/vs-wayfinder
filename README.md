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

The pathing tests do not need a game install. `Wayfinding.Pathing` is a game-free library. Building that project fails if it references a game assembly (`Vintagestory`, `VintagestoryAPI`, `VSEssentials`, `VSSurvivalMod`, `0Harmony`, and the other game assemblies), a strong name such as `VintagestoryAPI, Version=...`, or any HintPath under the `VINTAGE_STORY` install.

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
- Vintage Story 1.22.7 does not relay the `data` argument of a client chat message. The server replaces it with `from: <entityId>,withoutPrefix:<message>`. The payload is therefore also written into the message text, and it is still passed in `data` for a server that keeps it. `ChatMessage` does not include a sender uid. When the online player list maps that entity id to a uid, a payload for a different uid is ignored. A player whose entity is not loaded (often someone far away) cannot be checked that way.
- Evidence about other players is kept only for this session. After a restart they stay hidden until they announce again. Leaving a group drops evidence for that channel, including if you rejoin, until a new announcement arrives.
- Several groups are announced one channel at a time so the server chat rate limit does not drop them. One group is immediate. A long group list can take more than 5 seconds to finish.
