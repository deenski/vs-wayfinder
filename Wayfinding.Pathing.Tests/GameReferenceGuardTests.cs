using System.Diagnostics;
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
        "Vintagestory",
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

    [Fact]
    public void Build_fails_for_the_bare_client_assembly()
    {
        var result = BuildWithReference(
            """
            <ItemGroup>
              <Reference Include="Vintagestory">
                <HintPath>/x/Vintagestory.dll</HintPath>
              </Reference>
            </ItemGroup>
            """,
            vintageStory: null);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not reference game assemblies: Vintagestory.", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Vintagestory;Vintagestory", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_fails_for_a_dll_under_the_game_install()
    {
        var result = BuildWithReference(
            """
            <ItemGroup>
              <Reference Include="protobuf-net">
                <HintPath>$(VINTAGE_STORY)/Lib/protobuf-net.dll</HintPath>
              </Reference>
            </ItemGroup>
            """,
            vintageStory: "/tmp/fake-vs-install/");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not reference game assemblies: protobuf-net.", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("protobuf-net;protobuf-net", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_fails_for_a_strong_name_with_a_renamed_path()
    {
        var result = BuildWithReference(
            """
            <ItemGroup>
              <Reference Include="VintagestoryAPI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null">
                <HintPath>/tmp/renamed-api.dll</HintPath>
              </Reference>
            </ItemGroup>
            """,
            vintageStory: null);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("must not reference game assemblies", result.Output, StringComparison.Ordinal);
        Assert.Contains("VintagestoryAPI", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("VintagestoryAPI;VintagestoryAPI", result.Output, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) BuildWithReference(string itemGroup, string? vintageStory)
    {
        var temp = Path.Combine(Path.GetTempPath(), "wayfinding-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding.Pathing", "Wayfinding.Pathing.csproj"));
            var closing = source.LastIndexOf("</Project>", StringComparison.Ordinal);
            Assert.True(closing >= 0, "Copied project has no closing Project tag.");
            File.WriteAllText(Path.Combine(temp, "Wayfinding.Pathing.csproj"), source.Insert(closing, itemGroup + Environment.NewLine));

            var start = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = temp,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("build");
            start.ArgumentList.Add(Path.Combine(temp, "Wayfinding.Pathing.csproj"));
            start.ArgumentList.Add("-nologo");
            start.ArgumentList.Add("-v:q");
            if (vintageStory is null)
            {
                start.Environment.Remove("VINTAGE_STORY");
            }
            else
            {
                start.Environment["VINTAGE_STORY"] = vintageStory;
            }

            using var process = Process.Start(start);
            Assert.NotNull(process);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "dotnet build did not exit.");
            return (process.ExitCode, stdout.Result + stderr.Result);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
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
