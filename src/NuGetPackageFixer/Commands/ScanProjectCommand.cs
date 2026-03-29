namespace NuGetPackageFixer.Commands;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.ProjectSystem.Query;

using NuGetPackageFixer.ToolWindows;

/// <summary>
/// Context menu command on projects in Solution Explorer.
/// Right-click a .csproj → "Analyse NuGet Packages" → opens the tool window
/// and scans only that project.
/// </summary>
[VisualStudioContribution]
public class ScanProjectCommand : Command
{
    /// <inheritdoc />
    #pragma warning disable CEE0027 // String not localized
    public override CommandConfiguration CommandConfiguration => new("Analyse NuGet Packages")
    {
        TooltipText = "Scan this project for outdated, vulnerable, or deprecated NuGet packages",
        Placements =
        [
            // Project context menu in Solution Explorer
            CommandPlacement.VsctParent(
                new Guid("d309f791-903f-11d0-9efc-00a0c911004f"),
                id: 518,
                priority: 0x0100),
        ],
    };

    public ScanProjectCommand(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
    }

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        // Show the tool window
        await this.Extensibility.Shell().ShowToolWindowAsync<NuGetPackageFixerToolWindow>(
            activate: true, cancellationToken);
    }
}
