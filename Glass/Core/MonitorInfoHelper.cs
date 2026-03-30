using System.Runtime.InteropServices;
using Glass.Core;

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

    [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
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

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetDpiScaleAndDeviceName
    //
    // Returns the DPI scale, device name, and pixel dimensions for a given monitor handle.
    //
    // hMonitor:  The monitor handle to query.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
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

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // EnumerateMonitors
    //
    // Enumerates all connected logical monitors and invokes the provided action for each.
    //
    // monitorAction:  Called for each monitor with handle, DPI scale, device name, width, height.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static void EnumerateMonitors(Action<IntPtr, float, string, int, int> monitorAction)
    {
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, hdcMonitor, lprcMonitor, dwData) =>
        {
            (float dpiScale, string deviceName, int width, int height) = GetDpiScaleAndDeviceName(hMonitor);
            monitorAction(hMonitor, dpiScale, deviceName, width, height);
            return true;
        }, IntPtr.Zero);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // EnumerateDisplayDevices
    //
    // Enumerates all display adapters and their attached monitors via EnumDisplayDevices.
    // Returns a dictionary keyed by adapter device name (e.g. "\\.\DISPLAY1") mapping to
    // the list of monitors attached to that adapter.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static Dictionary<string, List<DisplayDeviceInfo>> EnumerateDisplayDevices()
    {
        Dictionary<string, List<DisplayDeviceInfo>> result = new Dictionary<string, List<DisplayDeviceInfo>>();

        DISPLAY_DEVICE adapter = new DISPLAY_DEVICE();
        adapter.cb = Marshal.SizeOf(adapter);

        uint adapterIndex = 0;
        while (EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
        {
            string adapterName = adapter.DeviceName.Trim();
            string adapterString = adapter.DeviceString.Trim();
            uint adapterFlags = adapter.StateFlags;

            List<DisplayDeviceInfo> monitors = new List<DisplayDeviceInfo>();

            DISPLAY_DEVICE monitor = new DISPLAY_DEVICE();
            monitor.cb = Marshal.SizeOf(monitor);

            uint monitorIndex = 0;
            while (EnumDisplayDevices(adapterName, monitorIndex, ref monitor, 0))
            {
                monitors.Add(new DisplayDeviceInfo
                {
                    AdapterName = adapterName,
                    AdapterString = adapterString,
                    AdapterFlags = adapterFlags,
                    MonitorName = monitor.DeviceName.Trim(),
                    MonitorString = monitor.DeviceString.Trim(),
                    MonitorID = monitor.DeviceID.Trim(),
                    MonitorKey = monitor.DeviceKey.Trim(),
                    MonitorFlags = monitor.StateFlags
                });

                monitor.cb = Marshal.SizeOf(monitor);
                monitorIndex++;
            }

            result[adapterName] = monitors;

            adapter.cb = Marshal.SizeOf(adapter);
            adapterIndex++;
        }

        return result;
    }
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// DisplayDeviceInfo
//
// Plain data record holding the combined output of EnumDisplayDevices for one
// monitor attached to one adapter.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class DisplayDeviceInfo
{
    public string AdapterName { get; set; } = string.Empty;
    public string AdapterString { get; set; } = string.Empty;
    public uint AdapterFlags { get; set; }
    public string MonitorName { get; set; } = string.Empty;
    public string MonitorString { get; set; } = string.Empty;
    public string MonitorID { get; set; } = string.Empty;
    public string MonitorKey { get; set; } = string.Empty;
    public uint MonitorFlags { get; set; }
}