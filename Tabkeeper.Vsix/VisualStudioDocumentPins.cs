using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Tabkeeper.Vsix;

/// <summary>
/// Encapsulates Visual Studio document-frame operations used to inspect and change pin state.
/// </summary>
internal sealed class VisualStudioDocumentPins
{
    private readonly IVsUIShell uiShell;
    private readonly IVsUIShellOpenDocument openDocument;
    private readonly DTE dte;

    /// <summary>
    /// Initializes document-frame access using Visual Studio shell and automation services.
    /// </summary>
    /// <param name="uiShell">The shell service that enumerates document frames and displays messages.</param>
    /// <param name="openDocument">The service that opens documents through their owning project.</param>
    /// <param name="dte">The automation service used to capture and restore focus.</param>
    public VisualStudioDocumentPins(IVsUIShell uiShell, IVsUIShellOpenDocument openDocument, DTE dte)
    {
        this.uiShell = uiShell;
        this.openDocument = openDocument;
        this.dte = dte;
    }

    /// <summary>
    /// Enumerates open document frames whose physical paths are descendants of a root directory.
    /// </summary>
    /// <param name="rootPath">The repository or solution-directory scope to filter by.</param>
    /// <returns>Open document frames associated with files inside the specified root.</returns>
    public IEnumerable<OpenDocumentFrame> GetOpenDocuments(string rootPath)
    {
        // The shell exposes document frames as UI-thread-affine COM objects.
        ThreadHelper.ThrowIfNotOnUIThread();

        // Normalize the scope root once instead of per frame.
        string normalizedRoot = AppendDirectorySeparator(Path.GetFullPath(rootPath));
        ErrorHandler.ThrowOnFailure(uiShell.GetDocumentWindowEnum(out IEnumWindowFrames windowFrames));
        IVsWindowFrame[] nextFrame = new IVsWindowFrame[1];
        while (windowFrames.Next(1, nextFrame, out uint fetched) == VSConstants.S_OK && fetched == 1)
        {
            IVsWindowFrame frame = nextFrame[0];
            nextFrame[0] = null!;
            if (frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out object documentPathValue) != VSConstants.S_OK ||
                !(documentPathValue is string documentPath))
            {
                continue;
            }

            string fullDocumentPath = Path.GetFullPath(documentPath);
            if (fullDocumentPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield return new OpenDocumentFrame(frame, fullDocumentPath);
            }
        }
    }

    /// <summary>
    /// Clears the pinned state of every currently open document inside a root directory.
    /// </summary>
    /// <param name="rootPath">The repository or solution-directory scope to unpin.</param>
    public void UnpinAll(string rootPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (OpenDocumentFrame document in GetOpenDocuments(rootPath))
        {
            SetPinned(document.Frame, false);
        }
    }

    /// <summary>
    /// Opens a file, trying the project-aware document service first and then the standard editor
    /// so that repository files outside any loaded project (documentation, configuration, files from
    /// sibling solutions) can still be restored.
    /// </summary>
    /// <param name="fullPath">The absolute path of the existing file to open.</param>
    /// <returns>The resulting document frame, or <see langword="null"/> when Visual Studio cannot open it.</returns>
    public IVsWindowFrame? OpenDocument(string fullPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Guid logicalView = VSConstants.LOGVIEWID_Primary;
        int viaProjectResult = openDocument.OpenDocumentViaProject(
            fullPath,
            ref logicalView,
            out Microsoft.VisualStudio.OLE.Interop.IServiceProvider serviceProvider,
            out IVsUIHierarchy hierarchy,
            out uint itemId,
            out IVsWindowFrame windowFrame);
        if (ErrorHandler.Succeeded(viaProjectResult) && windowFrame is not null)
        {
            return windowFrame;
        }

        logicalView = VSConstants.LOGVIEWID_Primary;
        int viaStandardEditorResult = openDocument.OpenStandardEditor(
            (uint)__VSOSEFLAGS.OSE_ChooseBestStdEditor,
            fullPath,
            ref logicalView,
            Path.GetFileName(fullPath),
            null,
            VSConstants.VSITEMID_NIL,
            new IntPtr(-1),
            null,
            out IVsWindowFrame standardEditorFrame);
        return ErrorHandler.Succeeded(viaStandardEditorResult) ? standardEditorFrame : null;
    }

    /// <summary>
    /// Gets the physical path of the document active before a restore operation.
    /// </summary>
    /// <returns>The active document path, or <see langword="null"/> when no document is active.</returns>
    public string? GetActiveDocumentPath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return dte.ActiveDocument?.FullName;
    }

    /// <summary>
    /// Restores focus to a previously active document when it still exists.
    /// </summary>
    /// <param name="documentPath">The optional path captured before restoring pinned tabs.</param>
    public void RestoreActiveDocument(string? documentPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!string.IsNullOrEmpty(documentPath) && File.Exists(documentPath))
        {
            // Opening saved tabs can activate the last restored document; restore the user's prior focus afterward.
            dte.ItemOperations.OpenFile(documentPath);
        }
    }

    /// <summary>
    /// Shows the configured missing-file warning in Visual Studio.
    /// </summary>
    /// <param name="relativePath">The repository-relative saved path that could not be restored.</param>
    public void ShowMissingFileMessage(string relativePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Guid callerGuid = TabkeeperCommands.CommandSet;
        uiShell.ShowMessageBox(
            0,
            ref callerGuid,
            "Tabkeeper",
            $"The saved pinned file '{relativePath}' does not exist on this branch.",
            null,
            0,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
            OLEMSGICON.OLEMSGICON_WARNING,
            0,
            out int result);
    }

    /// <summary>
    /// Reads the shell pin-state property for a document frame.
    /// </summary>
    /// <param name="frame">The document frame to inspect.</param>
    /// <returns><see langword="true"/> when the frame is pinned; otherwise <see langword="false"/>.</returns>
    public static bool IsPinned(IVsWindowFrame frame)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return frame.GetProperty((int)__VSFPROPID5.VSFPROPID_IsPinned, out object pinnedValue) == VSConstants.S_OK &&
            pinnedValue is not null && Convert.ToBoolean(pinnedValue, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Sets the shell pin-state property for a document frame.
    /// </summary>
    /// <param name="frame">The document frame to update.</param>
    /// <param name="isPinned">The desired pinned state.</param>
    public static void SetPinned(IVsWindowFrame frame, bool isPinned)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ErrorHandler.ThrowOnFailure(frame.SetProperty((int)__VSFPROPID5.VSFPROPID_IsPinned, isPinned));
    }

    /// <summary>
    /// Converts an absolute descendant path into a repository-relative path for persistence.
    /// </summary>
    /// <param name="rootPath">The repository root used as the relative-path base.</param>
    /// <param name="path">The absolute document path to convert.</param>
    /// <returns>A normalized platform-relative path.</returns>
    public static string MakeRelativePath(string rootPath, string path)
    {
        Uri rootUri = new Uri(AppendDirectorySeparator(rootPath));
        string relativePath = Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(path)).ToString());
        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Determines whether an absolute or relative path is a descendant of a root directory.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <param name="rootPath">The candidate containing directory.</param>
    /// <returns><see langword="true"/> when the path is inside the root; otherwise <see langword="false"/>.</returns>
    public static bool IsPathInRoot(string path, string rootPath)
    {
        string fullPath = Path.GetFullPath(path);
        string normalizedRoot = AppendDirectorySeparator(Path.GetFullPath(rootPath));
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares normalized filesystem paths using Windows case-insensitive semantics.
    /// </summary>
    /// <param name="first">The first path.</param>
    /// <param name="second">The second path.</param>
    /// <returns><see langword="true"/> when the normalized paths refer to the same location.</returns>
    public static bool PathsEqual(string first, string second)
    {
        return string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}

/// <summary>
/// Couples a Visual Studio document frame with its normalized physical file path. A value type so
/// enumerating open documents does not allocate one object per frame.
/// </summary>
internal readonly struct OpenDocumentFrame
{
    /// <summary>
    /// Initializes an open document frame record.
    /// </summary>
    /// <param name="frame">The Visual Studio document frame.</param>
    /// <param name="path">The normalized physical document path.</param>
    public OpenDocumentFrame(IVsWindowFrame frame, string path)
    {
        Frame = frame;
        Path = path;
    }

    public IVsWindowFrame Frame { get; }

    public string Path { get; }
}
