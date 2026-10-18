namespace Wayfinding.Infra;

/// <summary>
/// Elapsed time in milliseconds. The client passes
/// <c>capi.ElapsedMilliseconds</c>. Age math must use this clock, not tick counts.
/// </summary>
public interface IClock
{
    long ElapsedMilliseconds { get; }
}
