using System;
using Vintagestory.API.Client;
using Wayfinding.Infra;

namespace Wayfinding;

public static class ClientConfigStore
{
    public const string FileName = "wayfinding.json";

    public static WayfindingClientConfig Load(ICoreClientAPI api, IWayfindingLog log)
    {
        WayfindingClientConfig? loaded = null;
        var failed = false;
        try
        {
            loaded = api.LoadModConfig<WayfindingClientConfig>(FileName);
        }
        catch (Exception ex)
        {
            failed = true;
            log.Log(LogLevel.Warning, "could not read " + FileName + " (" + ex.Message + "); using defaults");
        }

        var missing = !failed && loaded is null;
        var config = failed || loaded is null ? new WayfindingClientConfig() : loaded;
        var originalVerbosity = config.debugVerbosity;
        var changed = WayfindingConfig.Normalize(config);
        if (!failed && !missing && originalVerbosity != config.debugVerbosity)
        {
            log.Log(
                LogLevel.Warning,
                "debugVerbosity " + originalVerbosity + " is outside 0-3; clamped to " + config.debugVerbosity);
        }
        else if (!failed && !missing && changed)
        {
            log.Log(LogLevel.Warning, FileName + " had values outside the allowed range; they were clamped or replaced with defaults");
        }

        if (missing || (!failed && changed))
        {
            try
            {
                api.StoreModConfig(config, FileName);
            }
            catch (Exception ex)
            {
                log.Log(LogLevel.Warning, "could not write " + FileName + " (" + ex.Message + ")");
            }
        }

        return config;
    }
}
