using System.Reflection;
using System.Xml.Linq;

namespace Wayfinding.Pathing.Tests;

public class GameReferenceGuardTests
{
    private static readonly string[] ForbiddenAssemblies =
    [
        "VintagestoryAPI",
        "VintagestoryLib",
        "VintagestoryServer",
        "VSEssentials",
        "VSSurvivalMod",
        "VSCreativeMod",
        "0Harmony",
        "cairo-sharp",
        "CairoSharp",
    ];

    [Fact]
    public void Project_file_does_not_reference_game_assemblies()
    {
        var csproj = Path.Combine(RepoRoot(), "Wayfinding.Pathing", "Wayfinding.Pathing.csproj");
        var doc = XDocument.Load(csproj);
        var hits = new List<string>();

        foreach (var element in doc.Descendants())
        {
            if (element.Name.LocalName is not ("Reference" or "PackageReference" or "ProjectReference"))
            {
                continue;
            }

            var include = element.Attribute("Include")?.Value ?? "";
            var hint = element.Elements().FirstOrDefault(e => e.Name.LocalName == "HintPath")?.Value ?? "";
            var blob = $"{include} {hint}".Trim();
            if (IsGameReference(blob))
            {
                hits.Add(blob);
            }
        }

        Assert.Empty(hits);
    }

    [Fact]
    public void Assembly_does_not_reference_game_assemblies()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Wayfinding.Pathing.dll");
        Assert.True(File.Exists(dll), "Wayfinding.Pathing.dll was not copied beside the tests.");

        var referenced = Assembly.LoadFrom(dll).GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name is not null && IsGameReference(name))
            .ToArray();

        Assert.Empty(referenced);
    }

    private static bool IsGameReference(string text)
    {
        if (text.Contains("VINTAGE_STORY", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ForbiddenAssemblies.Any(name => text.Contains(name, StringComparison.OrdinalIgnoreCase));
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
