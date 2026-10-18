using System;

namespace Wayfinding.Infra;

/// <summary>
/// Horizontal position in blocks. Y is set when the entity supplied it.
/// Remote tracking on 1.22.7 has X and Z only.
/// </summary>
public readonly struct TeammatePoint
{
    public TeammatePoint(double x, double z, double? y = null)
    {
        X = x;
        Z = z;
        Y = y;
    }

    public double X { get; }

    public double Z { get; }

    public double? Y { get; }
}

/// <summary>
/// What the map layer should draw for a teammate target.
/// </summary>
public enum TeammateFixKind
{
    None = 0,

    /// <summary>Entity or remote-tracking position from this tick.</summary>
    Live = 1,

    /// <summary>Greyed last-known marker. The fix is younger than 30 seconds.</summary>
    LastKnown = 2,

    /// <summary>The last fix is 30 seconds old or older.</summary>
    Stale = 3,
}

/// <summary>
/// One tether sample. <see cref="Visible"/> false means draw nothing.
/// Main-thread only; the client hook updates it from the game tick.
/// </summary>
public readonly struct TeammateTetherSample
{
    public TeammateTetherSample(
        bool visible,
        TeammateFixKind kind,
        bool hasPosition,
        TeammatePoint position,
        TimeSpan age)
    {
        Visible = visible;
        Kind = kind;
        HasPosition = hasPosition;
        Position = position;
        Age = age;
    }

    public static TeammateTetherSample Hidden { get; } = new(false, TeammateFixKind.None, false, default, TimeSpan.Zero);

    public bool Visible { get; }

    public TeammateFixKind Kind { get; }

    public bool HasPosition { get; }

    public TeammatePoint Position { get; }

    public TimeSpan Age { get; }
}

/// <summary>
/// Monotonic clock. Only differences are used. Tests supply their own.
/// </summary>
public interface ITeammateTetherClock
{
    long Milliseconds { get; }
}

/// <summary>
/// Hide inputs. A null map-hide, spectator, or share answer fails closed.
/// A null dimension is unknown: it does not accept a new point, and it keeps a fix already accepted.
/// </summary>
public interface ITeammateTetherInputs
{
    /// <summary>True when mapHideOtherPlayers is on. Null when the setting cannot be read.</summary>
    bool? MapHideOtherPlayers { get; }

    /// <summary>True when the target is in spectator. Null when the mode cannot be read.</summary>
    bool? IsSpectator(string? playerNameOrUid);

    /// <summary>
    /// True when both dimensions are known and equal. False on a mismatch.
    /// Null when either dimension is missing.
    /// </summary>
    bool? IsDimensionConfirmed(string? playerNameOrUid);

    /// <summary>True only when the target is sharing. Null when share state is unknown.</summary>
    bool? IsSharing(string? playerNameOrUid);

    bool TryGetLocalPosition(out TeammatePoint position);
}

/// <summary>
/// Client teammate tether. Position priority is the loaded entity, then remote
/// tracking, then a last-known marker that becomes stale after 30 seconds.
/// The line hides within 6 blocks and shows again only beyond 12.
/// Call <see cref="Tick"/> and read <see cref="Current"/> on the main thread only.
/// </summary>
public sealed class TeammateTether
{
    public const double HideWithinBlocks = 6d;

    public const double ShowBeyondBlocks = 12d;

    public const long StaleAfterMilliseconds = 30_000;

    /// <summary>
    /// Share-off must be reflected by a tick no later than this. The client
    /// listener interval stays under it.
    /// </summary>
    public const long ShareOffWithinMilliseconds = 5_000;

    public const int ClientTickIntervalMilliseconds = 200;

    private readonly ITeammatePositionSource positions;
    private readonly ITeammateVisibility teammates;
    private readonly ITeammateTetherInputs inputs;
    private readonly ITeammateTetherClock clock;
    private string? playerKey;
    private TeammatePoint? lastPoint;
    private long lastAt;
    private bool apart;

    public TeammateTether(
        ITeammatePositionSource positions,
        ITeammateVisibility? teammates,
        ITeammateTetherInputs inputs,
        ITeammateTetherClock clock)
    {
        this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
        this.teammates = teammates ?? NoTeammateVisibility.Instance;
        this.inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public TeammateTetherSample Current { get; private set; } = TeammateTetherSample.Hidden;

    public void Tick(string? playerNameOrUid)
    {
        var key = string.IsNullOrWhiteSpace(playerNameOrUid) ? null : playerNameOrUid;
        if (!string.Equals(playerKey, key, StringComparison.Ordinal))
        {
            ForgetFix();
            playerKey = key;
        }

        if (key is null)
        {
            Current = TeammateTetherSample.Hidden;
            return;
        }

        if (HiddenByPolicy(key))
        {
            ForgetFix();
            Current = TeammateTetherSample.Hidden;
            return;
        }

        var dimension = inputs.IsDimensionConfirmed(key);
        if (dimension == false)
        {
            ForgetFix();
            Current = TeammateTetherSample.Hidden;
            return;
        }

        var live = false;
        if (dimension == true)
        {
            if (positions.TryGetEntityPosition(key, out var entity))
            {
                NoteLive(entity);
                live = true;
            }
            else if (positions.TryGetTrackedPosition(key, out var tracked))
            {
                NoteLive(tracked);
                live = true;
            }
        }

        if (lastPoint is not TeammatePoint point)
        {
            Current = TeammateTetherSample.Hidden;
            return;
        }

        var ageMs = clock.Milliseconds - lastAt;
        if (ageMs < 0)
        {
            ageMs = 0;
        }

        var kind = live
            ? TeammateFixKind.Live
            : ageMs >= StaleAfterMilliseconds
                ? TeammateFixKind.Stale
                : TeammateFixKind.LastKnown;
        var visible = PassesHysteresis(point);
        Current = new TeammateTetherSample(visible, kind, true, point, TimeSpan.FromMilliseconds(ageMs));
    }

    private bool HiddenByPolicy(string playerNameOrUid)
    {
        if (inputs.MapHideOtherPlayers != false)
        {
            return true;
        }

        if (inputs.IsSpectator(playerNameOrUid) != false)
        {
            return true;
        }

        if (!teammates.IsVisibleTeammate(playerNameOrUid))
        {
            return true;
        }

        if (inputs.IsSharing(playerNameOrUid) != true)
        {
            return true;
        }

        return false;
    }

    private void NoteLive(TeammatePoint point)
    {
        lastPoint = point;
        lastAt = clock.Milliseconds;
    }

    private bool PassesHysteresis(TeammatePoint point)
    {
        if (!inputs.TryGetLocalPosition(out var local))
        {
            return false;
        }

        var distance = Horizontal(local, point);
        if (apart)
        {
            if (distance <= HideWithinBlocks)
            {
                apart = false;
            }
        }
        else if (distance > ShowBeyondBlocks)
        {
            apart = true;
        }

        return apart;
    }

    private void ForgetFix()
    {
        lastPoint = null;
        lastAt = 0;
        apart = false;
    }

    private static double Horizontal(TeammatePoint from, TeammatePoint to)
    {
        var dx = from.X - to.X;
        var dz = from.Z - to.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
