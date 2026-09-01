using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OptiGames.Core.Services;

/// <summary>
/// Makes shell tweaks visible without a sign-out. Most Explorer settings are cached in the
/// running shell process, so writing the registry alone appears to do nothing.
/// </summary>
public static class ShellRefresh
{
    private const int HWND_BROADCAST = 0xFFFF;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

    /// <summary>Tells every top-level window that user preferences changed.</summary>
    public static void Broadcast()
    {
        foreach (var section in new[] { "Environment", "WindowsThemeElement", "ImmersiveColorSet" })
        {
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, section,
                               SMTO_ABORTIFHUNG, 1000, out _);
        }
    }

    /// <summary>
    /// Restarts Explorer for the settings a broadcast cannot pick up (context menu, the
    /// notification centre). Windows relaunches the shell automatically.
    /// </summary>
    public static void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); p.WaitForExit(5000); }
            catch { /* another process already took it down */ }
            finally { p.Dispose(); }
        }

        // Explorer normally auto-restarts. If the shell-restart policy is off, start it.
        Thread.Sleep(600);
        if (Process.GetProcessesByName("explorer").Length == 0)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); }
            catch { /* nothing more we can do; the user can start it from Task Manager */ }
        }
    }
}
