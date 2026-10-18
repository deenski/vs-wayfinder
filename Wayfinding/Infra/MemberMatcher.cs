using System;
using System.Reflection;

namespace Wayfinding.Infra;

public static class MemberMatcher
{
    private const BindingFlags Flags =
        BindingFlags.Instance |
        BindingFlags.Static |
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.DeclaredOnly;

    public static bool HasMember(Type type, MemberRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(requirement);

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (requirement.Kind == MemberKind.Field)
            {
                if (current.GetField(requirement.MemberName, Flags) is not null)
                {
                    return true;
                }

                continue;
            }

            if (requirement.Kind == MemberKind.Property)
            {
                if (current.GetProperty(requirement.MemberName, Flags) is not null)
                {
                    return true;
                }

                continue;
            }

            foreach (var method in current.GetMethods(Flags))
            {
                if (!string.Equals(method.Name, requirement.MemberName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (requirement.Kind == MemberKind.GenericMethodDefinition)
                {
                    if (method.IsGenericMethodDefinition)
                    {
                        return true;
                    }
                }
                else if (!method.IsGenericMethodDefinition)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
