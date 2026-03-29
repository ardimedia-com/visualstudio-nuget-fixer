---
status: Stable
updated: 2026-03-29 12:00h
references:
  - ../../../github/ardimedia/visualstudio-binding-redirect-fixer/.claude/overview.md — Boilerplate extension architecture
---

# NuGet Package Fixer - Visual Studio Extension

## Problem Statement

Managing NuGet packages in .NET solutions has multiple pain points that Visual Studio's built-in tooling does not fully address:

**packages.config projects (.NET Framework):**

1. **`dotnet list package --outdated` does not work** with packages.config -- it only supports PackageReference format
2. **`nuget.exe update` resolves the entire dependency chain** for every single package update -- if any package in the project has a prerelease version (e.g., `5.0.1-dev-00860`), the resolution fails with "Unable to resolve dependency" because prerelease packages are invisible without the `-PreRelease` flag. This happens regardless of which feed the package comes from (nuget.org, private feeds, local). A private feed makes it worse (package not found at all), but the core issue is any prerelease version in the dependency chain.
3. **The `-PreRelease` flag is global** -- you cannot selectively allow prerelease for internal packages while keeping public packages on stable versions
4. **No migration to PackageReference** is possible for ASP.NET MVC 4.8 Web Apps (non-SDK .csproj, content packages like jQuery/Modernizr, build .props imports from packages/ folder)

**All project formats:**

5. **Vulnerable packages** (NU1903) are shown as warnings but VS does not help fix them -- no "update to patched version" action
6. **Deprecated packages** are silently used without warning -- no detection or replacement guidance
7. **Version inconsistencies** across projects in a solution cause subtle runtime errors
8. **Package downgrades** are often suppressed with `NoWarn NU1605` instead of fixed

These problems affect .NET projects of all types, especially in enterprise environments with large solutions and mixed NuGet feeds.

## Solution

A Visual Studio extension that provides a unified "NuGet health check" for any .NET solution:

- Detects outdated, vulnerable, deprecated, and inconsistent packages across all project formats
- Queries all configured NuGet sources (nuget.org, Azure Artifacts, GitHub Packages, GitLab, MyGet, etc.)
- Handles mixed stable/prerelease scenarios correctly
- For packages.config: updates by editing XML directly (bypassing `nuget.exe update` and its broken dependency resolution)
- For PackageReference: updates via .csproj XML edit
- Provides a single Issues list with category/severity filtering (VS Error List pattern)
- Tool window UI consistent with the Binding Redirect Fixer extension

## Architecture

Follow the same extension architecture as `visualstudio-binding-redirect-fixer`:

- **SDK:** Microsoft.VisualStudio.Extensibility (out-of-process, .NET 10)
- **UI:** Remote UI with XAML, themed to match VS dark/light/blue
- **Tool Window:** DocumentWell placement, accessible from Tools menu
- **Models:** DataContract-decorated for Remote UI proxy binding
- **Commands:** Single "NuGet Package Fixer" command in Tools menu

## Features

### F1: Multi-Source Package Scanning

Scan packages.config and check each package against all configured NuGet sources.

**Prerelease detection by version string (no prefix/hardcoding needed):**

A package's current version determines how the extension searches for updates:

| Current Version | Contains `-` | Search Mode |
|---|---|---|
| `4.14.1` | No | Search for latest **stable** version only (ignore prerelease candidates) |
| `4.0.0-20260128-01` | Yes | Search for latest version **including prerelease** |

This is generic and works for any package from any feed without needing prefix lists or package-to-feed mappings. The version string itself carries the intent: if you're on a prerelease version, you want prerelease updates.

**NuGet source discovery (no hardcoded feeds):**

The extension reads NuGet sources from the same places Visual Studio uses:

1. **VS Package Manager Settings** (Tools > NuGet Package Manager > Package Manager Settings > Package Sources) -- via `ISettings` from `NuGet.Configuration`
2. **nuget.config hierarchy** (solution directory, parent directories, user-level, machine-level) -- standard NuGet config resolution

The extension queries **all enabled sources** for each package. When multiple sources return a version for the same package, the highest version wins. This means:

- No need to know which feed a package comes from
- No need for prefix-to-feed configuration
- Works automatically when new feeds are added in VS settings
- Auth is handled by NuGet's credential providers (already configured in VS)

### F2: Version Comparison Engine

Robust version comparison that handles real-world edge cases discovered during development:

| Edge Case | Example | Handling |
|---|---|---|
| Bogus versions on nuget.org | `System.ComponentModel.Composition` 10.0.2 -> 2010.2.11.1 | Detect: latest.Major > current.Major * 10 + 100. Skip with warning. |
| Zero version | `System.Data.DataSetExtensions` 4.5.0 -> 0 | Detect: latest.Major == 0. Skip with warning. |
| Version format padding | `Owin` 1.0 vs 1.0.0 | Normalize to 3-part minimum before comparison |
| Major version bumps | `Microsoft.Data.SqlClient` 6.1.4 -> 7.0.0 | Flag as MAJOR, do not auto-update |
| Prerelease date comparison | `4.0.0-20260128-01` vs `4.0.0-20260323-01` | Compare base version first, then suffix lexicographically |

### F3: Update Classification

Every outdated package gets a classification that determines the UI treatment:

| Type | Condition | Auto-Update | UI Color |
|---|---|---|---|
| Patch | Same major version, stable to stable | Yes | Green |
| MAJOR | Different major version | No (manual only) | Yellow/Warning |
| Bogus | Version looks invalid | No | Gray/Skipped |
| Prerelease | Current is prerelease, newer prerelease available | Yes | Blue/Info |
| Stable Promotion | Current is prerelease, stable version now available (e.g. `4.0.0-rc1` -> `4.0.0`) | Yes | Green/Info |

### F4: XML-Based Update (No External Tools)

The extension does **not** depend on `nuget.exe` being installed. It operates entirely with:

- **NuGet.Protocol** SDK (NuGet client libraries) for querying feeds
- **NuGet.Configuration** SDK for reading sources and credentials
- **System.Xml.Linq** for editing packages.config

**Update approach -- edit XML, then restore:**

1. Parse packages.config as XML
2. Find the `<package>` element by `id` and `version` attributes
3. Update the `version` attribute via `SetAttribute("version", newVersion)`
4. Save the XML with UTF-8 BOM encoding
5. Sync `.csproj` path references for the updated package (see below)
6. Trigger NuGet restore automatically (see F8 for the full post-update flow)

This completely bypasses the dependency resolution problem because we only change what version is declared -- the restore step handles downloading.

**Why this works:**
- `nuget.exe update` tries to resolve ALL transitive dependencies for every change -- fails if any dependency is an unresolvable prerelease
- Restore only downloads what packages.config declares -- no resolution conflicts
- The developer is responsible for version compatibility (same as manually editing packages.config)

**Why no nuget.exe dependency:**
- The extension runs out-of-process (.NET 10) and can use NuGet.Protocol directly
- NuGet.Protocol handles feed authentication via the same credential providers as VS
- No need to locate, validate, or shell out to a CLI tool
- Faster (in-process HTTP calls vs spawning processes)

**`.csproj` Path Reference Sync (step 5)**

Classic `.NET Framework` projects store the package version in two separate files that must always be in sync. When `packages.config` is updated, the `.csproj` still contains the old version embedded as a folder-name token in up to three distinct element types:

```xml
<!-- 1. Assembly reference HintPath -->
<Reference Include="Serilog, Version=4.3.0.0, Culture=neutral, ...">
  <HintPath>..\packages\Serilog.4.3.0\lib\net471\Serilog.dll</HintPath>
</Reference>

<!-- 2. Build guard in EnsureNuGetPackageBuildImports target -->
<Error Condition="!Exists('..\packages\Serilog.4.3.0\build\Serilog.targets')" ... />

<!-- 3. Build targets import -->
<Import Project="..\packages\Serilog.4.3.0\build\Serilog.targets"
        Condition="Exists('..\packages\Serilog.4.3.0\build\Serilog.targets')" />
```

If only `packages.config` is updated (e.g. `4.3.0` → `4.3.1`), NuGet restore on a clean CI agent downloads `packages\Serilog.4.3.1\`, but the `.csproj` still checks for `packages\Serilog.4.3.0\build\Serilog.targets`. The `<Error Condition="!Exists(...)">` guard fires and **the build fails** -- even though the package was successfully restored at the correct version.

**The fix has two parts:**

**Part A -- Path token replacement:** For each updated package, replace every occurrence of `{PackageId}.{OldVersion}` with `{PackageId}.{NewVersion}` as a plain string in the `.csproj`. This covers HintPaths, `<Import>` paths, and `<Error>` condition paths in one pass.

**Part B -- Assembly version update in `<Reference Include>`:** The `Version=x.x.x.x` inside `<Reference Include="MailKit, Version=4.14.0.0, Culture=neutral, ...">` is the **assembly version baked into the DLL**. This is NOT the NuGet package version -- they frequently differ (e.g., MailKit package `4.15.1` ships a DLL with assembly version `4.15.0.0`). After the path token replacement (Part A) and NuGet restore, the new DLL is available in `packages/{id}.{newVersion}/lib/`. The extension must:

1. Locate the restored DLL via the updated HintPath
2. Read the actual assembly version from the DLL metadata (use `System.Reflection.Metadata` or `AssemblyName.GetAssemblyName()`)
3. Update the `Version=` value in the `<Reference Include>` attribute to match the DLL's assembly version

Real-world example (MailKit 4.14.1 -> 4.15.1):
```xml
<!-- Before -->
<Reference Include="MailKit, Version=4.14.0.0, Culture=neutral, PublicKeyToken=...">
  <HintPath>..\packages\MailKit.4.14.1\lib\net48\MailKit.dll</HintPath>
</Reference>

<!-- After Part A (path token) -->
<Reference Include="MailKit, Version=4.14.0.0, Culture=neutral, PublicKeyToken=...">
  <HintPath>..\packages\MailKit.4.15.1\lib\net48\MailKit.dll</HintPath>
</Reference>

<!-- After Part B (assembly version from DLL) -->
<Reference Include="MailKit, Version=4.15.0.0, Culture=neutral, PublicKeyToken=...">
  <HintPath>..\packages\MailKit.4.15.1\lib\net48\MailKit.dll</HintPath>
</Reference>
```

**Why Part B matters:** If the assembly version in `<Reference Include>` doesn't match the actual DLL, the build may succeed but runtime assembly resolution can fail or produce incorrect binding redirect ranges. This was discovered when updating MailKit/MimeKit 4.14.x -> 4.15.1 -- the HintPath was updated but `Version=4.14.0.0` remained, while the new DLL had `Version=4.15.0.0`.

**Sequencing:**

```
1. Edit packages.config (version attribute)
2. Edit .csproj Part A (path tokens: {PackageId}.{OldVersion} -> {PackageId}.{NewVersion})
3. NuGet restore (downloads new packages to packages/ folder)
4. Edit .csproj Part B (read assembly version from restored DLL, update <Reference Include>)
```

Part B runs AFTER restore because it needs the new DLL to read the actual assembly version. Parts 1-2 run BEFORE restore so the restore downloads the correct version.

**Scope:** The `.csproj` to update is the one in the same directory as the `packages.config` being patched. `SolutionScanner` already finds `.csproj` files per project -- use that result rather than re-scanning.

### F5: Batch Operations

- **Update Shown:** Update all currently visible (filtered) packages in one click
- **Filter by project:** When solution has multiple packages.config files
- **Filter by status:** Show only outdated, only MAJOR, only prerelease, etc.
- **Filter + Update pattern:** Filter to "patch" then click "Update Shown" to update only safe patches. Filter to "prerelease" to update only prerelease packages. This avoids confusing button labels like "Update All Prerelease" (which could be misread as "update everything TO prerelease").

### F6: Solution-Wide Scanning

Scan all `packages.config` files across the entire solution:

- Walk all project directories
- Group results by project
- Show cross-project version inconsistencies (same package at different versions in different projects)

### F7: Backup Before Update

- Create timestamped backup: `packages.config.2026-03-27-1830.bak`
- User can disable via checkbox (same pattern as Binding Redirect Fixer)

### F8: Post-Update Flow

After the user clicks "Update" (single or batch), the extension runs the full cycle automatically:

1. **XML edit Part A** -- update version attributes in packages.config + path tokens in .csproj (`{PackageId}.{OldVersion}` -> `{PackageId}.{NewVersion}`)
2. **NuGet restore** -- trigger via VS build system (`IVsSolutionBuildManager` or DTE `BuildProject`) which runs NuGet restore as part of the build pipeline. Downloads new packages to `packages/` folder.
3. **XML edit Part B** -- read assembly version from each restored DLL, update `Version=` in `<Reference Include>` to match actual DLL assembly version
4. **Content install** -- for content packages, run `install.ps1` from the new version (see F9)
5. **Show output** in an "Output" section at the bottom of the tool window (scrollable log, collapsible). Includes: updated packages, restore results, content script results, warnings, errors.
6. **Re-scan** -- automatically refresh the Issues list to reflect the new state

The user does **not** need to manually run restore or re-scan. The only manual step is clicking "Update".

For MAJOR updates (skipped by batch update): the detail panel shows a note with the major version change and suggests reviewing release notes. The user can still update a MAJOR package individually via the detail panel button.

**Out-of-process architecture note:**

The extension runs out-of-process (VisualStudio.Extensibility). It cannot use in-process COM APIs like `IVsPackageInstaller` directly. Restore and content script execution options:

| Approach | Mechanism | Pro | Con |
|---|---|---|---|
| VS Build command | `IVsSolutionBuildManager` via service broker | Native VS integration | Rebuilds entire project |
| DTE automation | `dte.ExecuteCommand("Build.RebuildSolution")` | Simple | Coarse-grained |
| Shell NuGet restore | Spawn `dotnet restore` or `nuget.exe restore` | Precise control | External tool dependency |
| NuGet.Protocol in-process | Use NuGet SDK to download packages directly | No external tools | Does not update packages/ folder layout for packages.config |

Recommended: Use VS build command for restore (triggers NuGet restore automatically). For content scripts (F9 Phase 2), evaluate whether the VS PowerShell host can be invoked from out-of-process, otherwise fall back to spawning PowerShell.

### F9: Content Package Handling

Some NuGet packages ship content files (JS, CSS, images) that are copied into the project via `install.ps1` scripts or `<content>` folder conventions. The XML-edit approach does **not** update these files -- only the DLLs change during restore. Content packages need special treatment.

**Detection:** The extension checks if the package folder in `packages/` contains a `content/` or `contentFiles/` directory, or if `install.ps1` / `uninstall.ps1` scripts exist. If so, the package is flagged as a content package.

**Known content packages** (built-in list as fallback when packages/ folder is not yet available):

| Package | Content Type |
|---|---|
| jQuery | JavaScript |
| jQuery.Validation | JavaScript |
| jQuery.UI | JavaScript + CSS |
| Modernizr | JavaScript |
| Respond | JavaScript |
| WebGrease | JavaScript |
| Bootstrap | CSS + JavaScript |
| Font-Awesome | CSS + Fonts |
| Microsoft.jQuery.Unobtrusive.* | JavaScript |

**Update strategy -- single pipeline, two phases:**

All packages go through the same pipeline. Content packages get an extra phase.

```
Phase 1: XML edit + restore (ALL packages)
  Update version attributes in packages.config
  NuGet restore downloads new packages to packages/ folder

Phase 2: Re-install content (ONLY content packages)
  For each content package that was updated:
    Run install.ps1 from NEW version: packages/{id}.{newVersion}/tools/install.ps1
```

Phase 2 simply runs `install.ps1` from the already-restored new package version. This copies the new content files (JS/CSS/fonts) into the project, overwriting the old ones.

No `uninstall.ps1` from the old version is needed -- the old package folder may already be gone after restore, and running uninstall would add complexity for little benefit. Content files are overwritten by install, and any orphaned files from the old version (e.g., renamed files) are a rare edge case the user can clean up manually.

**Why this works:**
- After Phase 1, the new package is in `packages/{id}.{newVersion}/` with its `install.ps1` ready
- No dependency on old package folder existing
- No timing issues -- we only use what restore just downloaded
- If Phase 2 fails, DLLs are still correctly updated from Phase 1
- Simple: one script per content package, no pre-collection needed

**UX behavior:**
- Content packages show a distinct icon in the Issues list (e.g., file/document icon)
- Detail panel shows: "Content package -- JS/CSS files will be updated automatically after restore"
- Output log shows Phase 1 (restore) and Phase 2 (content scripts) separately
- If a content script fails: warning in output log with the specific package name

### F10: Configuration Tab

A new tab "Configuration" (between "Issues" and "Background") showing the runtime environment:

```
+------------------------------------------------------------------+
| [Issues]  [Configuration]  [Background]  [Feedback]              |
+------------------------------------------------------------------+
|                                                                   |
| NuGet Sources (from VS Settings / nuget.config)                  |
| +---------+----------------------------------------------+-------+|
| | Enabled | Name               | Source URL              | Auth  ||
| +---------+----------------------------------------------+-------+|
| |  [x]    | nuget.org          | https://api.nuget.org/  | -     ||
| |  [x]    | am-private         | https://pkgs.dev.az...  | PAT   ||
| |  [x]    | GitHub Packages    | https://nuget.pkg.gi... | Token ||
| |  [ ]    | local-cache        | C:\local-packages       | -     ||
| +---------+----------------------------------------------+-------+|
|                                                                   |
| Config Files Used                                                 |
| - D:\CODE\amvs\ardimedia.com.amms\NuGet.Config                  |
| - D:\CODE\amvs\NuGet.Config                                     |
| - C:\Users\Harry\AppData\Roaming\NuGet\NuGet.Config             |
|                                                                   |
| Extension Settings                                                |
| [x] Create backup before update                                  |
| [ ] Auto-update major versions                                   |
| Skip packages: [Antlr, Modernizr______________ x]                |
|                                                                   |
+------------------------------------------------------------------+
```

**Purpose:**
- Transparency: user sees exactly which feeds are being queried
- Troubleshooting: if a package is "not found", user can check if the correct feed is enabled
- No separate settings dialog needed -- everything is visible in-context
- NuGet sources are read-only here (managed via VS Settings or nuget.config)
- Extension settings (backup, skip list) are editable directly in this tab

## Data Model

### Phase 1 (packages.config outdated only)

```csharp
[DataContract]
public class PackageIssue
{
    // Identity
    [DataMember] public string ProjectName { get; set; }
    [DataMember] public string ProjectPath { get; set; }
    [DataMember] public string PackageId { get; set; }
    [DataMember] public string CurrentVersion { get; set; }

    // Issue classification
    [DataMember] public IssueCategory Category { get; set; }     // Outdated, Vulnerable, etc.
    [DataMember] public IssueSeverity Severity { get; set; }     // Critical, Warning, Info
    [DataMember] public string SuggestedVersion { get; set; }    // Latest version or patched version
    [DataMember] public string Source { get; set; }              // Feed name where latest was found

    // Outdated-specific (C1)
    [DataMember] public UpdateType UpdateType { get; set; }      // Patch, Major, Prerelease, etc.
    [DataMember] public bool IsPrerelease { get; set; }          // Current version contains '-'
    [DataMember] public bool IsContentPackage { get; set; }      // Package has content/ folder
    [DataMember] public string ProjectFormat { get; set; }       // "packages.config" or "PackageReference"

    // UI binding
    [DataMember] public string StatusIcon { get; set; }
    [DataMember] public string DiagnosticMessage { get; set; }
    [DataMember] public bool IsFixableByBatch { get; set; }      // Can "Fix Shown" handle this?
}
```

### Phase 6+ (additional fields for C2-C8)

```csharp
// Added to PackageIssue for vulnerability/deprecation categories
[DataMember] public string AdvisoryUrl { get; set; }             // C2: GitHub Advisory link
[DataMember] public string AdvisoryId { get; set; }              // C2: GHSA-xxxx-xxxx
[DataMember] public string DeprecationReason { get; set; }       // C3: Legacy, CriticalBugs, Other
[DataMember] public string ReplacementPackageId { get; set; }    // C3: Suggested replacement
[DataMember] public List<string> InconsistentProjects { get; set; } // C4: Projects with different versions
```

### Enums

```csharp
public enum IssueCategory
{
    Outdated,           // C1: Newer version available
    Vulnerable,         // C2: Known security vulnerability
    Deprecated,         // C3: Package deprecated by author
    Inconsistent,       // C4: Different versions across projects
    Downgrade,          // C5: NU1605 suppressed
    Unused,             // C6: Can be removed (NU1510)
    MigrationReady,     // C7: Assessment (informational)
    CpmCandidate,       // C8: Assessment (informational)
    Orphaned            // C9: .csproj references package not in packages.config
}

public enum IssueSeverity
{
    Critical,           // Vulnerable (High/Critical)
    Warning,            // Outdated MAJOR, Deprecated, Vulnerable (Low/Moderate)
    Info                // Outdated Patch, Prerelease, Inconsistent
}

public enum UpdateType
{
    UpToDate,           // No update available
    Patch,              // Minor/patch update, safe to auto-apply
    Major,              // Major version bump, requires manual review
    Bogus,              // Invalid version on feed, skip
    Prerelease,         // Prerelease to newer prerelease
    StablePromotion     // Prerelease to stable (e.g., 4.0.0-rc1 -> 4.0.0)
}
```

## Services Architecture

Following the Binding Redirect Fixer pattern. Services are introduced per phase -- Phase 1 ships with the core services, later phases add new ones.

### Phase 1-5 Services (packages.config)

| Service | Responsibility |
|---|---|
| `SolutionScanner` | Finds all packages.config and .csproj files in the solution, determines project format |
| `NuGetSourceProvider` | Reads configured NuGet sources from VS settings and nuget.config hierarchy via `NuGet.Configuration.Settings` |
| `PackageVersionResolver` | Queries all enabled feeds for a package's latest version using `NuGet.Protocol` (handles auth, prerelease filtering) |
| `PackagesConfigParser` | Reads packages.config XML, determines prerelease status from version string |
| `PackagesConfigPatcher` | Updates version attributes in packages.config XML, creates backups, syncs `.csproj` path tokens (HintPaths, `<Import>`, `<Error>` guards) and assembly versions in `<Reference Include>` (read from restored DLL) |
| `VersionComparer` | Normalizes and compares versions, detects bogus/major/patch |
| `ContentPackageDetector` | Checks if a package has content/ or contentFiles/ in its packages/ folder; maintains known-list fallback |
| `OrphanedReferenceDetector` | Cross-references .csproj package paths against packages.config entries, flags orphaned references (C9) |
| `RestoreService` | Triggers NuGet restore via VS build system (see F8 architecture note) |

### Phase 6+ Services (PackageReference, C2-C9)

| Service | Phase | Responsibility |
|---|---|---|
| `PackageReferenceParser` | 6 | Reads PackageReference items from .csproj files |
| `PackageReferencePatcher` | 6 | Updates version attributes in .csproj XML |
| `VersionConsistencyAnalyzer` | 6 | Compares same PackageId across all projects, detects inconsistencies (C4) |
| `VulnerabilityChecker` | 7 | Queries NuGet vulnerability API / GitHub Advisory Database for known CVEs (C2) |
| `DeprecationChecker` | 7 | Queries NuGet package metadata API for deprecation status and replacement info (C3) |
| `DowngradeDetector` | 8 | Scans .csproj for `AllowDowngrade`, `NoWarn NU1605` (C5) |
| `UnusedPackageDetector` | 8 | Analyzes NU1510 build output, detects prunable packages (C6) |
| `MigrationAssessor` | 8 | Evaluates packages.config projects for PackageReference migration readiness (C7) |
| `CpmGenerator` | 8 | Generates Directory.Packages.props from existing PackageReference versions (C8) |

### Feed Query via NuGet.Protocol

Use the `NuGet.Protocol` SDK (same libraries Visual Studio uses internally). No external tools needed.

```csharp
// Read all configured sources (same as VS Package Manager Settings)
var settings = NuGet.Configuration.Settings.LoadDefaultSettings(solutionDirectory);
var sourceProvider = new PackageSourceProvider(settings);
var sources = sourceProvider.LoadPackageSources().Where(s => s.IsEnabled);

// Query each source for package versions
foreach (var source in sources)
{
    var repository = Repository.Factory.GetCoreV3(source);
    var resource = await repository.GetResourceAsync<FindPackageByIdResource>(ct);
    var versions = await resource.GetAllVersionsAsync(packageId, cache, logger, ct);

    // Filter: if current version is stable, only consider stable versions
    // If current version is prerelease, include prerelease versions
    var candidates = isCurrentPrerelease
        ? versions
        : versions.Where(v => !v.IsPrerelease);

    var latest = candidates.MaxBy(v => v);
}
```

This approach:
- Uses VS's own credential providers (Azure Artifacts, GitHub Packages, etc. all work automatically)
- Respects nuget.config hierarchy (solution > user > machine)
- No need to hardcode feed URLs or know feed types
- No dependency on nuget.exe

## UI Layout

Follow the Binding Redirect Fixer UI patterns.

### Target Layout (Phase 6+, all categories)

```
+----------------------------------------------------------------------+
| [Issues]  [Configuration]  [Background]  [Feedback]                  |
+----------------------------------------------------------------------+
| Project: [All v]  Filter: [_______ x]  Category: [All v]  Sev: [All]|
| [Analyse]  [Fix Shown]                           [x] Create Backup   |
+----------------------------------------------------------------------+
| !  | Package           | Current  | Suggested | Category    | Sev   |
|----|-------------------|----------|-----------|-------------|-------|
| !! | Microsoft.Owin    | 4.2.3    | 4.2.4     | Vulnerable  | Crit  |
| !  | MS.Data.SqlClient | 6.1.4    | 7.0.0     | Outdated    | Warn  |
|    | MailKit           | 4.14.1   | 4.15.1    | Outdated    | Info  |
|    | Am.BaseSystem     | -0128-01 | -0323-01  | Outdated    | Info  |
| !  | WindowsAzure.Stor.| 9.3.3    | -> Azure..| Deprecated  | Warn  |
| ~  | Newtonsoft.Json    | 13.0.3   | 13.0.4    | Inconsist.  | Info  |
|    | Microsoft.CSharp  | 4.7.0    | -         | Unused      | Info  |
| !  | FluentFTP         | 53.0.2   | -         | Orphaned    | Warn  |
+----------------------------------------------------------------------+
| Detail: Microsoft.Owin 4.2.3                                        |
| Vulnerability: GHSA-3rq8-h3gj-r5c6 (High severity)                 |
| Fixed in: 4.2.4                                                     |
| [Update to 4.2.4]                                                   |
+----------------------------------------------------------------------+
| Output: (collapsible)                                                |
| > Restored MailKit 4.15.1 from nuget.org                            |
| > Content: jQuery 3.7.1 - running install.ps1 for content files     |
+----------------------------------------------------------------------+
```

### Phase 1 Layout (packages.config outdated only)

In Phase 1, only the Outdated category exists, so the Category/Severity columns are hidden and the UI is simpler. The column structure is designed to expand to the target layout without breaking changes.

```
+------------------------------------------------------------------+
| [Issues]  [Configuration]  [Background]  [Feedback]              |
+------------------------------------------------------------------+
| Project: [All Projects v]  Filter: [_________ x]  Type: [All v] |
| [Analyse]  [Update Shown]                     [x] Create Backup  |
+------------------------------------------------------------------+
| Package          | Current        | Latest         | Type  | Src  |
|------------------|----------------|----------------|-------|------|
| MailKit          | 4.14.1         | 4.15.1         | patch | org  |
| Am.BaseSystem    | 4.0.0-0128-01  | 4.0.0-0323-01  | pre   | priv |
| MS.Data.SqlClient| 6.1.4          | 7.0.0          | MAJOR | org  |
+------------------------------------------------------------------+
| Detail: MailKit                                                   |
| Current: 4.14.1  ->  Latest: 4.15.1  (nuget.org)                |
| Type: Patch update (safe to auto-apply)                          |
| [Update This Package]                                            |
+------------------------------------------------------------------+
| Output: (collapsible)                                            |
| > Restored MailKit 4.15.1 from nuget.org                         |
+------------------------------------------------------------------+
```

### "Fix Shown" Action -- Category-Aware Behavior

The "Fix Shown" button applies the appropriate action per category. It does **not** blindly update everything:

| Category | Action | Confirmation |
|---|---|---|
| Outdated (Patch, Prerelease, StablePromotion) | Update version | No (safe) |
| Outdated (MAJOR) | Skipped by default | Only via detail panel |
| Vulnerable | Update to patched version | No (safe, same as Patch) |
| Deprecated | Skipped | Only via detail panel (package ID change) |
| Inconsistent | Consolidate to highest version | Confirmation dialog |
| Unused | Skipped | Only via detail panel (destructive) |
| Orphaned | Skipped | Only via detail panel (needs review) |
| Migration/CPM | N/A | Not actionable via "Fix Shown" |

"Fix Shown" = batch-apply safe fixes. Risky or complex actions are always per-package via the detail panel. This makes the button safe to click without fear.

### Background Tab Content

Educational content explaining:

- Why `dotnet list package --outdated` doesn't work with packages.config
- Why `nuget.exe update` fails with mixed feeds
- The XML-edit approach and why it's safe
- Version comparison edge cases (bogus versions, format padding)
- Major vs patch update risk assessment
- What vulnerability severities mean and how to prioritize

## Configuration

### NuGet Sources (no extension config needed)

The extension reads NuGet sources from the standard locations -- no feed configuration in the extension itself:

- **VS settings:** Tools > NuGet Package Manager > Package Manager Settings > Package Sources
- **nuget.config files:** Standard NuGet hierarchy (solution, user, machine level)

If a feed requires authentication, it must be configured in VS or nuget.config -- the extension does not manage credentials.

### Per-Solution Settings (`.vs/nuget-fixer.json`, optional)

```json
{
  "skipPackages": ["Antlr", "TypeLite"],
  "autoUpdateMajor": false
}
```

### User Settings (`%LOCALAPPDATA%/NuGetPackageFixer/settings.json`)

```json
{
  "createBackup": true,
  "detailSplitRatio": 0.35
}
```

## Known Limitations

**Phase 1-5 (packages.config focus):**
- Content packages (F9): `install.ps1` is best-effort -- orphaned files from old version (e.g., renamed JS files) are not automatically removed
- packages.config restore requires VS's built-in NuGet or `nuget.exe` -- `dotnet restore` does not support packages.config

**All phases:**
- Authenticated feeds must be configured in VS or nuget.config (extension reads but does not manage credentials)
- Out-of-process extension cannot use in-process VS COM APIs directly -- restore and script execution use VS build commands or spawned processes (see F8 architecture note)
- No dependency on nuget.exe for scanning -- restore may use VS's built-in NuGet or spawned process

**Phase 6+ (PackageReference):**
- PackageReference outdated detection (Phase 6) overlaps with VS's built-in "Manage NuGet Packages" UI -- the value-add is the unified issue list with vulnerability/deprecation/inconsistency data in one view
- C3 Deprecated "Replace" action changes the package ID, not just the version -- may require API/code changes by the developer (informational, not fully automated)
- C7 Migration Readiness and C8 CPM Adoption are assessment/wizard features, not auto-fix actions

## Tech Stack

- Microsoft.VisualStudio.Extensibility SDK 17.14+
- Remote UI with XAML (themed, no DataGrid -- use ListView + DataTemplate)
- NuGet.Protocol + NuGet.Configuration (feed discovery, version queries, auth)
- System.Xml.Linq for packages.config parsing and patching
- VS 2022 17.14+ / VS 2026
- No external tool dependencies (no nuget.exe, no dotnet CLI)

## Scope: Beyond packages.config

To make the extension valuable long-term, it should address NuGet problems across **all project formats**, not just packages.config. The packages.config outdated-check is the first use case, but the same tool window can surface many other NuGet issues.

### NuGet Problem Categories

| # | Category | Affects | Detection | Fix |
|---|---|---|---|---|
| C1 | **Outdated Packages** | packages.config + PackageReference | Query feeds for newer versions | XML edit (pc) / `dotnet add package` (PR) |
| C2 | **Vulnerable Packages** | Both | `dotnet list package --vulnerable` or NuGet audit API (NU1901-NU1904) | Update to patched version |
| C3 | **Deprecated Packages** | Both | `dotnet list package --deprecated` or NuGet API metadata | Replace with suggested alternative |
| C4 | **Version Inconsistencies** | Multi-project solutions | Compare same PackageId across all projects | Consolidate to highest version |
| C5 | **Package Downgrades** | PackageReference | NU1605 warnings, `AllowDowngrade` / `NoWarn` in .csproj | Align versions, remove suppressions |
| C6 | **Unused Packages** | PackageReference | NU1510 pruning hints, unreferenced packages | Remove from project |
| C7 | **Migration Readiness** | packages.config | Assess blockers (content packages, build imports, non-SDK .csproj) | Report + guided migration |
| C8 | **CPM Adoption** | PackageReference | Detect solutions without `Directory.Packages.props` | Generate CPM files, move versions |
| C9 | **Orphaned References** | packages.config | .csproj has HintPath/Import for a package not in packages.config | Remove from .csproj or add to packages.config |

### UI Structure: Single Issues List with Category Filter

**Not** one tab per category. Instead, follow the VS Error List pattern:

- **One Issues list** showing all problems from all categories
- **Category filter** in toolbar (dropdown or toggle buttons): Outdated, Vulnerable, Deprecated, Inconsistent, etc.
- **Severity column**: Critical (vulnerable), Warning (outdated major), Info (outdated patch)
- **Same columns work for all categories**: Project, Package, Current, Latest/Suggested, Category, Severity, Source

This scales to new categories without UI changes -- adding a new problem type is just a new entry in the Category filter. See "UI Layout" section for the full mockups (Phase 1 and target).

**Why single list, not tabs:**
- Vulnerabilities and outdated packages often overlap (same package, two issues)
- User sees the full picture at a glance -- "how healthy is my solution?"
- Filter to one category when needed, but default view shows everything sorted by severity
- Adding new categories in future versions is just a code change, no UI restructure
- Consistent with VS Error List mental model (developers already know this pattern)

### Category-Specific Details

**C2: Vulnerable Packages**

The detail panel shows vulnerability-specific information:
- Advisory ID and link (GitHub Advisory Database)
- Severity (Low, Moderate, High, Critical)
- Fixed version (if available)
- Whether the vulnerability is in a direct or transitive dependency
- For transitive: which direct package pulls it in (`dotnet nuget why`)

**C3: Deprecated Packages**

- Deprecation reason (from NuGet API: Legacy, CriticalBugs, Other)
- Suggested replacement package (if provided by package author)
- "Replace" action in detail panel that updates the PackageReference/packages.config

**C4: Version Inconsistencies**

- Show which projects have which version of the same package
- "Consolidate" action: update all projects to the highest version
- Prerequisite for CPM migration

**C5: Package Downgrades (NU1605)**

- Show packages with `AllowDowngrade=true` or `NoWarn NU1605`
- Explain the risk: runtime `MissingMethodException`
- "Fix" action: align to higher version, remove suppression

**C6: Unused Packages (NU1510)**

- Packages that can be safely removed (pruning hints)
- "Remove" action: delete PackageReference from .csproj

**C7: Migration Readiness (packages.config only)**

- Assessment report: which packages have content files, build imports, install scripts
- Blocker classification: Hard (non-SDK .csproj), Soft (content packages), None (library-only)
- Not a fix action -- informational, helps user decide if migration is feasible

**C8: CPM Adoption (PackageReference only)**

- Detect solutions with inconsistent versions that would benefit from CPM
- "Generate Directory.Packages.props" action: extract all versions into central file
- Guided workflow (separate from the Issues list, since it's a one-time migration)

**C9: Orphaned References (packages.config only)**

The .csproj file contains HintPaths, `<Import>`, or `<Error>` guards referencing a package that has no corresponding entry in packages.config. This happens when a package was removed from packages.config but the .csproj was not fully cleaned up.

Real-world example discovered during the packages.config update spike:

| Package | Version in .csproj | Situation |
|---|---|---|
| Am.AsposeLib | 4.0.0-20260127-02 | No entry in packages.config |
| FluentFTP | 53.0.2 | No entry in packages.config |
| Microsoft.EntityFrameworkCore | 3.1.32 | No entry in packages.config |
| Microsoft.EntityFrameworkCore.Abstractions | 3.1.32 | No entry in packages.config |

**Detection:** Cross-reference all `packages\{id}.{version}` path tokens in .csproj against packages.config entries. Any .csproj reference without a matching packages.config entry is orphaned.

**Detail panel shows:**
- Package name and version from .csproj path
- Which .csproj elements reference it (HintPath, Import, Error guard)
- Whether the package folder still exists in `packages/`

**Actions:**
- "Remove from .csproj" -- delete all HintPath/Import/Error elements referencing this package (safe if the package is truly unused)
- "Add to packages.config" -- re-add the package entry (if the reference is intentional)
- Both actions require confirmation (potentially breaking)

**Severity:** Warning -- orphaned references don't break the build immediately (the `<Error Condition="!Exists(...)">` guard may fire, but only if the package folder is also missing)

### Format Support Matrix

| Category | packages.config | PackageReference | CPM |
|---|---|---|---|
| C1 Outdated | Phase 1 (core) | Phase 6 | Phase 6 |
| C2 Vulnerable | Phase 7 | Phase 7 | Phase 7 |
| C3 Deprecated | Phase 7 | Phase 7 | Phase 7 |
| C4 Inconsistencies | Phase 4 | Phase 6 | N/A (CPM prevents this) |
| C5 Downgrades | N/A | Phase 8 | Phase 8 |
| C6 Unused | N/A | Phase 8 | Phase 8 |
| C7 Migration | Phase 8 | N/A | N/A |
| C8 CPM Adoption | N/A | Phase 8 | N/A |
| C9 Orphaned Refs | Phase 4 | N/A | N/A |

## Development Phases

### Phase 1: Core Scanning (F1, F2, F3)

- packages.config parser
- NuGet.Protocol integration for all feed types
- Auto-discovery of sources from VS settings and nuget.config
- Version comparison engine with edge case handling (bogus, padding, major)
- Prerelease detection by version string
- Tool window with results list (single project)

### Phase 2: Update Operations (F4, F7, F8)

- XML-based version patching
- Backup creation
- NuGet restore via VS build system
- Post-update flow (automatic restore + re-scan)
- Output log panel

### Phase 3: Content Packages (F9)

- Content package detection (folder scan + known list)
- Run `install.ps1` from new version after restore
- PowerShell script execution from out-of-process extension

### Phase 4: Solution-Wide + Batch (F5, F6, C9)

- Multi-project scanning
- Cross-project version consistency check
- Orphaned reference detection (C9): .csproj references packages not in packages.config
- Filter + "Update Shown" batch pattern
- Solution-level filters (project, status, text)

### Phase 5: Configuration + Polish (F10)

- Configuration tab (NuGet sources, config files, extension settings)
- Background tab with educational content
- Feedback tab
- Settings persistence
- Marketplace publishing (unsigned initially, Azure Trusted Signing later)

### Phase 6: PackageReference Outdated (C1, C4)

- Extend scanning to PackageReference projects (use `NuGet.Protocol` same as packages.config)
- Version inconsistency detection across projects (C4)
- "Consolidate" action for inconsistent versions
- Update via `dotnet add package` or direct .csproj XML edit

### Phase 7: Security + Deprecated (C2, C3)

- Vulnerable package detection via NuGet audit API / GitHub Advisory Database
- Deprecated package detection via NuGet API metadata
- Severity column and sorting (Critical > Warning > Info)
- Advisory links in detail panel
- Replacement suggestions for deprecated packages

### Phase 8: Advanced (C5, C6, C7, C8)

- Package downgrade detection (NU1605, AllowDowngrade removal)
- Unused package detection (NU1510 pruning)
- packages.config migration readiness assessment
- CPM adoption wizard (Directory.Packages.props generation)

## Resolved Spike Questions

The following questions were answered during the spike/PoC (2026-03-27):

| Question | Answer | Implementation |
|---|---|---|
| **Credentials for authenticated feeds (OOP)** | NuGet.Protocol alone returns 401. Fix: detect Azure DevOps feeds, spawn `CredentialProvider.Microsoft.exe` from VS installation to obtain token, set as `PackageSourceCredential`. | `NuGetSourceProvider.TryGetAuthenticatedSourceAsync()` |
| **Solution path in OOP extension** | `Extensibility.Workspaces().QueryProjectsAsync(q => q.With(p => p.Name).With(p => p.Path))` works correctly. Found 30 projects in test solution. | `NuGetPackageFixerToolWindowViewModel.ExecuteAnalyseAsync()` |
| **Parallel feed queries** | `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 5`, combined with `SemaphoreSlim(10)` throttle on HTTP requests in `NuGetSourceProvider`. 223 packages scanned in acceptable time. | `NuGetPackageFixerToolWindowViewModel` + `NuGetSourceProvider` |

## Open Questions (Remaining)

### Restore Mechanism for packages.config (Phase 2)

**Question:** `dotnet restore` does not support packages.config. How do we trigger a restore for packages.config projects from an OOP extension without depending on nuget.exe being installed?

**Options:**
1. Trigger VS build via `project.BuildAsync()` (which runs NuGet restore as part of the build pipeline)
2. Spawn `nuget.exe restore` (requires nuget.exe to be installed)
3. Show guidance: "Run NuGet restore in VS" (manual step)

**Status:** Not yet tested. Will be resolved when implementing update functionality (Phase 2).

### PowerShell Script Execution from OOP (F9, Phase 3)

**Question:** Can we run `install.ps1` from an OOP extension? Options: VS PowerShell host, spawned PowerShell process, or skip entirely.

**Decision point:** Content packages (jQuery, Modernizr) are a declining scenario. Most of these packages are no longer actively updated. The cost of implementing and testing PowerShell execution from OOP may exceed the benefit.

**Recommendation:** Defer to Phase 3. For Phase 1-2, show a warning for content packages and let the user handle content files manually. Re-evaluate when Phase 3 is planned.

## Implementation Status (2026-03-29)

### What is implemented

| Feature | Detection | Fix | Tests |
|---|---|---|---|
| C1 Outdated (packages.config) | Yes | Yes (XML edit + csproj sync + assembly version) | 10 |
| C1 Outdated (PackageReference) | Yes | Detection only (update not yet supported) | 7 |
| C2 Vulnerable | Yes (NuGet Metadata API) | Detection only | 2 |
| C3 Deprecated | Yes (NuGet Metadata API) | Detection only | 1 |
| C4 Inconsistent | Yes (cross-project, stable-vs-prerelease filtered) | Yes (consolidate to highest) | 5 |
| C7 Migration Readiness | Yes (5 blocker types) | Detection only | 6 |
| C9 Orphaned References | Yes (HintPath, Import, Error guards) | Yes (remove from .csproj) | 10 |
| F9 Content Package Detection | Yes (folder scan + known list) | Detection only | 5 |
| Prerelease-Mismatch | Yes (pc prerelease vs pr stable, VS bug documented) | Yes (downgrade to stable) | - |
| Version Comparison | All edge cases (bogus, zero, padding, major, prerelease suffix) | - | 10 |
| NuGet Source Discovery | nuget.config hierarchy + VS settings | - | 3 |
| Feed Queries | Parallel, deduplicated by (id, isPrerelease), Azure Artifacts credential provider | - | 5 |
| Solution Monitor | 2-pass debounce (10s stabilization) | - | - |
| Cancel Analyse | Fire-and-forget scan + CancellationTokenSource | - | - |
| VS Output Window | Dedicated "NuGet Package Fixer" channel | - | - |
| Context Menu | "Analyse NuGet Packages" on project right-click | - | - |
| **Total tests** | | | **76** |

### UI Features implemented

- 4 tabs: Package Issues, Configuration, Background, Feedback
- VS theming (EnvironmentColors for all controls)
- Column sorting (click anywhere on header)
- Filters: Project, Category, text search with clear button
- Detail panel with packages.config/csproj XML entry
- Context-aware button labels: "Update to X", "Consolidate to X", "Downgrade to Stable X", "Remove from .csproj", "No auto-fix available"
- Fixed items: checkmark icon, "DONE" suffix, "Fixed" category/severity
- Analyse/Cancel Analyse/Re-Analyse button toggle
- Update Shown: batch-apply safe fixes (patch + prerelease only)
- Backup checkbox (timestamped .bak files)
- Status bar with scan progress ("Querying 50 of 159: MailKit")

### What is NOT implemented

- PackageReference update (fix) -- Phase 6
- C5 Package Downgrades (NU1605) -- Phase 8
- C6 Unused Packages (NU1510) -- Phase 8
- C8 CPM Adoption wizard -- Phase 8
- Content package install.ps1 execution -- Phase 3
- Marketplace publishing -- needs README, CHANGELOG, icon

### Repository

- GitHub: https://github.com/ardimedia-com/visualstudio-nuget-fixer
- Initial commit: 2026-03-29
- 39 source files, 6396 lines
- Solution: `src/nugetpackagefixer.slnx`

## Boilerplate Reference

Use `visualstudio-binding-redirect-fixer` as the architectural template:

- Extension entry point: `NuGetPackageFixerExtension : Extension`
- Command: `ScanCommand` in Tools menu + `ScanProjectCommand` in Solution Explorer context menu
- Tool window: `NuGetPackageFixerToolWindow` with RemoteUserControl
- ViewModel: DataContract-decorated, ObservableList, IAsyncCommand
- Themed XAML: DynamicResource VS EnvironmentColors, no DataTriggers
- Services: static classes for pure logic, instance classes for stateful (NuGetSourceProvider, PackageMetadataService)
- VS Output Window: `OutputChannel` via `Extensibility.Views().Output`
