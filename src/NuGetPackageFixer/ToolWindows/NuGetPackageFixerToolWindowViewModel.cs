namespace NuGetPackageFixer.ToolWindows;

using System.Runtime.Serialization;

using Ardimedia.VsExtensions.Common.Services;
using Ardimedia.VsExtensions.Common.ViewModels;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.UI;
using Microsoft.VisualStudio.ProjectSystem.Query;

using NuGetPackageFixer.Models;
using NuGetPackageFixer.Services;

/// <summary>
/// ViewModel for the NuGet Package Fixer tool window.
/// Inherits solution monitoring, cancel/analyse, and scanning state from base class.
/// </summary>
[DataContract]
public class NuGetPackageFixerToolWindowViewModel : ToolWindowViewModelBase
{
    private readonly NuGetSourceProvider _sourceProvider = new();
    private readonly PackageMetadataService _metadataService = new();
    private readonly OutputChannelLogger _logger;
    private readonly List<PackageIssueViewModel> _allResults = [];
    private readonly Dictionary<string, string> _projectDirectories = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _updateCts;

    private string _statusText = "Ready. Click Analyse to scan.";
    private string _selectedProject = "All Projects";
    private string _selectedCategory = "All";
    private string _packageFilter = string.Empty;
    private string _sortColumn = "Severity";
    private bool _sortAscending = true;
    private PackageIssueViewModel? _selectedIssue;
    private bool _createBackup = true;
    private string _configSourcesText = string.Empty;
    private string _configFilesText = string.Empty;
    private bool _hasSolution;
    private bool _hasCompletedScan;

    // Tab state
    private bool _isIssuesTabSelected = true;
    private bool _isConfigTabSelected;
    private bool _isBackgroundTabSelected;
    private bool _isFeedbackTabSelected;

    public NuGetPackageFixerToolWindowViewModel(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
        _logger = new OutputChannelLogger(extensibility, "NuGet Package Fixer");
        this.UpdateSelectedCommand = new AsyncCommand(this.ExecuteUpdateSelectedAsync);
        this.UpdateShownCommand = new AsyncCommand(this.ExecuteUpdateShownAsync);
        this.SwitchToIssuesTabCommand = new AsyncCommand((_, _) => { this.SelectTab(issues: true); return Task.CompletedTask; });
        this.SwitchToConfigTabCommand = new AsyncCommand((_, _) => { this.SelectTab(config: true); return Task.CompletedTask; });
        this.SwitchToBackgroundTabCommand = new AsyncCommand((_, _) => { this.SelectTab(background: true); return Task.CompletedTask; });
        this.SwitchToFeedbackTabCommand = new AsyncCommand((_, _) => { this.SelectTab(feedback: true); return Task.CompletedTask; });
        this.ClearFilterCommand = new AsyncCommand((_, _) => { this.PackageFilter = string.Empty; return Task.CompletedTask; });
        this.SortCommand = new AsyncCommand(this.ExecuteSortAsync);
    }

    #region Properties

    [DataMember]
    public ObservableList<PackageIssueViewModel> Issues { get; } = [];

    [DataMember]
    public ObservableList<string> Projects { get; } = [];

    [DataMember]
    public ObservableList<string> Categories { get; } = ["All", "Outdated", "Vulnerable", "Deprecated", "Orphaned", "Inconsistent", "MigrationReady"];

    [DataMember]
    public string StatusText
    {
        get => _statusText;
        set => this.SetProperty(ref _statusText, value);
    }

    [DataMember]
    public string SelectedProject
    {
        get => _selectedProject;
        set
        {
            string effective = string.IsNullOrEmpty(value) ? "All Projects" : value;
            if (this.SetProperty(ref _selectedProject, effective) && _allResults.Count > 0)
            {
                this.ApplyFilters();
            }
        }
    }

    [DataMember]
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            string effective = string.IsNullOrEmpty(value) ? "All" : value;
            if (this.SetProperty(ref _selectedCategory, effective) && _allResults.Count > 0)
            {
                this.ApplyFilters();
            }
        }
    }

    [DataMember]
    public string PackageFilter
    {
        get => _packageFilter;
        set
        {
            if (this.SetProperty(ref _packageFilter, value))
            {
                this.RaiseNotifyPropertyChangedEvent(nameof(this.FilterClearVisibility));
                if (_allResults.Count > 0)
                {
                    this.ApplyFilters();
                }
            }
        }
    }

    [DataMember]
    public string FilterClearVisibility => !string.IsNullOrEmpty(_packageFilter) ? "Visible" : "Collapsed";

    [DataMember]
    public PackageIssueViewModel? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            this.SetProperty(ref _selectedIssue, value);
            this.RaiseNotifyPropertyChangedEvent(nameof(this.DetailPanelVisibility));
            this.RaiseNotifyPropertyChangedEvent(nameof(this.PackagesConfigEntryVisibility));
            this.RaiseNotifyPropertyChangedEvent(nameof(this.UpdateSelectedButtonLabel));
            this.RaiseNotifyPropertyChangedEvent(nameof(this.UpdateSelectedEnabled));
        }
    }

    [DataMember]
    public string DetailPanelVisibility => _selectedIssue is not null ? "Visible" : "Collapsed";

    [DataMember]
    public string PackagesConfigEntryVisibility =>
        _selectedIssue is not null && !string.IsNullOrEmpty(_selectedIssue.PackagesConfigEntry)
            ? "Visible" : "Collapsed";

    /// <summary>
    /// Button label changes based on context:
    /// - "Downgrade to Stable X.Y.Z" for prerelease mismatch
    /// - "Update This Package" for normal updates
    /// </summary>
    [DataMember]
    public string UpdateSelectedButtonLabel
    {
        get
        {
            if (_selectedIssue is null) return "Update This Package";

            // Prerelease → stable downgrade
            if (_selectedIssue.IsPrerelease && !_selectedIssue.SuggestedVersion.Contains('-')
                && _selectedIssue.SuggestedVersion != "-")
            {
                return $"Downgrade to Stable {_selectedIssue.SuggestedVersion}";
            }

            // Inconsistent → consolidate
            if (_selectedIssue.Category == IssueCategory.Inconsistent
                && _selectedIssue.SuggestedVersion is not "-" and not "Remove")
            {
                return $"Consolidate to {_selectedIssue.SuggestedVersion}";
            }

            // Orphaned → remove from .csproj
            if (_selectedIssue.Category == IssueCategory.Orphaned)
            {
                return "Remove from .csproj";
            }

            // Not fixable
            if (_selectedIssue.SuggestedVersion == "-" || _selectedIssue.ProjectFormat != "packages.config")
            {
                return "No auto-fix available";
            }

            return $"Update to {_selectedIssue.SuggestedVersion}";
        }
    }

    /// <summary>Update button is only enabled for fixable issues.</summary>
    [DataMember]
    public bool UpdateSelectedEnabled =>
        _selectedIssue is not null
        && !_selectedIssue.IsFixed
        && _selectedIssue.ProjectFormat == "packages.config"
        && (_selectedIssue.SuggestedVersion is not "-"
            || _selectedIssue.Category == IssueCategory.Orphaned);

    /// <summary>Extension version from the assembly.</summary>
    [DataMember]
    public string ExtensionVersion => GetType().Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Button label for the Analyse button (base class handles visibility).</summary>
    [DataMember]
    public string AnalyseButtonLabel => _hasCompletedScan ? "Re-Analyse" : "Analyse";

    /// <summary>Analyse is enabled when a solution is loaded.</summary>
    [DataMember]
    public bool AnalyseEnabled => _hasSolution;

    /// <summary>"Update Shown" is only enabled when not scanning and issues exist.</summary>
    [DataMember]
    public bool UpdateShownEnabled => !this.IsScanning && _hasSolution && _allResults.Count > 0;

    [DataMember]
    public bool CreateBackup
    {
        get => _createBackup;
        set => this.SetProperty(ref _createBackup, value);
    }

    [DataMember]
    public string ConfigSourcesText
    {
        get => _configSourcesText;
        set => this.SetProperty(ref _configSourcesText, value);
    }

    [DataMember]
    public string ConfigFilesText
    {
        get => _configFilesText;
        set => this.SetProperty(ref _configFilesText, value);
    }

    // ---- Tab visibility / styling (Remote UI cannot use converters) ----

    [DataMember] public string IssuesTabVisibility => _isIssuesTabSelected ? "Visible" : "Collapsed";
    [DataMember] public string ConfigTabVisibility => _isConfigTabSelected ? "Visible" : "Collapsed";
    [DataMember] public string BackgroundTabVisibility => _isBackgroundTabSelected ? "Visible" : "Collapsed";
    [DataMember] public string FeedbackTabVisibility => _isFeedbackTabSelected ? "Visible" : "Collapsed";

    [DataMember] public string IssuesTabFontWeight => _isIssuesTabSelected ? "Bold" : "Normal";
    [DataMember] public string ConfigTabFontWeight => _isConfigTabSelected ? "Bold" : "Normal";
    [DataMember] public string BackgroundTabFontWeight => _isBackgroundTabSelected ? "Bold" : "Normal";
    [DataMember] public string FeedbackTabFontWeight => _isFeedbackTabSelected ? "Bold" : "Normal";

    [DataMember] public string IssuesTabUnderline => _isIssuesTabSelected ? "0,0,0,2" : "0";
    [DataMember] public string ConfigTabUnderline => _isConfigTabSelected ? "0,0,0,2" : "0";
    [DataMember] public string BackgroundTabUnderline => _isBackgroundTabSelected ? "0,0,0,2" : "0";
    [DataMember] public string FeedbackTabUnderline => _isFeedbackTabSelected ? "0,0,0,2" : "0";

    [DataMember] public string IssuesTabOpacity => _isIssuesTabSelected ? "1.0" : "0.6";
    [DataMember] public string ConfigTabOpacity => _isConfigTabSelected ? "1.0" : "0.6";
    [DataMember] public string BackgroundTabOpacity => _isBackgroundTabSelected ? "1.0" : "0.6";
    [DataMember] public string FeedbackTabOpacity => _isFeedbackTabSelected ? "1.0" : "0.6";

    #endregion

    #region Commands

    [DataMember]
    public IAsyncCommand UpdateSelectedCommand { get; }

    [DataMember]
    public IAsyncCommand UpdateShownCommand { get; }

    [DataMember] public IAsyncCommand SwitchToIssuesTabCommand { get; }
    [DataMember] public IAsyncCommand SwitchToConfigTabCommand { get; }
    [DataMember] public IAsyncCommand SwitchToBackgroundTabCommand { get; }
    [DataMember] public IAsyncCommand SwitchToFeedbackTabCommand { get; }
    [DataMember] public IAsyncCommand ClearFilterCommand { get; }
    [DataMember] public IAsyncCommand SortCommand { get; }

    [DataMember] public string SortIndicatorPackage => GetSortIndicator("Package");
    [DataMember] public string SortIndicatorCurrent => GetSortIndicator("Current");
    [DataMember] public string SortIndicatorSuggested => GetSortIndicator("Suggested");
    [DataMember] public string SortIndicatorCategory => GetSortIndicator("Category");
    [DataMember] public string SortIndicatorSeverity => GetSortIndicator("Severity");
    [DataMember] public string SortIndicatorSource => GetSortIndicator("Source");
    [DataMember] public string SortIndicatorProject => GetSortIndicator("Project");

    private string GetSortIndicator(string column)
        => _sortColumn == column ? (_sortAscending ? " \u25B2" : " \u25BC") : "";

    #endregion

    #region Base Class Overrides

    protected override async Task OnSolutionOpenedAsync(CancellationToken cancellationToken)
    {
        await this.ExecuteAnalyseAsync(null, cancellationToken);
    }

    protected override void OnSolutionClosed()
    {
        this.Issues.Clear();
        _allResults.Clear();
        _projectDirectories.Clear();
        this.Projects.Clear();
        _hasSolution = false;
        _hasCompletedScan = false;
        this.StatusText = "Waiting for a solution to be opened...";
        this.SelectedIssue = null;
        this.RaiseNotifyPropertyChangedEvent(nameof(this.AnalyseButtonLabel));
        this.RaiseNotifyPropertyChangedEvent(nameof(this.AnalyseEnabled));
        this.RaiseNotifyPropertyChangedEvent(nameof(this.UpdateShownEnabled));
    }

    protected override void OnIsScanningChanged()
    {
        this.RaiseNotifyPropertyChangedEvent(nameof(this.AnalyseButtonLabel));
        this.RaiseNotifyPropertyChangedEvent(nameof(this.AnalyseEnabled));
        this.RaiseNotifyPropertyChangedEvent(nameof(this.UpdateShownEnabled));
    }

    protected override void OnCancelRequested()
    {
        _updateCts?.Cancel();
    }

    public override void Dispose()
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _sourceProvider.Dispose();
        _metadataService.Dispose();
        _logger.Dispose();
        base.Dispose();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Main analysis: find packages.config files, parse them, query feeds for updates.
    /// </summary>
    private async Task ExecuteAnalyseAsync(object? parameter, CancellationToken cancellationToken)
    {
        this.StatusText = "Scanning...";

        this.Issues.Clear();
        _allResults.Clear();
        _projectDirectories.Clear();
        this.Projects.Clear();
        this.Projects.Add("All Projects");
        this.SelectedProject = "All Projects";
        this.SelectedCategory = "All";
        this.SelectedIssue = null;

        try
        {
            var projects = await this.Extensibility.Workspaces().QueryProjectsAsync(
                q => q.With(p => p.Name).With(p => p.Path),
                cancellationToken).ConfigureAwait(false);

            var projectList = projects
                .Select(p => (Name: p.Name ?? "Unknown", Path: p.Path ?? string.Empty))
                .Where(p => !string.IsNullOrEmpty(p.Path))
                .ToList();

            _logger.WriteLine("");
            _logger.WriteLine("---- New analysis started ----");
            _logger.WriteLine($"Found {projectList.Count} project(s) in solution.");

            if (projectList.Count == 0)
            {
                _hasSolution = false;
                this.RaiseNotifyPropertyChangedEvent(nameof(this.AnalyseEnabled));
                this.StatusText = "Waiting for a solution to be opened...";
                return;
            }

            _hasSolution = true;
            this.RaiseNotifyPropertyChangedEvent(nameof(this.AnalyseEnabled));

            // Scan for both packages.config and PackageReference projects
            var scanResult = SolutionScanner.ScanProjects(projectList);
            var packagesConfigProjects = scanResult.PackagesConfigProjects;
            var packageReferenceProjects = scanResult.PackageReferenceProjects;

            _logger.WriteLine($"Found {packagesConfigProjects.Count} packages.config project(s), {packageReferenceProjects.Count} PackageReference project(s).");

            var allProjects = packagesConfigProjects.Concat(packageReferenceProjects).ToList();

            if (!scanResult.HasAnyPackages)
            {
                this.StatusText = "No NuGet packages found in this solution.";
                _logger.WriteLine("No packages.config or PackageReference projects found.");
                return;
            }

            // Populate project filter and directory map
            foreach (var project in allProjects)
            {
                this.Projects.Add(project.ProjectName);
                _projectDirectories[project.ProjectName] = project.ProjectDirectory;
            }

            var solutionDir = Path.GetDirectoryName(allProjects[0].ProjectDirectory) ?? ".";
            var sources = _sourceProvider.GetConfiguredSources(solutionDir);
            _logger.WriteLine($"Using {sources.Count} NuGet source(s): {string.Join(", ", sources.Select(s => s.Name))}");

            // Populate Configuration tab
            this.PopulateConfigTab(solutionDir);

            var configFiles = _sourceProvider.GetConfigFilePaths(solutionDir);
            foreach (var cf in configFiles)
            {
                _logger.WriteLine($"  Config: {cf}");
            }

            // Build flat list of all packages from both formats
            var allPackages = new List<(ProjectPackageInfo Project, PackageEntry Package)>();

            foreach (var project in packagesConfigProjects)
            {
                var packages = PackagesConfigParser.Parse(project.PackagesConfigPath);
                _logger.WriteLine($"{project.ProjectName} [packages.config]: {packages.Count} package(s)");
                allPackages.AddRange(packages.Select(pkg => (Project: project, Package: pkg)));
            }

            foreach (var project in packageReferenceProjects)
            {
                var packages = PackageReferenceParser.Parse(project.ProjectFilePath);
                _logger.WriteLine($"{project.ProjectName} [PackageReference]: {packages.Count} package(s)");
                allPackages.AddRange(packages.Select(pkg => (Project: project, Package: pkg)));
            }

            // Deduplicate: group by (packageId, isPrerelease) for feed queries.
            // The query "latest version of MailKit" is the same regardless of current version.
            var uniqueQueries = allPackages
                .GroupBy(p => $"{p.Package.Id}|{p.Package.IsPrerelease}", StringComparer.OrdinalIgnoreCase)
                .Select(g => (Id: g.First().Package.Id, IsPrerelease: g.First().Package.IsPrerelease))
                .ToList();

            _logger.WriteLine($"{allPackages.Count} total entries, {uniqueQueries.Count} unique feed queries.");

            // Phase 1: Query feeds for unique package IDs in parallel
            int processedPackages = 0;
            int totalOutdated = 0;
            var lockObj = new object();
            var versionResults = new System.Collections.Concurrent.ConcurrentDictionary<string, PackageVersionResult?>(
                StringComparer.OrdinalIgnoreCase);
            var contentCache = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(
                StringComparer.OrdinalIgnoreCase);

            await Parallel.ForEachAsync(
                uniqueQueries,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 5,
                    CancellationToken = cancellationToken,
                },
                async (query, ct) =>
                {
                    var current = Interlocked.Increment(ref processedPackages);
                    this.StatusText = $"Querying {current} of {uniqueQueries.Count}: {query.Id}";

                    var result = await _sourceProvider.GetLatestVersionAsync(
                        query.Id, query.IsPrerelease, sources, ct);

                    var cacheKey = $"{query.Id}|{query.IsPrerelease}";
                    versionResults[cacheKey] = result;
                });

            // Phase 2: Map results back to all project/package combinations (no HTTP calls)
            this.StatusText = "Processing results...";
            foreach (var (project, pkg) in allPackages)
            {
                var cacheKey = $"{pkg.Id}|{pkg.IsPrerelease}";
                if (!versionResults.TryGetValue(cacheKey, out var result) || result is null)
                {
                    continue;
                }

                var cmp = VersionComparer.Compare(pkg.Version, result.Version.ToString());

                if (cmp.IsBogus)
                {
                    continue;
                }

                if (!cmp.IsNewer)
                {
                    continue;
                }

                totalOutdated++;
                var isContent = contentCache.GetOrAdd($"{pkg.Id}|{pkg.Version}|{project.ProjectDirectory}",
                    _ => ContentPackageDetector.IsContentPackage(pkg.Id, pkg.Version, project.ProjectDirectory));

                var issue = new PackageIssueViewModel(new PackageIssue
                {
                    ProjectName = project.ProjectName,
                    ProjectPath = project.ProjectFilePath,
                    PackageId = pkg.Id,
                    CurrentVersion = pkg.Version,
                    SuggestedVersion = result.Version.ToString(),
                    Source = result.SourceName,
                    Category = IssueCategory.Outdated,
                    UpdateType = cmp.UpdateType,
                    IsPrerelease = pkg.IsPrerelease,
                    IsContentPackage = isContent,
                    ProjectFormat = project.Format,
                    PackagesConfigEntry = project.Format == "packages.config" ? pkg.ToXmlString() : "",
                    Severity = cmp.IsMajor ? IssueSeverity.Warning : IssueSeverity.Info,
                    IsFixableByBatch = !cmp.IsMajor && project.Format == "packages.config",
                    DiagnosticMessage = $"{pkg.Version} -> {result.Version} [{cmp.UpdateType}]"
                        + (isContent ? " [content package]" : "")
                        + (project.Format == "PackageReference" ? " [PackageReference - update not yet supported]" : ""),
                });

                _allResults.Add(issue);
                this.Issues.Add(issue);
            }

            // C9: Detect orphaned references (.csproj refs not in packages.config)
            this.StatusText = "Checking for orphaned references...";
            int totalOrphaned = 0;
            foreach (var project in packagesConfigProjects)
            {
                var orphaned = OrphanedReferenceDetector.Detect(
                    project.ProjectFilePath, project.PackagesConfigPath);

                foreach (var orphan in orphaned)
                {
                    totalOrphaned++;
                    var issue = new PackageIssueViewModel(new PackageIssue
                    {
                        ProjectName = project.ProjectName,
                        ProjectPath = project.ProjectFilePath,
                        PackageId = orphan.PackageId,
                        CurrentVersion = orphan.VersionInCsproj,
                        SuggestedVersion = "Remove",
                        Source = "",
                        ProjectFormat = "packages.config",
                        PackagesConfigEntry = orphan.AffectedSnippet,
                        Category = IssueCategory.Orphaned,
                        Severity = IssueSeverity.Warning,
                        IsFixableByBatch = false,
                        DiagnosticMessage = $"Referenced in .csproj ({orphan.ReferenceTypes}) but not in packages.config. "
                            + "This may be a leftover from a removed package. "
                            + $"Removing will delete {orphan.AffectedLines.Count} element(s) from the .csproj.",
                    });

                    _allResults.Add(issue);
                    this.Issues.Add(issue);
                }

                if (orphaned.Count > 0)
                {
                    _logger.WriteLine($"  {project.ProjectName}: {orphaned.Count} orphaned reference(s)");
                }
            }

            // C4: Detect version inconsistencies across projects (both formats)
            this.StatusText = "Checking version consistency...";
            int totalInconsistent = 0;
            if (allProjects.Count > 1)
            {
                var inconsistencies = VersionConsistencyAnalyzer.Detect(allProjects);

                foreach (var inc in inconsistencies)
                {
                    // Group by version -- show one entry per lower version, listing affected projects
                    var lowerVersions = inc.VersionsByProject
                        .Where(pv => !string.Equals(pv.Version, inc.TargetVersion, StringComparison.OrdinalIgnoreCase))
                        .GroupBy(pv => pv.Version, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var versionGroup in lowerVersions)
                    {
                        var allProjectNames = string.Join(", ", versionGroup.Select(pv => pv.ProjectName));

                        // Create one issue per affected project (needed for per-project fix)
                        foreach (var pv in versionGroup)
                        {
                            totalInconsistent++;
                            var proj = allProjects.FirstOrDefault(p => p.ProjectName == pv.ProjectName);
                            if (proj is null) continue;

                            var isFixable = proj.Format == "packages.config";
                            var pcEntry = isFixable
                                ? $"<package id=\"{inc.PackageId}\" version=\"{pv.Version}\" />"
                                : "";

                            var issue = new PackageIssueViewModel(new PackageIssue
                            {
                                ProjectName = pv.ProjectName,
                                ProjectPath = proj.ProjectFilePath,
                                PackageId = inc.PackageId,
                                CurrentVersion = pv.Version,
                                SuggestedVersion = inc.TargetVersion,
                                Source = "",
                                ProjectFormat = proj.Format,
                                PackagesConfigEntry = pcEntry,
                                Category = IssueCategory.Inconsistent,
                                Severity = IssueSeverity.Info,
                                IsFixableByBatch = isFixable,
                                DiagnosticMessage = $"Version mismatch: this project has {pv.Version}, "
                                    + $"highest in solution is {inc.TargetVersion}. "
                                    + $"Other projects with this version: {allProjectNames}",
                            });

                            _allResults.Add(issue);
                            this.Issues.Add(issue);
                        }
                    }
                }

                if (inconsistencies.Count > 0)
                {
                    _logger.WriteLine($"{inconsistencies.Count} package(s) with version inconsistencies across projects");
                }
            }

            // Prerelease-mismatch: packages.config has prerelease, but PR projects have stable
            // This is a common issue where VS NuGet Manager doesn't show the correct version
            if (packagesConfigProjects.Count > 0 && packageReferenceProjects.Count > 0)
            {
                var pcPackages = packagesConfigProjects
                    .SelectMany(p => PackagesConfigParser.Parse(p.PackagesConfigPath)
                        .Where(pkg => pkg.IsPrerelease)
                        .Select(pkg => (Project: p, Package: pkg)))
                    .ToList();

                var prPackageVersions = packageReferenceProjects
                    .SelectMany(p => PackageReferenceParser.Parse(p.ProjectFilePath)
                        .Where(pkg => !pkg.IsPrerelease)
                        .Select(pkg => (Id: pkg.Id, Version: pkg.Version)))
                    .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Version, StringComparer.OrdinalIgnoreCase);

                foreach (var (project, pkg) in pcPackages)
                {
                    if (prPackageVersions.TryGetValue(pkg.Id, out var stableVersion))
                    {
                        var issue = new PackageIssueViewModel(new PackageIssue
                        {
                            ProjectName = project.ProjectName,
                            ProjectPath = project.ProjectFilePath,
                            PackageId = pkg.Id,
                            CurrentVersion = pkg.Version,
                            SuggestedVersion = stableVersion,
                            Source = "-",
                            ProjectFormat = "packages.config",
                            PackagesConfigEntry = pkg.ToXmlString(),
                            Category = IssueCategory.Inconsistent,
                            Severity = IssueSeverity.Info,
                            IsPrerelease = true,
                            IsFixableByBatch = false,
                            DiagnosticMessage = $"packages.config uses prerelease {pkg.Version}, but PackageReference projects use stable {stableVersion}. "
                                + "VS NuGet Manager may show the wrong version for this package. "
                                + "Consider updating to the stable version or keeping the prerelease intentionally.",
                        });

                        _allResults.Add(issue);
                        this.Issues.Add(issue);
                        _logger.WriteLine($"  Prerelease mismatch: {pkg.Id} {pkg.Version} (pc) vs {stableVersion} (pr)");
                    }
                }
            }

            // C2+C3: Check for vulnerabilities and deprecated packages (parallel, deduplicated)
            this.StatusText = "Checking for vulnerabilities and deprecations...";
            int totalVulnerable = 0;
            int totalDeprecated = 0;

            // Only check stable, unique (packageId, version) pairs
            var securityPackages = allPackages
                .Where(p => !p.Package.Version.Contains('-'))
                .Select(p => (p.Package.Id, p.Package.Version))
                .Distinct()
                .ToList();

            int secChecked = 0;
            var securityResults = new System.Collections.Concurrent.ConcurrentDictionary<string, PackageSecurityInfo?>(
                StringComparer.OrdinalIgnoreCase);

            // Phase 1: Query security metadata in parallel
            await Parallel.ForEachAsync(
                securityPackages,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 5,
                    CancellationToken = cancellationToken,
                },
                async (item, ct) =>
                {
                    var current = Interlocked.Increment(ref secChecked);
                    this.StatusText = $"Security check {current} of {securityPackages.Count}: {item.Id}";

                    var secInfo = await _metadataService.GetSecurityInfoAsync(
                        item.Id, item.Version, sources, ct);

                    if (secInfo is not null)
                    {
                        securityResults[$"{item.Id}|{item.Version}"] = secInfo;
                    }
                });

            // Phase 2: Map security results to all project/package pairs
            foreach (var (project, pkg) in allPackages)
            {
                if (!securityResults.TryGetValue($"{pkg.Id}|{pkg.Version}", out var secInfo) || secInfo is null)
                {
                    continue;
                }

                if (secInfo.IsVulnerable)
                {
                    totalVulnerable++;
                    _allResults.Add(new PackageIssueViewModel(new PackageIssue
                    {
                        ProjectName = project.ProjectName,
                        ProjectPath = project.ProjectFilePath,
                        PackageId = pkg.Id,
                        CurrentVersion = pkg.Version,
                        SuggestedVersion = "-",
                        Source = secInfo.AdvisoryUrl,
                        Category = IssueCategory.Vulnerable,
                        Severity = secInfo.VulnerabilitySeverity is "Critical" or "High"
                            ? IssueSeverity.Critical
                            : IssueSeverity.Warning,
                        IsFixableByBatch = false,
                        DiagnosticMessage = $"{secInfo.VulnerabilitySeverity} vulnerability"
                            + (secInfo.VulnerabilityCount > 1 ? $" ({secInfo.VulnerabilityCount} advisories)" : "")
                            + (!string.IsNullOrEmpty(secInfo.AdvisoryUrl) ? $" -- {secInfo.AdvisoryUrl}" : ""),
                    }));
                }

                if (secInfo.IsDeprecated)
                {
                    totalDeprecated++;
                    _allResults.Add(new PackageIssueViewModel(new PackageIssue
                    {
                        ProjectName = project.ProjectName,
                        ProjectPath = project.ProjectFilePath,
                        PackageId = pkg.Id,
                        CurrentVersion = pkg.Version,
                        SuggestedVersion = !string.IsNullOrEmpty(secInfo.ReplacementPackageId) ? secInfo.ReplacementPackageId : "-",
                        Source = "-",
                        Category = IssueCategory.Deprecated,
                        Severity = IssueSeverity.Warning,
                        IsFixableByBatch = false,
                        DiagnosticMessage = $"Deprecated ({secInfo.DeprecationReasons})"
                            + (!string.IsNullOrEmpty(secInfo.ReplacementPackageId) ? $" -- replace with {secInfo.ReplacementPackageId}" : "")
                            + (!string.IsNullOrEmpty(secInfo.DeprecationMessage) ? $" -- {secInfo.DeprecationMessage}" : ""),
                    }));
                }
            }

            // Add security issues to visible list
            foreach (var issue in _allResults.Where(i => i.Category is IssueCategory.Vulnerable or IssueCategory.Deprecated))
            {
                this.Issues.Add(issue);
            }

            if (totalVulnerable > 0)
            {
                _logger.WriteLine($"{totalVulnerable} vulnerable package(s) found");
            }
            if (totalDeprecated > 0)
            {
                _logger.WriteLine($"{totalDeprecated} deprecated package(s) found");
            }

            // C7: Migration readiness assessment
            this.StatusText = "Assessing migration readiness...";
            int totalMigrationIssues = 0;
            foreach (var project in packagesConfigProjects)
            {
                var assessment = MigrationReadinessAssessor.Assess(
                    project.ProjectFilePath, project.PackagesConfigPath, project.ProjectDirectory);

                foreach (var blocker in assessment.Blockers)
                {
                    totalMigrationIssues++;
                    var blockerLabel = blocker.Type switch
                    {
                        BlockerType.NonSdkProject => "Non-SDK .csproj format",
                        BlockerType.ContentPackages => "Content packages (JS/CSS)",
                        BlockerType.BuildImports => "Build imports from packages/",
                        BlockerType.WebProjectType => "ASP.NET Web Application",
                        BlockerType.BuildImportGuards => "NuGet build import guards",
                        _ => blocker.Type.ToString(),
                    };

                    var issue = new PackageIssueViewModel(new PackageIssue
                    {
                        ProjectName = project.ProjectName,
                        ProjectPath = project.ProjectFilePath,
                        PackageId = $"Migration: {blockerLabel}",
                        CurrentVersion = blocker.Severity == BlockerSeverity.Hard ? "Blocked" : "Warning",
                        SuggestedVersion = "-",
                        Source = "-",
                        Category = IssueCategory.MigrationReady,
                        Severity = blocker.Severity == BlockerSeverity.Hard
                            ? IssueSeverity.Warning
                            : IssueSeverity.Info,
                        IsFixableByBatch = false,
                        DiagnosticMessage = blocker.Description,
                    });

                    _allResults.Add(issue);
                    this.Issues.Add(issue);
                }

                _logger.WriteLine($"  {project.ProjectName}: migration {assessment.ReadinessLabel} ({assessment.Summary})");
            }

            // Summary
            var parts = new List<string>();
            if (totalOutdated > 0) parts.Add($"{totalOutdated} outdated");
            if (totalVulnerable > 0) parts.Add($"{totalVulnerable} vulnerable");
            if (totalDeprecated > 0) parts.Add($"{totalDeprecated} deprecated");
            if (totalOrphaned > 0) parts.Add($"{totalOrphaned} orphaned");
            if (totalInconsistent > 0) parts.Add($"{totalInconsistent} inconsistent");
            if (totalMigrationIssues > 0) parts.Add($"{totalMigrationIssues} migration");
            var summary = parts.Count > 0 ? string.Join(", ", parts) : "no issues";
            this.StatusText = $"Scan complete: {summary} across {allProjects.Count} project(s) ({packagesConfigProjects.Count} pc, {packageReferenceProjects.Count} pr).";

            _hasCompletedScan = true;
            this.RaiseNotifyPropertyChangedEvent(nameof(this.AnalyseButtonLabel));
            this.RaiseNotifyPropertyChangedEvent(nameof(this.UpdateShownEnabled));
        }
        catch (OperationCanceledException)
        {
            this.StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            this.StatusText = $"Error: {ex.Message}";
            _logger.WriteLine($"ERROR: {ex}");
        }
    }

    /// <summary>
    /// Updates the selected package (single update from detail panel).
    /// Handles different fix strategies per category.
    /// </summary>
    private async Task ExecuteUpdateSelectedAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (_selectedIssue is null)
        {
            return;
        }

        // Orphaned: remove references from .csproj (different fix path)
        if (_selectedIssue.Category == IssueCategory.Orphaned)
        {
            await this.RemoveOrphanedReferenceAsync(_selectedIssue);
            return;
        }

        await this.UpdatePackagesAsync([_selectedIssue]);
    }

    /// <summary>
    /// Removes orphaned package references from the .csproj file.
    /// </summary>
    private Task RemoveOrphanedReferenceAsync(PackageIssueViewModel issue)
    {
        if (this.CreateBackup)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
            PackagesConfigPatcher.CreateBackup(issue.ProjectPath, timestamp);
            _logger.WriteLine($"Backup created ({timestamp})");
        }

        int removed = OrphanedReferencePatcher.RemoveOrphanedReferences(
            issue.ProjectPath, issue.PackageId, issue.CurrentVersion);

        if (removed > 0)
        {
            issue.MarkAsFixed($"Fixed: removed {removed} reference(s) from .csproj");
            this.StatusText = $"Removed {removed} orphaned reference(s) for {issue.PackageId}. Click Re-Analyse to refresh.";
            _logger.WriteLine($"  Removed {removed} orphaned reference(s) for {issue.PackageId}");
        }
        else
        {
            this.StatusText = $"No references found to remove for {issue.PackageId}.";
            _logger.WriteLine($"  No references found for {issue.PackageId} in .csproj");
        }

        this.RaiseNotifyPropertyChangedEvent(nameof(this.UpdateSelectedEnabled));
        this.RaiseNotifyPropertyChangedEvent(nameof(this.UpdateSelectedButtonLabel));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates all visible (filtered) packages that are safe to batch-update.
    /// Skips MAJOR updates and other non-batch-safe items.
    /// </summary>
    private async Task ExecuteUpdateShownAsync(object? parameter, CancellationToken cancellationToken)
    {
        var fixable = this.Issues.Where(i => i.IsFixableByBatch).ToList();
        if (fixable.Count == 0)
        {
            this.StatusText = "No packages to update. MAJOR updates must be applied individually via the detail panel.";
            return;
        }

        await this.UpdatePackagesAsync(fixable);
    }

    /// <summary>
    /// Core update logic: edit packages.config + .csproj path tokens, restore, update assembly versions.
    /// </summary>
    private async Task UpdatePackagesAsync(List<PackageIssueViewModel> issuesToFix)
    {
        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();
        var cancellationToken = _updateCts.Token;
        this.IsScanning = true;
        int updated = 0;
        int failed = 0;

        try
        {
            // Group by project (each project has its own packages.config + .csproj)
            var byProject = issuesToFix.GroupBy(i => i.ProjectPath);

            foreach (var projectGroup in byProject)
            {
                var projectPath = projectGroup.Key;
                var projectDir = Path.GetDirectoryName(projectPath) ?? ".";
                var packagesConfigPath = Path.Combine(projectDir, "packages.config");

                if (!File.Exists(packagesConfigPath))
                {
                    _logger.WriteLine($"WARNING: packages.config not found at {packagesConfigPath}");
                    continue;
                }

                // Backup
                if (this.CreateBackup)
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
                    PackagesConfigPatcher.CreateBackup(packagesConfigPath, timestamp);
                    PackagesConfigPatcher.CreateBackup(projectPath, timestamp);
                    _logger.WriteLine($"Backup created ({timestamp})");
                }

                // Phase 1: Edit packages.config + .csproj path tokens
                foreach (var issue in projectGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    this.StatusText = $"Updating {issue.PackageId} to {issue.SuggestedVersion}...";

                    bool pcSuccess = PackagesConfigPatcher.UpdateVersion(
                        packagesConfigPath, issue.PackageId, issue.CurrentVersion, issue.SuggestedVersion);

                    bool csprojSuccess = PackagesConfigPatcher.UpdateCsprojPathTokens(
                        projectPath, issue.PackageId, issue.CurrentVersion, issue.SuggestedVersion);

                    if (pcSuccess)
                    {
                        updated++;
                        _logger.WriteLine($"  Updated: {issue.PackageId} {issue.CurrentVersion} -> {issue.SuggestedVersion}");
                        if (csprojSuccess)
                        {
                            _logger.WriteLine($"    .csproj path tokens updated");
                        }
                    }
                    else
                    {
                        failed++;
                        _logger.WriteLine($"  FAILED: {issue.PackageId} - not found in packages.config");
                    }
                }

                // Phase 2: Trigger restore (build the project)
                this.StatusText = "Restoring packages...";
                _logger.WriteLine("Triggering NuGet restore...");

                try
                {
                    var buildProjects = await this.Extensibility.Workspaces().QueryProjectsAsync(
                        q => q.Where(p => p.Path == projectPath).With(p => p.Name),
                        cancellationToken).ConfigureAwait(false);

                    var buildProject = buildProjects.FirstOrDefault();
                    if (buildProject is not null)
                    {
                        await buildProject.BuildAsync(cancellationToken).ConfigureAwait(false);
                        _logger.WriteLine("Restore/build completed.");
                    }
                    else
                    {
                        _logger.WriteLine("WARNING: Could not find project for build. Run NuGet restore manually.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.WriteLine($"WARNING: Build/restore failed: {ex.Message}");
                    _logger.WriteLine("Run NuGet restore manually: right-click solution > Restore NuGet Packages");
                }

                // Phase 3: Update assembly versions in <Reference Include> from restored DLLs
                this.StatusText = "Updating assembly versions...";
                foreach (var issue in projectGroup)
                {
                    int asmUpdated = PackagesConfigPatcher.UpdateAssemblyVersionsFromDlls(
                        projectPath, projectDir, issue.PackageId, issue.SuggestedVersion);

                    if (asmUpdated > 0)
                    {
                        _logger.WriteLine($"    {issue.PackageId}: assembly version updated in <Reference Include> ({asmUpdated} reference(s))");
                    }
                }
            }

            this.StatusText = $"Update complete. {updated} updated, {failed} failed. Click Re-Analyse to refresh.";

            // Mark successfully updated issues as fixed (no re-scan needed)
            foreach (var issue in issuesToFix)
            {
                if (!issue.IsFixed) continue; // only mark if we set it below
            }

            // Mark successfully updated issues — ViewModel raises property change events
            foreach (var issue in issuesToFix)
            {
                var match = _allResults.FirstOrDefault(r =>
                    r.PackageId == issue.PackageId
                    && r.ProjectPath == issue.ProjectPath
                    && r.CurrentVersion == issue.CurrentVersion
                    && !r.IsFixed);

                match?.MarkAsFixed($"Fixed: {match.CurrentVersion} -> {match.SuggestedVersion}");
            }
            this.RaiseNotifyPropertyChangedEvent(nameof(this.UpdateShownEnabled));
        }
        catch (OperationCanceledException)
        {
            this.StatusText = "Update cancelled.";
        }
        catch (Exception ex)
        {
            this.StatusText = $"Update error: {ex.Message}";
            _logger.WriteLine($"ERROR: {ex}");
        }
        finally
        {
            this.IsScanning = false;
        }
    }

    private Task ExecuteSortAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not string column || _allResults.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (_sortColumn == column)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }

        foreach (var name in new[] { nameof(SortIndicatorPackage), nameof(SortIndicatorCurrent),
            nameof(SortIndicatorSuggested), nameof(SortIndicatorCategory), nameof(SortIndicatorSeverity),
            nameof(SortIndicatorSource), nameof(SortIndicatorProject) })
        {
            this.RaiseNotifyPropertyChangedEvent(name);
        }

        this.ApplyFilters();
        return Task.CompletedTask;
    }

    private void ApplyFilters()
    {
        this.Issues.Clear();

        IEnumerable<PackageIssueViewModel> filtered = _allResults;

        // Project filter
        if (_selectedProject != "All Projects")
        {
            filtered = filtered.Where(i =>
                i.ProjectName.Contains(_selectedProject, StringComparison.OrdinalIgnoreCase));
        }

        // Category filter
        if (_selectedCategory != "All")
        {
            filtered = filtered.Where(i =>
                string.Equals(i.Category.ToString(), _selectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        // Text filter
        if (!string.IsNullOrEmpty(_packageFilter))
        {
            filtered = filtered.Where(i =>
                i.PackageId.Contains(_packageFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Sort
        Func<PackageIssueViewModel, string> keySelector = _sortColumn switch
        {
            "Package" => i => i.PackageId,
            "Current" => i => i.CurrentVersion,
            "Suggested" => i => i.SuggestedVersion,
            "Category" => i => i.CategoryLabel,
            "Source" => i => i.Source,
            "Project" => i => i.ProjectName,
            _ => i => i.Severity.ToString() // "Severity" — default
        };

        filtered = _sortAscending
            ? filtered.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase)
            : filtered.OrderByDescending(keySelector, StringComparer.OrdinalIgnoreCase);

        foreach (var issue in filtered)
        {
            this.Issues.Add(issue);
        }

        this.StatusText = $"Showing {this.Issues.Count} of {_allResults.Count} issue(s).";
    }

    private void SelectTab(bool issues = false, bool config = false, bool background = false, bool feedback = false)
    {
        _isIssuesTabSelected = issues;
        _isConfigTabSelected = config;
        _isBackgroundTabSelected = background;
        _isFeedbackTabSelected = feedback;

        foreach (var name in new[]
        {
            nameof(IssuesTabVisibility), nameof(IssuesTabFontWeight), nameof(IssuesTabUnderline), nameof(IssuesTabOpacity),
            nameof(ConfigTabVisibility), nameof(ConfigTabFontWeight), nameof(ConfigTabUnderline), nameof(ConfigTabOpacity),
            nameof(BackgroundTabVisibility), nameof(BackgroundTabFontWeight), nameof(BackgroundTabUnderline), nameof(BackgroundTabOpacity),
            nameof(FeedbackTabVisibility), nameof(FeedbackTabFontWeight), nameof(FeedbackTabUnderline), nameof(FeedbackTabOpacity),
        })
        {
            this.RaiseNotifyPropertyChangedEvent(name);
        }
    }

    private void PopulateConfigTab(string solutionDir)
    {
        var sources = _sourceProvider.GetConfiguredSources(solutionDir);
        var configFiles = _sourceProvider.GetConfigFilePaths(solutionDir);

        var sb = new System.Text.StringBuilder();
        foreach (var source in sources)
        {
            var auth = source.Credentials is not null ? "  [auth]" : "";
            sb.AppendLine($"  {(source.IsEnabled ? "[x]" : "[ ]")}  {source.Name,-20}  {source.Source}{auth}");
        }

        this.ConfigSourcesText = sb.Length > 0 ? sb.ToString() : "No NuGet sources found.";
        this.ConfigFilesText = string.Join("\n", configFiles.Select(f => $"  {f}"));
    }

    #endregion
}
