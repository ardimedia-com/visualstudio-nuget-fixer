namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Services;

[TestClass]
public class SolutionScannerTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NuGetFixerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsPackagesConfigProject()
    {
        var projDir = Path.Combine(_tempDir, "WebApp");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "WebApp.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(projDir, "packages.config"), "<packages />");

        var result = SolutionScanner.ScanProjects([("WebApp", Path.Combine(projDir, "WebApp.csproj"))]);

        Assert.AreEqual(1, result.PackagesConfigProjects.Count);
        Assert.AreEqual(0, result.PackageReferenceProjects.Count);
        Assert.AreEqual("packages.config", result.PackagesConfigProjects[0].Format);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsPackageReferenceProject()
    {
        var projDir = Path.Combine(_tempDir, "LibProject");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "LibProject.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
              </ItemGroup>
            </Project>
            """);

        var result = SolutionScanner.ScanProjects([("LibProject", Path.Combine(projDir, "LibProject.csproj"))]);

        Assert.AreEqual(0, result.PackagesConfigProjects.Count);
        Assert.AreEqual(1, result.PackageReferenceProjects.Count);
        Assert.AreEqual("PackageReference", result.PackageReferenceProjects[0].Format);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsBothFormats()
    {
        var pcDir = Path.Combine(_tempDir, "WebApp");
        Directory.CreateDirectory(pcDir);
        File.WriteAllText(Path.Combine(pcDir, "WebApp.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(pcDir, "packages.config"), "<packages />");

        var prDir = Path.Combine(_tempDir, "LibProject");
        Directory.CreateDirectory(prDir);
        File.WriteAllText(Path.Combine(prDir, "LibProject.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Serilog" Version="4.3.1" />
              </ItemGroup>
            </Project>
            """);

        var result = SolutionScanner.ScanProjects([
            ("WebApp", Path.Combine(pcDir, "WebApp.csproj")),
            ("LibProject", Path.Combine(prDir, "LibProject.csproj")),
        ]);

        Assert.AreEqual(1, result.PackagesConfigProjects.Count);
        Assert.AreEqual(1, result.PackageReferenceProjects.Count);
        Assert.IsTrue(result.HasAnyPackages);
        Assert.AreEqual(2, result.TotalProjects);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PackagesConfigTakesPriorityOverPackageReference()
    {
        // Some projects have both -- packages.config wins
        var projDir = Path.Combine(_tempDir, "HybridProject");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "HybridProject.csproj"), """
            <Project>
              <ItemGroup>
                <PackageReference Include="Something" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(projDir, "packages.config"), "<packages />");

        var result = SolutionScanner.ScanProjects([("HybridProject", Path.Combine(projDir, "HybridProject.csproj"))]);

        Assert.AreEqual(1, result.PackagesConfigProjects.Count);
        Assert.AreEqual(0, result.PackageReferenceProjects.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProjectWithoutPackagesReturnsEmpty()
    {
        var projDir = Path.Combine(_tempDir, "EmptyProject");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "EmptyProject.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var result = SolutionScanner.ScanProjects([("EmptyProject", Path.Combine(projDir, "EmptyProject.csproj"))]);

        Assert.AreEqual(0, result.PackagesConfigProjects.Count);
        Assert.AreEqual(0, result.PackageReferenceProjects.Count);
        Assert.IsFalse(result.HasAnyPackages);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BackwardsCompatibleFindPackagesConfigFiles()
    {
        var projDir = Path.Combine(_tempDir, "WebApp");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "WebApp.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(projDir, "packages.config"), "<packages />");

        var result = SolutionScanner.FindPackagesConfigFiles([("WebApp", Path.Combine(projDir, "WebApp.csproj"))]);

        Assert.AreEqual(1, result.Count);
    }
}
