namespace NuGetPackageFixer.Services;

using NuGet.Versioning;

/// <summary>
/// Compares NuGet package versions with edge case handling for bogus versions,
/// format padding, major bumps, and prerelease suffixes.
/// </summary>
public static class VersionComparer
{
    /// <summary>
    /// Compares two version strings and returns a classification result.
    /// </summary>
    public static VersionCompareResult Compare(string current, string latest)
    {
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(latest))
        {
            return VersionCompareResult.NotNewer;
        }

        if (!NuGetVersion.TryParse(current, out var currentVersion))
        {
            return VersionCompareResult.NotNewer;
        }

        if (!NuGetVersion.TryParse(latest, out var latestVersion))
        {
            return VersionCompareResult.BogusVersion(latest, "Cannot parse version string");
        }

        // Detect bogus versions (e.g., year-based 2010.x when current is 10.x)
        if (latestVersion.Major > currentVersion.Major * 10 + 100)
        {
            return VersionCompareResult.BogusVersion(latest,
                $"Suspicious version jump: {currentVersion.Major} -> {latestVersion.Major}");
        }

        // Detect zero versions
        if (latestVersion.Major == 0 && currentVersion.Major > 0)
        {
            return VersionCompareResult.BogusVersion(latest, "Latest version is 0");
        }

        // Same version (accounting for normalization: 1.0 == 1.0.0)
        if (latestVersion <= currentVersion)
        {
            return VersionCompareResult.NotNewer;
        }

        // Prerelease to stable promotion (e.g., 4.0.0-rc1 -> 4.0.0)
        if (currentVersion.IsPrerelease && !latestVersion.IsPrerelease)
        {
            return new VersionCompareResult
            {
                IsNewer = true,
                IsMajor = latestVersion.Major > currentVersion.Major,
                UpdateType = Models.UpdateType.StablePromotion,
            };
        }

        // Major version bump
        if (latestVersion.Major > currentVersion.Major)
        {
            return new VersionCompareResult
            {
                IsNewer = true,
                IsMajor = true,
                UpdateType = Models.UpdateType.Major,
            };
        }

        // Prerelease to newer prerelease
        if (currentVersion.IsPrerelease && latestVersion.IsPrerelease)
        {
            return new VersionCompareResult
            {
                IsNewer = true,
                UpdateType = Models.UpdateType.Prerelease,
            };
        }

        // Patch / minor update
        return new VersionCompareResult
        {
            IsNewer = true,
            UpdateType = Models.UpdateType.Patch,
        };
    }
}

/// <summary>
/// Result of comparing two package versions.
/// </summary>
public class VersionCompareResult
{
    public bool IsNewer { get; init; }
    public bool IsMajor { get; init; }
    public bool IsBogus { get; init; }
    public string? BogusReason { get; init; }
    public Models.UpdateType UpdateType { get; init; } = Models.UpdateType.UpToDate;

    public static VersionCompareResult NotNewer => new();

    public static VersionCompareResult BogusVersion(string version, string reason) => new()
    {
        IsBogus = true,
        BogusReason = $"Bogus version '{version}': {reason}",
        UpdateType = Models.UpdateType.Bogus,
    };
}
