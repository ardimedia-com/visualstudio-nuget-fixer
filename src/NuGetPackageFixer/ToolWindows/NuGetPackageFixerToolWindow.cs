namespace NuGetPackageFixer.ToolWindows;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

/// <summary>
/// Tool window provider for the NuGet Package Fixer panel.
/// </summary>
[VisualStudioContribution]
public class NuGetPackageFixerToolWindow : ToolWindow
{
    private NuGetPackageFixerToolWindowViewModel? _viewModel;

    public NuGetPackageFixerToolWindow(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
        this.Title = "NuGet Package Fixer";
    }

    /// <inheritdoc />
    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
    };

    /// <inheritdoc />
    public override async Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        _viewModel = new NuGetPackageFixerToolWindowViewModel(this.Extensibility);

        _ = _viewModel.RunInitialAnalysisAsync(cancellationToken);

        return new NuGetPackageFixerToolWindowControl(_viewModel);
    }

    /// <inheritdoc />
    public override Task OnHideAsync(CancellationToken cancellationToken)
    {
        _viewModel?.ClearData();
        return base.OnHideAsync(cancellationToken);
    }
}
