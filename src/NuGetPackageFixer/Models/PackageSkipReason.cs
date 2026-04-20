namespace NuGetPackageFixer.Models;

/// <summary>
/// Structural reason a package is not auto-fixable, set at scan time.
/// Drives both <see cref="PackageIssue.IsFixableByBatch"/> (batch fix) and
/// the single-item fix gate in the tool window.
/// </summary>
public enum PackageSkipReason
{
    /// <summary>No structural skip — fixable in batch and single-item.</summary>
    None,

    /// <summary>Structurally fixable but excluded from batch; single-item fix still allowed.</summary>
    MajorUpdate,

    /// <summary>Project uses Central Package Management — edit Directory.Packages.props manually.</summary>
    Cpm,

    /// <summary>PackageReference or its parent ItemGroup has a Condition attribute.</summary>
    Conditional,

    /// <summary>Version contains a floating '*' token.</summary>
    Floating,

    /// <summary>VersionOverride attribute or child element is present.</summary>
    VersionOverride,
}
