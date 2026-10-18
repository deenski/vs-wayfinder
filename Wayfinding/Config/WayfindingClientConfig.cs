using System;
using Vintagestory.API.Client;
using Wayfinding.Infra;
using Wayfinding.Pathing.Map;

namespace Wayfinding;

public sealed class WayfindingClientConfig
{
    public int debugVerbosity = 0;

    public string clickBinding = "CtrlLeft";
}

public static class ClientConfigStore
{
    public const string FileName = "wayfinding.json";

    public static WayfindingClientConfig Load(ICoreClientAPI api, IWayfindingLog log)
    {
        WayfindingClientConfig? config = null;
        var missing = false;
        try
        {
            config = api.LoadModConfig<WayfindingClientConfig>(FileName);
            missing = config is null;
        }
        catch (Exception ex)
        {
            log.Log(LogLevel.Warning, "could not read " + FileName + " (" + ex.Message + "); using defaults");
        }

        config ??= new WayfindingClientConfig();
        var original = config.debugVerbosity;
        config.debugVerbosity = WayfindingLog.ClampVerbosity(config.debugVerbosity);
        ClickBindings.Parse(config.clickBinding, out bool bindingOk);
        if (!bindingOk)
        {
            log.Log(
                LogLevel.Warning,
                "clickBinding \"" + config.clickBinding + "\" is not CtrlLeft, Middle, or Both; using CtrlLeft");
            config.clickBinding = "CtrlLeft";
        }

        if (missing || config.debugVerbosity != original || !bindingOk)
        {
            if (!missing && config.debugVerbosity != original)
            {
                log.Log(
                    LogLevel.Warning,
                    "debugVerbosity " + original + " is outside 0-3; clamped to " + config.debugVerbosity);
            }

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
