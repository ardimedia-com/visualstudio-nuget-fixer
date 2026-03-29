namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Services;

[TestClass]
public class OrphanedReferencePatcherTests
{
    private string _tempDir = null!;
    private string _csprojPath = null!;

    /// <summary>
    /// Real-world .csproj content from Amx.Amms.Ui.Web (trimmed to relevant references).
    /// Contains Am.AsposeLib and FluentFTP as orphaned references.
    /// </summary>
    private const string TestCsprojContent = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project ToolsVersion="Current" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <Import Project="..\packages\Am.Domain.Template.4.0.0-20260319-01\build\Am.Domain.Template.props" Condition="Exists('..\packages\Am.Domain.Template.4.0.0-20260319-01\build\Am.Domain.Template.props')" />
          <Import Project="..\packages\Am.BaseSystem.Template.4.0.0-20260323-01\build\Am.BaseSystem.Template.props" Condition="Exists('..\packages\Am.BaseSystem.Template.4.0.0-20260323-01\build\Am.BaseSystem.Template.props')" />
          <Import Project="..\packages\EntityFramework.6.5.1\build\EntityFramework.props" Condition="Exists('..\packages\EntityFramework.6.5.1\build\EntityFramework.props')" />
          <ItemGroup>
            <Reference Include="Am.AsposeLib, Version=4.0.0.0, Culture=neutral, processorArchitecture=MSIL">
              <HintPath>..\packages\Am.AsposeLib.4.0.0-20260127-02\lib\netstandard2.0\Am.AsposeLib.dll</HintPath>
            </Reference>
            <Reference Include="Am.BaseSystem, Version=4.0.0.0, Culture=neutral, processorArchitecture=MSIL">
              <HintPath>..\packages\Am.BaseSystem.4.0.0-20260323-01\lib\net48\Am.BaseSystem.dll</HintPath>
            </Reference>
            <Reference Include="FluentFTP, Version=53.0.2.0, Culture=neutral, PublicKeyToken=f4af092b1d8df44f, processorArchitecture=MSIL">
              <HintPath>..\packages\FluentFTP.53.0.2\lib\net472\FluentFTP.dll</HintPath>
            </Reference>
            <Reference Include="MailKit, Version=4.15.0.0, Culture=neutral, PublicKeyToken=4e064fe7c44a8f1b, processorArchitecture=MSIL">
              <HintPath>..\packages\MailKit.4.15.1\lib\net48\MailKit.dll</HintPath>
            </Reference>
            <Reference Include="Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed, processorArchitecture=MSIL">
              <HintPath>..\packages\Newtonsoft.Json.13.0.4\lib\net45\Newtonsoft.Json.dll</HintPath>
            </Reference>
            <Reference Include="Serilog, Version=4.3.0.0, Culture=neutral, PublicKeyToken=24c2f752a8e58a10, processorArchitecture=MSIL">
              <HintPath>..\packages\Serilog.4.3.1\lib\net471\Serilog.dll</HintPath>
            </Reference>
          </ItemGroup>
        </Project>
        """;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NuGetFixerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _csprojPath = Path.Combine(_tempDir, "Test.csproj");
        File.WriteAllText(_csprojPath, TestCsprojContent);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemovesAmAsposeLibReference()
    {
        // Act: remove Am.AsposeLib orphaned reference
        int removed = OrphanedReferencePatcher.RemoveOrphanedReferences(
            _csprojPath, "Am.AsposeLib", "4.0.0-20260127-02");

        // Assert
        Assert.IsTrue(removed > 0, "Should have removed at least 1 reference");

        var content = File.ReadAllText(_csprojPath);

        // Am.AsposeLib HintPath should be gone
        Assert.IsFalse(content.Contains("Am.AsposeLib"), "Am.AsposeLib reference should be removed");
        Assert.IsFalse(content.Contains("Am.AsposeLib.4.0.0-20260127-02"), "Am.AsposeLib path should be removed");

        // Other references should still be present
        Assert.IsTrue(content.Contains("Am.BaseSystem"), "Am.BaseSystem should still exist");
        Assert.IsTrue(content.Contains("FluentFTP"), "FluentFTP should still exist");
        Assert.IsTrue(content.Contains("MailKit"), "MailKit should still exist");
        Assert.IsTrue(content.Contains("Newtonsoft.Json"), "Newtonsoft.Json should still exist");
        Assert.IsTrue(content.Contains("Serilog"), "Serilog should still exist");

        // Imports should not be affected
        Assert.IsTrue(content.Contains("Am.Domain.Template"), "Am.Domain.Template import should still exist");
        Assert.IsTrue(content.Contains("Am.BaseSystem.Template"), "Am.BaseSystem.Template import should still exist");
        Assert.IsTrue(content.Contains("EntityFramework"), "EntityFramework import should still exist");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemovesFluentFtpAfterAmAsposeLib()
    {
        // Act: first remove Am.AsposeLib
        OrphanedReferencePatcher.RemoveOrphanedReferences(
            _csprojPath, "Am.AsposeLib", "4.0.0-20260127-02");

        // Then remove FluentFTP
        int removed = OrphanedReferencePatcher.RemoveOrphanedReferences(
            _csprojPath, "FluentFTP", "53.0.2");

        // Assert
        Assert.IsTrue(removed > 0, "Should have removed at least 1 reference");

        var content = File.ReadAllText(_csprojPath);

        // Both should be gone
        Assert.IsFalse(content.Contains("Am.AsposeLib"), "Am.AsposeLib should be removed");
        Assert.IsFalse(content.Contains("FluentFTP"), "FluentFTP should be removed");

        // Remaining references should still be present
        Assert.IsTrue(content.Contains("Am.BaseSystem"), "Am.BaseSystem should still exist");
        Assert.IsTrue(content.Contains("MailKit"), "MailKit should still exist");
        Assert.IsTrue(content.Contains("Newtonsoft.Json"), "Newtonsoft.Json should still exist");
        Assert.IsTrue(content.Contains("Serilog"), "Serilog should still exist");

        // Count remaining Reference elements (should be 4: Am.BaseSystem, MailKit, Newtonsoft.Json, Serilog)
        int referenceCount = content.Split("<Reference Include=").Length - 1;
        Assert.AreEqual(4, referenceCount,
            $"Should have exactly 4 Reference elements remaining, found {referenceCount}");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DoesNotRemoveNonOrphanedReference()
    {
        // Act: try to remove a package that doesn't exist in the csproj
        int removed = OrphanedReferencePatcher.RemoveOrphanedReferences(
            _csprojPath, "NonExistentPackage", "1.0.0");

        // Assert
        Assert.AreEqual(0, removed, "Should not remove anything for non-existent package");

        var content = File.ReadAllText(_csprojPath);

        // All references should still be present
        Assert.IsTrue(content.Contains("Am.AsposeLib"), "Am.AsposeLib should still exist");
        Assert.IsTrue(content.Contains("FluentFTP"), "FluentFTP should still exist");
        Assert.IsTrue(content.Contains("MailKit"), "MailKit should still exist");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DoesNotRemoveReferenceWithDifferentVersion()
    {
        // Act: try to remove Am.AsposeLib with a wrong version
        int removed = OrphanedReferencePatcher.RemoveOrphanedReferences(
            _csprojPath, "Am.AsposeLib", "9.9.9");

        // Assert
        Assert.AreEqual(0, removed, "Should not remove reference with different version");

        var content = File.ReadAllText(_csprojPath);
        Assert.IsTrue(content.Contains("Am.AsposeLib"), "Am.AsposeLib should still exist");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PreservesImportsWhenRemovingReference()
    {
        // The csproj has Import elements for Am.Domain.Template, Am.BaseSystem.Template, EntityFramework
        // Removing Am.AsposeLib (a Reference) should NOT touch these Import elements

        OrphanedReferencePatcher.RemoveOrphanedReferences(
            _csprojPath, "Am.AsposeLib", "4.0.0-20260127-02");

        var content = File.ReadAllText(_csprojPath);

        // All 3 imports should still be present
        Assert.IsTrue(content.Contains(@"Am.Domain.Template.4.0.0-20260319-01\build\Am.Domain.Template.props"),
            "Am.Domain.Template import should be preserved");
        Assert.IsTrue(content.Contains(@"Am.BaseSystem.Template.4.0.0-20260323-01\build\Am.BaseSystem.Template.props"),
            "Am.BaseSystem.Template import should be preserved");
        Assert.IsTrue(content.Contains(@"EntityFramework.6.5.1\build\EntityFramework.props"),
            "EntityFramework import should be preserved");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CsprojRemainsValidXmlAfterRemoval()
    {
        // Remove both orphaned packages
        OrphanedReferencePatcher.RemoveOrphanedReferences(_csprojPath, "Am.AsposeLib", "4.0.0-20260127-02");
        OrphanedReferencePatcher.RemoveOrphanedReferences(_csprojPath, "FluentFTP", "53.0.2");

        // The resulting file should still be valid XML
        var content = File.ReadAllText(_csprojPath);
        Assert.IsTrue(content.Contains("<?xml"), "Should still have XML declaration");
        Assert.IsTrue(content.Contains("<Project"), "Should still have Project element");
        Assert.IsTrue(content.Contains("</Project>"), "Should still have closing Project element");

        // Should be parseable as XML without throwing
        var doc = System.Xml.Linq.XDocument.Parse(content);
        Assert.IsNotNull(doc.Root, "Should parse as valid XML");
    }
}
