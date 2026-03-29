namespace NuGetPackageFixer.Services;

using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>
/// Handles all file modifications for package updates:
/// - packages.config version attribute updates
/// - .csproj path token replacement (HintPath, Import, Error guards)
/// - .csproj assembly version updates from restored DLLs
/// - Backup creation
/// </summary>
public static partial class PackagesConfigPatcher
{
    /// <summary>
    /// Updates the version attribute for a specific package in packages.config.
    /// Returns true if the package was found and updated.
    /// </summary>
    public static bool UpdateVersion(string packagesConfigPath, string packageId, string oldVersion, string newVersion)
    {
        if (!File.Exists(packagesConfigPath))
        {
            return false;
        }

        var doc = XDocument.Load(packagesConfigPath);
        var package = doc.Root?.Elements("package")
            .FirstOrDefault(p =>
                string.Equals(p.Attribute("id")?.Value, packageId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Attribute("version")?.Value, oldVersion, StringComparison.Ordinal));

        if (package is null)
        {
            return false;
        }

        package.SetAttributeValue("version", newVersion);
        doc.Save(packagesConfigPath);
        return true;
    }

    /// <summary>
    /// Replaces all occurrences of {PackageId}.{OldVersion} with {PackageId}.{NewVersion}
    /// in the .csproj file. This covers HintPaths, Import paths, and Error condition paths.
    /// Returns true if any replacements were made.
    /// </summary>
    public static bool UpdateCsprojPathTokens(string csprojPath, string packageId, string oldVersion, string newVersion)
    {
        if (!File.Exists(csprojPath))
        {
            return false;
        }

        var content = File.ReadAllText(csprojPath);
        var oldToken = $"{packageId}.{oldVersion}";
        var newToken = $"{packageId}.{newVersion}";

        if (!content.Contains(oldToken, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Case-insensitive replacement preserving original casing of the package ID
        var updated = content.Replace(oldToken, newToken, StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(csprojPath, updated);
        return true;
    }

    /// <summary>
    /// Reads the actual assembly version from restored DLLs and updates the Version= attribute
    /// in Reference Include elements of the .csproj file.
    ///
    /// Example: Updates Version=4.14.0.0 to Version=4.15.0.0 when the DLL at the HintPath
    /// has assembly version 4.15.0.0.
    ///
    /// Returns the number of references updated.
    /// </summary>
    public static int UpdateAssemblyVersionsFromDlls(
        string csprojPath, string projectDir, string packageId, string packageVersion)
    {
        if (!File.Exists(csprojPath))
        {
            return 0;
        }

        var doc = XDocument.Load(csprojPath);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
        int updatedCount = 0;

        // Find all <Reference> elements that have a HintPath pointing to this package
        var references = doc.Descendants(ns + "Reference")
            .Where(r =>
            {
                var hintPath = r.Element(ns + "HintPath")?.Value;
                return hintPath is not null &&
                       hintPath.Contains($"{packageId}.{packageVersion}", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        foreach (var reference in references)
        {
            var includeAttr = reference.Attribute("Include")?.Value;
            var hintPath = reference.Element(ns + "HintPath")?.Value;

            if (includeAttr is null || hintPath is null)
            {
                continue;
            }

            // Resolve the DLL path relative to the project directory
            var dllPath = Path.GetFullPath(Path.Combine(projectDir, hintPath));

            if (!File.Exists(dllPath))
            {
                continue;
            }

            // Read the assembly version from the DLL
            string? assemblyVersion = ReadAssemblyVersion(dllPath);
            if (assemblyVersion is null)
            {
                continue;
            }

            // Update the Version= part in the Include attribute
            // e.g., "MailKit, Version=4.14.0.0, Culture=neutral, PublicKeyToken=..."
            var updatedInclude = VersionInIncludeRegex().Replace(includeAttr, $"Version={assemblyVersion}");

            if (updatedInclude != includeAttr)
            {
                reference.SetAttributeValue("Include", updatedInclude);
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            doc.Save(csprojPath);
        }

        return updatedCount;
    }

    /// <summary>
    /// Creates a timestamped backup of a file.
    /// </summary>
    public static string? CreateBackup(string filePath, string timestamp)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var backupPath = $"{filePath}.{timestamp}.bak";
        if (File.Exists(backupPath))
        {
            return backupPath; // Already backed up today
        }

        File.Copy(filePath, backupPath);
        return backupPath;
    }

    /// <summary>
    /// Reads the assembly version from a DLL file without locking it.
    /// </summary>
    private static string? ReadAssemblyVersion(string dllPath)
    {
        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(dllPath);
            return assemblyName.Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"Version=[\d\.]+")]
    private static partial Regex VersionInIncludeRegex();
}
