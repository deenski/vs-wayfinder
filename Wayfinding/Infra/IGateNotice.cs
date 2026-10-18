namespace Wayfinding.Infra;

public interface IGateNotice
{
    void ShowDisabled(Feature feature, string message);
}
