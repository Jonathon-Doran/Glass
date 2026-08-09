using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;

namespace Glass.Input;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// G510ReportParser
//
// Parses raw HID reports from the Logitech G510s keyboard (046D-C22D).
// Report ID is 0x03, G-key data in bytes 1-3 as a dense bitmask.
// Maintains previous report state to detect press and release transitions.
//
// Bit map:
// Byte 1: bits 0-7 = G1-G8
// Byte 2: bits 0-7 = G9-G16
// Byte 3: bit 0 = G17, bit 1 = G18.  Bits 2-7 are not mapped.
//
// The device also emits report ID 0x01 (a keyboard report echoing G-keys as
// F-keys) on a sibling collection.  Those reports are ignored.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class G510ReportParser : IParseHidReport
{
    private static readonly (int ByteIndex, int BitMask, string KeyName)[] KeyMap =
    {
        (1, 0x01, "G1"),
        (1, 0x02, "G2"),
        (1, 0x04, "G3"),
        (1, 0x08, "G4"),
        (1, 0x10, "G5"),
        (1, 0x20, "G6"),
        (1, 0x40, "G7"),
        (1, 0x80, "G8"),
        (2, 0x01, "G9"),
        (2, 0x02, "G10"),
        (2, 0x04, "G11"),
        (2, 0x08, "G12"),
        (2, 0x10, "G13"),
        (2, 0x20, "G14"),
        (2, 0x40, "G15"),
        (2, 0x80, "G16"),
        (3, 0x01, "G17"),
        (3, 0x02, "G18"),
    };

    private const int ReportId = 0x03;
    private const int ReportLength = 4;

    private byte[] _previousReport = new byte[ReportLength];

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyboardType Device => KeyboardType.G15;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Parse
    //
    // Compares the incoming report against the previous report to detect
    // key state transitions.  Reports whose ID is not 0x03 are discarded
    // without logging.  Returns one HidKeyEventArgs per changed key.
    //
    // report:  The raw report bytes from the device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<HidKeyEventArgs> Parse(byte[] report)
    {
        var results = new List<HidKeyEventArgs>();

        if ((report == null) || (report.Length == 0))
        {
            DebugLog.Write(LogChannel.Input, "G510ReportParser.Parse: null or empty report.", LogLevel.Warn);
            return results;
        }

        if (report[0] != ReportId)
        {
            return results;
        }

        if (report.Length < ReportLength)
        {
            string hex = BitConverter.ToString(report);
            DebugLog.Write(LogChannel.Input,
                $"G510ReportParser.Parse: short 0x03 report length={report.Length} bytes=[{hex}].", LogLevel.Warn);
            return results;
        }

        foreach (var (byteIndex, bitMask, keyName) in KeyMap)
        {
            bool wasPressed = (_previousReport[byteIndex] & bitMask) != 0;
            bool isPressed = (report[byteIndex] & bitMask) != 0;

            if (wasPressed != isPressed)
            {
                DebugLog.Write(LogChannel.Input,
                    $"G510ReportParser.Parse: key='{keyName}' isPressed={isPressed}.", LogLevel.Trace);
                results.Add(new HidKeyEventArgs(keyName, isPressed));
            }
        }

        return results;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateState
    //
    // Advances the previous report to the current report.  Only full-length
    // 0x03 reports are stored; other report types on this device would
    // corrupt transition detection and can be shorter than the G-key report.
    //
    // report:  The raw report bytes from the device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void UpdateState(byte[] report)
    {
        if ((report == null) || (report.Length < ReportLength) || (report[0] != ReportId))
        {
            return;
        }

        _previousReport = report;
    }
}