namespace NuGetPackageFixer.Services;

using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

/// <summary>
/// Queries NuGet package metadata for vulnerability and deprecation information.
/// Uses PackageMetadataResource which returns full metadata including security advisories
/// and deprecation status (unlike PackageSearchResource which omits these).
/// </summary>
public class PackageMetadataService : IDisposable
{
    private readonly SourceCacheContext _cache = new();
    private readonly ILogger _logger = NullLogger.Instance;
    private readonly SemaphoreSlim _throttle;

    public PackageMetadataService(int maxConcurrentQueries = 5)
    {
        _throttle = new SemaphoreSlim(maxConcurrentQueries);
    }

    /// <summary>
    /// Gets vulnerability and deprecation info for a specific package version.
    /// Queries all provided sources and returns the first result that has metadata.
    /// </summary>
    public async Task<PackageSecurityInfo?> GetSecurityInfoAsync(
        string packageId,
        string version,
        IReadOnlyList<PackageSource> sources,
        CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return null;
        }

        foreach (var source in sources)
        {
            await _throttle.WaitAsync(cancellationToken);
            try
            {
                var repository = Repository.Factory.GetCoreV3(source);
                var resource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);

                var metadata = await resource.GetMetadataAsync(
                    packageId,
                    includePrerelease: true,
                    includeUnlisted: false,
                    _cache,
                    _logger,
                    cancellationToken);

                // Find the specific version
                var versionMetadata = metadata
                    .FirstOrDefault(m => m.Identity.Version == nugetVersion);

                if (versionMetadata is null)
                {
                    continue;
                }

                var result = new PackageSecurityInfo();

                // Check vulnerabilities (property, not async method)
                var vulnerabilities = versionMetadata.Vulnerabilities;
                if (vulnerabilities?.Any() == true)
                {
                    var worst = vulnerabilities
                        .OrderByDescending(v => v.Severity)
                        .First();

                    result.IsVulnerable = true;
                    result.VulnerabilitySeverity = worst.Severity switch
                    {
                        0 => "Low",
                        1 => "Moderate",
                        2 => "High",
                        3 => "Critical",
                        _ => $"Severity {worst.Severity}"
                    };
                    result.AdvisoryUrl = worst.AdvisoryUrl?.ToString() ?? "";
                    result.VulnerabilityCount = vulnerabilities.Count();
                }

                // Check deprecation
                var deprecation = await versionMetadata.GetDeprecationMetadataAsync();
                if (deprecation is not null)
                {
                    result.IsDeprecated = true;
                    result.DeprecationReasons = string.Join(", ", deprecation.Reasons ?? []);
                    result.DeprecationMessage = deprecation.Message ?? "";

                    if (deprecation.AlternatePackage is not null)
                    {
                        result.ReplacementPackageId = deprecation.AlternatePackage.PackageId ?? "";
                        result.ReplacementVersionRange = deprecation.AlternatePackage.Range?.ToString() ?? "";
                    }
                }

                // Only return if we found something interesting
                if (result.IsVulnerable || result.IsDeprecated)
                {
                    return result;
                }

                // Package found but no issues -- return null (not vulnerable, not deprecated)
                return null;
            }
            catch (Exception)
            {
                // Feed unreachable or API not supported -- try next source
                continue;
            }
            finally
            {
                _throttle.Release();
            }
        }

        return null;
    }

    public void Dispose()
    {
        _cache.Dispose();
        _throttle.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Vulnerability and deprecation information for a specific package version.
/// </summary>
public class PackageSecurityInfo
{
    // Vulnerability
    public bool IsVulnerable { get; set; }
    public string VulnerabilitySeverity { get; set; } = "";
    public string AdvisoryUrl { get; set; } = "";
    public int VulnerabilityCount { get; set; }

    // Deprecation
    public bool IsDeprecated { get; set; }
    public string DeprecationReasons { get; set; } = "";
    public string DeprecationMessage { get; set; } = "";
    public string ReplacementPackageId { get; set; } = "";
    public string ReplacementVersionRange { get; set; } = "";
}
