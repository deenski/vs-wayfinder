namespace Wayfinding.Tests;

public class NavCommandShapeTests
{
    [Fact]
    public void Target_commands_share_the_nav_root_and_do_not_reuse_stats_or_debug()
    {
        var commands = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Commands", "NavTargetCommands.cs"));
        var root = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Commands", "NavCommands.cs"));
        var mod = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "WayfindingModSystem.cs"));

        Assert.Contains(".GetOrCreate(\"nav\")", commands, StringComparison.Ordinal);
        Assert.DoesNotContain(".Create(\"nav\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"waypoint\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"player\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"coord\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"off\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"status\")", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSubCommand(\"stats\")", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSubCommand(\"debug\")", commands, StringComparison.Ordinal);

        Assert.DoesNotContain("BeginSubCommand(\"waypoint\")", root, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSubCommand(\"player\")", root, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSubCommand(\"coord\")", root, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSubCommand(\"status\")", root, StringComparison.Ordinal);

        Assert.Contains("NavTargetCommands.Register", mod, StringComparison.Ordinal);
        Assert.Contains("NavCommands.Register", mod, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrCreate(\"nav\")", mod, StringComparison.Ordinal);
        Assert.DoesNotContain(".Create(\"nav\")", mod, StringComparison.Ordinal);
        Assert.Contains("NavTargets", mod, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Wayfinding.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find Wayfinding.sln above the test output.");
    }
}
