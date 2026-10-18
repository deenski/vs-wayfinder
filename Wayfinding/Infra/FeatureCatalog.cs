using System;
using System.Collections.Generic;

namespace Wayfinding.Infra;

/// <summary>
/// Members the mod reads from the game. Full names were checked against the
/// 1.22.7 VSEssentials assembly (not VintagestoryAPI): namespace
/// Vintagestory.GameContent. RegisterMapLayer is an open generic method.
/// ownWaypoints is a public field; the matcher still accepts non-public.
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

    public static readonly MemberRequirement ClampButPreserveAngle = new(
        "Vintagestory.GameContent.GuiElementMap",
        "ClampButPreserveAngle",
        MemberKind.Method);

    public static readonly MemberRequirement OwnWaypoints = new(
        "Vintagestory.GameContent.WaypointMapLayer",
        "ownWaypoints",
        MemberKind.Field);

    public static readonly MemberRequirement GetPlayerPositionInformation = new(
        "Vintagestory.GameContent.SystemRemotePlayerTracking",
        "GetPlayerPositionInformation",
        MemberKind.Method);

    private static readonly IReadOnlyList<MemberRequirement> MapLayerMembers =
    [
        MapLayers,
        RegisterMapLayer,
    ];

    private static readonly IReadOnlyList<MemberRequirement> MapViewportMembers =
    [
        CurrentBlockViewBounds,
        ClampButPreserveAngle,
    ];

    private static readonly IReadOnlyList<MemberRequirement> WaypointMembers =
    [
        OwnWaypoints,
    ];

    private static readonly IReadOnlyList<MemberRequirement> RemoteTrackingMembers =
    [
        GetPlayerPositionInformation,
    ];

    public static readonly IReadOnlyList<Feature> All =
    [
        Feature.MapLayer,
        Feature.MapViewport,
        Feature.Waypoints,
        Feature.RemoteTracking,
    ];

    public static IReadOnlyList<MemberRequirement> Requirements(Feature feature)
    {
        return feature switch
        {
            Feature.MapLayer => MapLayerMembers,
            Feature.MapViewport => MapViewportMembers,
            Feature.Waypoints => WaypointMembers,
            Feature.RemoteTracking => RemoteTrackingMembers,
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unknown feature."),
        };
    }
}
