using System.Xml.Linq;

namespace Wayfinding.Tests;

public class InfraBoundaryTests
{
    [Fact]
    public void Infra_sources_do_not_use_the_game()
    {
        var infra = Path.Combine(RepoRoot(), "Wayfinding", "Infra");
        var hits = Directory.GetFiles(infra, "*.cs")
            .SelectMany(path => File.ReadAllLines(path).Select((line, index) => (path, index, line)))
            .Where(row => row.line.TrimStart().StartsWith("using Vintagestory", StringComparison.Ordinal))
            .Select(row => Path.GetFileName(row.path) + ":" + (row.index + 1))
            .ToArray();

        Assert.Empty(hits);
    }

    [Fact]
    public void Test_project_links_infra_and_rejects_game_references()
    {
        var csproj = Path.Combine(RepoRoot(), "Wayfinding.Tests", "Wayfinding.Tests.csproj");
        var text = File.ReadAllText(csproj);
        Assert.Contains(@"..\Wayfinding\Infra\*.cs", text, StringComparison.Ordinal);
        Assert.Contains("RejectGameReferences", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProjectReference Include=", text, StringComparison.Ordinal);

        var doc = XDocument.Load(csproj);
        var refs = doc.Descendants()
            .Where(element => element.Name.LocalName is "Reference" or "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? "")
            .Where(include => include.Contains("Vintagestory", StringComparison.OrdinalIgnoreCase)
                || include.Contains("VSEssentials", StringComparison.OrdinalIgnoreCase)
                || include.Contains("VSSurvival", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(refs);
    }

    [Fact]
    public void Nav_root_is_registered_once_and_owns_stats_and_debug()
    {
        var root = RepoRoot();
        var commands = File.ReadAllText(Path.Combine(root, "Wayfinding", "Commands", "NavCommands.cs"));
        var mod = File.ReadAllText(Path.Combine(root, "Wayfinding", "WayfindingModSystem.cs"));
        var teams = File.ReadAllText(Path.Combine(root, "Wayfinding", "Teams", "TeamService.cs"));
        Assert.Equal(1, Count(commands, ".GetOrCreate(\"nav\")"));
        Assert.Contains("RegisterSubcommands(", commands, StringComparison.Ordinal);
        Assert.DoesNotContain(".Create(\"nav\")", commands, StringComparison.Ordinal);
        Assert.DoesNotContain(".Create(\"nav\")", mod, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrCreate(\"nav\")", mod, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrCreate(\"nav\")", teams, StringComparison.Ordinal);
        Assert.DoesNotContain(".Create(\"nav\")", teams, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"stats\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"debug\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"on\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"off\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"clear\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"throw\")", commands, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"share\")", teams, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"team\")", teams, StringComparison.Ordinal);
        Assert.Contains("if (Gate.IsEnabled(Feature.MapLayer))", mod, StringComparison.Ordinal);
        Assert.Contains("RegisterMapLayer<DebugMapLayer>", mod, StringComparison.Ordinal);
        Assert.Contains("TeammateTetherCommands.Register(nav, mod)", commands, StringComparison.Ordinal);
        Assert.Contains("DimensionArrivalCommands.Register(nav, mod)", commands, StringComparison.Ordinal);
        Assert.Contains("RegisterTeammateTether(api)", mod, StringComparison.Ordinal);
        Assert.Contains("RegisterDimensionAndArrival(api)", mod, StringComparison.Ordinal);
        Assert.Contains("DisposeTeammateTether()", mod, StringComparison.Ordinal);
        Assert.Contains("DisposeDimensionAndArrival()", mod, StringComparison.Ordinal);
    }

    // GetOrCreate("nav") may appear only in these files. One command root; the
    // allowlisted call sites share it. Create("nav") and RegisterCommand("nav"
    // are banned in every Wayfinding source file.
    // TeamService registers share and team on the root NavCommands creates
    // and does not call GetOrCreate("nav").
    private static readonly string[] NavGetOrCreateAllowlist =
    {
        "Commands/NavCommands.cs",
        "Commands/NavTargetCommands.cs",
    };

    [Fact]
    public void Nav_root_is_one_command_across_the_mod()
    {
        var modRoot = Path.Combine(RepoRoot(), "Wayfinding");
        var offenders = new List<string>();
        foreach (var path in Directory.GetFiles(modRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(path))
            {
                continue;
            }

            var relative = Path.GetRelativePath(modRoot, path).Replace('\\', '/');
            var text = File.ReadAllText(path);
            // GetOrCreate("nav") contains the Create("nav") text. Strip the shared-root
            // call before banning a real Create.
            var withoutSharedRoot = text.Replace("GetOrCreate(\"nav\")", "", StringComparison.Ordinal);
            if (withoutSharedRoot.Contains("Create(\"nav\")", StringComparison.Ordinal))
            {
                offenders.Add(relative + " calls Create(\"nav\")");
            }

            if (text.Contains("RegisterCommand(\"nav\"", StringComparison.Ordinal))
            {
                offenders.Add(relative + " calls RegisterCommand(\"nav\"");
            }

            if (text.Contains("GetOrCreate(\"nav\")", StringComparison.Ordinal)
                && Array.IndexOf(NavGetOrCreateAllowlist, relative) < 0)
            {
                offenders.Add(relative + " calls GetOrCreate(\"nav\") outside the allowlist");
            }
        }

        Assert.Empty(offenders);
    }

    private static bool IsBuildOutput(string path)
    {
        var bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        return path.Contains(bin, StringComparison.Ordinal)
            || path.Contains(obj, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_guard_force_throw_is_compiled_only_in_debug()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Infra", "RenderGuard.cs"));
        Assert.Contains("#if DEBUG", source, StringComparison.Ordinal);
        Assert.Contains("ForceThrow", source, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
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
