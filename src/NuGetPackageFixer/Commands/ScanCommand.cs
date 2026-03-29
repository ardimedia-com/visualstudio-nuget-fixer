namespace NuGetPackageFixer.Commands;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

using NuGetPackageFixer.ToolWindows;

/// <summary>
/// Command that opens the NuGet Package Fixer tool window.
/// Placed in the Tools menu.
/// </summary>
[VisualStudioContribution]
public class ScanCommand : Command
{
    /// <inheritdoc />
    #pragma warning disable CEE0027 // String not localized
    public override CommandConfiguration CommandConfiguration => new("NuGet Package Fixer")
    {
        TooltipText = "Scan all projects for outdated, vulnerable, or deprecated NuGet packages",
        Placements = [CommandPlacement.KnownPlacements.ToolsMenu],
    };

    public ScanCommand(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
    }

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await this.Extensibility.Shell().ShowToolWindowAsync<NuGetPackageFixerToolWindow>(
            activate: true, cancellationToken);
    }
}
