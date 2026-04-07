# NuGet Package Fixer

[![Build](https://github.com/ardimedia-com/visualstudio-nuget-fixer/actions/workflows/build.yml/badge.svg)](https://github.com/ardimedia-com/visualstudio-nuget-fixer/actions/workflows/build.yml)
[![Release](https://github.com/ardimedia-com/visualstudio-nuget-fixer/actions/workflows/release.yml/badge.svg)](https://github.com/ardimedia-com/visualstudio-nuget-fixer/actions/workflows/release.yml)
[![Visual Studio Marketplace](https://img.shields.io/visual-studio-marketplace/v/Ardimedia.NuGetPackageFixer.svg)](https://marketplace.visualstudio.com/items?itemName=Ardimedia.NuGetPackageFixer)
[![Visual Studio Marketplace Downloads](https://img.shields.io/visual-studio-marketplace/d/Ardimedia.NuGetPackageFixer.svg)](https://marketplace.visualstudio.com/items?itemName=Ardimedia.NuGetPackageFixer)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A Visual Studio 2022/2026 extension that scans your solution for NuGet package issues -- outdated, vulnerable, deprecated, orphaned, and inconsistent packages -- across both `packages.config` and PackageReference projects.

> **Note:** This extension is not yet fully implemented. If you are interested, please [open an issue](https://github.com/ardimedia-com/visualstudio-nuget-fixer/issues) to let us know.

## The Problem

Managing NuGet packages in .NET solutions has multiple pain points:

- **`dotnet list package --outdated`** does not work with `packages.config` projects
- **`nuget.exe update`** fails when any dependency is a prerelease package (resolves the entire dependency chain for every update)
- **Vulnerable packages** show as warnings but VS does not help fix them
- **Orphaned references** in `.csproj` files cause build failures after package removals
- **Version inconsistencies** across projects cause subtle runtime errors

This extension addresses all of these in a single tool window.

## Features

- **Multi-Source Scanning** -- queries all configured NuGet feeds (nuget.org, Azure Artifacts, GitHub Packages, etc.) automatically from VS settings and `nuget.config`
- **Both Package Formats** -- scans `packages.config` and PackageReference projects in the same solution
- **Prerelease-Aware** -- detects prerelease versions by the version string (no prefix configuration needed)
- **Vulnerability Detection** -- flags packages with known security vulnerabilities via the NuGet API
- **Deprecation Detection** -- identifies packages deprecated by their authors, with replacement suggestions
- **Orphaned Reference Detection** -- finds `.csproj` references (HintPath, Import, Error guards) for packages not in `packages.config`
- **Version Consistency Check** -- detects the same package at different versions across projects
- **Migration Readiness Assessment** -- evaluates whether `packages.config` projects can migrate to PackageReference
- **Content Package Detection** -- flags packages with JS/CSS content files that need special handling
- **XML-Based Updates** -- bypasses `nuget.exe update` entirely, editing `packages.config` and `.csproj` directly
- **Assembly Version Sync** -- reads actual assembly versions from restored DLLs and updates `<Reference Include>` attributes
- **Orphaned Reference Removal** -- cleans up stale HintPath, Import, and Error guard entries from `.csproj`
- **Column Sorting** -- click any column header to sort ascending/descending
- **VS Output Window** -- detailed scan logs in a dedicated "NuGet Package Fixer" output channel
- **Solution Monitor** -- automatically scans when a solution is opened or changed
- **Cancel Support** -- cancel a running scan at any time
- **Theme-Aware** -- fully adapts to Light, Dark, Blue, and High Contrast themes
- **Non-Destructive** -- creates timestamped backups before modifying any file

## Installation

### From VSIX File

1. Download the `.vsix` file from [Releases](https://github.com/ardimedia-com/visualstudio-nuget-fixer/releases)
2. Double-click the file to install, or use **Extensions** > **Manage Extensions** > **Install from File**

## Usage

1. Open a solution containing .NET projects
2. Go to **Tools** > **NuGet Package Fixer**
3. The tool window opens and automatically scans your solution
4. Review the detected issues, filtered by Category, Project, or text search
5. Click individual items to see details and apply fixes
6. Use **Update Shown** to batch-fix all safe updates (patch/prerelease only, MAJOR excluded)

## Issue Categories

| Category | Description | Auto-Fix |
|---|---|---|
| **Outdated** | Newer version available on NuGet feeds | Yes (packages.config) |
| **Vulnerable** | Known security vulnerability (NuGet API) | Detection only |
| **Deprecated** | Package deprecated by author | Detection only |
| **Orphaned** | `.csproj` references package not in `packages.config` | Yes (remove from .csproj) |
| **Inconsistent** | Same package at different versions across projects | Yes (consolidate) |
| **MigrationReady** | Assessment of `packages.config` to PackageReference migration blockers | Detection only |
| **Obsolete packages.config** | `packages.config` in SDK-style projects (.NET 5+) — file is ignored by the SDK | Detection only |

## How It Works

### Scanning

The extension reads package information from two sources:

| Format | Source | What It Reads |
|---|---|---|
| `packages.config` | XML file in project directory | Package ID, version, target framework |
| PackageReference | `.csproj` file | `<PackageReference Include="..." Version="..."/>` |

It then queries all configured NuGet feeds in parallel for the latest versions. Prerelease packages are detected by the version string (contains `-`) and searched with prerelease included.

### Fixing (packages.config only)

Updates follow a multi-phase approach:

1. **XML edit** -- update version in `packages.config` + path tokens in `.csproj`
2. **NuGet restore** -- trigger via VS build system
3. **Assembly version sync** -- read actual assembly version from restored DLL, update `<Reference Include>` attribute

This bypasses `nuget.exe update` and its broken dependency resolution entirely.

### Version Comparison Edge Cases

| Edge Case | Handling |
|---|---|
| Bogus versions on nuget.org (e.g., 2010.x) | Detected and skipped |
| Zero versions | Detected and skipped |
| Version format padding (1.0 vs 1.0.0) | Normalized before comparison |
| Major version bumps | Flagged as MAJOR, excluded from batch updates |
| Stable vs prerelease mismatch | Not flagged as inconsistent (intentional) |

## Tabs

| Tab | Content |
|---|---|
| **Package Issues** | Main issue list with filters, detail panel, and fix actions |
| **Configuration** | Shows configured NuGet sources and `nuget.config` file paths |
| **Background** | Educational content explaining the XML-edit approach, update types, and known VS issues |
| **Feedback** | GitHub issue link and extension info |

## Requirements

- Visual Studio 2022 17.14+ or Visual Studio 2026
- .NET Framework or .NET projects with NuGet packages
- NuGet feeds configured in VS Package Manager Settings or `nuget.config`

## Tech Stack

- Microsoft.VisualStudio.Extensibility SDK 17.14+ (out-of-process)
- .NET 10
- NuGet.Protocol + NuGet.Configuration
- Remote UI with XAML (themed)
- 76 tests (MSTest)

## License

MIT
