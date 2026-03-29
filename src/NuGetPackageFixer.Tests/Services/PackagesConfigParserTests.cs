namespace NuGetPackageFixer.Tests.Services;

using NuGetPackageFixer.Services;

[TestClass]
public class PackagesConfigParserTests
{
    private static string TestDataPath => Path.Combine(
        AppContext.BaseDirectory, "TestData", "packages.config");

    [TestMethod]
    [TestCategory("Unit")]
    public void ParsesPackagesConfig()
    {
        var packages = PackagesConfigParser.Parse(TestDataPath);

        Assert.IsTrue(packages.Count > 0);
        Assert.IsTrue(packages.Any(p => p.Id == "MailKit"));
        Assert.IsTrue(packages.Any(p => p.Id == "Am.BaseSystem"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsPrerelease()
    {
        var packages = PackagesConfigParser.Parse(TestDataPath);

        var amBase = packages.First(p => p.Id == "Am.BaseSystem");
        Assert.IsTrue(amBase.IsPrerelease, "Am.BaseSystem should be prerelease");

        var mailKit = packages.First(p => p.Id == "MailKit");
        Assert.IsFalse(mailKit.IsPrerelease, "MailKit should not be prerelease");

        var periodicBatching = packages.First(p => p.Id == "Serilog.Sinks.PeriodicBatching");
        Assert.IsTrue(periodicBatching.IsPrerelease, "Serilog.Sinks.PeriodicBatching should be prerelease");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParsesTargetFramework()
    {
        var packages = PackagesConfigParser.Parse(TestDataPath);

        var mailKit = packages.First(p => p.Id == "MailKit");
        Assert.AreEqual("net48", mailKit.TargetFramework);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParsesAllowedVersions()
    {
        var packages = PackagesConfigParser.Parse(TestDataPath);

        var amBase = packages.First(p => p.Id == "Am.BaseSystem");
        Assert.AreEqual("[4.0.0-*,)", amBase.AllowedVersions);

        var mailKit = packages.First(p => p.Id == "MailKit");
        Assert.IsNull(mailKit.AllowedVersions);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectsDevelopmentDependency()
    {
        var packages = PackagesConfigParser.Parse(TestDataPath);

        var typescript = packages.First(p => p.Id == "Microsoft.TypeScript.MSBuild");
        Assert.IsTrue(typescript.IsDevelopmentDependency);

        var mailKit = packages.First(p => p.Id == "MailKit");
        Assert.IsFalse(mailKit.IsDevelopmentDependency);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void HandlesNonExistentFile()
    {
        var packages = PackagesConfigParser.Parse("nonexistent.config");

        Assert.AreEqual(0, packages.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParsesCorrectPackageCount()
    {
        var packages = PackagesConfigParser.Parse(TestDataPath);

        Assert.AreEqual(17, packages.Count);
    }
}
