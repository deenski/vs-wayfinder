namespace Wayfinding.Infra;

/// <summary>
/// What the existing map label should show for a dimension relation.
/// The map layer already draws that label; this only chooses the text and whether a line is allowed.
/// </summary>
public static class DimensionPresentation
{
    public const string DifferentBadge = "different dimension";

    public static bool DrawRoute(DimensionRelation relation)
    {
        return relation == DimensionRelation.Same;
    }

    public static string? Label(DimensionRelation relation)
    {
        return relation == DimensionRelation.Different ? DifferentBadge : null;
    }
}
