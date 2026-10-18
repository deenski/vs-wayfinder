using System;
using System.Collections.Generic;
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
    readonly Vec4f restoreColor = new(1f, 1f, 1f, 1f);
    readonly float[] savedModelView = new float[16];
    readonly float[] savedProjection = new float[16];
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
    bool inputDisabled;
    bool inputLogged;
    bool clickDisabled;
    bool clickLogged;
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
            + (inputDisabled ? "; hit-testing disabled" : "")
            + (clickDisabled ? "; world click disabled" : "")
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

        if (!Active || inputDisabled || !CtrlDown || !TryViewPoint(args, mapElem, out float mouseX, out float mouseY))
        {
            return;
        }

        try
        {
            hoverText.AppendLine("Ctrl+click: " + DescribeHit(mapElem, mouseX, mouseY));
        }
        catch (Exception e)
        {
            NoteInputFailure(e);
        }
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
        if (!Active || !MapClick.IsNavigationClick(MapMouseButton.Left, ctrlHeld: true, dragged, inside))
        {
            return;
        }

        // Leave Handled false. GuiDialogWorldMap skips GuiElementMap.OnMouseUp when a layer
        // sets Handled, and that is what clears IsDragingMap.
        if (clickDisabled)
        {
            return;
        }

        double x;
        double y;
        double z;
        try
        {
            mapElem.TranslateViewPosToWorldPos(new Vec2f(mouseX, mouseY), ref worldScratch);
            x = worldScratch.X;
            y = worldScratch.Y;
            z = worldScratch.Z;
        }
        catch (Exception e)
        {
            NoteClickFailure(e);
            return;
        }

        string hit = "empty";
        if (!inputDisabled)
        {
            try
            {
                hit = DescribeHit(mapElem, mouseX, mouseY);
            }
            catch (Exception e)
            {
                NoteInputFailure(e);
            }
        }

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

    void NoteInputFailure(Exception e)
    {
        inputDisabled = true;
        if (inputLogged)
        {
            return;
        }

        inputLogged = true;
        api.Logger.Error("[wayfinding] debug map hit-testing disabled after an input error: " + e);
    }

    void NoteClickFailure(Exception e)
    {
        clickDisabled = true;
        if (clickLogged)
        {
            return;
        }

        clickLogged = true;
        api.Logger.Error("[wayfinding] debug map world click disabled after a coordinate error: " + e);
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
        bool restoreUniforms = false;
        try
        {
            CopyMatrix(capi.Render.CurrentModelviewMatrix, savedModelView);
            CopyMatrix(capi.Render.CurrentProjectionMatrix, savedProjection);
            restoreUniforms = true;
            SetFlatColor(prog);
            prog.UniformMatrix("projectionMatrix", savedProjection);

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
            if (restoreUniforms)
            {
                RestoreGuiUniforms(prog);
            }

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
        float bestDistance = float.MaxValue;
        Waypoint? hitWaypoint = null;
        PacketPlayerPosition? hitPlayer = null;

        WaypointMapLayer? waypointLayer = null;
        PlayerMapLayer? playerLayer = null;
        var layers = capi.ModLoader.GetModSystem<WorldMapManager>().MapLayers;
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] is WaypointMapLayer waypoints)
            {
                waypointLayer = waypoints;
            }
            else if (layers[i] is PlayerMapLayer players)
            {
                playerLayer = players;
            }
        }

        if (waypointLayer is { Active: true })
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

                if (TryCloser(x, y, waypointHalf, mouseX, mouseY, ref bestDistance))
                {
                    hitWaypoint = wp;
                    hitPlayer = null;
                }
            }
        }

        if (playerLayer is { Active: true })
        {
            foreach (PacketPlayerPosition packet in playerLayerPositionSource())
            {
                IPlayer? player = packet.AssociatedPlayer;
                double px = player?.Entity != null ? player.Entity.Pos.X : packet.PosX;
                double pz = player?.Entity != null ? player.Entity.Pos.Z : packet.PosZ;
                iconScratch.Set(px, 0, pz);
                map.TranslateWorldPosToViewPos(iconScratch, ref viewA);
                if (TryCloser(viewA.X, viewA.Y, playerHalf, mouseX, mouseY, ref bestDistance))
                {
                    hitPlayer = packet;
                    hitWaypoint = null;
                }
            }
        }

        if (hitWaypoint != null)
        {
            return "waypoint '" + hitWaypoint.Title + "' " + hitWaypoint.Guid;
        }

        if (hitPlayer != null)
        {
            IPlayer? player = hitPlayer.AssociatedPlayer ?? capi.World.PlayerByUid(hitPlayer.PlayerUid);
            string name = player?.PlayerName ?? hitPlayer.PlayerUid;
            return "player '" + name + "' " + hitPlayer.PlayerUid;
        }

        return "empty";

        IEnumerable<PacketPlayerPosition> playerLayerPositionSource()
        {
            return capi!.ModLoader.GetModSystem<SystemRemotePlayerTracking>().GetAllTrackedPlayerPositions();
        }
    }

    static bool TryCloser(
        float iconX,
        float iconY,
        float half,
        float mouseX,
        float mouseY,
        ref float bestDistance)
    {
        if (!MapHitTest.InsideSquare(iconX, iconY, mouseX, mouseY, half))
        {
            return false;
        }

        float dx = iconX - mouseX;
        float dy = iconY - mouseY;
        float distance = dx * dx + dy * dy;
        if (distance >= bestDistance)
        {
            return false;
        }

        bestDistance = distance;
        return true;
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

    void RestoreGuiUniforms(IShaderProgram prog)
    {
        // No getters on IShaderProgram. Put back the flags vanilla map icons set,
        // rgbaIn to white, and the matrices captured from IRenderAPI before the draw.
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
        prog.Uniform("rgbaIn", restoreColor);
        prog.UniformMatrix("modelViewMatrix", savedModelView);
        prog.UniformMatrix("projectionMatrix", savedProjection);
    }

    static void CopyMatrix(float[] source, float[] destination)
    {
        Array.Copy(source, destination, 16);
    }
}
