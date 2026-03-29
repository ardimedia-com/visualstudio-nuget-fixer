namespace NuGetPackageFixer.Models;

using System.Runtime.Serialization;

using Microsoft.VisualStudio.Extensibility.UI;

/// <summary>
/// UI-bound ViewModel wrapper for <see cref="PackageIssue"/>.
/// Extends <see cref="NotifyPropertyChangedObject"/> so Remote UI picks up
/// property changes (e.g. when marking an issue as fixed) without needing
/// to clone and replace the object.
/// </summary>
[DataContract]
public class PackageIssueViewModel : NotifyPropertyChangedObject
{
    private bool _isFixed;
    private bool _isFixableByBatch;
    private string _diagnosticMessage;
    private string _suggestedVersion;

    public PackageIssueViewModel(PackageIssue model)
    {
        this.ProjectName = model.ProjectName;
        this.ProjectPath = model.ProjectPath;
        this.PackageId = model.PackageId;
        this.CurrentVersion = model.CurrentVersion;
        this.Source = model.Source;
        this.Category = model.Category;
        this.Severity = model.Severity;
        this.UpdateType = model.UpdateType;
        this.IsPrerelease = model.IsPrerelease;
        this.IsContentPackage = model.IsContentPackage;
        this.ProjectFormat = model.ProjectFormat;
        this.PackagesConfigEntry = model.PackagesConfigEntry;

        _suggestedVersion = model.SuggestedVersion;
        _diagnosticMessage = model.DiagnosticMessage;
        _isFixableByBatch = model.IsFixableByBatch;
        _isFixed = model.IsFixed;
    }

    // Immutable properties (set once from model)
    [DataMember] public string ProjectName { get; }
    [DataMember] public string ProjectPath { get; }
    [DataMember] public string PackageId { get; }
    [DataMember] public string CurrentVersion { get; }
    [DataMember] public string Source { get; }
    [DataMember] public IssueCategory Category { get; }
    [DataMember] public IssueSeverity Severity { get; }
    [DataMember] public UpdateType UpdateType { get; }
    [DataMember] public bool IsPrerelease { get; }
    [DataMember] public bool IsContentPackage { get; }
    [DataMember] public string ProjectFormat { get; }
    [DataMember] public string PackagesConfigEntry { get; }

    // Mutable properties with change notification
    [DataMember]
    public string SuggestedVersion
    {
        get => _suggestedVersion;
        set => this.SetProperty(ref _suggestedVersion, value);
    }

    [DataMember]
    public string DiagnosticMessage
    {
        get => _diagnosticMessage;
        set => this.SetProperty(ref _diagnosticMessage, value);
    }

    [DataMember]
    public bool IsFixableByBatch
    {
        get => _isFixableByBatch;
        set => this.SetProperty(ref _isFixableByBatch, value);
    }

    [DataMember]
    public bool IsFixed
    {
        get => _isFixed;
        set
        {
            if (this.SetProperty(ref _isFixed, value))
            {
                this.RaiseNotifyPropertyChangedEvent(nameof(this.StatusIcon));
                this.RaiseNotifyPropertyChangedEvent(nameof(this.CategoryLabel));
                this.RaiseNotifyPropertyChangedEvent(nameof(this.SuggestedDisplay));
                this.RaiseNotifyPropertyChangedEvent(nameof(this.SeverityDisplay));
            }
        }
    }

    /// <summary>
    /// Marks this issue as fixed and notifies the UI of all affected properties.
    /// </summary>
    public void MarkAsFixed(string diagnosticMessage)
    {
        this.IsFixed = true;
        this.IsFixableByBatch = false;
        this.DiagnosticMessage = diagnosticMessage;
    }

    // Computed display properties
    [DataMember]
    public string StatusIcon => _isFixed ? "\u2713" : this.Severity switch
    {
        IssueSeverity.Critical => "!!",
        IssueSeverity.Warning => "!",
        _ => ""
    };

    [DataMember]
    public string FormatLabel => this.ProjectFormat switch
    {
        "packages.config" => "packages.config",
        "PackageReference" => "PackageReference",
        _ => ""
    };

    [DataMember]
    public string TypeLabel => this.UpdateType switch
    {
        Models.UpdateType.Patch => "patch",
        Models.UpdateType.Major => "MAJOR",
        Models.UpdateType.Prerelease => "pre",
        Models.UpdateType.StablePromotion => "stable",
        Models.UpdateType.Bogus => "bogus",
        _ => ""
    };

    [DataMember]
    public string CategoryLabel => _isFixed ? "\u2713 Fixed" : this.Category switch
    {
        IssueCategory.Outdated => "Outdated",
        IssueCategory.Vulnerable => "Vulnerable",
        IssueCategory.Deprecated => "Deprecated",
        IssueCategory.Inconsistent => "Inconsistent",
        IssueCategory.Orphaned => "Orphaned",
        IssueCategory.Unused => "Unused",
        _ => this.Category.ToString()
    };

    [DataMember]
    public string SeverityLabel => this.Severity switch
    {
        IssueSeverity.Critical => "\u26D4 Critical",
        IssueSeverity.Warning => "\u26A0 Warning",
        IssueSeverity.Info => "\u2139 Info",
        _ => ""
    };

    [DataMember]
    public string SuggestedDisplay => _isFixed
        ? $"{_suggestedVersion} \u2713 DONE"
        : _suggestedVersion;

    [DataMember]
    public string SeverityDisplay => _isFixed ? "\u2713 Fixed" : this.SeverityLabel;

    [DataMember]
    public string SourceDisplay => this.Source == "-" ? "" : this.Source;
}
