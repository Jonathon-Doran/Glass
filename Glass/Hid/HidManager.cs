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

    // Signaled by readers each time an event is enqueued — wakes the delivery thread
    private readonly SemaphoreSlim _deliverySignal = new SemaphoreSlim(0);

    // Per-type instance number assignment.  Counts up only — instance numbers are never reused,
    // so hotplug arrivals later get fresh numbers.
    private readonly Dictionary<KeyboardType, int> _instanceCounts = new();

    private Thread? _deliveryThread;
    private volatile bool _running;

    public event EventHandler<HidKeyEventArgs>? KeyStateChanged;
    public event EventHandler<HidAxisEventArgs>? AxisChanged;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HidManager
    //
    // Registers all known device parsers and LED builder factories, then
    // discovers connected devices.  Discovery enumerates supported HID
    // devices and constructs a reader, and where supported an LED writer and
    // builder, for each device instance.  Nothing is started or opened here —
    // Start handles lifecycle.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public HidManager()
    {
        RegisterParser(new G15ReportParser(), "046D-C222", "046D-C225", "046D-C226", "046D-C227");
        RegisterParser(new G13ReportParser(), "046D-C21C");
        RegisterParser(new G510ReportParser(), "046D-C22D");
        RegisterParser(new DominatorReportParser(), "0483-5750");

        RegisterLedBuilder(() => new DominatorLedReportBuilder(), "0483-5750");

        List<(HidDeviceInstance Instance, string DevicePath)> devices = EnumerateDevices();

        foreach ((HidDeviceInstance instance, string devicePath) in devices)
        {
            IParseHidReport parser = _parsers[instance.Pid];
            HidDeviceReader reader = new HidDeviceReader(devicePath, instance, parser, _keyQueue, _axisQueue, _deliverySignal);
            _readers.Add(reader);

            DebugLog.Write(LogChannel.Input, $"HidManager: discovered {instance}, reader constructed.", LogLevel.Trace);

            if (_ledBuilderFactories.TryGetValue(instance.Pid, out Func<IBuildLedReport>? factory))
            {
                _ledWriters[instance] = new HidDeviceWriter(devicePath, instance);
                _ledBuilders[instance] = factory();

                DebugLog.Write(LogChannel.Input, $"HidManager: {instance} supports LED output, writer constructed.", LogLevel.Trace);
            }
        }

        DebugLog.Write(LogChannel.Input, $"HidManager: discovery complete, {_readers.Count} device(s), {_ledWriters.Count} LED-capable.", LogLevel.Trace);
    }


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetConnectedInstances
    //
    // Returns the device instances that currently have a running reader,
    // in enumeration order.
    //
    // Returns:  A snapshot list of connected device instances
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<HidDeviceInstance> GetConnectedInstances()
    {
        List<HidDeviceInstance> instances = new List<HidDeviceInstance>();

        foreach (HidDeviceReader reader in _readers)
        {
            instances.Add(reader.Instance);
        }

        DebugLog.Write(LogChannel.Input, $"HidManager.GetConnectedInstances: {instances.Count} instance(s).", LogLevel.Trace);
        return instances;
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
    // Starts lifecycle for all discovered devices: starts each reader, opens
    // each LED writer, and starts the delivery thread.  Safe to call again
    // after Stop.  If already running, logs and returns.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Start()
    {
        if (_running)
        {
            DebugLog.Write(LogChannel.Input, "HidManager.Start: already running, ignoring.", LogLevel.Warn);
            return;
        }

        DebugLog.Write(LogChannel.Input, $"HidManager.Start: starting {_readers.Count} reader(s), {_ledWriters.Count} LED writer(s).", LogLevel.Trace);

        foreach (HidDeviceReader reader in _readers)
        {
            reader.Start();
        }

        foreach (KeyValuePair<HidDeviceInstance, HidDeviceWriter> entry in _ledWriters)
        {
            if (!entry.Value.Open())
            {
                DebugLog.Write(LogChannel.Input, $"HidManager.Start: LED writer open failed for {entry.Key}, removing from LED output.", LogLevel.Warn);

                _ledWriters.Remove(entry.Key);
                _ledBuilders.Remove(entry.Key);
            }
        }

        _running = true;
        _deliveryThread = new Thread(DeliveryThread)
        {
            Name = "HidManager_Delivery",
            IsBackground = true
        };
        _deliveryThread.Start();

        DebugLog.Write(LogChannel.Input, "HidManager.Start: running.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Stop
    //
    // Stops lifecycle for all discovered devices: stops each reader, closes
    // each LED writer, and stops the delivery thread.  The discovered device
    // collections are left intact so Start can run again without
    // rediscovery.  If not running, logs and returns.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Stop()
    {
        if (!_running)
        {
            DebugLog.Write(LogChannel.Input, "HidManager.Stop: not running, ignoring.", LogLevel.Warn);
            return;
        }

        DebugLog.Write(LogChannel.Input, "HidManager.Stop: stopping.", LogLevel.Trace);

        _running = false;
        _deliverySignal.Release();

        foreach (HidDeviceReader reader in _readers)
        {
            reader.Stop();
        }

        foreach (HidDeviceWriter writer in _ledWriters.Values)
        {
            writer.Close();
        }

        _deliveryThread?.Join(TimeSpan.FromSeconds(3));
        _deliveryThread = null;

        DebugLog.Write(LogChannel.Input, "HidManager.Stop: stopped.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeliveryThread
    //
    // Drains the key and axis queues and delivers each event to subscribers
    // via KeyStateChanged and AxisChanged.  Runs until stopped.  Delivery here
    // is transport only — bind dispatch happens downstream in KeyboardManager.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DeliveryThread()
    {
        DebugLog.Write(LogChannel.Input, "HidManager.DeliveryThread: starting.", LogLevel.Trace);

        while (_running)
        {
            _deliverySignal.Wait();

            while (_keyQueue.TryDequeue(out var keyArgs))
            {
                DebugLog.Write(LogChannel.Input, $"HidManager.DeliveryThread: delivering raw key='{keyArgs.KeyName}' {keyArgs.Device} isPressed={keyArgs.IsPressed}.", LogLevel.Trace);

                try
                {
                    KeyStateChanged?.Invoke(this, keyArgs);
                }
                catch (Exception ex)
                {
                    DebugLog.Write(LogChannel.Input, $"HidManager.DeliveryThread: exception in KeyStateChanged handler: {ex.Message}.", LogLevel.Error);
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
                    DebugLog.Write(LogChannel.Input, $"HidManager.DeliveryThread: exception in AxisChanged handler: {ex.Message}.", LogLevel.Error);
                }
            }

            Thread.Sleep(10);
        }

        DebugLog.Write(LogChannel.Input, "HidManager.DeliveryThread: exiting.", LogLevel.Trace);
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

            if (!_instanceCounts.TryGetValue(parser.Device, out int count))
            {
                count = 0;
            }
            count++;
            _instanceCounts[parser.Device] = count;

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