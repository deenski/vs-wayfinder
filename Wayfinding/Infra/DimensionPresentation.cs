namespace Wayfinding.Infra;

/// <summary>
/// What one map frame should draw for a dimension relation.
/// The map layer already has the line and the label; this only chooses which.
/// </summary>
public readonly struct MapDraw
{
    public MapDraw(bool line, string? badge)
    {
        Line = line;
        Badge = badge;
    }

    public bool Line { get; }

    public string? Badge { get; }
}

public static class DimensionPresentation
{
    public const string DifferentBadge = "different dimension";

    public static MapDraw For(DimensionRelation relation, NavTargetKind kind = NavTargetKind.None)
    {
        // The tether already hides a player whose dimension does not match, so this gate must not.
        if (kind == NavTargetKind.Player || relation == DimensionRelation.Same)
        {
            return new MapDraw(true, null);
        }

        if (relation == DimensionRelation.Different)
        {
            return new MapDraw(false, DifferentBadge);
        }

        return new MapDraw(false, null);
    }
}
