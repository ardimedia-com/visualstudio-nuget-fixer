namespace NuGetPackageFixer.Services;

using NuGet.Versioning;

/// <summary>
/// Detects version inconsistencies: the same package at different versions across
/// multiple projects in the solution. Supports both packages.config and PackageReference.
/// </summary>
public static class VersionConsistencyAnalyzer
{
    /// <summary>
    /// Analyzes all packages across projects and returns inconsistencies.
    /// An inconsistency is when the same PackageId appears with different versions
    /// in different projects.
    ///
    /// Filters out false positives:
    /// - Stable vs prerelease mismatch (intentional: one project pins a prerelease, others use stable)
    /// </summary>
    public static List<VersionInconsistency> Detect(IReadOnlyList<ProjectPackageInfo> projects)
    {
        var allEntries = new List<(string PackageId, string Version, string ProjectName, bool IsPrerelease)>();

        foreach (var project in projects)
        {
            var packages = project.Format == "PackageReference"
                ? PackageReferenceParser.Parse(project.ProjectFilePath)
                : PackagesConfigParser.Parse(project.PackagesConfigPath);

            foreach (var pkg in packages)
            {
                allEntries.Add((pkg.Id, pkg.Version, project.ProjectName, pkg.IsPrerelease));
            }
        }

        var inconsistencies = allEntries
            .GroupBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
            .Where(g =>
            {
                var versions = g.Select(e => e.Version).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (versions.Count <= 1)
                {
                    return false;
                }

                // Filter out stable vs prerelease mismatches.
                // If some projects use stable and others use prerelease of the same package,
                // that's intentional -- not a real inconsistency.
                bool hasStable = g.Any(e => !e.IsPrerelease);
                bool hasPrerelease = g.Any(e => e.IsPrerelease);
                if (hasStable && hasPrerelease)
                {
                    // Only flag if there are multiple DISTINCT stable versions
                    // or multiple DISTINCT prerelease versions
                    var stableVersions = g.Where(e => !e.IsPrerelease)
                        .Select(e => e.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    var prereleaseVersions = g.Where(e => e.IsPrerelease)
                        .Select(e => e.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count();

                    return stableVersions > 1 || prereleaseVersions > 1;
                }

                return true;
            })
            .Select(g =>
            {
                // Determine the target version: highest stable, or highest prerelease if all are prerelease
                bool allPrerelease = g.All(e => e.IsPrerelease);

                var targetVersion = allPrerelease
                    ? GetHighestVersion(g.Select(e => e.Version))
                    : GetHighestVersion(g.Where(e => !e.IsPrerelease).Select(e => e.Version));

                // Only include entries that differ from the target AND are the same type (stable/prerelease)
                var relevantEntries = allPrerelease
                    ? g.ToList()
                    : g.Where(e => !e.IsPrerelease).ToList();

                return new VersionInconsistency
                {
                    PackageId = g.Key,
                    TargetVersion = targetVersion,
                    VersionsByProject = relevantEntries
                        .Select(e => new ProjectVersion { ProjectName = e.ProjectName, Version = e.Version })
                        .ToList(),
                };
            })
            .ToList();

        return inconsistencies;
    }

    private static string GetHighestVersion(IEnumerable<string> versions)
    {
        return versions
            .OrderByDescending(v =>
            {
                NuGetVersion.TryParse(v, out var parsed);
                return parsed;
            })
            .First();
    }
}

/// <summary>
/// Represents a package that has different versions in different projects.
/// </summary>
public class VersionInconsistency
{
    public string PackageId { get; init; } = string.Empty;
    public string TargetVersion { get; init; } = string.Empty;
    public List<ProjectVersion> VersionsByProject { get; init; } = [];

    public string Summary =>
        string.Join(", ", this.VersionsByProject.Select(v => $"{v.ProjectName}: {v.Version}"));
}

public class ProjectVersion
{
    public string ProjectName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
}
