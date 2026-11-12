using System;
using System.Runtime.InteropServices;

namespace IndustrialConveyor.Simulation.HMI
{
    /// <summary>Shows the native Windows "Open file" dialog; unavailable on other platforms.</summary>
    public static class NativeFileDialog
    {
        /// <summary>Gets whether a native dialog exists on this platform.</summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Asks the user for a track workbook. Returns null when cancelled.</summary>
        public static string PickTrackFile(string initialPath)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var dialog = new OpenFileName();
            dialog.structSize = Marshal.SizeOf(dialog);
            dialog.owner = GetActiveWindow();
            dialog.filter = "Excel track files (*.xlsm;*.xlsx)\0*.xlsm;*.xlsx\0All files\0*.*\0\0";
            dialog.file = new string('\0', 1024);
            if (!string.IsNullOrEmpty(initialPath))
                dialog.file = initialPath + new string('\0', Math.Max(1, 1024 - initialPath.Length));
            dialog.maxFile = dialog.file.Length;
            dialog.title = "Select track file";
            dialog.flags = 0x00001000 | 0x00000800 | 0x00000008; // file must exist, path must exist, keep working directory
            if (!GetOpenFileName(dialog))
                return null;
            var end = dialog.file.IndexOf('\0');
            return end >= 0 ? dialog.file.Substring(0, end) : dialog.file;
#else
            return null;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("comdlg32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetOpenFileName([In, Out] OpenFileName dialog);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class OpenFileName
        {
            public int structSize;
            public IntPtr owner = IntPtr.Zero;
            public IntPtr instance = IntPtr.Zero;
            public string filter;
            public string customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public string file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defaultExtension;
            public IntPtr customData = IntPtr.Zero;
            public IntPtr hook = IntPtr.Zero;
            public string templateName;
            public IntPtr reservedPointer = IntPtr.Zero;
            public int reservedInt;
            public int flagsEx;
        }
#endif
    }
}
