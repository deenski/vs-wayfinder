using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Wayfinding.Teams;

/// <summary>
/// Debug log that fires once per key. WayfindingLog.LogOnce is not on main,
/// so share verification uses this local equivalent.
/// </summary>
internal sealed class LogOnce
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public void Debug(ILogger logger, string key, string message)
    {
        if (string.IsNullOrEmpty(key) || !_seen.Add(key))
        {
            return;
        }

        logger.Debug(message);
    }

    public void Clear()
    {
        _seen.Clear();
    }
}
