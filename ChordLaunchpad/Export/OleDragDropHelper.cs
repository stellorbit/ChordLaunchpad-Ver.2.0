using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace ChordLaunchpad.Export;

/// <summary>
/// DAWのトラックへ直接 .mid ファイルをドロップするための Win32 OLE DragDrop ヘルパー
/// </summary>
public static class OleDragDropHelper
{
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int DoDragDrop(
        System.Runtime.InteropServices.ComTypes.IDataObject pDataObj,
        IDropSource pDropSource,
        int dwOKEffects,
        out int pdwEffect);

    [ComImport]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag(int fEscapePressed, int grfKeyState);

        [PreserveSig]
        int GiveFeedback(int dwEffect);
    }

    private sealed class SimpleDropSource : IDropSource
    {
        public int QueryContinueDrag(int fEscapePressed, int grfKeyState)
        {
            if (fEscapePressed != 0) return 0x00040101; // DRAGDROP_S_CANCEL
            if ((grfKeyState & 1) == 0 && (grfKeyState & 2) == 0) // LButton or RButton released
            {
                return 0x00040100; // DRAGDROP_S_DROP
            }
            return 0; // S_OK
        }

        public int GiveFeedback(int dwEffect)
        {
            return 0x00040102; // DRAGDROP_S_USEDEFAULTCURSORS
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHCreateDataObject(
        IntPtr pidlFolder,
        uint cidl,
        IntPtr apidl,
        System.Runtime.InteropServices.ComTypes.IDataObject? pdtInner,
        ref Guid riid,
        out System.Runtime.InteropServices.ComTypes.IDataObject ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPath(string pszPath);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    /// <summary>
    /// ファイルパスを指定して OLE Drag & Drop (CF_HDROP) を同期的に開始
    /// </summary>
    public static int StartFileDrag(string filePath)
    {
        var pidl = ILCreateFromPath(filePath);
        if (pidl == IntPtr.Zero) return -1;

        try
        {
            var iid = new Guid("0000010e-0000-0000-C000-000000000046"); // IID_IDataObject
            var hr = SHCreateDataObject(IntPtr.Zero, 1, pidl, null, ref iid, out var dataObject);
            if (hr != 0 || dataObject == null) return hr;

            var dropSource = new SimpleDropSource();
            const int DROPEFFECT_COPY = 1;
            const int DROPEFFECT_MOVE = 2;

            return DoDragDrop(dataObject, dropSource, DROPEFFECT_COPY | DROPEFFECT_MOVE, out _);
        }
        finally
        {
            ILFree(pidl);
        }
    }
}
