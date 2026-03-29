namespace NuGetPackageFixer.Models;

using System.Runtime.Serialization;

/// <summary>
/// Represents a single NuGet package issue found during scanning.
/// DataContract-decorated for Remote UI proxy binding.
/// </summary>
[DataContract]
public class PackageIssue
{
    [DataMember] public string ProjectName { get; set; } = string.Empty;
    [DataMember] public string ProjectPath { get; set; } = string.Empty;
    [DataMember] public string PackageId { get; set; } = string.Empty;
    [DataMember] public string CurrentVersion { get; set; } = string.Empty;
    [DataMember] public string SuggestedVersion { get; set; } = string.Empty;
    [DataMember] public string Source { get; set; } = string.Empty;
    [DataMember] public IssueCategory Category { get; set; }
    [DataMember] public IssueSeverity Severity { get; set; }
    [DataMember] public UpdateType UpdateType { get; set; }
    [DataMember] public bool IsPrerelease { get; set; }
    [DataMember] public bool IsContentPackage { get; set; }
    [DataMember] public string ProjectFormat { get; set; } = "packages.config";
    [DataMember] public string DiagnosticMessage { get; set; } = string.Empty;
    [DataMember] public bool IsFixableByBatch { get; set; }
    [DataMember] public string PackagesConfigEntry { get; set; } = string.Empty;
    [DataMember] public bool IsFixed { get; set; }

    /// <summary>Status icon for UI binding. Shows checkmark when fixed.</summary>
    [DataMember]
    public string StatusIcon => this.IsFixed ? "\u2713" : this.Severity switch
    {
        IssueSeverity.Critical => "!!",
        IssueSeverity.Warning => "!",
        _ => ""
    };

    /// <summary>Format label for UI binding.</summary>
    [DataMember]
    public string FormatLabel => this.ProjectFormat switch
    {
        "packages.config" => "packages.config",
        "PackageReference" => "PackageReference",
        _ => ""
    };

    /// <summary>Update type label for UI binding.</summary>
    [DataMember]
    public string TypeLabel => this.UpdateType switch
    {
        UpdateType.Patch => "patch",
        UpdateType.Major => "MAJOR",
        UpdateType.Prerelease => "pre",
        UpdateType.StablePromotion => "stable",
        UpdateType.Bogus => "bogus",
        _ => ""
    };

    /// <summary>Category label for UI binding. Shows "Fixed" when resolved.</summary>
    [DataMember]
    public string CategoryLabel => this.IsFixed ? "\u2713 Fixed" : this.Category switch
    {
        IssueCategory.Outdated => "Outdated",
        IssueCategory.Vulnerable => "Vulnerable",
        IssueCategory.Deprecated => "Deprecated",
        IssueCategory.Inconsistent => "Inconsistent",
        IssueCategory.Orphaned => "Orphaned",
        IssueCategory.Unused => "Unused",
        _ => this.Category.ToString()
    };

    /// <summary>Severity label for UI binding.</summary>
    [DataMember]
    public string SeverityLabel => this.Severity switch
    {
        IssueSeverity.Critical => "\u26D4 Critical",
        IssueSeverity.Warning => "\u26A0 Warning",
        IssueSeverity.Info => "\u2139 Info",
        _ => ""
    };

    /// <summary>Suggested version display. Shows "DONE" suffix when fixed.</summary>
    [DataMember]
    public string SuggestedDisplay => this.IsFixed
        ? $"{this.SuggestedVersion} \u2713 DONE"
        : this.SuggestedVersion;

    /// <summary>Severity display. Shows "Fixed" when resolved.</summary>
    [DataMember]
    public string SeverityDisplay => this.IsFixed ? "\u2713 Fixed" : this.SeverityLabel;

    /// <summary>Source display -- empty instead of "-" for non-feed issues.</summary>
    [DataMember]
    public string SourceDisplay => this.Source == "-" ? "" : this.Source;
}

public enum IssueCategory
{
    Outdated,
    Vulnerable,
    Deprecated,
    Inconsistent,
    Downgrade,
    Unused,
    MigrationReady,
    CpmCandidate,
    Orphaned
}

public enum IssueSeverity
{
    Critical,
    Warning,
    Info
}

public enum UpdateType
{
    UpToDate,
    Patch,
    Major,
    Bogus,
    Prerelease,
    StablePromotion
}
