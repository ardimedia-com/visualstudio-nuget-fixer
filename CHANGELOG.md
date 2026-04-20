# Changelog

All notable changes to the NuGet Package Fixer extension will be documented in this file.

## [0.1.10] - 2026-04-20

### Added

- **Limited PackageReference auto-fix**: Outdated PackageReference packages can now be updated automatically when safe to do so
  - Supported: `Version="x.y.z"` (attribute form) and `<Version>x.y.z</Version>` (child element form)
  - Skipped with clear reasons: Central Package Management (CPM), conditional references (`Condition` on element or `ItemGroup`), floating versions (`*`), and `VersionOverride` attribute/element
  - CPM detection: checks for `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>` in `.csproj` (explicit `false` is honored as opt-out) or `Directory.Packages.props` anywhere from project directory up to drive root
  - Update flow: backs up `.csproj`, updates version in place, triggers NuGet restore via VS build system
  - No assembly-version sync for PackageReference (not applicable)
  - MAJOR updates still require individual action via detail panel (same as `packages.config`)
  - "Update Shown" batch-fix now includes fixable PackageReference items (non-MAJOR, non-CPM, non-conditional, non-floating, non-VersionOverride)
  - Detail panel shows specific skip reason for each unsupported case

### Fixed

- **Dark theme readability**: Column headers (including hover/pressed states) and selected item now use VS theme colors for Dark, Blue, and High Contrast themes
- **Status text**: Clarified "2 pc, 17 pr" to "2 packages.config, 17 PackageReference"
- **Button label**: Renamed "Re-Analyse" to "Analyse"

## [0.1.7] - 2026-04-07

### Added

- **Obsolete packages.config detection**: Flags `packages.config` files in SDK-style projects targeting .NET 5+ as obsolete
- **Remove packages.config**: "Remove file: packages.config" button deletes obsolete files (with backup if enabled)
- **Multi-target awareness**: Only flags `packages.config` as obsolete when ALL targets are modern .NET — keeps it if any .NET Framework target exists

### Fixed

- **GitHub URLs**: Corrected all URLs from `ardimedia/` to `ardimedia-com/`
- **Extension name in feedback**: Feedback body now includes "NuGet Package Fixer" in Extension Info

## [0.1.4] - 2026-04-06

### Fixed

- **Project configuration**: Removed legacy VSSDK properties to match official VS Extensibility SDK samples

## [0.1.3] - 2026-04-06

### Fixed

- **Project configuration**: Removed legacy VSSDK properties, added PrivateAssets on SDK packages

## [0.1.2] - 2026-04-05

### Fixed

- **Stale NuGet cache**: Reset SourceCacheContext on each analysis to ensure fresh feed data when re-analysing or switching solutions

## [0.1.1] - 2026-04-04

### Changed

- **Feedback Tab**: Full GitHub issue form with Bug/Feature toggle, title with BUG:/FEATURE: prefix, description pre-filled with version info
- **DeployExtension**: Added debug deployment to experimental instance

## [0.1.0] - 2026-04-04

### Added

- Initial release
- Multi-source NuGet scanning (packages.config + PackageReference)
- Outdated, vulnerable, deprecated, orphaned, and inconsistent package detection
- XML-based package updates (bypasses nuget.exe)
- Assembly version sync after updates
- Orphaned reference removal
- Version consistency analysis
- Column sorting, project/category filtering
- Feedback tab with GitHub issue form
- Background tab with extension info
- Theme-aware UI (Light, Dark, Blue, High Contrast)
