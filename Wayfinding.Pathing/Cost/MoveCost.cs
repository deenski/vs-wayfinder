namespace Wayfinding.Pathing.Cost;

/// <summary>Whether a move can be taken.</summary>
public enum MoveVerdict : byte
{
    /// <summary>The move is walkable (or swimmable) at <see cref="MoveCost.Cost"/>.</summary>
    Ok = 0,

    /// <summary>The move is known to be impossible or forbidden. Cost is +∞.</summary>
    Blocked = 1,

    /// <summary>
    /// A cell needed to decide is <see cref="Grid.CellClass.Unknown"/>. Cost is +∞. Planners stop here and hand
    /// the rest to an Unknown straight segment (WF-12), rather than treating it as blocked terrain.
    /// </summary>
    Unknown = 2,
}

/// <summary>Why a move is not <see cref="MoveVerdict.Ok"/>.</summary>
public enum BlockReason : byte
{
    /// <summary>Not blocked.</summary>
    None = 0,

    /// <summary>The destination, or a cell the body passes through, is lava.</summary>
    Lava,

    /// <summary>The destination, or a cell the body passes through, is a hazard (fire, boiling water...).</summary>
    Hazard,

    /// <summary>The step climbs more than <see cref="ClimbCostOptions.MaxClimb"/>.</summary>
    TooHigh,

    /// <summary>
    /// The step drops more than <see cref="ClimbCostOptions.MaxDrop"/> and is not an allowed drop into water
    /// at least <see cref="ClimbCostOptions.MinWaterDepth"/> deep.
    /// </summary>
    TooDeep,

    /// <summary>The destination is not a position a player can occupy (no footing, inside a block, submerged).</summary>
    NotStandable,

    /// <summary>A solid block is in the way of the body during the step (low ceiling, corner, ledge).</summary>
    Obstructed,

    /// <summary>The verdict is <see cref="MoveVerdict.Unknown"/>.</summary>
    Unknown,
}

/// <summary>The cost of one move, with its verdict and the parts the HUD needs.</summary>
/// <param name="Verdict">Whether the move can be taken.</param>
/// <param name="Cost">Total cost in blocks of flat walking; +∞ unless <see cref="MoveVerdict.Ok"/>.</param>
/// <param name="Reason">Why it is not Ok; <see cref="BlockReason.None"/> when it is.</param>
/// <param name="Climb">Blocks gained (≥ 0).</param>
/// <param name="Drop">Blocks lost (≥ 0).</param>
/// <param name="Swim">True when the destination has the feet in water.</param>
public readonly record struct MoveCost(MoveVerdict Verdict, double Cost, BlockReason Reason, int Climb = 0, int Drop = 0, bool Swim = false)
{
    /// <summary>True when the move can be taken.</summary>
    public bool IsOk => Verdict == MoveVerdict.Ok;

    /// <summary>A blocked move.</summary>
    /// <param name="reason">Why.</param>
    public static MoveCost Blocked(BlockReason reason) => new(MoveVerdict.Blocked, double.PositiveInfinity, reason);

    /// <summary>A move that depends on Unknown terrain.</summary>
    public static MoveCost Unknown { get; } = new(MoveVerdict.Unknown, double.PositiveInfinity, BlockReason.Unknown);
}
