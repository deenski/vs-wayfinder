using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Testing;

namespace Wayfinding.Pathing.Tests;

/// <summary>
/// Extends KAN-15's <see cref="GameReferenceGuardTests"/> (which covers Wayfinding.Pathing) to the other game-free
/// projects, and checks that test-only code can never reach the shipped mod.
/// </summary>
public class PathingProjectGuardTests
{
    private static readonly string[] ForbiddenAssemblies =
    [
        "VintagestoryAPI", "VintagestoryLib", "VintagestoryServer", "VSEssentials", "VSSurvivalMod",
        "VSCreativeMod", "0Harmony", "cairo-sharp", "CairoSharp", "Vintagestory",
    ];

    public static TheoryData<string> GuardedProjects => new()
    {
        "Wayfinding.Pathing",
        "Wayfinding.Pathing.Testing",
        "Wayfinding.Pathing.Tests",
    };

    [Theory]
    [MemberData(nameof(GuardedProjects))]
    public void Project_defines_the_RejectGameReferences_guard(string project)
    {
        XDocument doc = XDocument.Load(ProjectPath(project));
        XElement? target = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName == "Target" && (string?)e.Attribute("Name") == "RejectGameReferences");

        Assert.NotNull(target);
        Assert.Equal("ResolveAssemblyReferences", (string?)target.Attribute("BeforeTargets"));
    }

    [Fact]
    public void Guard_copies_match_the_Pathing_original()
    {
        string original = GuardText("Wayfinding.Pathing").Replace("Wayfinding.Pathing must not", "$(MSBuildProjectName) must not", StringComparison.Ordinal);
        Assert.Equal(original, GuardText("Wayfinding.Pathing.Testing"));
        Assert.Equal(original, GuardText("Wayfinding.Pathing.Tests"));
    }

    [Theory]
    [InlineData("Wayfinding.Pathing.Testing")]
    [InlineData("Wayfinding.Pathing.Tests")]
    public void Project_file_does_not_reference_game_assemblies(string project)
    {
        XDocument doc = XDocument.Load(ProjectPath(project));
        string[] hits = doc.Descendants()
            .Where(e => e.Name.LocalName is "Reference" or "PackageReference" or "ProjectReference")
            .Select(e => $"{(string?)e.Attribute("Include")} {e.Elements().FirstOrDefault(c => c.Name.LocalName == "HintPath")?.Value}")
            .Where(IsGameReference)
            .ToArray();

        Assert.Empty(hits);
    }

    [Fact]
    public void Testing_assembly_does_not_reference_game_assemblies()
    {
        string[] referenced = typeof(FakeGrid).Assembly.GetReferencedAssemblies()
            .Select(n => n.Name ?? "")
            .Where(IsGameReference)
            .ToArray();

        Assert.Empty(referenced);
    }

    [Fact]
    public async Task Testing_build_fails_for_a_game_reference()
    {
        string temp = Path.Combine(Path.GetTempPath(), "wayfinding-testing-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            // Drop the project reference (the copy has no sibling projects) and add a game reference.
            XDocument doc = XDocument.Load(ProjectPath("Wayfinding.Pathing.Testing"));
            doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference").Remove();
            doc.Root!.Add(XElement.Parse("""
                <ItemGroup>
                  <Reference Include="VintagestoryAPI"><HintPath>/x/VintagestoryAPI.dll</HintPath></Reference>
                </ItemGroup>
                """));
            string csproj = Path.Combine(temp, "Wayfinding.Pathing.Testing.csproj");
            doc.Save(csproj);

            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = temp, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "build", csproj, "-nologo", "-v:q" })
            {
                start.ArgumentList.Add(arg);
            }

            start.Environment.Remove("VINTAGE_STORY");
            using Process? process = Process.Start(start);
            Assert.NotNull(process);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "dotnet build did not exit.");
            string output = await stdout + await stderr;

            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("Wayfinding.Pathing.Testing must not reference game assemblies", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Mod_project_references_only_the_shipping_Pathing_project()
    {
        XDocument doc = XDocument.Load(ProjectPath("Wayfinding"));
        string[] projectRefs = doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? "").Replace('\\', '/')))
            .ToArray();

        Assert.Equal(["Wayfinding.Pathing"], projectRefs);
    }

    [Fact]
    public void Mod_zip_allowlist_admits_only_the_mod_and_Pathing_dlls()
    {
        // KAN-15's PackageModZip target refuses any staged dll other than these two, so Wayfinding.Pathing.Testing.dll
        // (FakeGrid, ReferenceSearch) can never be zipped even if something copied it into the output.
        string text = File.ReadAllText(ProjectPath("Wayfinding"));
        Match allow = Regex.Match(text, @"<_UnexpectedDll[^>]*Condition=""([^""]*)""", RegexOptions.Singleline);
        Assert.True(allow.Success, "PackageModZip no longer has the _UnexpectedDll allowlist.");
        string[] allowed = Regex.Matches(allow.Groups[1].Value, @"!= '([^']+)'").Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal(["Wayfinding.dll", "Wayfinding.Pathing.dll"], allowed);
    }

    [Fact]
    public void Test_oracles_do_not_ship_in_the_Pathing_assembly()
    {
        Type[] shipped = typeof(IBlockGrid).Assembly.GetExportedTypes();
        Assert.DoesNotContain(shipped, t => t.Name.StartsWith("ReferenceSearch", StringComparison.Ordinal));
        Assert.DoesNotContain(shipped, t => t.Name == "FakeGrid");
        Assert.Equal("Wayfinding.Pathing.Testing", typeof(ReferenceSearch).Assembly.GetName().Name);
    }

    private static string GuardText(string project)
    {
        string text = File.ReadAllText(ProjectPath(project));
        int start = text.IndexOf("<Target Name=\"RejectGameReferences\"", StringComparison.Ordinal);
        int end = text.IndexOf("</Target>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"{project} has no RejectGameReferences target.");
        return text[start..end];
    }

    private static bool IsGameReference(string text) =>
        text.Contains("VINTAGE_STORY", StringComparison.OrdinalIgnoreCase)
        || ForbiddenAssemblies.Any(name => text.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static string ProjectPath(string project) => Path.Combine(RepoRoot(), project, project + ".csproj");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Wayfinding.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find Wayfinding.sln above the test output.");
    }
}
