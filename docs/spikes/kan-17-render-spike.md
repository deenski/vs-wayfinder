# KAN-17 render spike (WF-3)

Game 1.22.7 (`vsessentialsmod` `06673b6731`, `vsapi` `63d33f7001`, server archive MD5 `3031212903b1c283084d5fe0cda6bce6` from `api.vintagestory.at/stable.json`). The client was not run. `dotnet build` and `dotnet test` are the checks that ran here. The checklist at the bottom is still required in game.

## Decisions

**Draw per-segment view quads, not one world-coordinate mesh.**

`gui.vsh` sets `gl_Position = projectionMatrix * modelViewMatrix * vertex` and `color = rgbaIn` unless `applyColor` is 1. `QuadMeshUtil.GetQuad()` has no vertex color, and vanilla map icons set `applyColor` to 0, so one draw is one color. `TranslateWorldPosToViewPos` is affine: view pixel = `(world - blockOrigin) / blockSize * innerSize`. A mesh stored in world X/Z could be transformed by that matrix (`MapProjection.ViewTransform`, unit-tested against the point formula). It is the wrong primitive for the route:

- Thickness would be in blocks, so zoom changes the line width. The route wants a stable pixel width (`GuiElement.scaled`).
- Known / Unknown / Build need different color, dash, and thickness. Dashes in world units also scale with zoom.
- The player end moves every frame, and the edge marker is a view-space `ClampButPreserveAngle`. The mesh would be re-uploaded anyway.

The spike uploads one unit quad and draws one transformed quad per segment (several for dashes), the same path as Translocator Paths and TL Path. KAN-19 should keep that.

**Teammates use the group-chat and position-packet fallback.**

`PacketPlayerPosition` in 1.22.7 is UID, X, Z, yaw, and a despawn flag. No group id and no Y. The server decides who receives the packet (same group, or within `mapPlayerRenderDistance`). KAN-14 already found that a remote `IPlayer.Groups` is not filled on the client. This spike does not read remote groups. WF-7 should treat a group-chat announcement, plus these X/Z packets, as the evidence that someone is a teammate.

**Claims: use `capi.World.Claims` areas, not `TestAccess`.**

`IWorldAccessor.Claims` is `ILandClaimAPI`. `TestAccess` and `TryAccess` are documented to return true on the client. `VintagestoryLib` has `Packet_LandClaims` (`Allclaims`, `Addclaims`) and `HandleLandClaims` / `SendClaims` / `BroadcastClaims`, which is a full-list plus incremental sync. Area tests in this repo copy `Cuboidi.Contains`: half-open, `>= min && < max`, either corner order (`ClaimBox`). Do not call `TestAccess`. Confirm in game that `Claims.All` is non-empty after a claim exists; that was not run here.

## What the prototype does

`DebugMapLayer` is a client `MapLayer` (`DataSide = Client`, group `wayfinding`, its own tab). `WayfindingModSystem.StartClientSide` calls `WorldMapManager.RegisterMapLayer` before level finalize, at position 1.1. Both the minimap and the world map host a `GuiElementMap` over the same layer list, so `Render` runs for each.

The demo path is three segments from the player to the target, recomputed every frame. It is not the KAN-26 route type.

| Span | Style |
|---|---|
| 0–42% | Known: solid steel blue |
| 42–72% | Unknown: grey dashes |
| 72–100% | Build: thicker amber |

`.nav debug on` aims the target 96 blocks east and 48 south of the player (off a default minimap, on the default world map). Ctrl+left-click on mouse-up replaces it. `.nav debug off` clears it.

## Hooks

`MapLayer` has `Render`, `OnMouseMoveClient`, and `OnMouseUpClient`. There is no mouse-down hook. `GuiElementMap.OnMouseDownOnElement` sets `IsDragingMap` for every left press, and `GuiDialogWorldMap.OnMouseUp` runs layer handlers *before* `GuiElementMap.OnMouseUp`, which is what clears `IsDragingMap`. Setting `Handled` therefore leaves the map dragging. This layer never sets `Handled`. Right-click returns immediately so the vanilla waypoint dialog still opens.

A click is navigation only when `MapClick` says so: left button, Ctrl (raw `GlKeys.LControl` / `RControl`, not the rebindable `Controls.CtrlKey`), pointer inside the element, and accumulated move while `IsDragingMap` was set is at most 5 pixels. `OnTick` zeroes that accumulator once the left button is up, including when the button was released outside the dialog.

World position comes from `GuiElementMap.TranslateViewPosToWorldPos` (absolute X/Z, Y from `GetRainMapHeightAt`). That call returns 0 when the map chunk is missing, so a printed Y of 0 can mean "no column" rather than sea level. The chat line also prints spawn-relative X/Z, which is what the vanilla hover label shows. Hit-testing uses `ownWaypoints` (the client list; `Waypoints` is the server list) and `SystemRemotePlayerTracking` positions, and only while that layer's own tab is `Active`. The label string is built for the closest hit, not for every icon. Pinned waypoints are passed through the same `ClampButPreserveAngle` the icon uses, then a square of `RuntimeEnv.GUIScale * 8`. Player icons use `GuiElement.scaled(5)`, matching `PlayerMapLayer`. Empty map reports `empty`. A failure inside hit-testing (including a missing field or method) is logged once with `[wayfinding]` and disables hit-testing only. A failure translating the click to world coordinates is logged once and disables the world click only. The line keeps drawing. A hidden Wayfinding tab ignores hover and click the same way the waypoint and player tabs do.

## Mesh and shader

Endpoints go through `MapProjection.TryWorldToView` (the game formula, plus `Bounds.renderX/Y`). `SegmentQuad` turns a segment into center, half-length, half-thickness, and `atan2` radians. `Matrixf` is set from `CurrentModelviewMatrix`, then translate, `RotateZ`, scale. No `GlPushMatrix`. Z is 60, same as the other map quads.

The Gui shader is already bound for the GUI pass. This layer does not call `Use` or `Stop`. It sets `noTexture` to 1 and the other flat-color uniforms (`alphaTest`, `darkEdges`, `transparentCenter`, `normalShaded`, `applyColor`, `applyAnimation`, …) because leftover state can drop the line. The shader API has no uniform getters, so `finally` puts every uniform this layer writes back to a known state: `noTexture` 0 and the other flags 0 (what vanilla map icons set), `rgbaIn` white, and `modelViewMatrix` / `projectionMatrix` copied from `IRenderAPI` before the draw. It also pops the extra scissor. `GuiElementMap` already scissors to the element; the extra scissor matches the line mods and keeps a segment that misses the element from painting the HUD.

`Render` catches exceptions, logs `[wayfinding]` once, and then returns immediately. `.nav debug throw` arms one exception on the next render. `.nav debug on` clears the disable flag so the line can be drawn again; the log stays once per session.

## Follow-ups for KAN-19

- Replace `DebugMapLayer` with `WayfindingMapLayer`. Keep the group code `wayfinding` and the quad path.
- Feed it KAN-26 segments instead of the 42/72 split. Draw each segment with its kind. Do not add that result type in this spike.
- Edge arrow and "412 m" label: `ClampPreserveAngle` and `MapMath.RoundedMeters` are already tested. Draw the arrow in view space at the clamped point. This spike only clips with the scissor.
- Move the layer under waypoint icons if the line should not cover them (`MapLayers` order).
- The Release zip target still packs only `modinfo.json` and the two DLLs. No asset was added here. Ask Senior before relying on a lang file for the tab title (`maplayer-wayfinding` until then).

## In-game checklist

1. `.nav debug on`. Minimap shows a 3-part line. Open the world map (M): the same line is there. Default zoom: the far end is off the minimap and on the world map.
2. Zoom in and out on both. The line stays about the same pixel width. Drag the world map: the line stays on the world point and the drag does not set a target.
3. Ctrl+click empty map: chat prints absolute world X,Y,Z and spawn-relative X,Z, and `empty`. The line retargets. Ctrl+click a waypoint icon and a player icon: the chat names that icon. Right-click still opens the waypoint dialog. A Ctrl+drag pans and does not retarget.
4. Toggle the Wayfinding tab. The line hides. Show it again.
5. `.nav debug throw`. The line disappears, `[wayfinding]` is logged once, and the rest of the HUD still draws. `.nav debug on` draws again. A second throw does not log a second line.
6. `.nav debug off` clears the line.
