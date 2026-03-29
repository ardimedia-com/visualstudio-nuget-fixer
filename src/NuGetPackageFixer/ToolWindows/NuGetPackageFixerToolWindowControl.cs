namespace NuGetPackageFixer.ToolWindows;

using Microsoft.VisualStudio.Extensibility.UI;

/// <summary>
/// Remote UI control for the NuGet Package Fixer tool window.
/// </summary>
public class NuGetPackageFixerToolWindowControl : RemoteUserControl
{
    public NuGetPackageFixerToolWindowControl(object? dataContext)
        : base(dataContext)
    {
    }
}
