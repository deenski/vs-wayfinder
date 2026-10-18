using System;
using System.Globalization;

namespace Wayfinding.Infra;

/// <summary>
/// Age drawn beside a greyed teammate marker. A live fix has no age.
/// </summary>
public static class TeammateAgeText
{
    /// <summary>
    /// Whole seconds under a minute (<c>12s</c>), otherwise whole minutes (<c>3m</c>).
    /// Null for <see cref="TeammateFixKind.None"/> and <see cref="TeammateFixKind.Live"/>.
    /// </summary>
    public static string? For(TeammateFixKind kind, TimeSpan age)
    {
        if (kind != TeammateFixKind.LastKnown && kind != TeammateFixKind.Stale)
        {
            return null;
        }

        long seconds = age.Ticks / TimeSpan.TicksPerSecond;
        if (seconds < 0)
        {
            seconds = 0;
        }

        if (seconds < 60)
        {
            return seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        return (seconds / 60).ToString(CultureInfo.InvariantCulture) + "m";
    }
}
