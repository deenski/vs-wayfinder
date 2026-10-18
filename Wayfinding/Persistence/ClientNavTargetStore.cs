using System;
using Vintagestory.API.Client;
using Wayfinding.Infra;

namespace Wayfinding;

/// <summary>
/// Per-client copy of the navigation target, keyed by savegame id inside one file.
/// The game serializer writes the public fields on <see cref="NavTargetDocument"/>.
/// </summary>
public sealed class ClientNavTargetStore : INavTargetStore
{
    public const string FileName = "wayfinding-targets.json";

    private readonly ICoreClientAPI api;
    private readonly IWayfindingLog log;

    public ClientNavTargetStore(ICoreClientAPI api, IWayfindingLog log)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public NavTargetDocument Load()
    {
        try
        {
            return api.LoadModConfig<NavTargetDocument>(FileName) ?? new NavTargetDocument();
        }
        catch (Exception ex)
        {
            log.Log(LogLevel.Warning, "could not read " + FileName + " (" + ex.Message + "); starting with no target");
            return new NavTargetDocument();
        }
    }

    public void Save(NavTargetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            api.StoreModConfig(document, FileName);
        }
        catch (Exception ex)
        {
            log.Log(LogLevel.Warning, "could not write " + FileName + " (" + ex.Message + ")");
        }
    }
}
