namespace NuGetPackageFixer;

using Microsoft.VisualStudio.Extensibility;

/// <summary>
/// Entry point for the NuGet Package Fixer extension.
/// </summary>
[VisualStudioContribution]
public class NuGetPackageFixerExtension : Extension
{
    /// <inheritdoc />
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "NuGetPackageFixer.B2C3D4E5-F6A7-8901-BCDE-F23456789012",
            version: ExtensionAssemblyVersion,
            publisherName: "Ardimedia",
            displayName: "NuGet Package Fixer",
            description: "Scans packages.config and PackageReference projects for outdated, vulnerable, and deprecated NuGet packages.")
        {
            Icon = ImageMoniker.Custom("Images/NuGetPackageFixer.128.128.png"),
            DotnetTargetVersions = [".net10.0"],
        },
    };
}
