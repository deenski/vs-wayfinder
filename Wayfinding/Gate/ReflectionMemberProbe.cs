using System;
using System.Reflection;
using Wayfinding.Infra;

namespace Wayfinding;

public sealed class ReflectionMemberProbe : IMemberProbe
{
    public bool HasMember(MemberRequirement requirement)
    {
        var type = FindType(requirement.TypeName);
        return type is not null && MemberMatcher.HasMember(type, requirement);
    }

    public static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            try
            {
                var type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (type is not null)
                {
                    return type;
                }
            }
            catch (Exception)
            {
                // One assembly with a broken dependency should not hide the others.
            }
        }

        return null;
    }
}
