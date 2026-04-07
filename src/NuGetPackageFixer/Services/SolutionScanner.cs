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
                if (IsSdkStyleWithModernTargetsOnly(path))
                {
                    // SDK-style project with only .NET 5+ targets — packages.config is obsolete
                    result.ObsoletePackagesConfigProjects.Add(new ProjectPackageInfo
                    {
                        ProjectName = name,
                        ProjectDirectory = projectDir,
                        ProjectFilePath = path,
                        PackagesConfigPath = packagesConfigPath,
                        Format = "packages.config (obsolete)",
                    });

                    // Also check for PackageReferences (the project likely already migrated)
                    if (HasPackageReferences(path))
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
                else
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

    /// <summary>
    /// Checks if a .csproj is SDK-style and targets only modern .NET (no .NET Framework).
    /// Returns true when packages.config is obsolete and can be safely deleted.
    /// </summary>
    private static bool IsSdkStyleWithModernTargetsOnly(string csprojPath)
    {
        try
        {
            var content = File.ReadAllText(csprojPath);

            // Must be SDK-style (has Sdk attribute on <Project>)
            if (!content.Contains("Sdk=", StringComparison.OrdinalIgnoreCase))
                return false;

            // Extract target frameworks
            var tfms = new List<string>();

            // <TargetFramework>net10.0</TargetFramework>
            var tfMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"<TargetFramework>(.*?)</TargetFramework>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (tfMatch.Success)
                tfms.Add(tfMatch.Groups[1].Value.Trim());

            // <TargetFrameworks>net10.0;net48</TargetFrameworks>
            var tfsMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"<TargetFrameworks>(.*?)</TargetFrameworks>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (tfsMatch.Success)
                tfms.AddRange(tfsMatch.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries));

            if (tfms.Count == 0)
                return false;

            // All targets must be modern .NET (not .NET Framework)
            return tfms.All(tfm => !IsNetFrameworkTfm(tfm.Trim()));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines if a TFM is .NET Framework (net45, net48, net472, etc.)
    /// vs modern .NET (net5.0, net8.0, net10.0, netcoreapp3.1, netstandard2.0).
    /// </summary>
    private static bool IsNetFrameworkTfm(string tfm)
    {
        // netcoreapp*, netstandard* = modern
        if (tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase) ||
            tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            return false;

        // net5.0+ = modern (contains a dot after "net" + digits)
        if (tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) && tfm.Contains('.'))
            return false;

        // net20, net35, net40, net45, net451, net452, net46, net461, net462, net47, net471, net472, net48, net481 = Framework
        if (tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) && tfm.Length >= 4)
        {
            var version = tfm[3..];
            if (version.All(char.IsDigit))
                return true;
        }

        // Unknown — assume not Framework
        return false;
    }
}

/// <summary>
/// Result of scanning all projects in a solution.
/// </summary>
public class ScanResult
{
    public List<ProjectPackageInfo> PackagesConfigProjects { get; } = [];
    public List<ProjectPackageInfo> PackageReferenceProjects { get; } = [];
    public List<ProjectPackageInfo> ObsoletePackagesConfigProjects { get; } = [];

    public int TotalProjects => this.PackagesConfigProjects.Count + this.PackageReferenceProjects.Count + this.ObsoletePackagesConfigProjects.Count;
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
