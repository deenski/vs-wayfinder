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

        Assert.Contains(
            "X and Z are spawn-relative, the way the map shows them. Y is absolute height, from 0 up to the world height minus 1.",
            commands,
            StringComparison.Ordinal);
        Assert.Contains("WithAdditionalInformation(", commands, StringComparison.Ordinal);
        Assert.Contains(
            "Y, when given, is absolute height and is not offset by spawn.",
            commands,
            StringComparison.Ordinal);
        Assert.Contains("BlockAccessor?.MapSizeY", commands, StringComparison.Ordinal);
        Assert.DoesNotContain("one constructor argument", commands, StringComparison.Ordinal);
        Assert.Contains("server-side name-to-uid lookup", commands, StringComparison.Ordinal);

        var model = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Infra", "NavTargets.cs"));
        var playerStart = model.IndexOf("public NavReply Player", StringComparison.Ordinal);
        var coordStart = model.IndexOf("public NavReply Coord", StringComparison.Ordinal);
        var player = model[playerStart..coordStart];
        var check = player.IndexOf("IsVisibleTeammate", StringComparison.Ordinal);
        var assign = player.IndexOf("current = NavTarget.Player", StringComparison.Ordinal);
        Assert.True(check >= 0 && assign > check);
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
