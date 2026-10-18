namespace Wayfinding.Pathing.Tests;

/// <summary>
/// A wall-clock performance test. Opt-in: it is skipped unless the environment variable
/// <c>WAYFINDING_PERF=1</c> is set <i>and</i> the tests were built in Release. Run with
/// <c>WAYFINDING_PERF=1 dotnet test -c Release --filter Category=Perf</c>. The default suite asserts
/// deterministic work counts instead (PR #6 review, must-fix 3).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PerfFactAttribute : FactAttribute
{
    /// <summary>Name of the opt-in environment variable.</summary>
    public const string EnvironmentVariable = "WAYFINDING_PERF";

    /// <summary>Sets <see cref="FactAttribute.Skip"/> unless perf runs are enabled.</summary>
    public PerfFactAttribute()
    {
#if DEBUG
        Skip = "Perf tests run only in Release builds (dotnet test -c Release) with " + EnvironmentVariable + "=1.";
#else
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
        {
            Skip = "Perf tests are opt-in: set " + EnvironmentVariable + "=1 (Release builds only).";
        }
#endif
    }
}
