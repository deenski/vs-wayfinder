using System;

namespace Wayfinding.Infra;

public enum Feature
{
    MapLayer,
    MapViewport,
    Waypoints,
    RemoteTracking,
    MinimapEdge,
}

public enum MemberKind
{
    Field,
    Method,
    GenericMethodDefinition,
    Property,
}

public sealed class MemberRequirement
{
    public MemberRequirement(string typeName, string memberName, MemberKind kind)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("A type name is required.", nameof(typeName));
        }

        if (string.IsNullOrWhiteSpace(memberName))
        {
            throw new ArgumentException("A member name is required.", nameof(memberName));
        }

        TypeName = typeName;
        MemberName = memberName;
        Kind = kind;
    }

    public string TypeName { get; }

    public string MemberName { get; }

    public MemberKind Kind { get; }

    public string Display => TypeName + "." + MemberName;
}
