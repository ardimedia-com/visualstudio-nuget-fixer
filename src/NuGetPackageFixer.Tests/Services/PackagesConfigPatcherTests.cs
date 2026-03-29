namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Services;

[TestClass]
public class PackagesConfigPatcherTests
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
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region UpdateVersion

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_UpdatesMatchingPackage()
    {
        var path = CreateTestPackagesConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="MailKit" version="4.14.1" targetFramework="net48" />
              <package id="Serilog" version="4.3.0" targetFramework="net48" />
            </packages>
            """);

        bool result = PackagesConfigPatcher.UpdateVersion(path, "MailKit", "4.14.1", "4.15.1");

        Assert.IsTrue(result);
        var content = File.ReadAllText(path);
        Assert.IsTrue(content.Contains("version=\"4.15.1\""));
        Assert.IsTrue(content.Contains("version=\"4.3.0\""), "Serilog should be unchanged");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_ReturnsFalseForMissingPackage()
    {
        var path = CreateTestPackagesConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="MailKit" version="4.14.1" targetFramework="net48" />
            </packages>
            """);

        bool result = PackagesConfigPatcher.UpdateVersion(path, "NonExistent", "1.0.0", "2.0.0");

        Assert.IsFalse(result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateVersion_ReturnsFalseForWrongVersion()
    {
        var path = CreateTestPackagesConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <packages>
              <package id="MailKit" version="4.14.1" targetFramework="net48" />
            </packages>
            """);

        bool result = PackagesConfigPatcher.UpdateVersion(path, "MailKit", "4.13.0", "4.15.1");

        Assert.IsFalse(result, "Should not update when current version doesn't match");
    }

    #endregion

    #region UpdateCsprojPathTokens

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateCsprojPathTokens_ReplacesHintPath()
    {
        var path = CreateTestFile("test.csproj", """
            <Reference Include="MailKit, Version=4.14.0.0">
              <HintPath>..\packages\MailKit.4.14.1\lib\net48\MailKit.dll</HintPath>
            </Reference>
            """);

        bool result = PackagesConfigPatcher.UpdateCsprojPathTokens(path, "MailKit", "4.14.1", "4.15.1");

        Assert.IsTrue(result);
        var content = File.ReadAllText(path);
        Assert.IsTrue(content.Contains("MailKit.4.15.1"));
        Assert.IsFalse(content.Contains("MailKit.4.14.1"));
        // Version=4.14.0.0 should NOT be changed (that's the assembly version, not NuGet version)
        Assert.IsTrue(content.Contains("Version=4.14.0.0"), "Assembly version in Include should not be touched by path token replacement");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateCsprojPathTokens_ReplacesImportAndErrorGuard()
    {
        var path = CreateTestFile("test.csproj", """
            <Import Project="..\packages\Serilog.4.3.0\build\Serilog.targets"
                    Condition="Exists('..\packages\Serilog.4.3.0\build\Serilog.targets')" />
            <Error Condition="!Exists('..\packages\Serilog.4.3.0\build\Serilog.targets')"
                   Text="NuGet packages missing" />
            """);

        bool result = PackagesConfigPatcher.UpdateCsprojPathTokens(path, "Serilog", "4.3.0", "4.3.1");

        Assert.IsTrue(result);
        var content = File.ReadAllText(path);
        Assert.IsTrue(content.Contains("Serilog.4.3.1"));
        Assert.IsFalse(content.Contains("Serilog.4.3.0"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateCsprojPathTokens_ReturnsFalseWhenNoMatch()
    {
        var path = CreateTestFile("test.csproj", """
            <Reference Include="SomeOther, Version=1.0.0.0">
              <HintPath>..\packages\SomeOther.1.0.0\lib\net48\SomeOther.dll</HintPath>
            </Reference>
            """);

        bool result = PackagesConfigPatcher.UpdateCsprojPathTokens(path, "MailKit", "4.14.1", "4.15.1");

        Assert.IsFalse(result);
    }

    #endregion

    #region Backup

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateBackup_CreatesTimestampedCopy()
    {
        var path = CreateTestFile("test.config", "<test />");

        var backupPath = PackagesConfigPatcher.CreateBackup(path, "2026-03-27-2100");

        Assert.IsNotNull(backupPath);
        Assert.IsTrue(File.Exists(backupPath));
        Assert.IsTrue(backupPath!.EndsWith(".2026-03-27-2100.bak"));
        Assert.AreEqual("<test />", File.ReadAllText(backupPath));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateBackup_SkipsIfAlreadyExists()
    {
        var path = CreateTestFile("test.config", "<original />");
        PackagesConfigPatcher.CreateBackup(path, "2026-03-27-2100");

        // Change the original
        File.WriteAllText(path, "<modified />");
        var backupPath = PackagesConfigPatcher.CreateBackup(path, "2026-03-27-2100");

        // Backup should still contain original content (not overwritten)
        Assert.AreEqual("<original />", File.ReadAllText(backupPath!));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateBackup_ReturnsNullForMissingFile()
    {
        var result = PackagesConfigPatcher.CreateBackup(
            Path.Combine(_tempDir, "nonexistent.config"), "2026-03-27-2100");

        Assert.IsNull(result);
    }

    #endregion

    #region Helpers

    private string CreateTestPackagesConfig(string content)
    {
        return CreateTestFile("packages.config", content);
    }

    private string CreateTestFile(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    #endregion
}
