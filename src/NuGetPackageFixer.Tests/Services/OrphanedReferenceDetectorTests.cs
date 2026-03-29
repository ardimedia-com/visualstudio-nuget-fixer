namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Services;

[TestClass]
public class OrphanedReferenceDetectorTests
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
    public void DetectsOrphanedHintPath()
    {
        var csproj = CreateFile("test.csproj", """
            <Reference Include="FluentFTP, Version=53.0.2.0">
              <HintPath>..\packages\FluentFTP.53.0.2\lib\net48\FluentFTP.dll</HintPath>
            </Reference>
            <Reference Include="MailKit, Version=4.15.0.0">
              <HintPath>..\packages\MailKit.4.15.1\lib\net48\MailKit.dll</HintPath>
            </Reference>
            """);
        var config = CreateFile("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="MailKit" version="4.15.1" targetFramework="net48" />
            </packages>
            """);

        var result = OrphanedReferenceDetector.Detect(csproj, config);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("FluentFTP", result[0].PackageId);
        Assert.IsTrue(result[0].HasHintPath);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsOrphanedImportAndErrorGuard()
    {
        var csproj = CreateFile("test.csproj", """
            <Import Project="..\packages\EntityFramework.6.5.1\build\EntityFramework.props"
                    Condition="Exists('..\packages\EntityFramework.6.5.1\build\EntityFramework.props')" />
            <Error Condition="!Exists('..\packages\EntityFramework.6.5.1\build\EntityFramework.props')"
                   Text="Missing" />
            """);
        var config = CreateFile("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages />
            """);

        var result = OrphanedReferenceDetector.Detect(csproj, config);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("EntityFramework", result[0].PackageId);
        Assert.IsTrue(result[0].HasImport);
        Assert.IsTrue(result[0].HasErrorGuard);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NoOrphansWhenAllInConfig()
    {
        var csproj = CreateFile("test.csproj", """
            <Reference Include="MailKit, Version=4.15.0.0">
              <HintPath>..\packages\MailKit.4.15.1\lib\net48\MailKit.dll</HintPath>
            </Reference>
            """);
        var config = CreateFile("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="MailKit" version="4.15.1" targetFramework="net48" />
            </packages>
            """);

        var result = OrphanedReferenceDetector.Detect(csproj, config);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void HandlesMultipleOrphans()
    {
        var csproj = CreateFile("test.csproj", """
            <HintPath>..\packages\FluentFTP.53.0.2\lib\FluentFTP.dll</HintPath>
            <HintPath>..\packages\Am.AsposeLib.4.0.0-20260127-02\lib\Am.AsposeLib.dll</HintPath>
            <HintPath>..\packages\MailKit.4.15.1\lib\MailKit.dll</HintPath>
            """);
        var config = CreateFile("packages.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="MailKit" version="4.15.1" targetFramework="net48" />
            </packages>
            """);

        var result = OrphanedReferenceDetector.Detect(csproj, config);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.Any(r => r.PackageId == "FluentFTP"));
        Assert.IsTrue(result.Any(r => r.PackageId == "Am.AsposeLib"));
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
