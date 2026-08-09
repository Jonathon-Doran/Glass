using Glass.Core;
using Glass.Core.Logging;
using System.Runtime.InteropServices;

namespace Glass.Input;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HidDeviceWriter
//
// Opens a single HID device by path for writing and sends OUT reports synchronously.
// Unlike HidDeviceReader, there is no background thread — writes are on-demand calls,
// not a continuous read loop, so no overlapped I/O is used.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
internal class HidDeviceWriter
{
    private readonly string _devicePath;
    private readonly HidDeviceInstance _instance;
    private IntPtr _fileHandle = HidNativeMethods.InvalidHandleValue;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HidDeviceWriter
    //
    // devicePath:  The Win32 device path from Raw Input enumeration
    // instance:    The device instance this writer is bound to
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public HidDeviceWriter(string devicePath, HidDeviceInstance instance)
    {
        _devicePath = devicePath;
        _instance = instance;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Open
    //
    // Opens the device handle for writing.
    //
    // Returns:  True if the handle was opened successfully.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool Open()
    {
        _fileHandle = HidNativeMethods.CreateFile(
            _devicePath,
            HidNativeMethods.GenericWrite,
            HidNativeMethods.FileShareRead | HidNativeMethods.FileShareWrite,
            IntPtr.Zero,
            HidNativeMethods.OpenExisting,
            0,
            IntPtr.Zero);

        if (_fileHandle == HidNativeMethods.InvalidHandleValue)
        {
            int error = Marshal.GetLastWin32Error();
            DebugLog.Write(LogChannel.Input, $"HidDeviceWriter.Open: {_instance} CreateFile failed error={error}.", LogLevel.Warn);
            return false;
        }

        DebugLog.Write(LogChannel.Input, $"HidDeviceWriter.Open: {_instance} opened for writing.", LogLevel.Info);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Close
    //
    // Closes the device handle if open.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Close()
    {
        if (_fileHandle == HidNativeMethods.InvalidHandleValue)
        {
            return;
        }

        HidNativeMethods.CloseHandle(_fileHandle);
        _fileHandle = HidNativeMethods.InvalidHandleValue;

        DebugLog.Write(LogChannel.Input, $"HidDeviceWriter.Close: {_instance} closed.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SendReport
    //
    // Writes a raw OUT report to the device.
    //
    // report:   The raw report bytes to send, including the leading report ID byte
    // Returns:  True if the full report was written successfully.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool SendReport(byte[] report)
    {
        if (_fileHandle == HidNativeMethods.InvalidHandleValue)
        {
            DebugLog.Write(LogChannel.Input, $"HidDeviceWriter.SendReport: {_instance} device not open, ignoring.", LogLevel.Warn);
            return false;
        }

        bool result = HidNativeMethods.WriteFile(_fileHandle, report, (uint)report.Length, out uint bytesWritten, IntPtr.Zero);

        if (!result || (bytesWritten != report.Length))
        {
            int error = Marshal.GetLastWin32Error();
            DebugLog.Write(LogChannel.Input, $"HidDeviceWriter.SendReport: {_instance} WriteFile failed error={error} bytesWritten={bytesWritten}.", LogLevel.Warn);
            return false;
        }

        DebugLog.Write(LogChannel.Input, $"HidDeviceWriter.SendReport: {_instance} wrote {bytesWritten} bytes.", LogLevel.Trace);
        return true;
    }
}
