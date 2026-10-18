using System;

namespace Wayfinding.Infra;

/// <summary>
/// Client hide inputs. Dimension comes from the local and target dimension
/// numbers the game reads off <c>EntityPos</c>. A missing number is unknown.
/// </summary>
public sealed class ClientTeammateTetherInputs : ITeammateTetherInputs
{
    private readonly Func<bool?> mapHide;
    private readonly Func<string?, bool?> spectator;
    private readonly Func<int?> localDimension;
    private readonly Func<string?, int?> targetDimension;
    private readonly Func<string?, bool?> sharing;
    private readonly Func<TeammatePoint?> localPosition;

    public ClientTeammateTetherInputs(
        Func<bool?> mapHide,
        Func<string?, bool?> spectator,
        Func<int?> localDimension,
        Func<string?, int?> targetDimension,
        Func<string?, bool?> sharing,
        Func<TeammatePoint?> localPosition)
    {
        this.mapHide = mapHide ?? throw new ArgumentNullException(nameof(mapHide));
        this.spectator = spectator ?? throw new ArgumentNullException(nameof(spectator));
        this.localDimension = localDimension ?? throw new ArgumentNullException(nameof(localDimension));
        this.targetDimension = targetDimension ?? throw new ArgumentNullException(nameof(targetDimension));
        this.sharing = sharing ?? throw new ArgumentNullException(nameof(sharing));
        this.localPosition = localPosition ?? throw new ArgumentNullException(nameof(localPosition));
    }

    public bool? MapHideOtherPlayers => mapHide();

    public bool? IsSpectator(string? playerNameOrUid)
    {
        return spectator(playerNameOrUid);
    }

    public bool? IsDimensionConfirmed(string? playerNameOrUid)
    {
        return Confirmed(localDimension(), targetDimension(playerNameOrUid));
    }

    /// <summary>
    /// Both dimensions known and equal is true. Both known and different is false.
    /// Either one missing is null.
    /// </summary>
    public static bool? Confirmed(int? localDimension, int? targetDimension)
    {
        if (localDimension is not int local || targetDimension is not int target)
        {
            return null;
        }

        return local == target;
    }

    public bool? IsSharing(string? playerNameOrUid)
    {
        return sharing(playerNameOrUid);
    }

    public bool TryGetLocalPosition(out TeammatePoint position)
    {
        if (localPosition() is TeammatePoint point)
        {
            position = point;
            return true;
        }

        position = default;
        return false;
    }
}
