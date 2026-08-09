using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Glass.Input;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HidDeviceReader
//
// Opens a single HID device by path and reads reports on a dedicated background thread.
// Parsed key state changes are pushed into the shared queue provided by HidKeyInput.
// Uses overlapped I/O so the reader thread can be shut down cleanly.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
internal class HidDeviceReader
{
    private readonly string _devicePath;
    private readonly HidDeviceInstance _instance;
    private readonly IParseHidReport _parser;
    private readonly ConcurrentQueue<HidKeyEventArgs> _keyQueue;
    private readonly ConcurrentQueue<HidAxisEventArgs> _axisQueue;
    private volatile bool _running;
    private Thread? _thread;

    private readonly SemaphoreSlim _deliverySignal;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HidDeviceReader
    //
    // devicePath:  The Win32 device path from Raw Input enumeration
    // instance:    The device instance this reader is bound to
    // parser:      The report parser for this device type
    // keyQueue:    The shared queue to push parsed key events into
    // axisQueue:   The shared queue to push parsed axis events into
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public HidDeviceReader(
        string devicePath,
        HidDeviceInstance instance,
        IParseHidReport parser,
        ConcurrentQueue<HidKeyEventArgs> keyQueue,
        ConcurrentQueue<HidAxisEventArgs> axisQueue,
        SemaphoreSlim deliverySignal)
    {
        _devicePath = devicePath;
        _instance = instance;
        _parser = parser;
        _keyQueue = keyQueue;
        _axisQueue = axisQueue;
        _deliverySignal = deliverySignal;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // The device instance this reader is bound to.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public HidDeviceInstance Instance => _instance;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeviceType
    //
    // The keyboard type this reader is bound to.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyboardType DeviceType => _instance.Type;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Start
    //
    // Starts the reader thread.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Start()
    {
        DebugLog.Write(LogChannel.Input, $"HidDeviceReader.Start: {_instance}.");

        _running = true;
        _thread = new Thread(ReaderThread)
        {
            Name = $"HID_{_instance}",
            IsBackground = true
        };
        _thread.Start();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Stop
    //
    // Signals the reader thread to stop and waits for it to exit.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Stop()
    {
        DebugLog.Write(LogChannel.Input, $"HidDeviceReader.Stop: {_instance}.");

        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;

        DebugLog.Write(LogChannel.Input, $"HidDeviceReader.Stop: {_instance} stopped.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReaderThread
    //
    // Opens the device and reads HID reports in a loop until stopped.
    // Uses overlapped I/O with a timeout so shutdown is responsive.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ReaderThread()
    {
        DebugLog.Write(LogChannel.Input, $"HidDeviceReader.ReaderThread: {_instance} opening device.", LogLevel.Trace);

        IntPtr fileHandle = HidNativeMethods.CreateFile(
            _devicePath,
            HidNativeMethods.GenericRead,
            HidNativeMethods.FileShareRead | HidNativeMethods.FileShareWrite,
            IntPtr.Zero,
            HidNativeMethods.OpenExisting,
            HidNativeMethods.FileFlagOverlapped,
            IntPtr.Zero);

        if (fileHandle == HidNativeMethods.InvalidHandleValue)
        {
            int error = Marshal.GetLastWin32Error();
            DebugLog.Write(LogChannel.Input, $"HidDeviceReader.ReaderThread: {_instance} CreateFile failed error={error}.", LogLevel.Error);
            return;
        }

        DebugLog.Write(LogChannel.Input, $"HidDeviceReader.ReaderThread: {_instance} device opened successfully.", LogLevel.Trace);

        IntPtr eventHandle = HidNativeMethods.CreateEvent(IntPtr.Zero, true, false, null);

        if (eventHandle == IntPtr.Zero)
        {
            DebugLog.Write(LogChannel.Input, $"HidDeviceReader.ReaderThread: {_instance} CreateEvent failed.", LogLevel.Error);
            HidNativeMethods.CloseHandle(fileHandle);
            return;
        }

        const int bufferSize = 64;
        byte[] buffer = new byte[bufferSize];
        GCHandle bufferPin = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            while (_running)
            {
                HidNativeMethods.ResetEvent(eventHandle);

                var overlapped = new HidNativeMethods.Overlapped
                {
                    hEvent = eventHandle
                };

                bool readResult = HidNativeMethods.ReadFile(
                    fileHandle,
                    bufferPin.AddrOfPinnedObject(),
                    (uint)bufferSize,
                    out uint bytesRead,
                    ref overlapped);

                if (!readResult)
                {
                    int error = Marshal.GetLastWin32Error();

                    if (error != HidNativeMethods.ErrorIoPending)
                    {
                        DebugLog.Write(LogChannel.Input, $"HidDeviceReader.ReaderThread: {_instance} ReadFile failed error={error}.", LogLevel.Error);
                        break;
                    }

                    uint waitResult = HidNativeMethods.WaitForSingleObject(eventHandle, 500);

                    if (waitResult == HidNativeMethods.WaitObject0)
                    {
                        if (!HidNativeMethods.GetOverlappedResult(fileHandle, ref overlapped, out bytesRead, false))
                        {
                            DebugLog.Write(LogChannel.Input, $"HidDeviceReader.ReaderThread: {_instance} GetOverlappedResult failed error={Marshal.GetLastWin32Error()}.", LogLevel.Error);
                            break;
                        }
                    }
                    else if (waitResult == HidNativeMethods.WaitTimeout)
                    {
                        continue;
                    }
                    else
                    {
                        DebugLog.Write(LogChannel.Input, $"HidDeviceReader.ReaderThread:{_instance} WaitForSingleObject returned {waitResult}.", LogLevel.Trace);
                        break;
                    }
                }

                if (bytesRead > 0)
                {
                    byte[] report = new byte[bytesRead];
                    Array.Copy(buffer, report, (int)bytesRead);

                    IReadOnlyList<HidKeyEventArgs> keyEvents = _parser.Parse(report);

                    foreach (HidKeyEventArgs evt in keyEvents)
                    {
                        evt.Device = _instance;
                        DebugLog.Write(LogChannel.Input, $"HidDeviceReader.ReaderThread: {_instance} key='{evt.KeyName}' isPressed={evt.IsPressed}.", LogLevel.Trace);
                        _keyQueue.Enqueue(evt);
                    }

                    bool enqueuedAny = keyEvents.Count > 0;

                    if (_parser is IParseHidAxes axisParser)
                    {
                        IReadOnlyList<HidAxisEventArgs> axisEvents = axisParser.ParseAxes(report);

                        foreach (HidAxisEventArgs evt in axisEvents)
                        {
                            evt.Device = _instance;
                            _axisQueue.Enqueue(evt);
                        }

                        enqueuedAny = enqueuedAny || (axisEvents.Count > 0);
                    }

                    _parser.UpdateState(report);

                    if (enqueuedAny)
                    {
                        _deliverySignal.Release();
                    }
                }
            }
        }
        finally
        {
            bufferPin.Free();
            HidNativeMethods.CancelIo(fileHandle);
            HidNativeMethods.CloseHandle(eventHandle);
            HidNativeMethods.CloseHandle(fileHandle);
            DebugLog.Write(LogChannel.Input, $"HidDeviceReader.ReaderThread: {_instance} thread exiting.", LogLevel.Trace);
        }
    }
}