namespace Wayfinding.Pathing.Teams;

public readonly record struct TeamGroup(int GroupId, string Name);

public enum TeamRestrictResult
{
    Restricted,
    NotAMember,
    Ambiguous,
}
