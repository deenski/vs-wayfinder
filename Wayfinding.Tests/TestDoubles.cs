using Wayfinding.Infra;

namespace Wayfinding.Tests;

internal sealed class ListSink : ILogSink
{
    public List<(LogLevel Level, string Line)> Lines { get; } = new();

    public void Write(LogLevel level, string line)
    {
        Lines.Add((level, line));
    }
}

internal sealed class FakeProbe : IMemberProbe
{
    private readonly HashSet<string> missing;

    public FakeProbe(params string[] missingDisplays)
    {
        missing = new HashSet<string>(missingDisplays, StringComparer.Ordinal);
    }

    public bool HasMember(MemberRequirement requirement)
    {
        return !missing.Contains(requirement.Display);
    }
}

internal sealed class NoticeSpy : IGateNotice
{
    public List<(Feature Feature, string Message)> Calls { get; } = new();

    public void ShowDisabled(Feature feature, string message)
    {
        Calls.Add((feature, message));
    }
}
