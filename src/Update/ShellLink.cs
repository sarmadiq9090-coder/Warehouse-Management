using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Stores
{
    /// <summary>Windows shortcuts (.lnk) through IShellLinkW. (WScript.Shell loses the characters outside the computer's
    /// code page: an Arabic folder on an English Windows.) Compiled into the program and into the setup.</summary>
    static class ShellLink
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class CShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
            void GetIDList(out IntPtr pidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int show);
            void SetShowCmd(int show);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        /// <summary>The target path stored in the shortcut ("" when it has none or cannot be read). Nothing is searched on disk.</summary>
        public static string Target(string lnk)
        {
            object o = null;
            try
            {
                o = new CShellLink();
                ((IPersistFile)o).Load(lnk, 0);
                StringBuilder sb = new StringBuilder(1024);
                ((IShellLinkW)o).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            catch { return ""; }
            finally { if (o != null) Marshal.ReleaseComObject(o); }
        }

        public static void Create(string lnk, string target, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            object o = new CShellLink();
            try
            {
                IShellLinkW l = (IShellLinkW)o;
                l.SetPath(target); l.SetWorkingDirectory(Path.GetDirectoryName(target));
                l.SetDescription(description ?? ""); l.SetIconLocation(target, 0);
                ((IPersistFile)o).Save(lnk, true);
            }
            finally { Marshal.ReleaseComObject(o); }
        }

        /// <summary>Points an existing shortcut to another file (its other properties stay).</summary>
        public static void Retarget(string lnk, string target)
        {
            object o = new CShellLink();
            try
            {
                ((IPersistFile)o).Load(lnk, 2);   // STGM_READWRITE
                IShellLinkW l = (IShellLinkW)o;
                l.SetPath(target); l.SetWorkingDirectory(Path.GetDirectoryName(target)); l.SetIconLocation(target, 0);
                ((IPersistFile)o).Save(lnk, true);
            }
            finally { Marshal.ReleaseComObject(o); }
        }
    }
}
