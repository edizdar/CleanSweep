using System.Runtime.InteropServices;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Native;

public static class RestartManagerHelper
{
    private const int RmRebootReasonNone = 0;
    private const int CchRmSessionKey = 32;
    private const int ErrorMoreData = 234;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    private enum RM_APP_TYPE
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public RM_APP_TYPE ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFileNames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);

    public static List<LockingProcessInfo> GetLockingProcesses(params string[] filePaths)
    {
        var result = new List<LockingProcessInfo>();
        string[] existing = filePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToArray();
        if (existing.Length == 0) return result;

        uint sessionHandle = 0;
        try
        {
            string key = Guid.NewGuid().ToString("N");
            int res = RmStartSession(out sessionHandle, 0, key);
            if (res != 0) return result;

            res = RmRegisterResources(sessionHandle, (uint)existing.Length, existing, 0, null, 0, null);
            if (res != 0) return result;

            uint pnProcInfoNeeded = 0;
            uint pnProcInfo = 0;
            uint lpdwRebootReasons = RmRebootReasonNone;

            res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, null, ref lpdwRebootReasons);
            if (res == ErrorMoreData && pnProcInfoNeeded > 0)
            {
                var processInfo = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;
                res = RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, processInfo, ref lpdwRebootReasons);
                if (res == 0)
                {
                    for (int i = 0; i < pnProcInfo; i++)
                    {
                        var info = processInfo[i];
                        string procName = info.strAppName;
                        try
                        {
                            using var p = System.Diagnostics.Process.GetProcessById(info.Process.dwProcessId);
                            procName = p.ProcessName;
                        }
                        catch
                        {
                            // Keep strAppName if process metadata is inaccessible
                        }

                        result.Add(new LockingProcessInfo
                        {
                            Pid = info.Process.dwProcessId,
                            ProcessName = procName,
                            AppName = info.strAppName
                        });
                    }
                }
            }
        }
        catch
        {
            // Gracefully degrade when Restart Manager cannot query locked resources
        }
        finally
        {
            if (sessionHandle != 0)
            {
                try { RmEndSession(sessionHandle); } catch { }
            }
        }

        return result;
    }
}
