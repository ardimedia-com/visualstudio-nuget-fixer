namespace NuGetPackageFixer.Services;

using System.Xml.Linq;

/// <summary>
/// Handles PackageReference version updates in SDK-style .csproj files.
/// Only supports plain Version attributes/elements. Skips CPM, conditional, floating, and VersionOverride cases.
/// </summary>
public static class PackageReferencePatcher
{
    /// <summary>
    /// Loads the .csproj, updates the version of a single PackageReference, and saves.
    /// For multi-package batch updates in the same project, prefer loading once via
    /// <see cref="XDocument.Load(string)"/> and calling <see cref="UpdateVersionInDoc"/>
    /// per package, then saving once — this avoids N load/save cycles.
    /// </summary>
    public static UpdateResult UpdateVersion(string csprojPath, string packageId, string oldVersion, string newVersion)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            var result = UpdateVersionInDoc(doc, packageId, oldVersion, newVersion);
            if (result.Status == UpdateStatus.Success)
            {
                doc.Save(csprojPath, SaveOptions.DisableFormatting);
            }
            return result;
        }
        catch (Exception ex)
        {
            return new UpdateResult(UpdateStatus.NotFound, $"Error updating .csproj: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the version of a single PackageReference in a pre-loaded <see cref="XDocument"/>.
    /// Caller is responsible for saving. Does not throw — returns a descriptive status.
    /// </summary>
    public static UpdateResult UpdateVersionInDoc(XDocument doc, string packageId, string oldVersion, string newVersion)
    {
        var root = doc.Root;
        if (root is null)
        {
            return new UpdateResult(UpdateStatus.NotFound, "Invalid .csproj: no root element");
        }

        var ns = root.GetDefaultNamespace();

        var packageRefs = root.Descendants(ns + "PackageReference")
            .Where(e => string.Equals(e.Attribute("Include")?.Value, packageId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (packageRefs.Count == 0)
        {
            return new UpdateResult(UpdateStatus.NotFound, $"PackageReference for {packageId} not found in .csproj");
        }

        if (packageRefs.Count > 1)
        {
            return new UpdateResult(UpdateStatus.SkippedAmbiguous, $"Multiple PackageReference elements for {packageId} found; ambiguous");
        }

        var element = packageRefs[0];

        if (element.Attribute("Condition") is not null || element.Parent?.Attribute("Condition") is not null)
        {
            return new UpdateResult(UpdateStatus.SkippedConditional, "PackageReference or parent ItemGroup has Condition attribute");
        }

        var versionAttr = element.Attribute("Version");
        var versionElement = element.Element(ns + "Version");
        var currentVersion = versionAttr?.Value ?? versionElement?.Value;

        if (string.IsNullOrEmpty(currentVersion))
        {
            return new UpdateResult(UpdateStatus.SkippedCpm, "No Version found (likely CPM or implicit version)");
        }

        if (currentVersion.Contains('*'))
        {
            return new UpdateResult(UpdateStatus.SkippedFloating, $"Floating version: {currentVersion}");
        }

        if (element.Attribute("VersionOverride") is not null
            || element.Element(ns + "VersionOverride") is not null)
        {
            return new UpdateResult(UpdateStatus.SkippedVersionOverride, "VersionOverride attribute or element present");
        }

        if (!string.Equals(currentVersion, oldVersion, StringComparison.Ordinal))
        {
            return new UpdateResult(UpdateStatus.NotFound, $"Version mismatch: expected {oldVersion}, found {currentVersion}");
        }

        if (versionAttr is not null)
        {
            versionAttr.Value = newVersion;
        }
        else
        {
            versionElement!.Value = newVersion;
        }

        return new UpdateResult(UpdateStatus.Success, $"Updated {packageId} from {oldVersion} to {newVersion}");
    }
}

/// <summary>
/// Result of a PackageReference update operation.
/// </summary>
public record UpdateResult(UpdateStatus Status, string Message);

/// <summary>
/// Status of a PackageReference update operation.
/// </summary>
public enum UpdateStatus
{
    Success,
    NotFound,
    SkippedCpm,
    SkippedConditional,
    SkippedFloating,
    SkippedVersionOverride,
    SkippedAmbiguous,
}
