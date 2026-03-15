using System.Runtime.InteropServices;

namespace Glass.Core;

public static class MonitorInfoHelper
{
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    // Returns the DPI scale, device name, and pixel dimensions for a given monitor handle.
    public static (float dpiScale, string deviceName, int width, int height) GetDpiScaleAndDeviceName(IntPtr hMonitor)
    {
        int result = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
        if (result != 0)
        {
            throw new System.ComponentModel.Win32Exception(result);
        }

        MONITORINFOEX mi = new MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf(mi);
        if (!GetMonitorInfo(hMonitor, ref mi))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        int width = mi.rcMonitor.right - mi.rcMonitor.left;
        int height = mi.rcMonitor.bottom - mi.rcMonitor.top;
        return (dpiX / 96.0f, mi.szDevice, width, height);
    }

    // Enumerates all connected monitors and invokes the provided action for each.
    public static void EnumerateMonitors(Action<IntPtr, float, string, int, int> monitorAction)
    {
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, hdcMonitor, lprcMonitor, dwData) =>
        {
            var (dpiScale, deviceName, width, height) = GetDpiScaleAndDeviceName(hMonitor);
            monitorAction(hMonitor, dpiScale, deviceName, width, height);
            return true;
        }, IntPtr.Zero);
    }
}