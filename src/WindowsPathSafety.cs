using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Cloud placeholders are reparse points, but do not redirect a path like links do.
// Unknown tags stay blocked. Opening metadata must not hydrate a cloud file.
public static class WindowsPathSafety
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo { public uint Attributes, Tag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileAttributesW(string path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out AttributeTagInfo info, uint size);
    public static bool IsCloudTag(uint tag) { return (tag & 0xFFFF0FFFu) == 0x9000001Au; }
    private static string NativePath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        path = Path.GetFullPath(path);
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
    }
    public static uint ReparseTag(string path)
    {
        string native = NativePath(path);
        uint attributes = GetFileAttributesW(native);
        if (attributes == UInt32.MaxValue) {
            int error = Marshal.GetLastWin32Error();
            if (error == 2 || error == 3) return 0; // A future destination may not exist yet.
            throw new IOException("Cannot inspect path safety.", new Win32Exception(error));
        }
        if ((attributes & 0x400u) == 0) return 0;
        using (var handle = CreateFileW(native, 0, 7, IntPtr.Zero, 3, 0x02200000u, IntPtr.Zero)) {
            if (handle.IsInvalid) throw new IOException("Cannot inspect reparse point.", new Win32Exception(Marshal.GetLastWin32Error()));
            AttributeTagInfo info;
            if (!GetFileInformationByHandleEx(handle, 9, out info, 8)) throw new IOException("Cannot inspect reparse tag.", new Win32Exception(Marshal.GetLastWin32Error()));
            // A concurrent metadata change cannot turn an unrecognised tag into permission.
            if ((info.Attributes & 0x400u) == 0) return 0;
            if (info.Tag == 0) throw new IOException("Reparse tag is unavailable.");
            return info.Tag;
        }
    }
    public static bool IsUnsafeReparsePoint(string path)
    {
        uint tag = ReparseTag(path);
        return tag != 0 && !IsCloudTag(tag);
    }
    public static void AssertNoLinks(string path)
    {
        for (string probe = Path.GetFullPath(path); !String.IsNullOrEmpty(probe); probe = Path.GetDirectoryName(probe))
            if (IsUnsafeReparsePoint(probe)) throw new IOException("Path contains a link/junction or unsupported reparse point.");
    }
}
