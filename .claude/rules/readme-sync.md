---
status: Stable
updated: 2026-03-29 16:00h
globs: src/NuGetPackageFixer/**
---

# README Sync Rule

When adding, removing, or renaming issue categories (IssueCategory enum), features, or fix actions:

1. Update `README.md` to reflect the change:
   - **Issue Categories table** -- must list every IssueCategory with correct description and auto-fix
   - **Features section** -- must mention all user-visible capabilities
2. Update `CHANGELOG.md` in the next release

Checklist before publishing a release:
- [ ] All `IssueCategory` enum values are documented in README > Issue Categories
- [ ] All fix actions (Update, Consolidate, Remove, Downgrade) are reflected in the auto-fix column
- [ ] New user-visible features are listed in README > Features
- [ ] Version bumped in `NuGetPackageFixer.csproj`
