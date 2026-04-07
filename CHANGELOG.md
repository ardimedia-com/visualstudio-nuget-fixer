# Changelog

All notable changes to the NuGet Package Fixer extension will be documented in this file.

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
