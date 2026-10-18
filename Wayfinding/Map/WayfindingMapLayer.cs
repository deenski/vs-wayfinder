using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Wayfinding.Infra;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Map;
using Wayfinding.Pathing.Search;

namespace Wayfinding;

/// <summary>
/// Production map layer. Draws the current route on the minimap and the world map.
/// The route for this ticket is one Unknown segment from the player to the target.
/// Owned by <see cref="WayfindingModSystem"/> so a world rejoin gets a new instance.
/// </summary>
public sealed class WayfindingMapLayer : MapLayer
{
    public const string LayerCode = "wayfinding";
    public const double SampleOffsetX = 96;
    public const double SampleOffsetZ = 48;

    const float LineZ = 60f;
    const float LabelZ = 70f;
    const int MaxDashes = 64;
    const float LabelOffset = 16f;

    static readonly Vec4f RestoreColor = new(1f, 1f, 1f, 1f);

    readonly WayfindingModSystem mod;
    readonly ICoreClientAPI? capi;
    readonly Action drawGuarded;
    readonly Matrixf matrix = new();
    readonly float[] savedModelView = new float[16];
    readonly float[] savedProjection = new float[16];

    MeshRef? quad;
    LoadedTexture? labelTexture;
    CairoFont? labelFont;
    string? labelText;
    int labelMeters = -1;
    int labelRevision;
    int targetRevision;
    NavTargets? watched;
    bool waypointDrawDisabled;
    int uploadedMeters = -1;
    long uploadedAtMs;
    bool disposed;
    bool armThrow;
    bool inputDisabled;
    bool clickDisabled;
    bool waypointHitsDisabled;
    bool playerHitsDisabled;
    bool minimapDisabled;
    bool mouseDownHooked;
    bool hasPress;
    MapMouseButton pressButton;
    int pressX;
    int pressY;
    FineRoute? straight;
    GridPos straightFrom;
    GridPos straightTo;
    GuiElementMap? drawing;

    public override string Title => "Wayfinding";
    public override string LayerGroupCode => LayerCode;
    public override EnumMapAppSide DataSide => EnumMapAppSide.Client;
    public override bool RequireChunkLoaded => false;

    public WayfindingMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
    {
        capi = api as ICoreClientAPI;
        mod = api.ModLoader.GetModSystem<WayfindingModSystem>();
        drawGuarded = RenderGuarded;
        Active = true;
        mod.AttachMapLayer(this);
        WatchTargets();
        if (capi != null && mod.IsEnabled(Feature.ClientInput))
        {
            capi.Event.MouseDown += OnMouseDown;
            mouseDownHooked = true;
        }
    }

    public void ArmRenderException()
    {
        armThrow = true;
    }

    public override void Render(GuiElementMap map, float dt)
    {
        if (disposed || !Active || capi == null || !mod.RenderGuard.LayerEnabled || !mod.RouteVisible)
        {
            return;
        }

        if (!mod.IsEnabled(Feature.MapViewport))
        {
            return;
        }

        drawing = map;
        try
        {
            mod.RenderGuard.Draw(drawGuarded);
        }
        finally
        {
            drawing = null;
        }
    }

    public override void OnMouseMoveClient(MouseEvent args, GuiElementMap mapElem, StringBuilder hoverText)
    {
        if (capi == null || !mod.RouteVisible)
        {
            return;
        }

        WatchTargets();
        DropStaleLabel();
        if (!Active || !SurfaceEnabled() || !HoverLabelPolicy.Show(HasTarget(), mod.RenderGuard.LayerEnabled))
        {
            return;
        }

        if (TryMeters(out int meters))
        {
            NoteMeters(meters);
        }
        else
        {
            labelText = null;
        }

        if (labelText != null)
        {
            hoverText.AppendLine(labelText);
        }
    }

    public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
    {
        if (!MapInputPolicy.AllowsInput(inputDisabled) || capi == null || args.Handled || !mod.IsEnabled(Feature.ClientInput))
        {
            return;
        }

        MapMouseButton button = ToButton(args.Button);
        bool sameButton = hasPress && pressButton == button;
        int downX = pressX;
        int downY = pressY;
        if (sameButton)
        {
            hasPress = false;
        }

        bool inside = ViewPoint(args, mapElem, out float mouseX, out float mouseY);
        if (!Active || clickDisabled || !mod.IsEnabled(Feature.MapViewport)
            || !MapSelection.IsSelection(mod.ClickBinding, button, CtrlHeld(), inside, IsWorldMapDialog(), sameButton, downX, downY, args.X, args.Y))
        {
            return;
        }

        float best = float.MaxValue;
        Waypoint? waypoint = null;
        PacketPlayerPosition? player = null;
        if (mod.IsEnabled(Feature.Waypoints) && !waypointHitsDisabled)
        {
            try
            {
                ConsiderWaypoints(mapElem, mouseX, mouseY, ref best, ref waypoint);
            }
            catch (Exception ex)
            {
                waypointHitsDisabled = true;
                mod.Log.LogOnce("map-waypoint-hit", LogLevel.Error, "waypoint hit-testing disabled after " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        if (mod.IsEnabled(Feature.RemoteTracking) && !playerHitsDisabled)
        {
            try
            {
                ConsiderPlayers(mapElem, mouseX, mouseY, ref best, ref waypoint, ref player);
            }
            catch (Exception ex)
            {
                playerHitsDisabled = true;
                mod.Log.LogOnce("map-player-hit", LogLevel.Error, "player hit-testing disabled after " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        bool applied;
        try
        {
            applied = TryApplyHit(waypoint, player);
        }
        catch (Exception ex)
        {
            inputDisabled = true;
            mod.Log.LogOnce("map-input", LogLevel.Error, "map input disabled after " + ex.GetType().Name + ": " + ex.Message);
            return;
        }

        if (applied)
        {
            return;
        }

        try
        {
            TryEmpty(mapElem, mouseX, mouseY);
        }
        catch (Exception ex)
        {
            clickDisabled = true;
            mod.Log.LogOnce("map-click", LogLevel.Error, "map click disabled after " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    public override void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (mouseDownHooked && capi != null)
        {
            capi.Event.MouseDown -= OnMouseDown;
            mouseDownHooked = false;
        }

        UnwatchTargets();
        quad?.Dispose();
        quad = null;
        labelTexture?.Dispose();
        labelTexture = null;
        mod.DetachMapLayer(this);
    }

    void RenderGuarded()
    {
        if (armThrow)
        {
            armThrow = false;
            throw new InvalidOperationException("wayfinding map layer render exception");
        }

        GuiElementMap? map = drawing;
        if (map == null || capi?.World.Player?.Entity == null || !SurfaceEnabled() || !TryResolve(out double targetX, out double targetY, out double targetZ))
        {
            return;
        }

        DropStaleLabel();
        var pos = capi.World.Player.Entity.Pos;
        GridPos from = CellOf(pos.X, pos.Y, pos.Z);
        GridPos to = CellOf(targetX, targetY, targetZ);
        FineRoute route = StraightUnknown(from, to);
        Draw(map, route);
    }

    FineRoute StraightUnknown(GridPos from, GridPos to)
    {
        if (straight != null && straightFrom == from && straightTo == to)
        {
            return straight;
        }

        straightFrom = from;
        straightTo = to;
        straight = new FineRoute(
            FineSearchStatus.UnknownBoundary,
            double.PositiveInfinity,
            [],
            [],
            new UnknownSegment(from, to),
            0,
            0);
        return straight;
    }

    void Draw(GuiElementMap map, FineRoute route)
    {
        if (capi == null || !TryView(map, out MapViewRect view))
        {
            return;
        }

        quad ??= capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
        IShaderProgram prog = capi.Render.GetEngineShader(EnumShaderProgram.Gui);
        bool minimap = IsMinimap();
        float originX = (float)map.Bounds.renderX;
        float originY = (float)map.Bounds.renderY;
        capi.Render.PushScissor(map.Bounds, true);
        bool restore = false;
        try
        {
            CopyMatrix(capi.Render.CurrentModelviewMatrix, savedModelView);
            CopyMatrix(capi.Render.CurrentProjectionMatrix, savedProjection);
            restore = true;
            SetFlatColor(prog);
            prog.UniformMatrix("projectionMatrix", savedProjection);

            IReadOnlyList<RouteStep> steps = route.Steps;
            bool tail = route.Unknown != null;
            for (int i = 1; i < steps.Count; i++)
            {
                bool goal = !tail && i == steps.Count - 1;
                DrawWorldSegment(map, prog, view, originX, originY, minimap, steps[i - 1].Pos, steps[i].Pos, steps[i].Segment, goal);
            }

            if (route.Unknown is UnknownSegment unknown)
            {
                DrawWorldSegment(map, prog, view, originX, originY, minimap, unknown.From, unknown.To, SegmentKind.Unknown, goal: true);
            }
        }
        finally
        {
            if (restore)
            {
                RestoreGuiUniforms(prog);
            }

            capi.Render.PopScissor();
        }
    }

    void DrawWorldSegment(
        GuiElementMap map,
        IShaderProgram prog,
        MapViewRect view,
        float originX,
        float originY,
        bool minimap,
        GridPos from,
        GridPos to,
        SegmentKind kind,
        bool goal)
    {
        if (!MapProjection.TryWorldToView(view, from.X, from.Z, out float x1, out float y1)
            || !MapProjection.TryWorldToView(view, to.X, to.Z, out float x2, out float y2))
        {
            return;
        }

        SegmentStyle style = SegmentStyles.For(kind, mod.Config);
        float thickness = (float)GuiElement.scaled(style.Thickness);
        if (goal && minimap && MapEdge.TryEdgePoint(x2, y2, view.InnerWidth, view.InnerHeight, MapEdge.ArrowBorder, out float edgeX, out float edgeY, out float angle))
        {
            DrawClipped(prog, x1, y1, edgeX, edgeY, originX, originY, view, style, thickness);
            DrawArrow(prog, edgeX + originX, edgeY + originY, angle, style.Color);
            DrawLabel(map, edgeX, edgeY, view);
            prog.Uniform("noTexture", 1f);
            prog.UniformMatrix("projectionMatrix", savedProjection);
            return;
        }

        DrawClipped(prog, x1, y1, x2, y2, originX, originY, view, style, thickness);
    }

    void DrawClipped(
        IShaderProgram prog,
        float x1,
        float y1,
        float x2,
        float y2,
        float originX,
        float originY,
        MapViewRect view,
        SegmentStyle style,
        float thickness)
    {
        if (!MapClip.TryClip(x1, y1, x2, y2, 0, 0, (float)view.InnerWidth, (float)view.InnerHeight, out float cx1, out float cy1, out float cx2, out float cy2))
        {
            return;
        }

        prog.Uniform("rgbaIn", style.Color);
        DrawStyled(prog, cx1 + originX, cy1 + originY, cx2 + originX, cy2 + originY, thickness, style.Dashed);
    }

    void DrawStyled(IShaderProgram prog, float x1, float y1, float x2, float y2, float thickness, bool dashed)
    {
        if (!dashed)
        {
            DrawQuad(prog, x1, y1, x2, y2, thickness);
            return;
        }

        Span<DashSegment> dashes = stackalloc DashSegment[MaxDashes];
        float dash = (float)GuiElement.scaled(7);
        float gap = (float)GuiElement.scaled(5);
        int count = DashLayout.Copy(x1, y1, x2, y2, dash, gap, MaxDashes, dashes);
        if (count < 0 || count > dashes.Length)
        {
            DrawQuad(prog, x1, y1, x2, y2, thickness);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            DrawQuad(prog, dashes[i].X1, dashes[i].Y1, dashes[i].X2, dashes[i].Y2, thickness);
        }
    }

    void DrawArrow(IShaderProgram prog, float tipX, float tipY, float angle, Vec4f color)
    {
        float length = (float)GuiElement.scaled(14);
        float tailX = tipX - MathF.Cos(angle) * length;
        float tailY = tipY - MathF.Sin(angle) * length;
        prog.Uniform("rgbaIn", color);
        DrawQuad(prog, tailX, tailY, tipX, tipY, (float)GuiElement.scaled(6));
    }

    void DrawLabel(GuiElementMap map, float edgeX, float edgeY, MapViewRect view)
    {
        if (capi == null || !TryMeters(out int meters))
        {
            return;
        }

        long now = capi.World.ElapsedMilliseconds;
        NoteMeters(meters);
        if (labelText == null)
        {
            return;
        }

        if (LabelUpload.ShouldUpload(meters, uploadedMeters, now, uploadedAtMs))
        {
            labelFont ??= CairoFont.WhiteDetailText();
            capi.Gui.TextTexture.GenOrUpdateTextTexture(labelText, labelFont, ref labelTexture);
            uploadedMeters = meters;
            uploadedAtMs = now;
        }

        if (labelTexture == null || labelTexture.TextureId == 0)
        {
            return;
        }

        MapEdge.LabelAnchor(edgeX, edgeY, view.InnerWidth, view.InnerHeight, LabelOffset, out float anchorX, out float anchorY);
        float drawX = (float)map.Bounds.renderX + anchorX - labelTexture.Width / 2f;
        float drawY = (float)map.Bounds.renderY + anchorY - labelTexture.Height / 2f;
        capi.Render.Render2DTexture(labelTexture.TextureId, drawX, drawY, labelTexture.Width, labelTexture.Height, LabelZ);
    }

    void DrawQuad(IShaderProgram prog, float x1, float y1, float x2, float y2, float thickness)
    {
        if (capi == null || quad == null || !SegmentQuad.TryFromEndpoints(x1, y1, x2, y2, thickness, out SegmentQuad segment))
        {
            return;
        }

        matrix.Set(capi.Render.CurrentModelviewMatrix)
            .Translate(segment.CenterX, segment.CenterY, LineZ);
        if (segment.AngleRadians != 0f)
        {
            matrix.RotateZ(segment.AngleRadians);
        }

        matrix.Scale(segment.HalfLength, segment.HalfThickness, 0f);
        prog.UniformMatrix("modelViewMatrix", matrix.Values);
        capi.Render.RenderMesh(quad);
    }

    internal bool TryHudLine(out string line, out int meters, out bool grey)
    {
        line = "";
        meters = 0;
        grey = false;
        if (!TryMeters(out meters))
        {
            return false;
        }

        NavTarget current = mod.NavTargets?.Current ?? NavTarget.None;
        line = HudLine.Format(
            HudLine.Label(current.Kind, current.Name),
            DistanceText.Format(meters),
            HudLine.ModeBadge,
            HudLine.Status(current.Kind));
        if (current.Kind == NavTargetKind.Player && mod.TeammateSample is TeammateTetherSample sample)
        {
            line = HudLine.WithTether(line, sample.Kind, sample.Age, out grey);
        }

        return true;
    }

    bool SurfaceEnabled()
    {
        return IsMinimap() ? mod.Config.showOnMinimap : mod.Config.showOnWorldMap;
    }

    bool IsMinimap()
    {
        if (!mod.IsEnabled(Feature.MinimapEdge) || minimapDisabled || capi == null)
        {
            return false;
        }

        try
        {
            return DialogIsHud();
        }
        catch (Exception ex)
        {
            minimapDisabled = true;
            mod.Log.LogOnce("map-minimap", LogLevel.Error, "minimap edge disabled after " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    bool DialogIsHud()
    {
        return capi!.ModLoader.GetModSystem<WorldMapManager>().worldMapDlg?.DialogType == EnumDialogType.HUD;
    }

    // Don't set Handled. The game still pans on left-down and still opens the waypoint dialog on right-up.
    // shortcut: MouseDown has no map bounds, so a press just outside the map can still select when the
    // release is inside and within 5 px. A longer drag from outside is rejected by that distance.
    void OnMouseDown(MouseEvent args)
    {
        pressButton = ToButton(args.Button);
        pressX = args.X;
        pressY = args.Y;
        hasPress = true;
    }

    static MapMouseButton ToButton(EnumMouseButton button)
    {
        return button switch
        {
            EnumMouseButton.Left => MapMouseButton.Left,
            EnumMouseButton.Middle => MapMouseButton.Middle,
            EnumMouseButton.Right => MapMouseButton.Right,
            _ => MapMouseButton.Other,
        };
    }

    bool IsWorldMapDialog()
    {
        if (capi == null)
        {
            return false;
        }

        try
        {
            return capi.ModLoader.GetModSystem<WorldMapManager>().worldMapDlg?.DialogType == EnumDialogType.Dialog;
        }
        catch (Exception ex)
        {
            clickDisabled = true;
            mod.Log.LogOnce("map-click", LogLevel.Error, "map click disabled after " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    bool TryApplyHit(Waypoint? waypoint, PacketPlayerPosition? player)
    {
        NavTargets? targets = mod.NavTargets;
        if (targets == null)
        {
            return false;
        }

        if (waypoint?.Position != null && Guid.TryParse(waypoint.Guid, out Guid id) && id != Guid.Empty)
        {
            Say(targets.SetWaypoint(id, waypoint.Title).Message);
            return true;
        }

        // shortcut: NavTargets.Player stays unused until a teammate position source exists.
        // A visible player icon sets a coordinate at that position. Hidden players never get here.
        if (player != null && TrackedPlayerPosition.TryHorizontal(player, out double playerX, out double playerZ))
        {
            Say(targets.SetCoordinate(FloorCoord(playerX, PlayerFeetY(player), playerZ), Spawn()).Message);
            return true;
        }

        return false;
    }

    static double? PlayerFeetY(PacketPlayerPosition player)
    {
        var entity = player.AssociatedPlayer?.Entity;
        return entity != null ? entity.Pos.Y : null;
    }

    static void SetFlatColor(IShaderProgram prog)
    {
        prog.Uniform("noTexture", 1f);
        prog.Uniform("alphaTest", 0f);
        prog.Uniform("darkEdges", 0);
        prog.Uniform("transparentCenter", 0);
        prog.Uniform("normalShaded", 0);
        prog.Uniform("tempGlowMode", 0);
        prog.Uniform("sepiaLevel", 0f);
        prog.Uniform("damageEffect", 0f);
        prog.Uniform("overlayOpacity", 0f);
        prog.Uniform("applyColor", 0);
        prog.Uniform("applyAnimation", 0);
        prog.Uniform("applyModelMat", 0);
        prog.Uniform("extraGlow", 0);
    }

    void RestoreGuiUniforms(IShaderProgram prog)
    {
        prog.Uniform("noTexture", 0f);
        prog.Uniform("alphaTest", 0f);
        prog.Uniform("darkEdges", 0);
        prog.Uniform("transparentCenter", 0);
        prog.Uniform("normalShaded", 0);
        prog.Uniform("tempGlowMode", 0);
        prog.Uniform("sepiaLevel", 0f);
        prog.Uniform("damageEffect", 0f);
        prog.Uniform("overlayOpacity", 0f);
        prog.Uniform("applyColor", 0);
        prog.Uniform("applyAnimation", 0);
        prog.Uniform("applyModelMat", 0);
        prog.Uniform("extraGlow", 0);
        prog.Uniform("rgbaIn", RestoreColor);
        prog.UniformMatrix("modelViewMatrix", savedModelView);
        prog.UniformMatrix("projectionMatrix", savedProjection);
    }

    static void CopyMatrix(float[] source, float[] destination)
    {
        Array.Copy(source, destination, 16);
    }

    void ConsiderWaypoints(GuiElementMap map, float mouseX, float mouseY, ref float best, ref Waypoint? hit)
    {
        if (!TryView(map, out MapViewRect view))
        {
            return;
        }

        WaypointMapLayer? waypointLayer = null;
        for (int i = 0; i < map.mapLayers.Count; i++)
        {
            if (map.mapLayers[i] is WaypointMapLayer found)
            {
                waypointLayer = found;
                break;
            }
        }

        if (waypointLayer is not { Active: true })
        {
            return;
        }

        float half = RuntimeEnv.GUIScale * mod.Config.waypointClickRadius;
        for (int i = 0; i < waypointLayer.ownWaypoints.Count; i++)
        {
            Waypoint? wp = waypointLayer.ownWaypoints[i];
            if (wp?.Position == null)
            {
                continue;
            }

            if (!MapProjection.TryWorldToView(view, wp.Position.X, wp.Position.Z, out float x, out float y))
            {
                continue;
            }

            if (wp.Pinned)
            {
                MapProjection.ClampPreserveAngle(ref x, ref y, view.InnerWidth, view.InnerHeight, MapEdge.ArrowBorder);
                x = Math.Clamp(x, MapEdge.ArrowBorder, (float)view.InnerWidth - MapEdge.ArrowBorder);
                y = Math.Clamp(y, MapEdge.ArrowBorder, (float)view.InnerHeight - MapEdge.ArrowBorder);
            }

            if (!Closer(x, y, half, mouseX, mouseY, ref best))
            {
                continue;
            }

            hit = wp;
        }
    }

    void ConsiderPlayers(
        GuiElementMap map,
        float mouseX,
        float mouseY,
        ref float best,
        ref Waypoint? waypoint,
        ref PacketPlayerPosition? player)
    {
        if (capi == null || !TryView(map, out MapViewRect view))
        {
            return;
        }

        PlayerMapLayer? playerLayer = null;
        for (int i = 0; i < map.mapLayers.Count; i++)
        {
            if (map.mapLayers[i] is PlayerMapLayer found)
            {
                playerLayer = found;
                break;
            }
        }

        if (playerLayer is not { Active: true })
        {
            return;
        }

        float half = (float)GuiElement.scaled(mod.Config.playerClickRadius);
        foreach (PacketPlayerPosition packet in TrackedPlayers(capi))
        {
            if (!TrackedPlayerPosition.TryHorizontal(packet, out double px, out double pz))
            {
                continue;
            }

            if (!MapProjection.TryWorldToView(view, px, pz, out float x, out float y))
            {
                continue;
            }

            if (!PlayerPrivacy.MayHit(packet.PlayerUid, TeammateVisible(packet.PlayerUid)))
            {
                continue;
            }

            if (!Closer(x, y, half, mouseX, mouseY, ref best))
            {
                continue;
            }

            player = packet;
            waypoint = null;
        }
    }

    static IEnumerable<PacketPlayerPosition> TrackedPlayers(ICoreClientAPI capi)
    {
        return capi.ModLoader.GetModSystem<SystemRemotePlayerTracking>().GetAllTrackedPlayerPositions();
    }

    static bool Closer(float iconX, float iconY, float half, float mouseX, float mouseY, ref float best)
    {
        if (!MapHit.InsideSquare(iconX, iconY, mouseX, mouseY, half))
        {
            return false;
        }

        float dx = iconX - mouseX;
        float dy = iconY - mouseY;
        float distance = dx * dx + dy * dy;
        if (distance >= best)
        {
            return false;
        }

        best = distance;
        return true;
    }

    static GridPos CellOf(double x, double y, double z)
    {
        return new GridPos((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
    }

    void TryEmpty(GuiElementMap map, float mouseX, float mouseY)
    {
        if (capi == null || !TryView(map, out MapViewRect view))
        {
            return;
        }

        if (!MapProjection.TryViewToWorld(view, mouseX, mouseY, out double worldX, out double worldZ))
        {
            return;
        }

        NavTargets? targets = mod.NavTargets;
        if (targets == null)
        {
            return;
        }

        int y = capi.World.BlockAccessor.GetRainMapHeightAt((int)Math.Floor(worldX), (int)Math.Floor(worldZ));
        Say(targets.SetCoordinate(FloorCoord(worldX, y, worldZ), Spawn()).Message);
    }

    void Say(string message)
    {
        capi?.ShowChatMessage("[wayfinding] " + message);
    }

    SpawnOrigin? Spawn()
    {
        var pos = capi?.World?.DefaultSpawnPosition;
        if (pos == null)
        {
            return null;
        }

        var block = pos.AsBlockPos;
        return new SpawnOrigin(block.X, block.Z);
    }

    static AbsoluteCoord FloorCoord(double x, double? y, double z)
    {
        return new AbsoluteCoord((int)Math.Floor(x), y is double feet ? (int)Math.Floor(feet) : null, (int)Math.Floor(z));
    }

    bool TeammateVisible(string? uid)
    {
        ITeammateVisibility visibility = mod.Teams as ITeammateVisibility ?? NoTeammateVisibility.Instance;
        return visibility.IsVisibleTeammate(uid);
    }

    bool CtrlHeld()
    {
        if (capi == null)
        {
            return false;
        }

        try
        {
            bool right = KeyDown((int)GlKeys.RControl);
            var ctrl = capi.Input.GetHotKeyByCode("ctrl");
            int code = ctrl?.CurrentMapping == null ? -1 : ctrl.CurrentMapping.KeyCode;
            return KeyDown(code) || right;
        }
        catch (Exception ex)
        {
            inputDisabled = true;
            mod.Log.LogOnce("map-input", LogLevel.Error, "map input disabled after " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    bool KeyDown(int code)
    {
        var keys = capi!.Input.KeyboardKeyStateRaw;
        return (uint)code < (uint)keys.Length && keys[code];
    }

    static bool ViewPoint(MouseEvent args, GuiElementMap map, out float x, out float y)
    {
        x = (float)(args.X - map.Bounds.renderX);
        y = (float)(args.Y - map.Bounds.renderY);
        return x >= 0 && y >= 0 && x <= map.Bounds.InnerWidth && y <= map.Bounds.InnerHeight;
    }

    static bool TryView(GuiElementMap map, out MapViewRect view)
    {
        var bounds = map.CurrentBlockViewBounds;
        view = new MapViewRect
        {
            BlockX1 = bounds.X1,
            BlockZ1 = bounds.Z1,
            BlockX2 = bounds.X2,
            BlockZ2 = bounds.Z2,
            InnerWidth = map.Bounds.InnerWidth,
            InnerHeight = map.Bounds.InnerHeight,
            OriginX = 0,
            OriginY = 0,
        };
        return view.BlockWidth != 0 && view.BlockHeight != 0 && view.InnerWidth != 0 && view.InnerHeight != 0;
    }

    void DropStaleLabel()
    {
        if (!HoverLabelPolicy.ShouldReset(labelRevision, targetRevision, HasTarget(), mod.RenderGuard.LayerEnabled))
        {
            return;
        }

        labelText = null;
        labelMeters = -1;
        uploadedMeters = -1;
        labelRevision = targetRevision;
    }

    void WatchTargets()
    {
        NavTargets? targets = mod.NavTargets;
        if (ReferenceEquals(watched, targets))
        {
            return;
        }

        UnwatchTargets();
        if (targets == null)
        {
            return;
        }

        targets.Changed += OnTargetChanged;
        watched = targets;
        targetRevision++;
    }

    void UnwatchTargets()
    {
        if (watched == null)
        {
            return;
        }

        watched.Changed -= OnTargetChanged;
        watched = null;
    }

    void OnTargetChanged()
    {
        targetRevision++;
    }

    bool HasTarget()
    {
        NavTargets? targets = mod.NavTargets;
        return targets != null && targets.MapAllowed && targets.Current.Kind != NavTargetKind.None;
    }

    bool TryResolve(out double x, out double y, out double z)
    {
        x = 0;
        y = 0;
        z = 0;
        NavTargets? targets = mod.NavTargets;
        if (targets == null || !targets.MapAllowed)
        {
            return false;
        }

        NavTarget current = targets.Current;
        if (current.Kind == NavTargetKind.Coordinate && current.Coordinate is AbsoluteCoord coord)
        {
            x = coord.X;
            y = coord.Y ?? 0;
            z = coord.Z;
            return true;
        }

        if (current.Kind == NavTargetKind.Player)
        {
            return TryPlayerTether(out x, out y, out z);
        }

        if (current.Kind != NavTargetKind.Waypoint || !mod.IsEnabled(Feature.Waypoints) || waypointDrawDisabled)
        {
            return false;
        }

        try
        {
            return TryWaypointPosition(current.WaypointId, out x, out y, out z);
        }
        catch (Exception ex)
        {
            waypointDrawDisabled = true;
            mod.Log.LogOnce("map-waypoint-draw", LogLevel.Error, "waypoint target drawing disabled after " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Player targets use the tether sample. That sample already applied
    /// <see cref="ITeammatePositionSource"/> (entity, then remote tracking),
    /// the greyed last-known and 30 s stale rules, hysteresis, and the hide
    /// conditions. Read on the render thread, which is the client main thread.
    /// </summary>
    bool TryPlayerTether(out double x, out double y, out double z)
    {
        x = 0;
        y = 0;
        z = 0;
        if (mod.TeammateSample is not TeammateTetherSample sample || !sample.Visible || !sample.HasPosition)
        {
            return false;
        }

        x = sample.Position.X;
        y = sample.Position.Y ?? 0;
        z = sample.Position.Z;
        return true;
    }

    bool TryWaypointPosition(Guid id, out double x, out double y, out double z)
    {
        x = 0;
        y = 0;
        z = 0;
        if (capi == null)
        {
            return false;
        }

        var layers = capi.ModLoader.GetModSystem<WorldMapManager>().MapLayers;
        if (layers == null)
        {
            return false;
        }

        WaypointMapLayer? waypointLayer = null;
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] is WaypointMapLayer found)
            {
                waypointLayer = found;
                break;
            }
        }

        if (waypointLayer?.ownWaypoints == null)
        {
            return false;
        }

        for (int i = 0; i < waypointLayer.ownWaypoints.Count; i++)
        {
            Waypoint? wp = waypointLayer.ownWaypoints[i];
            if (wp?.Position == null || !Guid.TryParse(wp.Guid, out Guid wpId) || wpId != id)
            {
                continue;
            }

            x = wp.Position.X;
            y = wp.Position.Y;
            z = wp.Position.Z;
            return true;
        }

        return false;
    }

    void NoteMeters(int meters)
    {
        string text = DistanceText.Format(meters);
        if (mod.TeammateSample is TeammateTetherSample sample)
        {
            string? age = TeammateAgeText.For(sample.Kind, sample.Age);
            if (age != null)
            {
                text = text + " " + age;
            }
        }

        if (meters == labelMeters && labelText == text)
        {
            return;
        }

        // Age can change while the rounded meters stay put. The upload rule
        // skips an unchanged distance, so an age-only change uploads once.
        if (meters == labelMeters)
        {
            uploadedMeters = -1;
        }

        labelMeters = meters;
        labelText = text;
    }

    bool TryMeters(out int meters)
    {
        meters = 0;
        if (capi?.World.Player?.Entity == null || !TryResolve(out double targetX, out _, out double targetZ))
        {
            return false;
        }

        var pos = capi.World.Player.Entity.Pos;
        meters = MapMath.RoundedMeters(MapMath.HorizontalDistance(pos.X, pos.Z, targetX, targetZ));
        return true;
    }
}
