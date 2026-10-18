using System;
using System.Collections.Generic;

namespace Wayfinding.Infra;

/// <summary>
/// Members the mod reads from the game. Names were checked against the 1.22.7
/// assemblies. <see cref="MemberKind.Property"/> is for members such as
/// <c>GuiDialogWorldMap.DialogType</c>. The map layer no longer calls
/// <c>ClampButPreserveAngle</c> or <c>GetPlayerPositionInformation</c>.
/// </summary>
public static class FeatureCatalog
{
    public static readonly MemberRequirement MapLayers = new(
        "Vintagestory.GameContent.WorldMapManager",
        "MapLayers",
        MemberKind.Field);

    public static readonly MemberRequirement RegisterMapLayer = new(
        "Vintagestory.GameContent.WorldMapManager",
        "RegisterMapLayer",
        MemberKind.GenericMethodDefinition);

    public static readonly MemberRequirement CurrentBlockViewBounds = new(
        "Vintagestory.GameContent.GuiElementMap",
        "CurrentBlockViewBounds",
        MemberKind.Field);

    public static readonly MemberRequirement IsDragingMap = new(
        "Vintagestory.GameContent.GuiElementMap",
        "IsDragingMap",
        MemberKind.Field);

    public static readonly MemberRequirement OwnWaypoints = new(
        "Vintagestory.GameContent.WaypointMapLayer",
        "ownWaypoints",
        MemberKind.Field);

    public static readonly MemberRequirement MapElementLayers = new(
        "Vintagestory.GameContent.GuiElementMap",
        "mapLayers",
        MemberKind.Field);

    public static readonly MemberRequirement GetAllTrackedPlayerPositions = new(
        "Vintagestory.GameContent.SystemRemotePlayerTracking",
        "GetAllTrackedPlayerPositions",
        MemberKind.Method);

    public static readonly MemberRequirement PacketPlayerUid = new(
        "Vintagestory.GameContent.PacketPlayerPosition",
        "PlayerUid",
        MemberKind.Field);

    public static readonly MemberRequirement PacketPosX = new(
        "Vintagestory.GameContent.PacketPlayerPosition",
        "PosX",
        MemberKind.Field);

    public static readonly MemberRequirement PacketPosZ = new(
        "Vintagestory.GameContent.PacketPlayerPosition",
        "PosZ",
        MemberKind.Field);

    public static readonly MemberRequirement PacketDespawn = new(
        "Vintagestory.GameContent.PacketPlayerPosition",
        "Despawn",
        MemberKind.Field);

    public static readonly MemberRequirement PacketAssociatedPlayer = new(
        "Vintagestory.GameContent.PacketPlayerPosition",
        "AssociatedPlayer",
        MemberKind.Field);

    public static readonly MemberRequirement WorldMapDialog = new(
        "Vintagestory.GameContent.WorldMapManager",
        "worldMapDlg",
        MemberKind.Field);

    public static readonly MemberRequirement DialogType = new(
        "Vintagestory.GameContent.GuiDialogWorldMap",
        "DialogType",
        MemberKind.Property);

    public static readonly MemberRequirement DialogTypeHud = new(
        "Vintagestory.API.Client.EnumDialogType",
        "HUD",
        MemberKind.Field);

    public static readonly MemberRequirement RegisterHotKey = new(
        "Vintagestory.API.Client.IInputAPI",
        "RegisterHotKey",
        MemberKind.Method);

    public static readonly MemberRequirement SetHotKeyHandler = new(
        "Vintagestory.API.Client.IInputAPI",
        "SetHotKeyHandler",
        MemberKind.Method);

    public static readonly MemberRequirement GetHotKeyByCode = new(
        "Vintagestory.API.Client.IInputAPI",
        "GetHotKeyByCode",
        MemberKind.Method);

    public static readonly MemberRequirement MouseDown = new(
        "Vintagestory.API.Client.IClientEventAPI",
        "MouseDown",
        MemberKind.Event);

    public static readonly MemberRequirement DialogTypeDialog = new(
        "Vintagestory.API.Client.EnumDialogType",
        "Dialog",
        MemberKind.Field);

    private static readonly IReadOnlyList<MemberRequirement> MapLayerMembers =
    [
        MapLayers,
        RegisterMapLayer,
    ];

    private static readonly IReadOnlyList<MemberRequirement> MapViewportMembers =
    [
        CurrentBlockViewBounds,
        IsDragingMap,
    ];

    private static readonly IReadOnlyList<MemberRequirement> WaypointMembers =
    [
        OwnWaypoints,
        MapElementLayers,
    ];

    private static readonly IReadOnlyList<MemberRequirement> RemoteTrackingMembers =
    [
        GetAllTrackedPlayerPositions,
        PacketPlayerUid,
        PacketPosX,
        PacketPosZ,
        PacketDespawn,
        PacketAssociatedPlayer,
        MapElementLayers,
    ];

    private static readonly IReadOnlyList<MemberRequirement> MinimapEdgeMembers =
    [
        WorldMapDialog,
        DialogType,
        DialogTypeHud,
    ];

    private static readonly IReadOnlyList<MemberRequirement> ClientInputMembers =
    [
        RegisterHotKey,
        SetHotKeyHandler,
        GetHotKeyByCode,
        MouseDown,
        DialogTypeDialog,
    ];

    public static readonly IReadOnlyList<Feature> All =
    [
        Feature.MapLayer,
        Feature.MapViewport,
        Feature.Waypoints,
        Feature.RemoteTracking,
        Feature.MinimapEdge,
        Feature.ClientInput,
    ];

    public static IReadOnlyList<MemberRequirement> Requirements(Feature feature)
    {
        return feature switch
        {
            Feature.MapLayer => MapLayerMembers,
            Feature.MapViewport => MapViewportMembers,
            Feature.Waypoints => WaypointMembers,
            Feature.RemoteTracking => RemoteTrackingMembers,
            Feature.MinimapEdge => MinimapEdgeMembers,
            Feature.ClientInput => ClientInputMembers,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown feature."),
        };
    }
}
