using System;
using Vintagestory.API.Client;
using Wayfinding.Infra;
using Wayfinding.Pathing.Map;

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
        var originalBinding = config.clickBinding;
        var changed = WayfindingConfig.Normalize(config);
        ClickBindings.Parse(originalBinding, out var bindingOk);
        if (!failed && !missing && originalVerbosity != config.debugVerbosity)
        {
            log.Log(
                LogLevel.Warning,
                "debugVerbosity " + originalVerbosity + " is outside 0-3; clamped to " + config.debugVerbosity);
        }

        if (!failed && !missing && !bindingOk)
        {
            log.Log(
                LogLevel.Warning,
                "clickBinding \"" + originalBinding + "\" is not CtrlLeft, Middle, or Both; using CtrlLeft");
        }
        else if (!failed && !missing && changed && originalVerbosity == config.debugVerbosity)
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
