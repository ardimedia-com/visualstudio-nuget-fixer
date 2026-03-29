namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Services;

[TestClass]
public class VersionConsistencyAnalyzerTests
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
    public void DetectsInconsistentVersions()
    {
        var projects = new List<ProjectPackageInfo>
        {
            CreateProject("ProjectA", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
                </packages>
                """),
            CreateProject("ProjectB", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="13.0.4" targetFramework="net48" />
                </packages>
                """),
        };

        var result = VersionConsistencyAnalyzer.Detect(projects);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Newtonsoft.Json", result[0].PackageId);
        Assert.AreEqual("13.0.4", result[0].TargetVersion);
        Assert.AreEqual(2, result[0].VersionsByProject.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NoInconsistencyWhenSameVersion()
    {
        var projects = new List<ProjectPackageInfo>
        {
            CreateProject("ProjectA", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="13.0.4" targetFramework="net48" />
                </packages>
                """),
            CreateProject("ProjectB", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="13.0.4" targetFramework="net48" />
                </packages>
                """),
        };

        var result = VersionConsistencyAnalyzer.Detect(projects);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SingleProjectNeverInconsistent()
    {
        var projects = new List<ProjectPackageInfo>
        {
            CreateProject("ProjectA", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
                  <package id="MailKit" version="4.15.1" targetFramework="net48" />
                </packages>
                """),
        };

        var result = VersionConsistencyAnalyzer.Detect(projects);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void StableVsPrereleaseIsNotInconsistent()
    {
        // Serilog.Sinks.PeriodicBatching 5.0.0 (stable) in most projects,
        // 5.0.1-dev-00860 (prerelease) in one project -- not a real inconsistency
        var projects = new List<ProjectPackageInfo>
        {
            CreateProject("ProjectA", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Serilog.Sinks.PeriodicBatching" version="5.0.0" targetFramework="net48" />
                </packages>
                """),
            CreateProject("ProjectB", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Serilog.Sinks.PeriodicBatching" version="5.0.1-dev-00860" targetFramework="net48" />
                </packages>
                """),
        };

        var result = VersionConsistencyAnalyzer.Detect(projects);

        Assert.AreEqual(0, result.Count, "Stable vs prerelease should not be flagged as inconsistent");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MultipleStableVersionsStillInconsistent()
    {
        // Same package at different STABLE versions -- that IS a real inconsistency
        var projects = new List<ProjectPackageInfo>
        {
            CreateProject("ProjectA", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
                </packages>
                """),
            CreateProject("ProjectB", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="13.0.4" targetFramework="net48" />
                </packages>
                """),
            CreateProject("ProjectC", """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Newtonsoft.Json" version="13.0.3" targetFramework="net48" />
                  <package id="Serilog" version="4.3.0-dev" targetFramework="net48" />
                </packages>
                """),
        };

        var result = VersionConsistencyAnalyzer.Detect(projects);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Newtonsoft.Json", result[0].PackageId);
        Assert.AreEqual("13.0.4", result[0].TargetVersion);
    }

    private ProjectPackageInfo CreateProject(string name, string packagesConfigContent)
    {
        var projectDir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(projectDir);
        var configPath = Path.Combine(projectDir, "packages.config");
        File.WriteAllText(configPath, packagesConfigContent);

        return new ProjectPackageInfo
        {
            ProjectName = name,
            ProjectDirectory = projectDir,
            ProjectFilePath = Path.Combine(projectDir, $"{name}.csproj"),
            PackagesConfigPath = configPath,
            Format = "packages.config",
        };
    }
}
