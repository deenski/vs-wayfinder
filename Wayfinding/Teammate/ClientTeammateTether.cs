using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Wayfinding.Infra;

namespace Wayfinding.Teammate;

/// <summary>
/// Reads 1.22.7 client positions. <see cref="SystemRemotePlayerTracking.GetPlayerPositionInformation"/>
/// returns the packet for one uid, or null. The packet is X, Z, yaw, and a despawn
/// flag. The client removes the entry when the server sends despawn, so a stored
/// packet is a current fix. The server also sends that packet for players inside
/// <c>mapPlayerRenderDistance</c> who are not in the group. This type does not
/// decide who is a teammate.
/// </summary>
internal sealed class ClientTeammatePositionSource : ITeammatePositionSource
{
    private readonly ICoreClientAPI api;
    private readonly WayfindingModSystem mod;
    private bool trackingFailed;

    public ClientTeammatePositionSource(ICoreClientAPI api, WayfindingModSystem mod)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.mod = mod ?? throw new ArgumentNullException(nameof(mod));
    }

    public bool TryGetEntityPosition(string? playerNameOrUid, out TeammatePoint position)
    {
        position = default;
        if (!ClientPlayers.TryFind(api, playerNameOrUid, out var player))
        {
            return false;
        }

        var pos = player.Entity?.Pos;
        if (pos == null)
        {
            return false;
        }

        position = new TeammatePoint(pos.X, pos.Z, pos.Y);
        return true;
    }

    public bool TryGetTrackedPosition(string? playerNameOrUid, out TeammatePoint position)
    {
        position = default;
        if (trackingFailed || !mod.IsEnabled(Feature.RemoteTracking))
        {
            return false;
        }

        if (!ClientPlayers.TryFind(api, playerNameOrUid, out var player) || string.IsNullOrEmpty(player.PlayerUID))
        {
            return false;
        }

        PacketPlayerPosition? packet;
        try
        {
            packet = api.ModLoader.GetModSystem<SystemRemotePlayerTracking>()
                .GetPlayerPositionInformation(player.PlayerUID);
        }
        catch (Exception ex)
        {
            trackingFailed = true;
            mod.Log.LogOnce(
                "teammate-tracking",
                LogLevel.Warning,
                "remote player tracking is unavailable (" + ex.GetType().Name + ": " + ex.Message + ")");
            return false;
        }

        if (packet == null || packet.Despawn)
        {
            return false;
        }

        position = new TeammatePoint(packet.PosX, packet.PosZ);
        return true;
    }
}

/// <summary>
/// Hide inputs that can be read without KAN-21 or KAN-19.
/// Share stays unknown until KAN-21 is wired, so the tether stays hidden.
/// Dimension is confirmed only when both entities are loaded and their
/// <c>EntityPos.Dimension</c> values match. A tracking packet has no dimension.
/// </summary>
internal sealed class ClientTeammateTetherInputs : ITeammateTetherInputs
{
    private readonly ICoreClientAPI api;

    public ClientTeammateTetherInputs(ICoreClientAPI api)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public bool? MapHideOtherPlayers
    {
        get
        {
            // The 1.22.7 server reads this world-config key with GetBool, whose
            // default is false. A missing key is off. A missing config is unknown.
            var config = api.World?.Config;
            if (config == null)
            {
                return null;
            }

            return config.GetBool("mapHideOtherPlayers");
        }
    }

    public bool? IsSpectator(string? playerNameOrUid)
    {
        if (!ClientPlayers.TryFind(api, playerNameOrUid, out var player) || player.WorldData == null)
        {
            return null;
        }

        return player.WorldData.CurrentGameMode == EnumGameMode.Spectator;
    }

    public bool? IsDimensionConfirmed(string? playerNameOrUid)
    {
        var local = api.World?.Player?.Entity?.Pos;
        if (local == null || !ClientPlayers.TryFind(api, playerNameOrUid, out var player))
        {
            return null;
        }

        var remote = player.Entity?.Pos;
        if (remote == null)
        {
            return null;
        }

        return local.Dimension == remote.Dimension;
    }

    public bool? IsSharing(string? playerNameOrUid)
    {
        // KAN-21 owns share state. Its public service does not expose a separate
        // share query yet, and it is not on main. Unknown stays hidden.
        _ = playerNameOrUid;
        return null;
    }

    public bool TryGetLocalPosition(out TeammatePoint position)
    {
        position = default;
        var pos = api.World?.Player?.Entity?.Pos;
        if (pos == null)
        {
            return false;
        }

        position = new TeammatePoint(pos.X, pos.Z, pos.Y);
        return true;
    }
}

internal sealed class ClientElapsedClock : ITeammateTetherClock
{
    private readonly ICoreClientAPI api;

    public ClientElapsedClock(ICoreClientAPI api)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public long Milliseconds => api.ElapsedMilliseconds;
}

internal static class ClientPlayers
{
    public static bool TryFind(ICoreClientAPI api, string? playerNameOrUid, out IPlayer player)
    {
        player = null!;
        if (string.IsNullOrWhiteSpace(playerNameOrUid))
        {
            return false;
        }

        var world = api.World;
        if (world == null)
        {
            return false;
        }

        var byUid = world.PlayerByUid(playerNameOrUid);
        if (byUid != null)
        {
            player = byUid;
            return true;
        }

        var online = world.AllOnlinePlayers;
        if (online == null)
        {
            return false;
        }

        IPlayer? found = null;
        foreach (var candidate in online)
        {
            if (candidate == null)
            {
                continue;
            }

            var nameMatch = string.Equals(candidate.PlayerName, playerNameOrUid, StringComparison.Ordinal);
            var uidMatch = string.Equals(candidate.PlayerUID, playerNameOrUid, StringComparison.Ordinal);
            if (!nameMatch && !uidMatch)
            {
                continue;
            }

            if (found != null && !string.Equals(found.PlayerUID, candidate.PlayerUID, StringComparison.Ordinal))
            {
                return false;
            }

            found = candidate;
        }

        if (found == null)
        {
            return false;
        }

        player = found;
        return true;
    }
}
