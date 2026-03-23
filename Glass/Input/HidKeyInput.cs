using Glass.Core;
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

    private readonly ConcurrentQueue<HidKeyEventArgs> _queue = new();
    private readonly List<HidDeviceReader> _readers = new();
    private readonly Dictionary<string, IParseHidReport> _parsers = new();

    private Thread? _dispatcherThread;
    private volatile bool _running;

    public event EventHandler<HidKeyEventArgs>? KeyStateChanged;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HidKeyInput
    //
    // Registers all known device parsers.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public HidKeyInput()
    {
        RegisterParser(new G15ReportParser());
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RegisterParser
    //
    // Registers a report parser for a device type.
    // The parser's DeviceId is used to match against enumerated devices.
    //
    // parser:  The parser to register
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RegisterParser(IParseHidReport parser)
    {
        DebugLog.Write($"HidKeyInput.RegisterParser: device={parser.Device}.");
        _parsers[GetPidForDevice(parser.Device)] = parser;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetPidForDevice
    //
    // Returns the PID string for a known device type.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static string GetPidForDevice(Glass.Data.Models.KeyboardType device)
    {
        return device switch
        {
            Glass.Data.Models.KeyboardType.G15 => "046D-C222",
            Glass.Data.Models.KeyboardType.G13 => "046D-C21C",
            Glass.Data.Models.KeyboardType.DominatorX36 => "046D-????",
            _ => string.Empty
        };
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Start
    //
    // Enumerates Logitech HID devices, creates readers for known devices,
    // and starts the dispatcher thread.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Start()
    {
        DebugLog.Write("HidKeyInput.Start: enumerating devices.");

        var devices = EnumerateDevices();

        foreach (var (instance, devicePath) in devices)
        {
            DebugLog.Write($"HidKeyInput.Start: creating reader for {instance}.");
            var parser = _parsers[GetPidForDevice(instance.Type)];
            var reader = new HidDeviceReader(devicePath, instance, parser, _queue);
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

        DebugLog.Write($"HidKeyInput.Start: started {_readers.Count} readers.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Stop
    //
    // Stops all readers and the dispatcher thread.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Stop()
    {
        DebugLog.Write("HidKeyInput.Stop: stopping.");

        _running = false;

        foreach (var reader in _readers)
        {
            reader.Stop();
        }

        _readers.Clear();
        _dispatcherThread?.Join(TimeSpan.FromSeconds(3));
        _dispatcherThread = null;

        DebugLog.Write("HidKeyInput.Stop: stopped.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DispatcherThread
    //
    // Drains the event queue and fires KeyStateChanged for each event.
    // Runs until stopped.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DispatcherThread()
    {
        DebugLog.Write("HidKeyInput.DispatcherThread: starting.");

        while (_running)
        {
            while (_queue.TryDequeue(out var args))
            {
                DebugLog.Write($"HidKeyInput.DispatcherThread: dispatching key='{args.KeyName}' {args.Device} isPressed={args.IsPressed}.");

                try
                {
                    KeyStateChanged?.Invoke(this, args);
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"HidKeyInput.DispatcherThread: exception in handler: {ex.Message}.");
                }
            }

            Thread.Sleep(10);
        }

        DebugLog.Write("HidKeyInput.DispatcherThread: exiting.");
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
            DebugLog.Write("HidKeyInput.EnumerateDevices: no Raw Input devices found.");
            return results;
        }

        var deviceList = new HidNativeMethods.RawInputDeviceList[deviceCount];
        uint found = HidNativeMethods.GetRawInputDeviceList(deviceList, ref deviceCount, structSize);

        if (found == unchecked((uint)-1))
        {
            DebugLog.Write($"HidKeyInput.EnumerateDevices: GetRawInputDeviceList failed error={Marshal.GetLastWin32Error()}.");
            return results;
        }

        DebugLog.Write($"HidKeyInput.EnumerateDevices: scanning {found} devices.");

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

            var instance = new HidDeviceInstance(parser.Device, count);

            DebugLog.Write($"HidKeyInput.EnumerateDevices: found deviceId='{deviceId}', {instance}, path='{path}'.");
            results.Add((instance, path));
        }

        DebugLog.Write($"HidKeyInput.EnumerateDevices: found {results.Count} Logitech HID devices.");
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
}