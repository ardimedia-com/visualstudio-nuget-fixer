namespace NuGetPackageFixer.Services;

using System.Xml.Linq;

/// <summary>
/// Parses packages.config XML files and returns structured package entries.
/// </summary>
public static class PackagesConfigParser
{
    /// <summary>
    /// Parses a packages.config file and returns all package entries.
    /// </summary>
    public static List<PackageEntry> Parse(string packagesConfigPath)
    {
        if (!File.Exists(packagesConfigPath))
        {
            return [];
        }

        var doc = XDocument.Load(packagesConfigPath);
        var packages = doc.Root?.Elements("package") ?? [];

        return packages
            .Select(p => new PackageEntry
            {
                Id = p.Attribute("id")?.Value ?? string.Empty,
                Version = p.Attribute("version")?.Value ?? string.Empty,
                TargetFramework = p.Attribute("targetFramework")?.Value ?? string.Empty,
                AllowedVersions = p.Attribute("allowedVersions")?.Value,
                IsDevelopmentDependency = string.Equals(
                    p.Attribute("developmentDependency")?.Value, "true",
                    StringComparison.OrdinalIgnoreCase),
            })
            .Where(p => !string.IsNullOrEmpty(p.Id) && !string.IsNullOrEmpty(p.Version))
            .ToList();
    }
}

/// <summary>
/// Represents a single package entry from packages.config.
/// </summary>
public class PackageEntry
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public string? AllowedVersions { get; set; }
    public bool IsDevelopmentDependency { get; set; }

    /// <summary>
    /// Returns true if the current version is a prerelease version (contains '-').
    /// </summary>
    public bool IsPrerelease => this.Version.Contains('-');

    /// <summary>
    /// Returns the raw XML representation of this package entry.
    /// </summary>
    public string ToXmlString() =>
        $"<package id=\"{this.Id}\" version=\"{this.Version}\" targetFramework=\"{this.TargetFramework}\" />";
}
