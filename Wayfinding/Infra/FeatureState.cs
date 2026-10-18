namespace Wayfinding.Infra;

public sealed class FeatureState
{
    public FeatureState(bool enabled, string missing)
    {
        Enabled = enabled;
        Missing = missing ?? "";
    }

    public bool Enabled { get; }

    public string Missing { get; }
}
