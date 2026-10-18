namespace Wayfinding.Infra;

public interface IMemberProbe
{
    bool HasMember(MemberRequirement requirement);
}
