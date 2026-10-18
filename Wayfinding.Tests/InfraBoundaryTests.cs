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
    public void Nav_root_uses_get_or_create_and_only_stats()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Commands", "NavCommands.cs"));
        Assert.Contains(".GetOrCreate(\"nav\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Create(\"nav\")", source, StringComparison.Ordinal);
        Assert.Contains("BeginSubCommand(\"stats\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSubCommand(\"share\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSubCommand(\"team\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_guard_force_throw_is_compiled_only_in_debug()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Infra", "RenderGuard.cs"));
        Assert.Contains("#if DEBUG", source, StringComparison.Ordinal);
        Assert.Contains("ForceThrow", source, StringComparison.Ordinal);
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
