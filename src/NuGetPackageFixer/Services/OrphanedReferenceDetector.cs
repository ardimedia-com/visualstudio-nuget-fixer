namespace NuGetPackageFixer.Services;

using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// Detects .csproj references (HintPath, Import, Error guards) that reference packages
/// not present in packages.config. These are orphaned references from packages that were
/// removed from packages.config but whose .csproj entries were not cleaned up.
/// </summary>
public static partial class OrphanedReferenceDetector
{
    /// <summary>
    /// Finds all package references in the .csproj that have no matching entry in packages.config.
    /// Returns a list of orphaned package IDs with their version and reference types.
    /// </summary>
    public static List<OrphanedReference> Detect(string csprojPath, string packagesConfigPath)
    {
        if (!File.Exists(csprojPath) || !File.Exists(packagesConfigPath))
        {
            return [];
        }

        // Read all package IDs from packages.config
        var configPackages = PackagesConfigParser.Parse(packagesConfigPath)
            .Select(p => p.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Scan .csproj for all packages\{PackageId}.{Version} path tokens
        var csprojContent = File.ReadAllText(csprojPath);
        var csprojReferences = PackagePathRegex().Matches(csprojContent);

        var orphaned = new Dictionary<string, OrphanedReference>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in csprojReferences)
        {
            var packageId = match.Groups["id"].Value;
            var version = match.Groups["version"].Value;

            if (configPackages.Contains(packageId))
            {
                continue;
            }

            if (!orphaned.TryGetValue(packageId, out var existing))
            {
                existing = new OrphanedReference
                {
                    PackageId = packageId,
                    VersionInCsproj = version,
                };
                orphaned[packageId] = existing;
            }

            // Classify the reference type and capture the affected line
            var line = GetLineContaining(csprojContent, match.Index).Trim();
            existing.AffectedLines.Add(line);

            if (line.Contains("HintPath", StringComparison.OrdinalIgnoreCase))
            {
                existing.HasHintPath = true;
            }
            else if (line.Contains("<Import", StringComparison.OrdinalIgnoreCase))
            {
                existing.HasImport = true;
            }
            else if (line.Contains("<Error", StringComparison.OrdinalIgnoreCase))
            {
                existing.HasErrorGuard = true;
            }
        }

        return [.. orphaned.Values];
    }

    private static string GetLineContaining(string content, int index)
    {
        var lineStart = content.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var lineEnd = content.IndexOf('\n', index);
        if (lineEnd < 0) lineEnd = content.Length;
        return content[lineStart..lineEnd];
    }

    /// <summary>
    /// Matches packages\{PackageId}.{Version}\ patterns in .csproj content.
    /// Captures the package ID and version separately.
    /// </summary>
    [GeneratedRegex(@"packages\\(?<id>[A-Za-z][\w\.\-]+?)\.(?<version>\d+[\d\.\-\w]*?)\\", RegexOptions.IgnoreCase)]
    private static partial Regex PackagePathRegex();
}

/// <summary>
/// Represents a package referenced in .csproj but not in packages.config.
/// </summary>
public class OrphanedReference
{
    public string PackageId { get; init; } = string.Empty;
    public string VersionInCsproj { get; init; } = string.Empty;
    public bool HasHintPath { get; set; }
    public bool HasImport { get; set; }
    public bool HasErrorGuard { get; set; }

    public List<string> AffectedLines { get; set; } = [];

    public string ReferenceTypes
    {
        get
        {
            var types = new List<string>();
            if (this.HasHintPath) types.Add("HintPath");
            if (this.HasImport) types.Add("Import");
            if (this.HasErrorGuard) types.Add("Error guard");
            return string.Join(", ", types);
        }
    }

    public string AffectedSnippet => string.Join("\n", this.AffectedLines);
}
