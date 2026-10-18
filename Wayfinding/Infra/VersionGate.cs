using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Wayfinding.Infra;

public sealed class VersionGate
{
    private readonly Dictionary<Feature, FeatureState> states = new();
    private readonly HashSet<Feature> notified = new();
    private bool evaluated;

    public VersionGate()
    {
        foreach (var feature in FeatureCatalog.All)
        {
            states[feature] = new FeatureState(false, "");
        }
    }

    public bool IsEvaluated => Volatile.Read(ref evaluated);

    public bool IsEnabled(Feature feature)
    {
        if (!Volatile.Read(ref evaluated))
        {
            return false;
        }

        return states[feature].Enabled;
    }

    public FeatureState State(Feature feature)
    {
        if (!Volatile.Read(ref evaluated) || !states.TryGetValue(feature, out var state))
        {
            return new FeatureState(false, "");
        }

        return state;
    }

    /// <summary>
    /// Main-thread only. The evaluated check and the write are not safe for concurrent callers.
    /// </summary>
    public void Evaluate(IMemberProbe probe, IWayfindingLog log)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(log);
        if (IsEvaluated)
        {
            return;
        }

        foreach (var feature in FeatureCatalog.All)
        {
            var missing = new List<string>();
            foreach (var requirement in FeatureCatalog.Requirements(feature))
            {
                bool present;
                try
                {
                    present = probe.HasMember(requirement);
                }
                catch (Exception ex)
                {
                    present = false;
                    log.LogOnce(
                        "gate-probe:" + requirement.Display,
                        LogLevel.Error,
                        "member probe failed for " + requirement.Display + ": " + ex.Message);
                }

                if (!present)
                {
                    missing.Add(requirement.Display);
                }
            }

            if (missing.Count == 0)
            {
                states[feature] = new FeatureState(true, "");
                continue;
            }

            var missingText = string.Join(", ", missing);
            states[feature] = new FeatureState(false, missingText);
            log.LogOnce("gate:" + feature, LogLevel.Warning, DisabledMessage(feature, missingText));
        }

        // Publish only after every state write. A reader that sees true also sees those states.
        Volatile.Write(ref evaluated, true);
        log.Log(LogLevel.Notice, Summary());
    }

    /// <summary>
    /// Main-thread only. The per-feature notice set is not safe for concurrent callers.
    /// </summary>
    public void Notify(IGateNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (!IsEvaluated)
        {
            return;
        }

        foreach (var feature in FeatureCatalog.All)
        {
            var state = states[feature];
            if (state.Enabled || !notified.Add(feature))
            {
                continue;
            }

            notice.ShowDisabled(feature, DisabledMessage(feature, state.Missing));
        }
    }

    public string Summary()
    {
        if (!IsEvaluated)
        {
            return "version gate: not evaluated";
        }

        var builder = new StringBuilder("version gate:");
        foreach (var feature in FeatureCatalog.All)
        {
            builder.Append(' ');
            builder.Append(feature);
            builder.Append(states[feature].Enabled ? " enabled," : " disabled,");
        }

        if (builder[^1] == ',')
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    public static string DisabledMessage(Feature feature, string missing)
    {
        return feature + " disabled; missing " + missing;
    }
}
