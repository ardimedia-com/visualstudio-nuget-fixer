namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Services;

[TestClass]
public class ContentPackageDetectorTests
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
    public void DetectsKnownContentPackage()
    {
        Assert.IsTrue(ContentPackageDetector.IsContentPackage("jQuery", "3.7.1", _tempDir));
        Assert.IsTrue(ContentPackageDetector.IsContentPackage("Modernizr", "2.8.3", _tempDir));
        Assert.IsTrue(ContentPackageDetector.IsContentPackage("Bootstrap", "5.0.0", _tempDir));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NonContentPackageNotDetected()
    {
        Assert.IsFalse(ContentPackageDetector.IsContentPackage("MailKit", "4.15.1", _tempDir));
        Assert.IsFalse(ContentPackageDetector.IsContentPackage("Serilog", "4.3.1", _tempDir));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsContentFolder()
    {
        // Create packages/MyPkg.1.0.0/content/ structure
        var packagesDir = Path.Combine(_tempDir, "packages", "MyPkg.1.0.0", "content");
        Directory.CreateDirectory(packagesDir);

        // Project directory is one level below temp dir
        var projectDir = Path.Combine(_tempDir, "MyProject");
        Directory.CreateDirectory(projectDir);

        Assert.IsTrue(ContentPackageDetector.IsContentPackage("MyPkg", "1.0.0", projectDir));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsInstallScript()
    {
        // Create packages/MyPkg.1.0.0/tools/install.ps1
        var toolsDir = Path.Combine(_tempDir, "packages", "MyPkg.1.0.0", "tools");
        Directory.CreateDirectory(toolsDir);
        File.WriteAllText(Path.Combine(toolsDir, "install.ps1"), "# install script");

        var projectDir = Path.Combine(_tempDir, "MyProject");
        Directory.CreateDirectory(projectDir);

        Assert.IsTrue(ContentPackageDetector.IsContentPackage("MyPkg", "1.0.0", projectDir));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void KnownListIsCaseInsensitive()
    {
        Assert.IsTrue(ContentPackageDetector.IsContentPackage("jquery", "3.7.1", _tempDir));
        Assert.IsTrue(ContentPackageDetector.IsContentPackage("JQUERY", "3.7.1", _tempDir));
    }
}
