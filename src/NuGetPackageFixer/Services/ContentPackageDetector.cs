namespace NuGetPackageFixer.Services;

/// <summary>
/// Detects whether a NuGet package is a "content package" that ships JS, CSS, fonts,
/// or other content files that are copied into the project via install.ps1 scripts.
/// These packages need special handling during updates (Phase 3).
/// </summary>
public static class ContentPackageDetector
{
    /// <summary>
    /// Known content packages (fallback when packages/ folder is not available).
    /// </summary>
    private static readonly HashSet<string> KnownContentPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "jQuery",
        "jQuery.Validation",
        "jQuery.UI",
        "jQuery.UI.Combined",
        "Modernizr",
        "Respond",
        "WebGrease",
        "Bootstrap",
        "Font-Awesome",
        "FontAwesome",
        "Microsoft.jQuery.Unobtrusive.Ajax",
        "Microsoft.jQuery.Unobtrusive.Validation",
        "Antlr",
    };

    /// <summary>
    /// Checks if a package is a content package by examining:
    /// 1. The package folder in packages/ for content/ or contentFiles/ directories
    /// 2. The package folder for install.ps1 / uninstall.ps1 scripts
    /// 3. The known content packages list (fallback)
    /// </summary>
    public static bool IsContentPackage(string packageId, string packageVersion, string projectDirectory)
    {
        // Try to find the packages/ folder by walking up from the project directory
        var packagesFolder = FindPackagesFolder(projectDirectory);

        if (packagesFolder is not null)
        {
            var packageFolder = Path.Combine(packagesFolder, $"{packageId}.{packageVersion}");

            if (Directory.Exists(packageFolder))
            {
                // Check for content directories
                if (Directory.Exists(Path.Combine(packageFolder, "content")) ||
                    Directory.Exists(Path.Combine(packageFolder, "contentFiles")))
                {
                    return true;
                }

                // Check for install/uninstall scripts
                var toolsFolder = Path.Combine(packageFolder, "tools");
                if (Directory.Exists(toolsFolder) &&
                    (File.Exists(Path.Combine(toolsFolder, "install.ps1")) ||
                     File.Exists(Path.Combine(toolsFolder, "uninstall.ps1"))))
                {
                    return true;
                }
            }
        }

        // Fallback: check known content packages list
        return KnownContentPackages.Contains(packageId);
    }

    /// <summary>
    /// Walks up the directory tree to find the packages/ folder (NuGet packages.config convention).
    /// </summary>
    private static string? FindPackagesFolder(string startDirectory)
    {
        var dir = startDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var packagesPath = Path.Combine(dir, "packages");
            if (Directory.Exists(packagesPath))
            {
                return packagesPath;
            }

            var parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }

        return null;
    }
}
