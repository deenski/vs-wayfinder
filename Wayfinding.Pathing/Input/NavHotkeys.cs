namespace Wayfinding.Pathing.Input;

/// <summary>
/// Ignores OS key-repeat while a hotkey is held.
/// shortcut: key-up is not observed. Each event inside <see cref="WindowMs"/> of the
/// previous one is a repeat and refreshes the window, so a held key fires once.
/// A new press sooner than <see cref="WindowMs"/> after the last repeat is ignored too.
/// </summary>
public sealed class HotkeyRepeatGuard
{
    /// <summary>Gap, in milliseconds, that separates a new press from a repeat.</summary>
    public const int WindowMs = 250;

    private long lastMs = long.MinValue;

    /// <summary>True for the first event, then false until <see cref="WindowMs"/> has passed.</summary>
    public bool Accept(long nowMs)
    {
        if (lastMs != long.MinValue && nowMs - lastMs < WindowMs)
        {
            lastMs = nowMs;
            return false;
        }

        lastMs = nowMs;
        return true;
    }
}

/// <summary>One waypoint in <c>ownWaypoints</c> order.</summary>
public readonly struct PinnedStep
{
    /// <summary>Creates a step. <paramref name="guidText"/> is parsed only when <paramref name="pinned"/> is true.</summary>
    public PinnedStep(string? guidText, string? name, bool pinned)
    {
        GuidText = guidText;
        Name = name;
        Pinned = pinned;
    }

    /// <summary><c>Waypoint.Guid</c>, which is a string.</summary>
    public string? GuidText { get; }

    /// <summary>Waypoint title. May be empty.</summary>
    public string? Name { get; }

    /// <summary>Whether the waypoint is pinned.</summary>
    public bool Pinned { get; }
}

/// <summary>Steps through pinned waypoints and wraps to the first.</summary>
public static class PinnedCycle
{
    /// <summary>
    /// The pinned waypoint after <paramref name="current"/>, in list order.
    /// Unpinned rows and rows whose guid does not parse are skipped.
    /// No pinned waypoint returns false and leaves the outs empty.
    /// When <paramref name="current"/> is missing, the first pinned waypoint is chosen.
    /// The last pinned waypoint wraps to the first.
    /// </summary>
    public static bool TryNext(IReadOnlyList<PinnedStep> entries, Guid? current, out Guid id, out string name)
    {
        ArgumentNullException.ThrowIfNull(entries);
        id = Guid.Empty;
        name = "";
        int count = 0;
        int currentIndex = -1;
        Guid[] ids = new Guid[entries.Count];
        string[] names = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            PinnedStep entry = entries[i];
            if (!entry.Pinned || !Guid.TryParse(entry.GuidText, out Guid parsed) || parsed == Guid.Empty)
            {
                continue;
            }

            if (current is Guid active && active == parsed && currentIndex < 0)
            {
                currentIndex = count;
            }

            ids[count] = parsed;
            names[count] = entry.Name ?? "";
            count++;
        }

        if (count == 0)
        {
            return false;
        }

        int next = currentIndex < 0 ? 0 : (currentIndex + 1) % count;
        id = ids[next];
        name = names[next];
        return true;
    }
}
