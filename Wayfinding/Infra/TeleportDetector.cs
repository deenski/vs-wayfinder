namespace Wayfinding.Infra;

/// <summary>
/// Horizontal distance, in blocks, between consecutive samples that counts as a teleport.
/// A jump is strictly more than this. Exactly <see cref="Blocks"/> is ordinary movement.
/// </summary>
public static class TeleportLimits
{
    public const double Blocks = 64;
}

/// <summary>
/// Returns true when this sample is a horizontal jump of more than
/// <see cref="TeleportLimits.Blocks"/> blocks since the previous sample.
/// The first sample, and the first sample after <see cref="ResetBaseline"/>,
/// establish a baseline and are never a jump. Vertical movement is ignored.
/// Unavailable samples are ignored and do not move the baseline.
/// </summary>
public sealed class TeleportDetector
{
    private bool hasBaseline;
    private double baselineX;
    private double baselineZ;

    public bool Observe(PlayerFix sample)
    {
        if (!sample.IsAvailable)
        {
            return false;
        }

        if (!hasBaseline)
        {
            baselineX = sample.X;
            baselineZ = sample.Z;
            hasBaseline = true;
            return false;
        }

        var dx = sample.X - baselineX;
        var dz = sample.Z - baselineZ;
        baselineX = sample.X;
        baselineZ = sample.Z;
        return dx * dx + dz * dz > TeleportLimits.Blocks * TeleportLimits.Blocks;
    }

    public void ResetBaseline()
    {
        hasBaseline = false;
    }
}
