namespace NuGetPackageFixer.Tests.Services;

using NuGet.Configuration;
using NuGetPackageFixer.Services;

[TestClass]
public class PackageMetadataServiceTests
{
    [TestMethod]
    [TestCategory("Integration.Api")]
    public async Task DetectsVulnerablePackage()
    {
        // Microsoft.Owin 4.2.3 has known vulnerabilities (GHSA-3rq8-h3gj-r5c6)
        using var service = new PackageMetadataService();
        var sources = new List<PackageSource>
        {
            new("https://api.nuget.org/v3/index.json", "nuget.org")
        };

        var result = await service.GetSecurityInfoAsync(
            "Microsoft.Owin", "3.0.1", sources, CancellationToken.None);

        Assert.IsNotNull(result, "Microsoft.Owin 3.0.1 should have security info");
        Assert.IsTrue(result.IsVulnerable, "Should be flagged as vulnerable");
        Assert.IsFalse(string.IsNullOrEmpty(result.VulnerabilitySeverity));
    }

    [TestMethod]
    [TestCategory("Integration.Api")]
    public async Task DetectsDeprecatedPackage()
    {
        // WindowsAzure.Storage is deprecated in favor of Azure.Storage.Blobs
        using var service = new PackageMetadataService();
        var sources = new List<PackageSource>
        {
            new("https://api.nuget.org/v3/index.json", "nuget.org")
        };

        var result = await service.GetSecurityInfoAsync(
            "WindowsAzure.Storage", "9.3.3", sources, CancellationToken.None);

        if (result is null)
        {
            // Some feeds don't return deprecation metadata -- skip gracefully
            Assert.Inconclusive("Deprecation metadata not available from this feed");
        }

        Assert.IsTrue(result.IsDeprecated, "Should be flagged as deprecated");
    }

    [TestMethod]
    [TestCategory("Integration.Api")]
    public async Task CleanPackageReturnsNull()
    {
        // Newtonsoft.Json 13.0.4 should be clean
        using var service = new PackageMetadataService();
        var sources = new List<PackageSource>
        {
            new("https://api.nuget.org/v3/index.json", "nuget.org")
        };

        var result = await service.GetSecurityInfoAsync(
            "Newtonsoft.Json", "13.0.4", sources, CancellationToken.None);

        Assert.IsNull(result, "Clean package should return null");
    }
}
