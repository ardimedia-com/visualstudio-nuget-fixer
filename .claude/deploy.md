---
status: Stable
updated: 2026-03-29 16:00h
---

# Publishing to the Visual Studio Marketplace

This extension uses the **VisualStudio.Extensibility SDK** (out-of-proc model) and requires **VS 2022 17.14+**.

## 1. Create a Publisher Account

- Go to the [Visual Studio Marketplace Publishing Portal](https://marketplace.visualstudio.com/manage)
- Sign in with a Microsoft account
- Create a **publisher** (e.g., `Ardimedia`) -- this is your publisher ID

## 2. Create a Personal Access Token (PAT)

- Go to `dev.azure.com` > User Settings > Personal Access Tokens
- Create a token with scope **Marketplace > Manage**
- Save the token securely -- you will need it for publishing
- Add as GitHub secret `VS_MARKETPLACE_PAT` in the repo settings

## 3. Build the VSIX

```bash
dotnet build src/nugetpackagefixer.slnx -c Release
```

This produces a `.vsix` file in `src/NuGetPackageFixer/bin/Release/net10.0/`.

## 4. Publish

### Option A: Automated via GitHub Actions (recommended)

1. Bump the `<Version>` in `NuGetPackageFixer.csproj`
2. Update `CHANGELOG.md`
3. Commit and tag: `git tag v0.1.1 && git push --tags`
4. GitHub Actions builds, tests, creates a GitHub Release with the VSIX, and publishes to VS Marketplace

### Option B: Manual Upload

- Go to [marketplace.visualstudio.com/manage](https://marketplace.visualstudio.com/manage)
- Click **New Extension** > **Visual Studio**
- Upload the `.vsix` file

## 5. Required Metadata

Ensure the extension includes:

- **README.md** -- used as the marketplace description page
- **Icon** (128x128 PNG recommended) -- set via `<Icon>` in the `.csproj`
- **LICENSE** file
- Version, description, and tags in the `.csproj` (already configured)

## 6. Updating an Existing Extension

To publish an update:

1. Bump the `<Version>` in `NuGetPackageFixer.csproj`
2. Update `CHANGELOG.md`
3. Commit, tag with `v{version}`, push tags
4. GitHub Actions handles the rest

## 7. Required GitHub Secrets

| Secret | Purpose |
|---|---|
| `VS_MARKETPLACE_PAT` | Personal Access Token for VS Marketplace publishing |
| `AZURE_TENANT_ID` | Azure Trusted Signing (when enabled) |
| `AZURE_CLIENT_ID` | Azure Trusted Signing (when enabled) |
| `AZURE_CLIENT_SECRET` | Azure Trusted Signing (when enabled) |
