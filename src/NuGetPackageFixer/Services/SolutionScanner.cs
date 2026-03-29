namespace NuGetPackageFixer.Services;

/// <summary>
/// Finds NuGet package files across all projects in a solution.
/// Detects both packages.config and PackageReference formats.
/// </summary>
public static class SolutionScanner
{
    /// <summary>
    /// Scans all projects and returns info about their package management format.
    /// A project can be packages.config, PackageReference, or neither (no NuGet packages).
    /// </summary>
    public static ScanResult ScanProjects(IEnumerable<(string Name, string Path)> projects)
    {
        var result = new ScanResult();

        foreach (var (name, path) in projects)
        {
            var projectDir = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(projectDir))
            {
                continue;
            }

            var packagesConfigPath = System.IO.Path.Combine(projectDir, "packages.config");

            if (File.Exists(packagesConfigPath))
            {
                result.PackagesConfigProjects.Add(new ProjectPackageInfo
                {
                    ProjectName = name,
                    ProjectDirectory = projectDir,
                    ProjectFilePath = path,
                    PackagesConfigPath = packagesConfigPath,
                    Format = "packages.config",
                });
            }
            else if (HasPackageReferences(path))
            {
                result.PackageReferenceProjects.Add(new ProjectPackageInfo
                {
                    ProjectName = name,
                    ProjectDirectory = projectDir,
                    ProjectFilePath = path,
                    PackagesConfigPath = string.Empty,
                    Format = "PackageReference",
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Backwards-compatible method for existing callers.
    /// </summary>
    public static List<ProjectPackageInfo> FindPackagesConfigFiles(
        IEnumerable<(string Name, string Path)> projects)
    {
        return ScanProjects(projects).PackagesConfigProjects;
    }

    /// <summary>
    /// Checks if a .csproj file contains PackageReference elements.
    /// </summary>
    private static bool HasPackageReferences(string csprojPath)
    {
        if (!File.Exists(csprojPath) || !csprojPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var content = File.ReadAllText(csprojPath);
            return content.Contains("<PackageReference", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Result of scanning all projects in a solution.
/// </summary>
public class ScanResult
{
    public List<ProjectPackageInfo> PackagesConfigProjects { get; } = [];
    public List<ProjectPackageInfo> PackageReferenceProjects { get; } = [];

    public int TotalProjects => this.PackagesConfigProjects.Count + this.PackageReferenceProjects.Count;
    public bool HasAnyPackages => this.TotalProjects > 0;
}

/// <summary>
/// Information about a project's package management format and file locations.
/// </summary>
public class ProjectPackageInfo
{
    public string ProjectName { get; init; } = string.Empty;
    public string ProjectDirectory { get; init; } = string.Empty;
    public string ProjectFilePath { get; init; } = string.Empty;
    public string PackagesConfigPath { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
}
