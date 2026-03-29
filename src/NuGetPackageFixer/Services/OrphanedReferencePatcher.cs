namespace NuGetPackageFixer.Services;

using System.Text.RegularExpressions;

/// <summary>
/// Removes orphaned package references from .csproj files.
/// Handles HintPath references, Import elements, and Error guard elements.
/// </summary>
public static partial class OrphanedReferencePatcher
{
    /// <summary>
    /// Removes all references to a specific package version from a .csproj file.
    /// This includes:
    /// - Reference elements with HintPath pointing to packages\{id}.{version}\
    /// - Import elements pointing to packages\{id}.{version}\
    /// - Error elements with conditions referencing packages\{id}.{version}\
    /// Returns the number of elements removed.
    /// </summary>
    public static int RemoveOrphanedReferences(string csprojPath, string packageId, string version)
    {
        if (!File.Exists(csprojPath))
        {
            return 0;
        }

        var content = File.ReadAllText(csprojPath);
        var packageToken = $"{packageId}.{version}";
        int removedCount = 0;

        // Remove <Reference> elements that contain a HintPath to this package.
        // Use [^<]* instead of [\s\S]*? to prevent matching across multiple Reference elements.
        var referencePattern = $@"\s*<Reference[^>]*>[^<]*<HintPath>[^<]*packages\\{Regex.Escape(packageToken)}\\[^<]*</HintPath>[^<]*</Reference>";
        var (newContent, count1) = RemovePattern(content, referencePattern);
        content = newContent;
        removedCount += count1;

        // Remove single-line <Reference .../> elements (self-closing)
        var refSelfClosingPattern = $@"<Reference[^>]*HintPath[^>]*packages\\{Regex.Escape(packageToken)}\\[^>]*/>\s*";
        var (newContent2, count2) = RemovePattern(content, refSelfClosingPattern);
        content = newContent2;
        removedCount += count2;

        // Remove <Import> elements referencing this package
        var importPattern = $@"<Import[^>]*packages\\{Regex.Escape(packageToken)}\\[^>]*/>\s*";
        var (newContent3, count3) = RemovePattern(content, importPattern);
        content = newContent3;
        removedCount += count3;

        // Remove <Error> elements referencing this package
        var errorPattern = $@"<Error[^>]*packages\\{Regex.Escape(packageToken)}\\[^>]*/>\s*";
        var (newContent4, count4) = RemovePattern(content, errorPattern);
        content = newContent4;
        removedCount += count4;

        if (removedCount > 0)
        {
            File.WriteAllText(csprojPath, content);
        }

        return removedCount;
    }

    private static (string NewContent, int Count) RemovePattern(string content, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        var matches = regex.Matches(content);
        var newContent = regex.Replace(content, "");
        return (newContent, matches.Count);
    }
}
