using System;

namespace Wayfinding.Infra;

/// <summary>
/// How the active target's dimension compares with the local player.
/// <see cref="DimensionPresentation"/> tells the map layer to draw a line only for
/// <see cref="Same"/>. <see cref="Different"/> is the "different dimension" label and no line.
/// <see cref="Unconfirmed"/> draws nothing.
/// </summary>
public enum DimensionRelation
{
    Same = 0,
    Different = 1,
    Unconfirmed = 2,
}

/// <summary>
/// Dimension relation for our own navigation target. Player targets stay
/// unconfirmed until teammate positions exist.
/// </summary>
public sealed class OwnTargetDimensionDetector
{
    /// <param name="targetDimension">
    /// Stamped coordinate dimension, or the dimension decoded from a waypoint's
    /// position Y. Null when that dimension is not known yet.
    /// </param>
    public DimensionRelation Evaluate(int? playerDimension, NavTarget target, int? targetDimension)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Kind == NavTargetKind.None || playerDimension is null)
        {
            return DimensionRelation.Unconfirmed;
        }

        if (target.Kind == NavTargetKind.Player)
        {
            // TODO(KAN-22): teammate dimension comes from ITeammatePositionSource.
            // Until that source exists, a player target stays Unconfirmed. The map still
            // draws it; the tether hides a different dimension.
            return DimensionRelation.Unconfirmed;
        }

        if (targetDimension is null)
        {
            return DimensionRelation.Unconfirmed;
        }

        return targetDimension.Value == playerDimension.Value
            ? DimensionRelation.Same
            : DimensionRelation.Different;
    }
}
