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
        if (!File.Exists(csprojPath))
        {
            return [];
        }

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
            var packageRefs = root.Descendants(ns + "PackageReference")
                .Concat(root.Descendants("PackageReference")); // handle no-namespace case

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in packageRefs)
            {
                var id = element.Attribute("Include")?.Value ?? element.Attribute("include")?.Value;
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                // Avoid duplicates from namespace + no-namespace query
                if (!seen.Add(id))
                {
                    continue;
                }

                // Version can be attribute or child element
                var version = element.Attribute("Version")?.Value
                    ?? element.Attribute("version")?.Value
                    ?? element.Element(ns + "Version")?.Value
                    ?? element.Element("Version")?.Value
                    ?? string.Empty;

                // Skip packages without a version (e.g., CPM, or implicit version)
                if (string.IsNullOrEmpty(version))
                {
                    continue;
                }

                // Check for PrivateAssets="all" (development dependency)
                var privateAssets = element.Attribute("PrivateAssets")?.Value
                    ?? element.Element(ns + "PrivateAssets")?.Value
                    ?? element.Element("PrivateAssets")?.Value;

                results.Add(new PackageEntry
                {
                    Id = id,
                    Version = version,
                    TargetFramework = string.Empty, // Not applicable for PackageReference
                    IsDevelopmentDependency = string.Equals(privateAssets, "all", StringComparison.OrdinalIgnoreCase),
                });
            }

            return results;
        }
        catch
        {
            return [];
        }
    }
}
