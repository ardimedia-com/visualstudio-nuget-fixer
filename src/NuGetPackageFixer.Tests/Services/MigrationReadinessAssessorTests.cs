namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Services;

[TestClass]
public class MigrationReadinessAssessorTests
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
    public void DetectsNonSdkProject()
    {
        var csproj = CreateFile("test.csproj", """
            <Project ToolsVersion="Current" DefaultTargets="Build"
                     xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
            </Project>
            """);
        var config = CreateFile("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages />
            """);

        var result = MigrationReadinessAssessor.Assess(csproj, config, _tempDir);

        Assert.IsFalse(result.IsReady);
        Assert.IsTrue(result.Blockers.Any(b => b.Type == BlockerType.NonSdkProject));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsWebProjectType()
    {
        var csproj = CreateFile("test.csproj", """
            <Project ToolsVersion="Current">
              <PropertyGroup>
                <ProjectTypeGuids>{349c5851-65df-11da-9384-00065b846f21};{fae04ec0-301f-11d3-bf4b-00c04f79efbc}</ProjectTypeGuids>
              </PropertyGroup>
            </Project>
            """);
        var config = CreateFile("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages />
            """);

        var result = MigrationReadinessAssessor.Assess(csproj, config, _tempDir);

        Assert.IsFalse(result.IsReady);
        Assert.IsTrue(result.Blockers.Any(b => b.Type == BlockerType.WebProjectType));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsBuildImports()
    {
        var csproj = CreateFile("test.csproj", """
            <Project ToolsVersion="Current">
              <Import Project="..\packages\EntityFramework.6.5.1\build\EntityFramework.props" />
            </Project>
            """);
        var config = CreateFile("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages />
            """);

        var result = MigrationReadinessAssessor.Assess(csproj, config, _tempDir);

        Assert.IsTrue(result.Blockers.Any(b => b.Type == BlockerType.BuildImports));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsEnsureNuGetPackageBuildImports()
    {
        var csproj = CreateFile("test.csproj", """
            <Project ToolsVersion="Current">
              <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
                <Error Condition="!Exists('..\packages\EntityFramework.6.5.1\build\EntityFramework.props')" />
              </Target>
            </Project>
            """);
        var config = CreateFile("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages />
            """);

        var result = MigrationReadinessAssessor.Assess(csproj, config, _tempDir);

        Assert.IsTrue(result.Blockers.Any(b => b.Type == BlockerType.BuildImportGuards));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsContentPackages()
    {
        var config = CreateFile("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="jQuery" version="3.7.1" targetFramework="net48" />
              <package id="MailKit" version="4.15.1" targetFramework="net48" />
            </packages>
            """);
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """);

        var result = MigrationReadinessAssessor.Assess(csproj, config, _tempDir);

        Assert.IsTrue(result.Blockers.Any(b => b.Type == BlockerType.ContentPackages));
        var blocker = result.Blockers.First(b => b.Type == BlockerType.ContentPackages);
        Assert.IsTrue(blocker.Description.Contains("jQuery"));
        Assert.IsFalse(blocker.Description.Contains("MailKit"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SdkProjectWithNoBlockersIsReady()
    {
        var csproj = CreateFile("test.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var config = CreateFile("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="MailKit" version="4.15.1" targetFramework="net48" />
            </packages>
            """);

        var result = MigrationReadinessAssessor.Assess(csproj, config, _tempDir);

        Assert.IsTrue(result.IsReady);
        Assert.AreEqual(0, result.Blockers.Count);
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
