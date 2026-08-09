using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Glass.Input;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HidManager
//
// Manages direct HID access to supported gaming input devices.
// Enumerates Logitech HID devices on start, creates a HidDeviceReader per device,
// and dispatches parsed key state changes to registered consumers via KeyStateChanged.
// All KeyStateChanged callbacks fire on a dedicated dispatcher thread.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class HidManager
{
    private readonly ConcurrentQueue<HidKeyEventArgs> _keyQueue = new();
    private readonly ConcurrentQueue<HidAxisEventArgs> _axisQueue = new();
    private readonly List<HidDeviceReader> _readers = new();
    private readonly Dictionary<string, IParseHidReport> _parsers = new();
    private readonly Dictionary<(HidDeviceInstance, string), byte> _axisState = new();
    private readonly object _axisStateLock = new();
    private readonly Dictionary<string, Func<IBuildLedReport>> _ledBuilderFactories = new();
    private readonly Dictionary<HidDeviceInstance, HidDeviceWriter> _ledWriters = new();
    private readonly Dictionary<HidDeviceInstance, IBuildLedReport> _ledBuilders = new();

    private Thread? _dispatcherThread;
    private volatile bool _running;

    public event EventHandler<HidKeyEventArgs>? KeyStateChanged;
    public event EventHandler<HidAxisEventArgs>? AxisChanged;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HidKeyInput
    //
    // Registers all known device parsers.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public HidManager()
    {
        RegisterParser(new G15ReportParser(), "046D-C222", "046D-C225", "046D-C226", "046D-C227");
        RegisterParser(new G13ReportParser(), "046D-C21C");
        RegisterParser(new G510ReportParser(), "046D-C22D");
        RegisterParser(new DominatorReportParser(), "0483-5750");

        RegisterLedBuilder(() => new DominatorLedReportBuilder(), "0483-5750");
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
            DebugLog.Write(LogChannel.Input, $"HidKeyInput.RegisterParser: pid='{pid}' device={parser.Device}.", LogLevel.Trace);
            _parsers[pid] = parser;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RegisterLedBuilder
    //
    // Registers an LED report builder factory for one or more device PIDs.
    // A new builder instance is created per connected device and holds instance state.  Builders hold
    // per-device color state and are not shared across multiple physical
    // devices of the same type.
    //
    // factory:  Creates a new builder instance
    // pids:     One or more device PID strings e.g. "0483-5750"
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RegisterLedBuilder(Func<IBuildLedReport> factory, params string[] pids)
    {
        foreach (var pid in pids)
        {
            DebugLog.Write(LogChannel.Input, $"HidManager.RegisterLedBuilder: pid='{pid}'.", LogLevel.Trace);
            _ledBuilderFactories[pid] = factory;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Start
    //
    // Enumerates HID devices, creates readers for known input devices, opens LED
    // writers for devices that support LED output, and starts the dispatcher
    // thread.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Start()
    {
        DebugLog.Write(LogChannel.Input, "HidManager.Start: enumerating devices.", LogLevel.Trace);

        List<(HidDeviceInstance Instance, string DevicePath)> devices = EnumerateDevices();

        foreach ((HidDeviceInstance instance, string devicePath) in devices)
        {
            DebugLog.Write(LogChannel.Input, $"HidManager.Start: creating reader for {instance}.", LogLevel.Trace);

            IParseHidReport parser = _parsers[instance.Pid];
            HidDeviceReader reader = new HidDeviceReader(devicePath, instance, parser, _keyQueue, _axisQueue);
            _readers.Add(reader);
            reader.Start();

            if (_ledBuilderFactories.TryGetValue(instance.Pid, out Func<IBuildLedReport>? factory))
            {
                HidDeviceWriter writer = new HidDeviceWriter(devicePath, instance);

                if (writer.Open())
                {
                    _ledWriters[instance] = writer;
                    _ledBuilders[instance] = factory();

                    DebugLog.Write(LogChannel.Input, $"HidManager.Start: opened LED writer for {instance}.", LogLevel.Trace);
                }
                else
                {
                    DebugLog.Write(LogChannel.Input, $"HidManager.Start: failed to open LED writer for {instance}.", LogLevel.Warn);
                }
            }
        }

        _running = true;
        _dispatcherThread = new Thread(DispatcherThread)
        {
            Name = "HidManager_Dispatcher",
            IsBackground = true
        };
        _dispatcherThread.Start();

        DebugLog.Write(LogChannel.Input, $"HidManager.Start: started {_readers.Count} readers, {_ledWriters.Count} LED writers.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Stop
    //
    // Stops all readers and the dispatcher thread, and closes all LED writers.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Stop()
    {
        DebugLog.Write(LogChannel.Input, "HidManager.Stop: stopping.", LogLevel.Trace);

        _running = false;

        foreach (HidDeviceReader reader in _readers)
        {
            reader.Stop();
        }

        _readers.Clear();

        foreach (HidDeviceWriter writer in _ledWriters.Values)
        {
            writer.Close();
        }

        _ledWriters.Clear();
        _ledBuilders.Clear();

        _dispatcherThread?.Join(TimeSpan.FromSeconds(3));
        _dispatcherThread = null;

        DebugLog.Write(LogChannel.Input, "HidManager.Stop: stopped.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DispatcherThread
    //
    // Drains the event queue and fires KeyStateChanged for each event.
    // Runs until stopped.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DispatcherThread()
    {
        DebugLog.Write(LogChannel.Input, "HidKeyInput.DispatcherThread: starting.", LogLevel.Trace);

        while (_running)
        {
            while (_keyQueue.TryDequeue(out var keyArgs))
            {
                DebugLog.Write(LogChannel.Input, $"HidKeyInput.DispatcherThread: dispatching key='{keyArgs.KeyName}' {keyArgs.Device} isPressed={keyArgs.IsPressed}.", LogLevel.Trace);

                try
                {
                    KeyStateChanged?.Invoke(this, keyArgs);
                }
                catch (Exception ex)
                {
                    DebugLog.Write(LogChannel.Input, $"HidKeyInput.DispatcherThread: exception in KeyStateChanged handler: {ex.Message}.", LogLevel.Error);
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
                    DebugLog.Write(LogChannel.Input, $"HidKeyInput.DispatcherThread: exception in AxisChanged handler: {ex.Message}.", LogLevel.Error);
                }
            }

            Thread.Sleep(10);
        }

        DebugLog.Write(LogChannel.Input, "HidKeyInput.DispatcherThread: exiting.", LogLevel.Trace);
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

            DebugLog.Write(LogChannel.Input, $"HidKeyInput.EnumerateDevices: found deviceId='{deviceId}', {instance}, path='{path}'.", LogLevel.Info);
            results.Add((instance, path));
        }

        DebugLog.Write(LogChannel.Input, $"HidKeyInput.EnumerateDevices: found {results.Count} Logitech HID devices.", LogLevel.Info);
        return results;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // IsDeviceConnected
    //
    // Returns true if a reader is currently running for the given device type.
    //
    // type:  The keyboard type to check
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool IsDeviceConnected(KeyboardType type)
    {
        bool connected = _readers.Any(reader => reader.DeviceType == type);

        DebugLog.Write(LogChannel.Input, $"HidKeyInput.IsDeviceConnected: type={type} connected={connected}.", LogLevel.Trace);

        return connected;
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

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetLedColor
    //
    // Sets a single key's LED color and sends whatever OUT report(s) are
    // needed to reflect the change.
    //
    // instance:  The device instance to update
    // keyName:   The key whose LED to update
    // r:         Red component
    // g:         Green component
    // b:         Blue component
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SetLedColor(HidDeviceInstance instance, string keyName, byte r, byte g, byte b)
    {
        Dictionary<string, (byte R, byte G, byte B)> colors = new Dictionary<string, (byte R, byte G, byte B)>
        {
            { keyName, (r, g, b) }
        };

        SendLedColors(instance, colors);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PaintGrid
    //
    // Sets multiple keys' LED colors at once and sends whatever OUT report(s)
    // are needed to reflect the change.
    //
    // instance:  The device instance to update
    // colors:    One or more key names mapped to their new (R, G, B) color
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void PaintGrid(HidDeviceInstance instance, IReadOnlyDictionary<string, (byte R, byte G, byte B)> colors)
    {
        SendLedColors(instance, colors);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SendLedColors
    //
    // Looks up the writer and builder for the given instance, builds the
    // OUT report(s) needed for the given color changes, and sends them.
    // Logs and returns if the instance has no LED writer.
    //
    // instance:  The device instance to update
    // colors:    One or more key names mapped to their new (R, G, B) color
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SendLedColors(HidDeviceInstance instance, IReadOnlyDictionary<string, (byte R, byte G, byte B)> colors)
    {
        if (!_ledWriters.TryGetValue(instance, out HidDeviceWriter? writer) || !_ledBuilders.TryGetValue(instance, out IBuildLedReport? builder))
        {
            DebugLog.Write(LogChannel.Input, $"HidManager.SendLedColors: no LED writer for {instance}, ignoring.", LogLevel.Warn);
            return;
        }

        IReadOnlyList<byte[]> reports = builder.SetColors(colors);

        foreach (byte[] report in reports)
        {
            writer.SendReport(report);
        }

        DebugLog.Write(LogChannel.Input, $"HidManager.SendLedColors: {instance} sent {reports.Count} report(s).", LogLevel.Trace);
    }
}