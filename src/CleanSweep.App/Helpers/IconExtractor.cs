using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CleanSweep.App.Helpers;

public static class IconExtractor
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbSizeFileInfo,
        uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string? displayIcon, string? installLocation, string? uninstallString)
    {
        string? candidate = ResolveCandidateExePath(displayIcon, installLocation, uninstallString);
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        return Cache.GetOrAdd(candidate, ExtractFrozenIcon);
    }

    private static string? ResolveCandidateExePath(string? displayIcon, string? installLocation, string? uninstallString)
    {
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            string p = displayIcon.Split(',')[0].Trim().Trim('"');
            if (File.Exists(p)) return p;
        }

        if (!string.IsNullOrWhiteSpace(uninstallString))
        {
            string exe = CleanSweep.Core.Discovery.Win32RegistryProvider.ExtractExecutablePath(uninstallString);
            if (!string.IsNullOrEmpty(exe) &&
                !exe.Contains("msiexec", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(exe))
                return exe;
        }

        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            try
            {
                return Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            }
            catch { }
        }

        return null;
    }

    private static ImageSource? ExtractFrozenIcon(string filePath)
    {
        try
        {
            var shinfo = new SHFILEINFO();
            IntPtr res = SHGetFileInfoW(filePath, 0, ref shinfo, (uint)Marshal.SizeOf<SHFILEINFO>(), ShgfiIcon | ShgfiSmallIcon);
            if (res == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero) return null;

            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }
}
