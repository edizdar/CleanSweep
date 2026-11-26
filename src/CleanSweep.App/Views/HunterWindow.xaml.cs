using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CleanSweep.Core.Models;
using CleanSweep.Core.Quarantine;
using CleanSweep.Core.Scanning;
using CleanSweep.Core.Uninstall;

namespace CleanSweep.App.Views;

public partial class HunterWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private readonly IReadOnlyList<InstalledApp> _allApps;
    private readonly LeftoverScanEngine _scanEngine;
    private readonly QuarantineManager _quarantineManager;
    private readonly Action _onReturnToMain;
    private bool _isHunting;

    public HunterWindow(
        IReadOnlyList<InstalledApp> allApps,
        LeftoverScanEngine scanEngine,
        QuarantineManager quarantineManager,
        Action onReturnToMain)
    {
        InitializeComponent();
        _allApps = allApps;
        _scanEngine = scanEngine;
        _quarantineManager = quarantineManager;
        _onReturnToMain = onReturnToMain;

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Top + 24;
    }

    private void Crosshair_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ReturnToMain();
            return;
        }

        _isHunting = true;
        CrosshairBorder.CaptureMouse();
        TargetLabel.Text = "Hedef pencere üzerine bırakın...";
    }

    private void Crosshair_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isHunting) return;
        if (GetCursorPos(out var pt))
        {
            Left = pt.X - (Width / 2);
            Top = pt.Y - 40;

            var proc = GetProcessAtPoint(pt);
            if (proc != null)
            {
                TargetLabel.Text = $"Hedef: {proc.ProcessName}.exe";
                proc.Dispose();
            }
        }
    }

    private void Crosshair_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isHunting) return;
        _isHunting = false;
        CrosshairBorder.ReleaseMouseCapture();

        if (GetCursorPos(out var pt))
        {
            var proc = GetProcessAtPoint(pt);
            if (proc != null)
            {
                string? exePath = null;
                try { exePath = proc.MainModule?.FileName; } catch { }
                ShowTargetActionMenu(proc.ProcessName, exePath, proc);
            }
            else
            {
                TargetLabel.Text = "Hedef algılanamadı";
            }
        }
    }

    private void Crosshair_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ReturnToMain();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        string dropped = files[0];
        string name = Path.GetFileNameWithoutExtension(dropped);
        string? exePath = dropped.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? dropped : null;

        ShowTargetActionMenu(name, exePath, null);
    }

    private Process? GetProcessAtPoint(POINT pt)
    {
        int selfPid = Environment.ProcessId;
        uint foundPid = 0;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == selfPid) return true;

            if (GetWindowRect(hwnd, out var rect))
            {
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w > 40 && h > 40 &&
                    pt.X >= rect.Left && pt.X <= rect.Right &&
                    pt.Y >= rect.Top && pt.Y <= rect.Bottom)
                {
                    foundPid = pid;
                    return false; // First visible Z-order window under cursor
                }
            }
            return true;
        }, IntPtr.Zero);

        if (foundPid == 0) return null;
        try { return Process.GetProcessById((int)foundPid); }
        catch { return null; }
    }

    private void ShowTargetActionMenu(string targetName, string? exePath, Process? liveProcess)
    {
        string? installDir = !string.IsNullOrEmpty(exePath) ? Path.GetDirectoryName(exePath) : null;
        var matchedApp = MatchInstalledApp(targetName, installDir);

        string displayTitle = matchedApp?.Name ?? targetName;
        TargetLabel.Text = $"Seçilen: {displayTitle}";

        var menu = new ContextMenu();

        var header = new MenuItem { Header = $"Hedef: {displayTitle}", IsEnabled = false, FontWeight = FontWeights.Bold };
        menu.Items.Add(header);
        menu.Items.Add(new Separator());

        var mUninstall = new MenuItem { Header = "Uygulamayı Kaldır & Kalıntı Tara" };
        mUninstall.IsEnabled = matchedApp != null;
        mUninstall.Click += async (_, _) =>
        {
            if (matchedApp == null) return;
            await UninstallExecutor.RunNativeUninstallerAsync(matchedApp, preferQuiet: false);
            var scan = await _scanEngine.ScanAsync(matchedApp, _allApps);
            var review = new LeftoverReviewWindow(matchedApp.Name, scan.Items, scan.Warnings, _quarantineManager);
            review.ShowDialog();
        };
        menu.Items.Add(mUninstall);

        var mScan = new MenuItem { Header = "Sadece Kalıntı Tara (Önizleme)" };
        mScan.Click += async (_, _) =>
        {
            var target = matchedApp ?? new InstalledApp
            {
                Id = $"hunter:{targetName}",
                Name = targetName,
                InstallLocation = installDir ?? string.Empty,
                Source = AppSource.Win32
            };
            var scan = await _scanEngine.ScanAsync(target, _allApps);
            var review = new LeftoverReviewWindow(target.Name, scan.Items, scan.Warnings, _quarantineManager);
            review.ShowDialog();
        };
        menu.Items.Add(mScan);

        if (liveProcess != null)
        {
            var mKill = new MenuItem { Header = $"Süreci Sonlandır ({liveProcess.ProcessName}.exe)" };
            mKill.Click += (_, _) =>
            {
                try
                {
                    liveProcess.Kill(entireProcessTree: true);
                    TargetLabel.Text = "Süreç sonlandırıldı.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Süreç sonlandırılamadı: {ex.Message}", "Avcı Modu", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            menu.Items.Add(mKill);
        }

        if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
        {
            var mOpen = new MenuItem { Header = "Dosya Konumunu Aç" };
            mOpen.Click += (_, _) => Process.Start(new ProcessStartInfo { FileName = installDir, UseShellExecute = true });
            menu.Items.Add(mOpen);
        }

        menu.Items.Add(new Separator());
        var mReturn = new MenuItem { Header = "Avcı Modundan Çık (Ana Pencere)" };
        mReturn.Click += (_, _) => ReturnToMain();
        menu.Items.Add(mReturn);

        menu.PlacementTarget = CrosshairBorder;
        menu.IsOpen = true;
    }

    private InstalledApp? MatchInstalledApp(string processName, string? installDir)
    {
        if (!string.IsNullOrEmpty(installDir))
        {
            var byPath = _allApps.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(a.InstallLocation) &&
                installDir.StartsWith(a.InstallLocation, StringComparison.OrdinalIgnoreCase));
            if (byPath != null) return byPath;
        }

        return _allApps.FirstOrDefault(a =>
            a.Name.Equals(processName, StringComparison.OrdinalIgnoreCase) ||
            a.Name.Contains(processName, StringComparison.OrdinalIgnoreCase));
    }

    private void ReturnToMain()
    {
        Close();
        _onReturnToMain();
    }
}
