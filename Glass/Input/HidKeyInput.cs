using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Glass.Input;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HidKeyInput
//
// Manages direct HID access to supported gaming input devices.
// Enumerates Logitech HID devices on start, creates a HidDeviceReader per device,
// and dispatches parsed key state changes to registered consumers via KeyStateChanged.
// All KeyStateChanged callbacks fire on a dedicated dispatcher thread.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class HidKeyInput
{
    private const string LogitechVendorId = "046D";

    private readonly ConcurrentQueue<HidKeyEventArgs> _keyQueue = new();
    private readonly ConcurrentQueue<HidAxisEventArgs> _axisQueue = new();
    private readonly List<HidDeviceReader> _readers = new();
    private readonly Dictionary<string, IParseHidReport> _parsers = new();
    private readonly Dictionary<(HidDeviceInstance, string), byte> _axisState = new();
    private readonly object _axisStateLock = new();

    private Thread? _dispatcherThread;
    private volatile bool _running;

    public event EventHandler<HidKeyEventArgs>? KeyStateChanged;
    public event EventHandler<HidAxisEventArgs>? AxisChanged;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HidKeyInput
    //
    // Registers all known device parsers.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public HidKeyInput()
    {
        RegisterParser(new G15ReportParser(), "046D-C222", "046D-C225", "046D-C226", "046D-C227", "046D-C22D");
        RegisterParser(new G13ReportParser(), "046D-C21C");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RegisterParser
    //
    // Registers a report parser for one or more device PIDs.
    // Multiple PIDs can map to the same parser for device families
    // that share a report format.
    //
    // parser:  The parser to register
    // pids:    One or more device PID strings e.g. "046D-C222"
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RegisterParser(IParseHidReport parser, params string[] pids)
    {
        foreach (var pid in pids)
        {
            DebugLog.Write(LogChannel.Input, $"HidKeyInput.RegisterParser: pid='{pid}' device={parser.Device}.", LogLevel.Info);
            _parsers[pid] = parser;
        }
    }


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Start
    //
    // Enumerates Logitech HID devices, creates readers for known devices,
    // and starts the dispatcher thread.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Start()
    {
        DebugLog.Write(LogChannel.Input, "HidKeyInput.Start: enumerating devices.", LogLevel.Info);

        var devices = EnumerateDevices();

        foreach (var (instance, devicePath) in devices)
        {
            DebugLog.Write(LogChannel.Input, $"HidKeyInput.Start: creating reader for {instance}.", LogLevel.Info);
            var parser = _parsers[instance.Pid];
            var reader = new HidDeviceReader(devicePath, instance, parser, _keyQueue, _axisQueue);
            _readers.Add(reader);
            reader.Start();
        }

        _running = true;
        _dispatcherThread = new Thread(DispatcherThread)
        {
            Name = "HidKeyInput_Dispatcher",
            IsBackground = true
        };
        _dispatcherThread.Start();

        DebugLog.Write(LogChannel.Input, $"HidKeyInput.Start: started {_readers.Count} readers.", LogLevel.Info);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Stop
    //
    // Stops all readers and the dispatcher thread.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Stop()
    {
        DebugLog.Write(LogChannel.Input, "HidKeyInput.Stop: stopping.");

        _running = false;

        foreach (var reader in _readers)
        {
            reader.Stop();
        }

        _readers.Clear();
        _dispatcherThread?.Join(TimeSpan.FromSeconds(3));
        _dispatcherThread = null;

        DebugLog.Write(LogChannel.Input, "HidKeyInput.Stop: stopped.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DispatcherThread
    //
    // Drains the event queue and fires KeyStateChanged for each event.
    // Runs until stopped.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DispatcherThread()
    {
        DebugLog.Write(LogChannel.Input, "HidKeyInput.DispatcherThread: starting.");

        while (_running)
        {
            while (_keyQueue.TryDequeue(out var keyArgs))
            {
                DebugLog.Write(LogChannel.Input, $"HidKeyInput.DispatcherThread: dispatching key='{keyArgs.KeyName}' {keyArgs.Device} isPressed={keyArgs.IsPressed}.");

                try
                {
                    KeyStateChanged?.Invoke(this, keyArgs);
                }
                catch (Exception ex)
                {
                    DebugLog.Write(LogChannel.Input, $"HidKeyInput.DispatcherThread: exception in KeyStateChanged handler: {ex.Message}.");
                }
            }

            while (_axisQueue.TryDequeue(out var axisArgs))
            {
                if (axisArgs == null)
                {
                    continue;
                }

                if (axisArgs.Device.HasValue)
                {
                    lock (_axisStateLock)
                    {
                        _axisState[(axisArgs.Device.Value, axisArgs.AxisName)] = axisArgs.Value;
                    }
                }

                try
                {
                    AxisChanged?.Invoke(this, axisArgs);
                }
                catch (Exception ex)
                {
                    DebugLog.Write(LogChannel.Input, $"HidKeyInput.DispatcherThread: exception in AxisChanged handler: {ex.Message}.");
                }
            }

            Thread.Sleep(10);
        }

        DebugLog.Write(LogChannel.Input, "HidKeyInput.DispatcherThread: exiting.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // EnumerateDevices
    //
    // Uses Raw Input to enumerate HID devices, filtering by whether a parser
    // exists for the device ID. Parser registration is the authoritative list
    // of supported devices — no hardcoded vendor filtering.
    // Assigns instance numbers per device type in enumeration order.
    // Returns a list of (HidDeviceInstance, devicePath) pairs.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private List<(HidDeviceInstance Instance, string DevicePath)> EnumerateDevices()
    {
        var results = new List<(HidDeviceInstance, string)>();
        var instanceCounts = new Dictionary<KeyboardType, int>();

        uint deviceCount = 0;
        uint structSize = (uint)Marshal.SizeOf<HidNativeMethods.RawInputDeviceList>();

        HidNativeMethods.GetRawInputDeviceList(null, ref deviceCount, structSize);

        if (deviceCount == 0)
        {
            DebugLog.Write(LogChannel.Input, "HidKeyInput.EnumerateDevices: no Raw Input devices found.");
            return results;
        }

        var deviceList = new HidNativeMethods.RawInputDeviceList[deviceCount];
        uint found = HidNativeMethods.GetRawInputDeviceList(deviceList, ref deviceCount, structSize);

        if (found == unchecked((uint)-1))
        {
            DebugLog.Write(LogChannel.Input, $"HidKeyInput.EnumerateDevices: GetRawInputDeviceList failed error={Marshal.GetLastWin32Error()}.");
            return results;
        }

        DebugLog.Write(LogChannel.Input, $"HidKeyInput.EnumerateDevices: scanning {found} devices.");

        for (int i = 0; i < found; i++)
        {
            if (deviceList[i].dwType != HidNativeMethods.RimTypeHid)
            {
                continue;
            }

            IntPtr handle = deviceList[i].hDevice;

            string? path = GetDevicePath(handle);
            if (path == null)
            {
                continue;
            }

            if (!TryParseDeviceId(path, out string deviceId))
            {
                continue;
            }

            if (!_parsers.TryGetValue(deviceId, out var parser))
            {
                continue;
            }

            if (!instanceCounts.TryGetValue(parser.Device, out int count))
            {
                count = 0;
            }
            count++;
            instanceCounts[parser.Device] = count;

            var instance = new HidDeviceInstance(parser.Device, count, deviceId);

            DebugLog.Write(LogChannel.Input, $"HidKeyInput.EnumerateDevices: found deviceId='{deviceId}', {instance}, path='{path}'.");
            results.Add((instance, path));
        }

        DebugLog.Write(LogChannel.Input, $"HidKeyInput.EnumerateDevices: found {results.Count} Logitech HID devices.");
        return results;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetDevicePath
    //
    // Retrieves the Win32 device path for a Raw Input device handle.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static string? GetDevicePath(IntPtr deviceHandle)
    {
        uint nameSize = 0;
        HidNativeMethods.GetRawInputDeviceInfo(deviceHandle, HidNativeMethods.RidiDeviceName, IntPtr.Zero, ref nameSize);

        if (nameSize == 0)
        {
            return null;
        }

        IntPtr nameBuffer = Marshal.AllocHGlobal((int)(nameSize * 2));

        try
        {
            uint result = HidNativeMethods.GetRawInputDeviceInfo(deviceHandle, HidNativeMethods.RidiDeviceName, nameBuffer, ref nameSize);

            if (result == unchecked((uint)-1))
            {
                return null;
            }

            return Marshal.PtrToStringUni(nameBuffer);
        }
        finally
        {
            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryParseDeviceId
    //
    // Extracts "VVVV-PPPP" from a device path like \\?\HID#VID_046D&PID_C222&...
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static bool TryParseDeviceId(string path, out string deviceId)
    {
        deviceId = string.Empty;
        string upper = path.ToUpperInvariant();

        int vidIdx = upper.IndexOf("VID_");
        int pidIdx = upper.IndexOf("PID_");

        if ((vidIdx < 0) || (pidIdx < 0))
        {
            return false;
        }

        if ((vidIdx + 8 > upper.Length) || (pidIdx + 8 > upper.Length))
        {
            return false;
        }

        string vid = upper.Substring(vidIdx + 4, 4);
        string pid = upper.Substring(pidIdx + 4, 4);

        deviceId = $"{vid}-{pid}";
        return true;
    }

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// GetAxisValue
//
// Returns the current value of a named axis for a device instance,
// or null if the device type does not support analog axes.
// Returns 127 (center) if the device supports axes but no value has been received yet.
//
// device:    The device instance to query
// axisName:  The axis name e.g. "JoystickX", "JoystickY"
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public byte? GetAxisValue(HidDeviceInstance device, string axisName)
{
    if (!DeviceSupportsAxes(device.Type))
    {
        return null;
    }

    lock (_axisStateLock)
    {
        if (_axisState.TryGetValue((device, axisName), out byte value))
        {
            return value;
        }
    }

    return 0x7F;
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// DeviceSupportsAxes
//
// Returns true if the given keyboard type supports analog axis input.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
private static bool DeviceSupportsAxes(KeyboardType type)
{
    return type switch
    {
        KeyboardType.G13 => true,
        _ => false
    };
}
}