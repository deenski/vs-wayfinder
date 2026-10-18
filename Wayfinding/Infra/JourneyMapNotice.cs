namespace Wayfinding.Infra;

/// <summary>
/// One notice per owner instance when Swixy JourneyMap is loaded.
/// The mod system keeps one instance for the client session.
/// </summary>
public sealed class JourneyMapNotice
{
    public const string ModId = "swixyjourneymap";

    public const string Message = "Swixy JourneyMap is loaded and hides the wayfinding map layer.";

    private bool shown;

    public bool TryFire(bool modLoaded)
    {
        if (!modLoaded || shown)
        {
            return false;
        }

        shown = true;
        return true;
    }
}
