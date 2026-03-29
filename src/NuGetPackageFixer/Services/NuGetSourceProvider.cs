namespace NuGetPackageFixer.Services;

using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

/// <summary>
/// Reads configured NuGet sources from VS settings / nuget.config hierarchy
/// and queries feeds for package versions. Answers OQ1 (credentials) and OQ2 (source discovery).
/// </summary>
public class NuGetSourceProvider : IDisposable
{
    private readonly SourceCacheContext _cache = new();
    private readonly ILogger _logger = NullLogger.Instance;
    private readonly SemaphoreSlim _throttle;
    /// <summary>
    /// Creates a new instance with the specified max concurrent feed queries.
    /// </summary>
    public NuGetSourceProvider(int maxConcurrentQueries = 10)
    {
        _throttle = new SemaphoreSlim(maxConcurrentQueries);
    }

    /// <summary>
    /// Reads all enabled NuGet sources from the nuget.config hierarchy
    /// starting at the given root directory.
    /// </summary>
    public List<PackageSource> GetConfiguredSources(string rootDirectory)
    {
        var settings = Settings.LoadDefaultSettings(rootDirectory);
        var sourceProvider = new PackageSourceProvider(settings);

        return sourceProvider.LoadPackageSources()
            .Where(s => s.IsEnabled)
            .ToList();
    }

    /// <summary>
    /// Returns the paths of all nuget.config files that were loaded.
    /// </summary>
    public List<string> GetConfigFilePaths(string rootDirectory)
    {
        var settings = Settings.LoadDefaultSettings(rootDirectory);
        return settings.GetConfigFilePaths().ToList();
    }

    /// <summary>
    /// Queries all enabled feeds for the latest version of a package.
    /// If includePrerelease is false, only stable versions are returned.
    /// Returns null if the package is not found on any feed.
    /// </summary>
    public async Task<PackageVersionResult?> GetLatestVersionAsync(
        string packageId,
        bool includePrerelease,
        IReadOnlyList<PackageSource> sources,
        CancellationToken cancellationToken)
    {
        var tasks = sources.Select(source =>
            QueryFeedAsync(packageId, includePrerelease, source, cancellationToken));

        var results = await Task.WhenAll(tasks);

        return results
            .Where(r => r is not null)
            .MaxBy(r => r!.Version);
    }

    /// <summary>
    /// Queries a single feed for all versions of a package and returns the latest
    /// matching version (stable or prerelease depending on the flag).
    /// On 401, retries with credentials obtained from the credential provider.
    /// </summary>
    private async Task<PackageVersionResult?> QueryFeedAsync(
        string packageId,
        bool includePrerelease,
        PackageSource source,
        CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            // First attempt with the source as-is (credentials from nuget.config if present)
            var result = await TryQueryAsync(packageId, includePrerelease, source, cancellationToken);
            if (result is not null)
            {
                return result;
            }

            // If the source has no credentials and looks like Azure DevOps, try credential provider
            if (source.Credentials is null or { IsPasswordClearText: false, Password: null or "" }
                && IsAzureDevOpsFeed(source.Source))
            {
                var authenticatedSource = await TryGetAuthenticatedSourceAsync(source, cancellationToken);
                if (authenticatedSource is not null)
                {
                    return await TryQueryAsync(packageId, includePrerelease, authenticatedSource, cancellationToken);
                }
            }

            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private async Task<PackageVersionResult?> TryQueryAsync(
        string packageId,
        bool includePrerelease,
        PackageSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            var repository = Repository.Factory.GetCoreV3(source);
            var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

            var versions = await resource.GetAllVersionsAsync(
                packageId, _cache, _logger, cancellationToken);

            var candidates = includePrerelease
                ? versions
                : versions.Where(v => !v.IsPrerelease);

            var latest = candidates.MaxBy(v => v);
            if (latest is null)
            {
                return null;
            }

            return new PackageVersionResult
            {
                Version = latest,
                SourceName = source.Name,
                SourceUrl = source.Source,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Detects whether a feed URL is an Azure DevOps / Azure Artifacts feed.
    /// </summary>
    private static bool IsAzureDevOpsFeed(string sourceUrl)
    {
        return sourceUrl.Contains("pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
            || sourceUrl.Contains(".pkgs.visualstudio.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tries to obtain credentials for a feed by running VS's CredentialProvider.Microsoft.exe.
    /// Returns a new PackageSource with credentials, or null if no credential provider is found.
    /// </summary>
    private static async Task<PackageSource?> TryGetAuthenticatedSourceAsync(
        PackageSource source, CancellationToken cancellationToken)
    {
        var credProviderPath = FindCredentialProviderPath();
        if (credProviderPath is null)
        {
            return null;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = credProviderPath,
                Arguments = $"-Uri \"{source.Source}\" -NonInteractive -IsRetry -F Json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            // Parse JSON response: {"Username":"...","Password":"..."}
            var json = System.Text.Json.JsonDocument.Parse(output);
            var username = json.RootElement.GetProperty("Username").GetString() ?? "";
            var password = json.RootElement.GetProperty("Password").GetString() ?? "";

            if (string.IsNullOrEmpty(password))
            {
                return null;
            }

            var authenticatedSource = new PackageSource(source.Source, source.Name);
            authenticatedSource.Credentials = new PackageSourceCredential(
                source: source.Name,
                username: username,
                passwordText: password,
                isPasswordClearText: true,
                validAuthenticationTypesText: null);

            return authenticatedSource;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Searches for the CredentialProvider.Microsoft.exe in common VS installation paths.
    /// </summary>
    private static string? FindCredentialProviderPath()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] searchPaths =
        [
            // User-installed credential provider (cross-platform)
            Path.Combine(userProfile, ".nuget", "plugins", "netcore",
                "CredentialProvider.Microsoft", "CredentialProvider.Microsoft.exe"),
            // VS 2026 editions
            ..new[] { "Insiders", "Enterprise", "Professional", "Community" }.Select(edition =>
                Path.Combine(programFiles, "Microsoft Visual Studio", "18", edition,
                    "Common7", "IDE", "CommonExtensions", "Microsoft", "NuGet", "Plugins",
                    "CredentialProvider.Microsoft", "CredentialProvider.Microsoft.exe")),
            // VS 2022 editions
            ..new[] { "Enterprise", "Professional", "Community" }.Select(edition =>
                Path.Combine(programFiles, "Microsoft Visual Studio", "2022", edition,
                    "Common7", "IDE", "CommonExtensions", "Microsoft", "NuGet", "Plugins",
                    "CredentialProvider.Microsoft", "CredentialProvider.Microsoft.exe")),
        ];

        return searchPaths.FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _throttle.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Result of querying a NuGet feed for the latest version of a package.
/// </summary>
public class PackageVersionResult
{
    public required NuGetVersion Version { get; init; }
    public required string SourceName { get; init; }
    public required string SourceUrl { get; init; }
}
