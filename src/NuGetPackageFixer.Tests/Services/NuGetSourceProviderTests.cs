namespace NuGetPackageFixer.Tests.Services;

using NuGet.Configuration;
using NuGetPackageFixer.Services;

[TestClass]
public class NuGetSourceProviderTests
{
    #region Unit Tests

    [TestMethod]
    [TestCategory("Unit")]
    public void CanReadSourcesFromNuGetConfig()
    {
        // Uses the user's global nuget.config (always exists)
        using var provider = new NuGetSourceProvider();
        var sources = provider.GetConfiguredSources(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        Assert.IsTrue(sources.Count > 0, "Should find at least nuget.org");
        Assert.IsTrue(sources.Any(s =>
            s.Source.Contains("nuget.org", StringComparison.OrdinalIgnoreCase)),
            "Should include nuget.org");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CanGetConfigFilePaths()
    {
        using var provider = new NuGetSourceProvider();
        var paths = provider.GetConfigFilePaths(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        Assert.IsTrue(paths.Count > 0, "Should find at least one nuget.config");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CanReadSourcesFromSolutionDirectory()
    {
        // Use the amvs directory which has its own nuget.config
        var amvsDir = @"D:\CODE\amvs";
        if (!Directory.Exists(amvsDir))
        {
            Assert.Inconclusive("amvs directory not available");
        }

        using var provider = new NuGetSourceProvider();
        var sources = provider.GetConfiguredSources(amvsDir);

        // Should find nuget.org + am-private at minimum
        Assert.IsTrue(sources.Count >= 2,
            $"Expected at least 2 sources, found {sources.Count}: {string.Join(", ", sources.Select(s => s.Name))}");
    }

    #endregion

    #region Integration Tests (API calls)

    [TestMethod]
    [TestCategory("Integration.Api")]
    public async Task CanQueryNuGetOrgForStableVersions()
    {
        using var provider = new NuGetSourceProvider();
        var sources = new List<PackageSource>
        {
            new("https://api.nuget.org/v3/index.json", "nuget.org")
        };

        var result = await provider.GetLatestVersionAsync(
            "MailKit", includePrerelease: false, sources, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Version.Major >= 4, $"MailKit should be at least v4, got {result.Version}");
        Assert.IsFalse(result.Version.IsPrerelease, "Should be stable version");
        Assert.AreEqual("nuget.org", result.SourceName);
    }

    [TestMethod]
    [TestCategory("Integration.Api")]
    public async Task CanQueryNuGetOrgFilterPrerelease()
    {
        using var provider = new NuGetSourceProvider();
        var sources = new List<PackageSource>
        {
            new("https://api.nuget.org/v3/index.json", "nuget.org")
        };

        // Query stable only
        var stable = await provider.GetLatestVersionAsync(
            "Serilog.Sinks.PeriodicBatching", includePrerelease: false, sources, CancellationToken.None);

        // Query including prerelease
        var withPre = await provider.GetLatestVersionAsync(
            "Serilog.Sinks.PeriodicBatching", includePrerelease: true, sources, CancellationToken.None);

        Assert.IsNotNull(stable);
        Assert.IsFalse(stable.Version.IsPrerelease, "Stable query should return stable version");

        Assert.IsNotNull(withPre);
        // withPre may or may not be prerelease depending on what's on nuget.org,
        // but it should be >= the stable version
        Assert.IsTrue(withPre.Version >= stable.Version);
    }

    [TestMethod]
    [TestCategory("Integration.Api")]
    public async Task CanQueryAzureArtifactsFeed()
    {
        // OQ1 SPIKE: This test verifies that NuGet.Protocol can query
        // an authenticated Azure Artifacts feed from outside VS.
        var amvsDir = @"D:\CODE\amvs";
        if (!Directory.Exists(amvsDir))
        {
            Assert.Inconclusive("amvs directory not available");
        }

        using var provider = new NuGetSourceProvider();
        var sources = provider.GetConfiguredSources(amvsDir);

        var amPrivate = sources.FirstOrDefault(s =>
            s.Name.Contains("private", StringComparison.OrdinalIgnoreCase) ||
            s.Source.Contains("am-private", StringComparison.OrdinalIgnoreCase));

        if (amPrivate is null)
        {
            Assert.Inconclusive("am-private feed not configured");
        }

        var result = await provider.GetLatestVersionAsync(
            "Am.BaseSystem", includePrerelease: true,
            [amPrivate], CancellationToken.None);

        Assert.IsNotNull(result, "Should find Am.BaseSystem on am-private feed");
        Assert.IsTrue(result.Version.IsPrerelease, "Am.BaseSystem should be prerelease");
        Assert.IsTrue(result.Version.ToString().Contains("4.0.0"),
            $"Expected 4.0.0-*, got {result.Version}");
    }

    [TestMethod]
    [TestCategory("Integration.Api")]
    public async Task CanQueryMultipleFeedsInParallel()
    {
        using var provider = new NuGetSourceProvider();
        var sources = new List<PackageSource>
        {
            new("https://api.nuget.org/v3/index.json", "nuget.org")
        };

        // Query 10 packages in parallel to test throttling
        var packageIds = new[]
        {
            "MailKit", "Serilog", "Newtonsoft.Json", "EntityFramework",
            "Azure.Identity", "NodaTime", "CsvHelper", "HtmlAgilityPack",
            "SkiaSharp", "System.Text.Json"
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = packageIds.Select(id =>
            provider.GetLatestVersionAsync(id, false, sources, CancellationToken.None));
        var results = await Task.WhenAll(tasks);
        sw.Stop();

        var found = results.Count(r => r is not null);
        Assert.IsTrue(found >= 8, $"Expected at least 8 results, got {found}");
        Assert.IsTrue(sw.ElapsedMilliseconds < 30_000,
            $"Parallel queries should complete within 30s, took {sw.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    [TestCategory("Integration.Api")]
    public async Task DetectsBogusVersions()
    {
        // System.ComponentModel.Composition has a bogus version 2010.x on nuget.org
        using var provider = new NuGetSourceProvider();
        var sources = new List<PackageSource>
        {
            new("https://api.nuget.org/v3/index.json", "nuget.org")
        };

        var result = await provider.GetLatestVersionAsync(
            "System.ComponentModel.Composition", includePrerelease: false,
            sources, CancellationToken.None);

        // The provider returns the raw latest version -- bogus detection is done by VersionComparer
        Assert.IsNotNull(result);

        var cmp = VersionComparer.Compare("10.0.2", result.Version.ToString());
        if (result.Version.Major > 200)
        {
            Assert.IsTrue(cmp.IsBogus, "VersionComparer should detect bogus version");
        }
    }

    #endregion
}
