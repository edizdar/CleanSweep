using System.Runtime.InteropServices;
using System.Security.Principal;

namespace CleanSweep.Core.Native;

public static class NativeFileOps
{
    private const int MovefileDelayUntilReboot = 0x4;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string lpExistingFileName, string? lpNewFileName, int dwFlags);

    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool ScheduleDeleteOnReboot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return MoveFileExW(path, null, MovefileDelayUntilReboot);
        }
        catch
        {
            return false;
        }
    }
}
