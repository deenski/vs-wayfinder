using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Wayfinding.Pathing.Map;

namespace Wayfinding;

/// <summary>
/// Client debug layer for KAN-17. Draws a hard-coded 3-segment line on whatever
/// <see cref="GuiElementMap"/> is rendering (minimap and world map share the layer list).
/// Render and input run on the game thread. <see cref="OnOffThreadTick"/> is left unused
/// so this layer never reads the world off-thread.
/// </summary>
public sealed class DebugMapLayer : MapLayer
{
    public const string LayerCode = "wayfinding";
    public const double SampleOffsetX = 96;
    public const double SampleOffsetZ = 48;

    const float LineZ = 60f;
    const int MaxDashes = 64;
    const int ArrowBorder = 2;

    public static DebugMapLayer? Instance { get; private set; }

    readonly ICoreClientAPI? capi;
    readonly Matrixf matrix = new();
    readonly Vec4f knownColor = new(0.27f, 0.51f, 0.71f, 0.95f);
    readonly Vec4f unknownColor = new(0.75f, 0.75f, 0.75f, 0.55f);
    readonly Vec4f buildColor = new(1f, 0.62f, 0.12f, 0.95f);
    Vec2f viewA = new();
    Vec3d worldScratch = new();
    Vec3d iconScratch = new();

    MeshRef? quad;
    bool hasTarget;
    double targetX;
    double targetY;
    double targetZ;
    bool guardDisabled;
    bool guardLogged;
    bool armThrow;
    float dragPixels;
    bool dragOpen;
    string? lastHit;

    public override string Title => "Wayfinding";
    public override string LayerGroupCode => LayerCode;
    public override EnumMapAppSide DataSide => EnumMapAppSide.Client;
    public override bool RequireChunkLoaded => false;

    public bool GuardDisabled => guardDisabled;
    public bool HasTarget => hasTarget;
    public string? LastHit => lastHit;

    public DebugMapLayer(ICoreAPI api, IWorldMapManager mapSink) : base(api, mapSink)
    {
        capi = api as ICoreClientAPI;
        Instance = this;
    }

    public string Status()
    {
        string target = hasTarget
            ? "target " + (int)targetX + ", " + (int)targetY + ", " + (int)targetZ
            : "no target";
        return target
            + (guardDisabled ? "; render guard disabled the layer" : "")
            + (armThrow ? "; render exception armed" : "")
            + (Active ? "" : "; tab hidden")
            + (lastHit is null ? "" : "; last hit " + lastHit);
    }

    public void SetSampleTarget()
    {
        if (capi?.World.Player?.Entity == null)
        {
            return;
        }

        var pos = capi.World.Player.Entity.Pos;
        SetTarget(pos.X + SampleOffsetX, pos.Y, pos.Z + SampleOffsetZ, "sample");
        guardDisabled = false;
    }

    public void ClearTarget()
    {
        hasTarget = false;
        lastHit = null;
    }

    public void ArmRenderException()
    {
        armThrow = true;
        guardDisabled = false;
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
        if (guardDisabled || !Active || capi == null)
        {
            return;
        }

        try
        {
            if (armThrow)
            {
                armThrow = false;
                throw new InvalidOperationException("wayfinding debug render exception");
            }

            if (!hasTarget || capi.World.Player?.Entity == null)
            {
                return;
            }

            Draw(map);
        }
        catch (Exception e)
        {
            guardDisabled = true;
            if (!guardLogged)
            {
                guardLogged = true;
                api.Logger.Error("[wayfinding] debug map layer disabled after a render error: " + e);
            }
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

        if (!CtrlDown || !TryViewPoint(args, mapElem, out float mouseX, out float mouseY))
        {
            return;
        }

        string hint = DescribeHit(mapElem, mouseX, mouseY);
        hoverText.AppendLine("Ctrl+click: " + hint);
    }

    public override void OnMouseUpClient(MouseEvent args, GuiElementMap mapElem)
    {
        if (capi == null || args.Handled || args.Button != EnumMouseButton.Left || !CtrlDown)
        {
            return;
        }

        bool inside = TryViewPoint(args, mapElem, out float mouseX, out float mouseY);
        bool dragged = MapClick.IsDrag(dragPixels);
        dragPixels = 0;
        dragOpen = false;
        if (!MapClick.IsNavigationClick(MapMouseButton.Left, ctrlHeld: true, dragged, inside))
        {
            return;
        }

        // Leave Handled false. GuiDialogWorldMap skips GuiElementMap.OnMouseUp when a layer
        // sets Handled, and that is what clears IsDragingMap.
        mapElem.TranslateViewPosToWorldPos(new Vec2f(mouseX, mouseY), ref worldScratch);
        double x = worldScratch.X;
        double y = worldScratch.Y;
        double z = worldScratch.Z;
        string hit = DescribeHit(mapElem, mouseX, mouseY);
        SetTarget(x, y, z, hit);

        var spawn = capi.World.DefaultSpawnPosition.AsBlockPos;
        int sx = (int)x - spawn.X;
        int sz = (int)z - spawn.Z;
        capi.ShowChatMessage(
            "[wayfinding] world "
            + (int)x + ", " + (int)y + ", " + (int)z
            + " (spawn " + sx + ", " + sz + ") " + hit);
    }

    public override void Dispose()
    {
        quad?.Dispose();
        quad = null;
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void SetTarget(double x, double y, double z, string hit)
    {
        hasTarget = true;
        targetX = x;
        targetY = y;
        targetZ = z;
        lastHit = hit;
    }

    bool CtrlDown =>
        capi != null && (
            capi.Input.KeyboardKeyStateRaw[(int)GlKeys.LControl]
            || capi.Input.KeyboardKeyStateRaw[(int)GlKeys.RControl]);

    void Draw(GuiElementMap map)
    {
        var pos = capi!.World.Player.Entity.Pos;
        quad ??= capi.Render.UploadMesh(QuadMeshUtil.GetQuad());

        IShaderProgram prog = capi.Render.GetEngineShader(EnumShaderProgram.Gui);
        capi.Render.PushScissor(map.Bounds, true);
        try
        {
            SetFlatColor(prog);
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);

            double x0 = pos.X;
            double z0 = pos.Z;
            Lerp(x0, z0, targetX, targetZ, 0.42, out double x1, out double z1);
            Lerp(x0, z0, targetX, targetZ, 0.72, out double x2, out double z2);
            DrawSegment(map, prog, x0, z0, x1, z1, knownColor, scaledThickness: 2.5, dashed: false);
            DrawSegment(map, prog, x1, z1, x2, z2, unknownColor, scaledThickness: 2.0, dashed: true);
            DrawSegment(map, prog, x2, z2, targetX, targetZ, buildColor, scaledThickness: 4.5, dashed: false);
        }
        finally
        {
            prog.Uniform("noTexture", 0f);
            capi.Render.PopScissor();
        }
    }

    void DrawSegment(
        GuiElementMap map,
        IShaderProgram prog,
        double x1,
        double z1,
        double x2,
        double z2,
        Vec4f color,
        double scaledThickness,
        bool dashed)
    {
        if (!ToScreen(map, x1, z1, out float ax, out float ay) || !ToScreen(map, x2, z2, out float bx, out float by))
        {
            return;
        }

        double left = map.Bounds.renderX;
        double top = map.Bounds.renderY;
        double right = left + map.Bounds.InnerWidth;
        double bottom = top + map.Bounds.InnerHeight;
        if (OffSameSide(ax, bx, left, right) || OffSameSide(ay, by, top, bottom))
        {
            return;
        }

        float thickness = (float)GuiElement.scaled(scaledThickness);
        prog.Uniform("rgbaIn", color);
        if (!dashed)
        {
            DrawQuad(prog, ax, ay, bx, by, thickness);
            return;
        }

        Span<DashSegment> dashes = stackalloc DashSegment[MaxDashes];
        float dash = (float)GuiElement.scaled(7);
        float gap = (float)GuiElement.scaled(5);
        int count = DashLayout.Copy(ax, ay, bx, by, dash, gap, MaxDashes, dashes);
        if (count < 0 || count > dashes.Length)
        {
            DrawQuad(prog, ax, ay, bx, by, thickness);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            DrawQuad(prog, dashes[i].X1, dashes[i].Y1, dashes[i].X2, dashes[i].Y2, thickness);
        }
    }

    void DrawQuad(IShaderProgram prog, float x1, float y1, float x2, float y2, float thickness)
    {
        if (!SegmentQuad.TryFromEndpoints(x1, y1, x2, y2, thickness, out SegmentQuad segment))
        {
            return;
        }

        matrix.Set(capi!.Render.CurrentModelviewMatrix)
            .Translate(segment.CenterX, segment.CenterY, LineZ);
        if (segment.AngleRadians != 0f)
        {
            matrix.RotateZ(segment.AngleRadians);
        }

        matrix.Scale(segment.HalfLength, segment.HalfThickness, 0f);
        prog.UniformMatrix("modelViewMatrix", matrix.Values);
        capi.Render.RenderMesh(quad);
    }

    string DescribeHit(GuiElementMap map, float mouseX, float mouseY)
    {
        if (capi == null)
        {
            return "empty";
        }

        float waypointHalf = RuntimeEnv.GUIScale * 8f;
        float playerHalf = (float)GuiElement.scaled(5);
        string? best = null;
        float bestDistance = float.MaxValue;

        var waypoints = capi.ModLoader.GetModSystem<WorldMapManager>().MapLayers;
        WaypointMapLayer? waypointLayer = null;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] is WaypointMapLayer layer)
            {
                waypointLayer = layer;
                break;
            }
        }

        if (waypointLayer != null)
        {
            for (int i = 0; i < waypointLayer.ownWaypoints.Count; i++)
            {
                Waypoint? wp = waypointLayer.ownWaypoints[i];
                if (wp?.Position == null)
                {
                    continue;
                }

                map.TranslateWorldPosToViewPos(wp.Position, ref viewA);
                float x = viewA.X;
                float y = viewA.Y;
                if (wp.Pinned)
                {
                    MapProjection.ClampPreserveAngle(ref x, ref y, map.Bounds.InnerWidth, map.Bounds.InnerHeight, ArrowBorder);
                    x = Math.Clamp(x, ArrowBorder, (float)map.Bounds.InnerWidth - ArrowBorder);
                    y = Math.Clamp(y, ArrowBorder, (float)map.Bounds.InnerHeight - ArrowBorder);
                }

                Consider(x, y, waypointHalf, "waypoint '" + wp.Title + "' " + wp.Guid, ref best, ref bestDistance, mouseX, mouseY);
            }
        }

        var tracking = capi.ModLoader.GetModSystem<SystemRemotePlayerTracking>();
        foreach (PacketPlayerPosition packet in tracking.GetAllTrackedPlayerPositions())
        {
            IPlayer? player = packet.AssociatedPlayer ?? capi.World.PlayerByUid(packet.PlayerUid);
            double px;
            double pz;
            if (player?.Entity != null)
            {
                px = player.Entity.Pos.X;
                pz = player.Entity.Pos.Z;
            }
            else
            {
                px = packet.PosX;
                pz = packet.PosZ;
            }

            iconScratch.Set(px, 0, pz);
            map.TranslateWorldPosToViewPos(iconScratch, ref viewA);
            string name = player?.PlayerName ?? packet.PlayerUid;
            Consider(viewA.X, viewA.Y, playerHalf, "player '" + name + "' " + packet.PlayerUid, ref best, ref bestDistance, mouseX, mouseY);
        }

        return best ?? "empty";
    }

    static void Consider(
        float iconX,
        float iconY,
        float half,
        string label,
        ref string? best,
        ref float bestDistance,
        float mouseX,
        float mouseY)
    {
        if (!MapHitTest.InsideSquare(iconX, iconY, mouseX, mouseY, half))
        {
            return;
        }

        float dx = iconX - mouseX;
        float dy = iconY - mouseY;
        float distance = dx * dx + dy * dy;
        if (distance < bestDistance)
        {
            bestDistance = distance;
            best = label;
        }
    }

    static bool ToScreen(GuiElementMap map, double worldX, double worldZ, out float screenX, out float screenY)
    {
        var view = new MapViewRect
        {
            BlockX1 = map.CurrentBlockViewBounds.X1,
            BlockZ1 = map.CurrentBlockViewBounds.Z1,
            BlockX2 = map.CurrentBlockViewBounds.X2,
            BlockZ2 = map.CurrentBlockViewBounds.Z2,
            InnerWidth = map.Bounds.InnerWidth,
            InnerHeight = map.Bounds.InnerHeight,
            OriginX = map.Bounds.renderX,
            OriginY = map.Bounds.renderY,
        };
        return MapProjection.TryWorldToView(view, worldX, worldZ, out screenX, out screenY);
    }

    static bool TryViewPoint(MouseEvent args, GuiElementMap map, out float x, out float y)
    {
        x = (float)(args.X - map.Bounds.renderX);
        y = (float)(args.Y - map.Bounds.renderY);
        return x >= 0 && y >= 0 && x <= map.Bounds.InnerWidth && y <= map.Bounds.InnerHeight;
    }

    static bool OffSameSide(double a, double b, double min, double max)
    {
        return (a < min && b < min) || (a > max && b > max);
    }

    static void Lerp(double ax, double az, double bx, double bz, double t, out double x, out double z)
    {
        x = ax + (bx - ax) * t;
        z = az + (bz - az) * t;
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
}
