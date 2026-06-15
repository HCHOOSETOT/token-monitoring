using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TokenMonitoring.Services;

public static class ForegroundAppMonitor
{
    public static bool IsCodexOrCurrentAppActive()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId)
        {
            return HasVisibleCodexWindow();
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase)
                && IsWindowVisible(window)
                && !IsIconic(window);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasVisibleCodexWindow()
    {
        foreach (var process in Process.GetProcessesByName("Codex"))
        {
            try
            {
                var mainWindow = process.MainWindowHandle;
                if (mainWindow != IntPtr.Zero && IsWindowVisible(mainWindow) && !IsIconic(mainWindow))
                {
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);
}
