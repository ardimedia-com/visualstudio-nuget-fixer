namespace NuGetPackageFixer.Services;

using System.Xml.Linq;

/// <summary>
/// Assesses whether a packages.config project can be migrated to PackageReference format.
/// Reports blockers and warnings without making any changes.
/// </summary>
public static class MigrationReadinessAssessor
{
    /// <summary>
    /// Analyzes a project for PackageReference migration readiness.
    /// Returns a list of blockers and warnings.
    /// </summary>
    public static MigrationAssessment Assess(string csprojPath, string packagesConfigPath, string projectDirectory)
    {
        var result = new MigrationAssessment();

        if (!File.Exists(csprojPath))
        {
            return result;
        }

        var csprojContent = File.ReadAllText(csprojPath);

        // Check 1: Is this a non-SDK style project?
        if (!csprojContent.Contains("<Project Sdk=", StringComparison.OrdinalIgnoreCase))
        {
            result.Blockers.Add(new MigrationBlocker
            {
                Type = BlockerType.NonSdkProject,
                Severity = BlockerSeverity.Hard,
                Description = "Non-SDK style .csproj (old format with ToolsVersion/ProjectTypeGuids). "
                    + "Must be converted to SDK-style first, which is not supported for all project types "
                    + "(e.g., ASP.NET MVC Web Apps).",
            });
        }

        // Check 2: Content packages
        if (File.Exists(packagesConfigPath))
        {
            var packages = PackagesConfigParser.Parse(packagesConfigPath);
            var contentPackages = packages
                .Where(p => ContentPackageDetector.IsContentPackage(p.Id, p.Version, projectDirectory))
                .ToList();

            if (contentPackages.Count > 0)
            {
                result.Blockers.Add(new MigrationBlocker
                {
                    Type = BlockerType.ContentPackages,
                    Severity = BlockerSeverity.Soft,
                    Description = $"{contentPackages.Count} content package(s) use install.ps1/content folders "
                        + "which don't work with PackageReference: "
                        + string.Join(", ", contentPackages.Select(p => p.Id)),
                });
            }
        }

        // Check 3: Build imports from packages/ folder
        if (csprojContent.Contains(@"packages\", StringComparison.OrdinalIgnoreCase) &&
            csprojContent.Contains("<Import Project=", StringComparison.OrdinalIgnoreCase))
        {
            // Count how many imports reference the packages folder
            var importCount = csprojContent.Split("<Import Project=")
                .Skip(1)
                .Count(s => s.Contains(@"packages\", StringComparison.OrdinalIgnoreCase));

            if (importCount > 0)
            {
                result.Blockers.Add(new MigrationBlocker
                {
                    Type = BlockerType.BuildImports,
                    Severity = BlockerSeverity.Soft,
                    Description = $"{importCount} build import(s) reference packages/ folder (.props/.targets). "
                        + "These paths change with PackageReference (global cache instead of local packages/).",
                });
            }
        }

        // Check 4: Web project type GUIDs (ASP.NET MVC/WebForms)
        if (csprojContent.Contains("{349c5851-65df-11da-9384-00065b846f21}", StringComparison.OrdinalIgnoreCase))
        {
            result.Blockers.Add(new MigrationBlocker
            {
                Type = BlockerType.WebProjectType,
                Severity = BlockerSeverity.Hard,
                Description = "ASP.NET Web Application project type. "
                    + "Migration to SDK-style/PackageReference is not officially supported.",
            });
        }

        // Check 5: EnsureNuGetPackageBuildImports target
        if (csprojContent.Contains("EnsureNuGetPackageBuildImports", StringComparison.OrdinalIgnoreCase))
        {
            result.Blockers.Add(new MigrationBlocker
            {
                Type = BlockerType.BuildImportGuards,
                Severity = BlockerSeverity.Soft,
                Description = "EnsureNuGetPackageBuildImports target with Error guards. "
                    + "These are not needed with PackageReference and should be removed during migration.",
            });
        }

        // Overall readiness
        result.IsReady = result.Blockers.All(b => b.Severity != BlockerSeverity.Hard);

        return result;
    }
}

public class MigrationAssessment
{
    public bool IsReady { get; set; } = true;
    public List<MigrationBlocker> Blockers { get; set; } = [];

    public string ReadinessLabel => this.IsReady
        ? "Ready (with warnings)"
        : this.Blockers.Any(b => b.Severity == BlockerSeverity.Hard)
            ? "Blocked"
            : "Ready";

    public string Summary => this.Blockers.Count == 0
        ? "No migration blockers found."
        : $"{this.Blockers.Count(b => b.Severity == BlockerSeverity.Hard)} hard blocker(s), "
          + $"{this.Blockers.Count(b => b.Severity == BlockerSeverity.Soft)} warning(s)";
}

public class MigrationBlocker
{
    public BlockerType Type { get; init; }
    public BlockerSeverity Severity { get; init; }
    public string Description { get; init; } = "";
}

public enum BlockerType
{
    NonSdkProject,
    ContentPackages,
    BuildImports,
    WebProjectType,
    BuildImportGuards,
}

public enum BlockerSeverity
{
    Hard,
    Soft,
}
