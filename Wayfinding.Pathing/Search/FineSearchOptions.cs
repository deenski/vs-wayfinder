using Wayfinding.Pathing.Cost;

namespace Wayfinding.Pathing.Search;

/// <summary>
/// Build-move tuning (design v2 §3, "Build moves"). Build moves are soft costs inside the search: each placed
/// block adds <see cref="BuildPenalty"/> blocks of walking, so a no-build detour wins whenever it is shorter.
/// </summary>
/// <remarks>
/// <para>The search only proposes moves; the mod never places blocks. Supported moves:</para>
/// <list type="bullet">
/// <item><description><b>Step</b> (1 block) and <b>Pillar</b> (2 or more blocks), for climbs from
/// <c>MaxClimb + 1</c> to <see cref="MaxPillarHeight"/>: <c>climb − MaxClimb</c> blocks are stacked in the
/// player's own column, starting in the feet cell, then the player makes an ordinary <c>+MaxClimb</c> step into
/// the neighbouring column. With the default <c>MaxClimb = 1</c>: Step is climb 2 with 1 block, Pillar is
/// climb 3 or 4 with climb − 1 blocks. Any of the 8 directions.</description></item>
/// <item><description><b>Bridge</b>: across 1 to <see cref="MaxBridgeLength"/> cells of air or lava at the same
/// feet level, one block under each gap cell, between two standable ends. The 4 straight directions only.</description></item>
/// </list>
/// <para>Blocks are never placed into a cell flagged <see cref="Grid.CellFlags.BuildForbidden"/>, into a known
/// solid, water or hazard cell, or into Unknown terrain. <c>maxBlocks</c> (WF-14) is enforced after the search,
/// not here.</para>
/// <para><b>Placed blocks are per move.</b> Each build move is checked against the real grid plus only its own
/// blocks. A later move in the same search never sees blocks an earlier move placed, and a later walking step
/// never stands on or bumps into them. That costs nothing on optimal routes today, but it means a route is
/// not re-validated as a whole: WF-14 (KAN-28) must re-validate the full route with all of its build sites
/// placed before showing it.</para>
/// </remarks>
public sealed record BuildMoveOptions
{
    /// <summary>Build moves off: pure walking and swimming (the default, and what <c>ReferenceSearch</c> models).</summary>
    public static BuildMoveOptions Disabled { get; } = new();

    /// <summary>Build moves on with the design defaults and blocks available.</summary>
    public static BuildMoveOptions EnabledDefaults { get; } = new() { Enabled = true };

    /// <summary>Whether the user enabled build moves.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Whether the player carries placeable blocks. The mod sets this from the inventory when
    /// <c>requireBlocksInInventory</c> is on; with false, no build move is used. Default true.
    /// </summary>
    public bool BlocksAvailable { get; init; } = true;

    /// <summary>Cost per placed block, in blocks of walking. Default 24. Must be finite and ≥ 0.</summary>
    public double BuildPenalty { get; init; } = 24;

    /// <summary>Highest climb a Step/Pillar covers. Default 4. Values ≤ <see cref="ClimbCostOptions.MaxClimb"/> disable pillars.</summary>
    public int MaxPillarHeight { get; init; } = 4;

    /// <summary>Longest bridge, in gap cells. Default 4. 0 disables bridges.</summary>
    public int MaxBridgeLength { get; init; } = 4;

    /// <summary>True when build moves may be used: <see cref="Enabled"/> and <see cref="BlocksAvailable"/>.</summary>
    public bool Allowed => Enabled && BlocksAvailable;

    /// <summary>Throws if any value is out of range.</summary>
    /// <returns>This instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public BuildMoveOptions Validate()
    {
        if (!double.IsFinite(BuildPenalty) || BuildPenalty < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BuildPenalty), BuildPenalty, "BuildPenalty must be a finite number ≥ 0.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(MaxPillarHeight, nameof(MaxPillarHeight));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxPillarHeight, 16, nameof(MaxPillarHeight));
        ArgumentOutOfRangeException.ThrowIfNegative(MaxBridgeLength, nameof(MaxBridgeLength));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxBridgeLength, 16, nameof(MaxBridgeLength));
        return this;
    }
}

/// <summary>Inputs for one <see cref="FineAStar"/> run besides the grid and the endpoints.</summary>
public sealed record FineSearchOptions
{
    /// <summary>Design defaults: default cost model, 100 000 nodes, no time limit, no build moves.</summary>
    public static FineSearchOptions Default { get; } = new();

    /// <summary>The cost model. Default <see cref="ClimbCostOptions.Default"/>.</summary>
    public ClimbCostModel Model { get; init; } = new();

    /// <summary>Node budget: most positions expanded before giving up. Default 100 000. Must be ≥ 1.</summary>
    public int MaxNodes { get; init; } = 100_000;

    /// <summary>Wall-clock budget, or null for none. Checked every <see cref="TimeCheckInterval"/> expansions.</summary>
    public TimeSpan? MaxTime { get; init; }

    /// <summary>Clock for <see cref="MaxTime"/>. Default <see cref="TimeProvider.System"/>; tests pass a fake.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Build-move settings. Default <see cref="BuildMoveOptions.Disabled"/>.</summary>
    public BuildMoveOptions Build { get; init; } = BuildMoveOptions.Disabled;

    /// <summary>How many expansions pass between clock reads.</summary>
    public const int TimeCheckInterval = 64;

    /// <summary>Throws if any value is out of range.</summary>
    /// <returns>This instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    /// <exception cref="ArgumentNullException">A required value is null.</exception>
    public FineSearchOptions Validate()
    {
        ArgumentNullException.ThrowIfNull(Model, nameof(Model));
        ArgumentNullException.ThrowIfNull(TimeProvider, nameof(TimeProvider));
        ArgumentNullException.ThrowIfNull(Build, nameof(Build));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxNodes, 1, nameof(MaxNodes));
        if (MaxTime is { } t && t < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTime), t, "MaxTime must not be negative.");
        }

        Build.Validate();
        return this;
    }
}
