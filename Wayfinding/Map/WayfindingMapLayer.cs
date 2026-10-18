using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
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

    static readonly Vec4f ArrowColor = new(1f, 1f, 1f, 0.95f);
    static readonly Vec4f RestoreColor = new(1f, 1f, 1f, 1f);
    static readonly System.Func<GuiElementMap, bool>? FollowsPlayer = CreateFollowsPlayerReader();

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
    bool disposed;
    bool armThrow;
    bool clickDisabled;
    bool waypointHitsDisabled;
    bool playerHitsDisabled;
    bool minimapReaderLogged;
    float dragPixels;
    bool dragOpen;
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
    }

    public void ArmRenderException()
    {
        armThrow = true;
    }

    public override void OnTick(float dt)
    {
        if (capi != null && !capi.Input.MouseButton.Left)
        {
            dragPixels = 0;
            dragOpen = false;
        }
    }

    public override void Render(GuiElementMap map, float dt)
    {
        if (disposed || !Active || capi == null || !mod.RenderGuard.LayerEnabled)
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
        if (capi == null)
        {
            return;
        }

        if (mapElem.IsDragingMap)
        {
            if (!dragOpen)
            {
                dragPixels = 0;
                dragOpen = true;
            }

            dragPixels += Math.Abs(args.DeltaX) + Math.Abs(args.DeltaY);
        }
        else
        {
            dragOpen = false;
            dragPixels = 0;
        }

        if (!Active || labelText == null)
        {
            return;
        }

        hoverText.AppendLine(labelText);
    }

    public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
    {
        if (capi == null || args.Handled || args.Button != EnumMouseButton.Left || !CtrlDown)
        {
            return;
        }

        bool inside = ViewPoint(args, mapElem, out float mouseX, out float mouseY);
        bool dragged = MapClick.IsDrag(dragPixels);
        dragPixels = 0;
        dragOpen = false;
        if (!Active || clickDisabled || !mod.IsEnabled(Feature.MapViewport)
            || !MapClick.IsNavigationClick(MapMouseButton.Left, ctrlHeld: true, dragged, inside))
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
                mod.Log.LogOnce("map-waypoint-hit", LogLevel.Error, "waypoint hit-testing disabled after an input error: " + ex.Message);
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
                mod.Log.LogOnce("map-player-hit", LogLevel.Error, "player hit-testing disabled after an input error: " + ex.Message);
            }
        }

        if (waypoint?.Position != null)
        {
            mod.Target.Set(waypoint.Position.X, waypoint.Position.Y, waypoint.Position.Z, NavTargetKind.Waypoint, waypoint.Guid);
            Say("waypoint '" + (waypoint.Title ?? waypoint.Guid) + "'");
            return;
        }

        if (player != null && TrackedPlayerPosition.TryHorizontal(player, out double playerX, out double playerZ))
        {
            double playerY = player.AssociatedPlayer?.Entity != null ? player.AssociatedPlayer.Entity.Pos.Y : 0;
            mod.Target.Set(playerX, playerY, playerZ, NavTargetKind.Coordinate, null);
            Say("player " + player.PlayerUid);
            return;
        }

        try
        {
            TryEmpty(mapElem, mouseX, mouseY);
        }
        catch (Exception ex)
        {
            clickDisabled = true;
            mod.Log.LogOnce("map-click", LogLevel.Error, "map click disabled after a coordinate error: " + ex.Message);
        }
    }

    public override void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
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
        if (map == null || !mod.Target.Has || capi?.World.Player?.Entity == null)
        {
            return;
        }

        var pos = capi.World.Player.Entity.Pos;
        GridPos from = CellOf(pos.X, pos.Y, pos.Z);
        GridPos to = CellOf(mod.Target.X, mod.Target.Y, mod.Target.Z);
        FineRoute route = StraightUnknown(from, to);
        UpdateLabel(pos.X, pos.Z, mod.Target.X, mod.Target.Z);
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
        bool minimap = IsMinimap(map);
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

        SegmentStyle style = SegmentStyles.For(kind);
        float thickness = (float)GuiElement.scaled(style.Thickness);
        if (goal && minimap && MapEdge.TryEdgePoint(x2, y2, view.InnerWidth, view.InnerHeight, MapEdge.ArrowBorder, out float edgeX, out float edgeY, out float angle))
        {
            DrawClipped(prog, x1, y1, edgeX, edgeY, originX, originY, view, style, thickness);
            DrawArrow(prog, edgeX + originX, edgeY + originY, angle);
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

    void DrawArrow(IShaderProgram prog, float tipX, float tipY, float angle)
    {
        float length = (float)GuiElement.scaled(14);
        float tailX = tipX - MathF.Cos(angle) * length;
        float tailY = tipY - MathF.Sin(angle) * length;
        prog.Uniform("rgbaIn", ArrowColor);
        DrawQuad(prog, tailX, tailY, tipX, tipY, (float)GuiElement.scaled(6));
    }

    void DrawLabel(GuiElementMap map, float edgeX, float edgeY, MapViewRect view)
    {
        if (capi == null || labelTexture == null || labelTexture.TextureId == 0)
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

    bool IsMinimap(GuiElementMap map)
    {
        if (FollowsPlayer == null)
        {
            if (!minimapReaderLogged)
            {
                minimapReaderLogged = true;
                mod.Log.LogOnce(
                    "map-minimap",
                    LogLevel.Warning,
                    "minimap edge arrow unavailable; GuiElementMap.snapToPlayer could not be read. Both maps clip the line.");
            }

            return false;
        }

        return FollowsPlayer(map);
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

        float half = RuntimeEnv.GUIScale * 8f;
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

        float half = (float)GuiElement.scaled(5);
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

        int y = capi.World.BlockAccessor.GetRainMapHeightAt((int)Math.Floor(worldX), (int)Math.Floor(worldZ));
        mod.Target.Set(worldX, y, worldZ, NavTargetKind.Coordinate, null);
        Say("coordinate");
    }

    void Say(string hit)
    {
        if (capi == null)
        {
            return;
        }

        var spawn = capi.World.DefaultSpawnPosition.AsBlockPos;
        int sx = (int)mod.Target.X - spawn.X;
        int sz = (int)mod.Target.Z - spawn.Z;
        capi.ShowChatMessage(
            "[wayfinding] target "
            + (int)mod.Target.X + ", " + (int)mod.Target.Y + ", " + (int)mod.Target.Z
            + " (spawn " + sx + ", " + sz + ") " + hit);
    }

    bool CtrlDown =>
        capi != null && (
            capi.Input.KeyboardKeyStateRaw[(int)GlKeys.LControl]
            || capi.Input.KeyboardKeyStateRaw[(int)GlKeys.RControl]);

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

    void UpdateLabel(double fromX, double fromZ, double toX, double toZ)
    {
        if (capi == null)
        {
            return;
        }

        int meters = MapMath.RoundedMeters(MapMath.HorizontalDistance(fromX, fromZ, toX, toZ));
        if (meters == labelMeters && labelTexture != null)
        {
            return;
        }

        labelMeters = meters;
        labelText = DistanceText.Format(meters);
        labelFont ??= CairoFont.WhiteDetailText();
        capi.Gui.TextTexture.GenOrUpdateTextTexture(labelText, labelFont, ref labelTexture);
    }

    static System.Func<GuiElementMap, bool>? CreateFollowsPlayerReader()
    {
        FieldInfo? field = typeof(GuiElementMap).GetField(
            "snapToPlayer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(bool))
        {
            return null;
        }

        try
        {
            ParameterExpression map = Expression.Parameter(typeof(GuiElementMap), "map");
            return Expression.Lambda<System.Func<GuiElementMap, bool>>(Expression.Field(map, field), map).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
