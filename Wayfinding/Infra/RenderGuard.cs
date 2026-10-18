using System;

namespace Wayfinding.Infra;

/// <summary>
/// One guard per layer. A throw is logged once and disables that layer, so
/// later draws on this guard are skipped. Other guards are left alone.
/// </summary>
public sealed partial class RenderGuard
{
    public const string LogKey = "render-guard";

    private readonly IWayfindingLog log;

    public RenderGuard(IWayfindingLog log)
    {
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        LayerEnabled = true;
    }

    public bool LayerEnabled { get; private set; }

    public string? Fault { get; private set; }

    public void Draw(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (!LayerEnabled)
        {
            return;
        }

        try
        {
            ThrowIfForced();
            draw();
        }
        catch (Exception ex)
        {
            LayerEnabled = false;
            Fault = ex.GetType().Name + ": " + ex.Message;
            log.LogOnce(LogKey, LogLevel.Error, "render guard disabled the wayfinding layer (" + Fault + ")");
        }
    }

    partial void ThrowIfForced();
}

#if DEBUG
public sealed partial class RenderGuard
{
    // In-game use of this hook is KAN-19. Release builds omit it.
    public bool ForceThrow { get; set; }

    partial void ThrowIfForced()
    {
        if (ForceThrow)
        {
            throw new InvalidOperationException("forced render exception");
        }
    }
}
#endif
