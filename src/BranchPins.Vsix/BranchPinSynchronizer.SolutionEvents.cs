using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace BranchPins.Vsix;

/// <summary>
/// Persists the outgoing branch state and resets monitoring as solutions change.
/// </summary>
internal sealed partial class BranchPinSynchronizer
{
    int IVsSolutionEvents.OnAfterOpenProject(IVsHierarchy hierarchy, int added) => VSConstants.S_OK;

    int IVsSolutionEvents.OnQueryCloseProject(IVsHierarchy hierarchy, int removing, ref int cancel) => VSConstants.S_OK;

    int IVsSolutionEvents.OnBeforeCloseProject(IVsHierarchy hierarchy, int removing) => VSConstants.S_OK;

    int IVsSolutionEvents.OnAfterLoadProject(IVsHierarchy stubHierarchy, IVsHierarchy realHierarchy) => VSConstants.S_OK;

    int IVsSolutionEvents.OnQueryUnloadProject(IVsHierarchy realHierarchy, ref int cancel) => VSConstants.S_OK;

    int IVsSolutionEvents.OnBeforeUnloadProject(IVsHierarchy realHierarchy, IVsHierarchy stubHierarchy) => VSConstants.S_OK;

    int IVsSolutionEvents.OnAfterOpenSolution(object reserved, int newSolution)
    {
        QueueSynchronization();
        return VSConstants.S_OK;
    }

    int IVsSolutionEvents.OnQueryCloseSolution(object reserved, ref int cancel) => VSConstants.S_OK;

    int IVsSolutionEvents.OnBeforeCloseSolution(object reserved)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!disposed && currentRepository is not null)
        {
            SaveCurrentPins(currentRepository);
        }

        return VSConstants.S_OK;
    }

    int IVsSolutionEvents.OnAfterCloseSolution(object reserved)
    {
        currentRepository = null;
        if (headWatcher is not null)
        {
            headWatcher.EnableRaisingEvents = false;
            headWatcher.Dispose();
            headWatcher = null;
        }

        return VSConstants.S_OK;
    }
}
