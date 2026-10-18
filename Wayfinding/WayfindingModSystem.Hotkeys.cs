using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.GameContent;
using Wayfinding.Infra;
using Wayfinding.Pathing.Input;
using Wayfinding.Pathing.Map;

namespace Wayfinding;

public partial class WayfindingModSystem
{
    public const string ToggleHotkey = "wayfinding-toggle";
    public const string ClearHotkey = "wayfinding-clear";
    public const string CycleHotkey = "wayfinding-cycle";

    private readonly HotkeyRepeatGuard toggleRepeat = new();
    private readonly HotkeyRepeatGuard clearRepeat = new();
    private readonly HotkeyRepeatGuard cycleRepeat = new();
    private bool cycleDisabled;

    public bool RouteVisible { get; private set; } = true;

    public ClickBinding ClickBinding { get; private set; } = ClickBinding.CtrlLeft;

    private void ApplyClientConfig(WayfindingClientConfig config)
    {
        Log.Verbosity = config.debugVerbosity;
        ClickBinding = ClickBindings.Parse(config.clickBinding, out _);
    }

    private void RegisterHotkeys(ICoreClientAPI api)
    {
        if (!IsEnabled(Feature.ClientInput))
        {
            return;
        }

        Bind(api, ToggleHotkey, "Wayfinding: toggle route", GlKeys.N, toggleRepeat, OnToggle);
        Bind(api, ClearHotkey, "Wayfinding: clear target", GlKeys.X, clearRepeat, OnClear);
        Bind(api, CycleHotkey, "Wayfinding: cycle pinned waypoints", GlKeys.C, cycleRepeat, OnCycle);
    }

    private static void Bind(
        ICoreClientAPI api,
        string code,
        string name,
        GlKeys key,
        HotkeyRepeatGuard guard,
        Action action)
    {
        api.Input.RegisterHotKey(code, name, key, HotkeyType.HelpAndOverlays, ctrlPressed: true, shiftPressed: true);
        api.Input.SetHotKeyHandler(code, _ =>
        {
            if (!guard.Accept(Environment.TickCount64))
            {
                return true;
            }

            action();
            return true;
        });
    }

    private void OnToggle()
    {
        RouteVisible = !RouteVisible;
    }

    private void OnClear()
    {
        var targets = NavTargets;
        if (targets == null)
        {
            return;
        }

        Say(targets.Off().Message);
    }

    private void OnCycle()
    {
        var targets = NavTargets;
        if (targets == null || !TryReadPinned(out var entries))
        {
            return;
        }

        Guid? current = targets.Current.Kind == NavTargetKind.Waypoint ? targets.Current.WaypointId : null;
        if (!PinnedCycle.TryNext(entries, current, out var id, out var name))
        {
            return;
        }

        Say(targets.SetWaypoint(id, name).Message);
    }

    private bool TryReadPinned(out List<PinnedStep> entries)
    {
        entries = new List<PinnedStep>();
        if (capi == null || cycleDisabled || !IsEnabled(Feature.Waypoints))
        {
            return false;
        }

        try
        {
            var layers = capi.ModLoader.GetModSystem<WorldMapManager>()?.MapLayers;
            if (layers == null)
            {
                return false;
            }

            WaypointMapLayer? layer = null;
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i] is WaypointMapLayer found)
                {
                    layer = found;
                    break;
                }
            }

            if (layer?.ownWaypoints == null)
            {
                return false;
            }

            for (var i = 0; i < layer.ownWaypoints.Count; i++)
            {
                var wp = layer.ownWaypoints[i];
                entries.Add(new PinnedStep(wp?.Guid, wp?.Title, wp != null && wp.Pinned));
            }

            return true;
        }
        catch (Exception ex)
        {
            cycleDisabled = true;
            Log.LogOnce(
                "hotkey-cycle",
                LogLevel.Error,
                "pinned waypoint cycle disabled after " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    private void Say(string message)
    {
        capi?.ShowChatMessage("[wayfinding] " + message);
    }
}
