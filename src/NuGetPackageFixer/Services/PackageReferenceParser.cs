namespace NuGetPackageFixer.Services;

using System.Xml.Linq;

/// <summary>
/// Parses PackageReference elements from SDK-style .csproj files.
/// </summary>
public static class PackageReferenceParser
{
    /// <summary>
    /// Reads all PackageReference elements from a .csproj file.
    /// Handles both default namespace and explicit MSBuild namespace.
    /// </summary>
    public static List<PackageEntry> Parse(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            var root = doc.Root;
            if (root is null)
            {
                return [];
            }

            var ns = root.GetDefaultNamespace();
            var results = new List<PackageEntry>();

            // Find all PackageReference elements (in any ItemGroup)
            var packageRefs = root.Descendants(ns + "PackageReference");

            // First-wins for multi-entry same-Include (e.g. multi-target conditional ItemGroups).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in packageRefs)
            {
                var id = element.Attribute("Include")?.Value;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (!seen.Add(id))
                {
                    continue;
                }

                // Version can be attribute or child element
                var version = element.Attribute("Version")?.Value
                    ?? element.Element(ns + "Version")?.Value
                    ?? string.Empty;

                // Skip packages without a version (e.g., CPM, or implicit version)
                if (string.IsNullOrEmpty(version))
                {
                    continue;
                }

                var privateAssets = element.Attribute("PrivateAssets")?.Value
                    ?? element.Element(ns + "PrivateAssets")?.Value;

                var hasCondition = element.Attribute("Condition") is not null
                    || element.Parent?.Attribute("Condition") is not null;

                var isFloating = version.Contains('*');

                var hasVersionOverride = element.Attribute("VersionOverride") is not null
                    || element.Element(ns + "VersionOverride") is not null;

                results.Add(new PackageEntry
                {
                    Id = id,
                    Version = version,
                    TargetFramework = string.Empty, // Not applicable for PackageReference
                    IsDevelopmentDependency = string.Equals(privateAssets, "all", StringComparison.OrdinalIgnoreCase),
                    HasCondition = hasCondition,
                    IsFloating = isFloating,
                    HasVersionOverride = hasVersionOverride,
                });
            }

            return results;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Determines if a project uses Central Package Management (CPM).
    /// Returns true if the .csproj has ManagePackageVersionsCentrally set to true,
    /// or if a Directory.Packages.props exists anywhere from the project directory up to the drive root.
    /// </summary>
    public static bool ProjectUsesCpm(string csprojPath)
    {
        try
        {
            // Check .csproj for explicit ManagePackageVersionsCentrally
            var doc = XDocument.Load(csprojPath);
            var root = doc.Root;
            if (root is not null)
            {
                var ns = root.GetDefaultNamespace();
                var cpmProperty = root.Descendants(ns + "ManagePackageVersionsCentrally")
                    .FirstOrDefault();

                if (cpmProperty is not null)
                {
                    var val = cpmProperty.Value.Trim();
                    if (string.Equals(val, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (string.Equals(val, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        // Explicit opt-out — don't walk directories for Directory.Packages.props
                        return false;
                    }
                    // Unknown/whitespace value: fall through to directory walk
                }
            }

            // Walk ancestor directories looking for Directory.Packages.props
            var projectDir = Path.GetDirectoryName(csprojPath);
            if (string.IsNullOrEmpty(projectDir))
            {
                return false;
            }

            var currentDir = new DirectoryInfo(projectDir);
            while (currentDir is not null)
            {
                var propsFile = Path.Combine(currentDir.FullName, "Directory.Packages.props");
                if (File.Exists(propsFile))
                {
                    return true;
                }

                currentDir = currentDir.Parent;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
