using System;

namespace Wayfinding.Infra;

public enum Feature
{
    MapLayer,
    MapViewport,
    Waypoints,
    RemoteTracking,
}

public enum MemberKind
{
    Field,
    Method,
    GenericMethodDefinition,
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
