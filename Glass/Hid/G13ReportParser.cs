using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;

namespace Glass.Input;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// G13ReportParser
//
// Parses raw HID reports from the Logitech G13 Advanced Gameboard (046D-C21C).
// Report ID is 0x01, 8 bytes total.
// Bytes 1-2 are analog joystick axes (center=0x7F).
// Maintains previous report state to detect digital key press/release transitions.
// Analog axis changes are reported separately via axis events.
//
// Bit map:
// Byte 1: JoystickX (analog)
// Byte 2: JoystickY (analog)
// Byte 3: bit 0=G1,  bit 1=G2,  bit 2=G3,  bit 3=G4,  bit 4=G5,  bit 5=G6,  bit 6=G7,  bit 7=G8
// Byte 4: bit 0=G9,  bit 1=G10, bit 2=G11, bit 3=G12, bit 4=G13, bit 5=G14, bit 6=G15, bit 7=G16
// Byte 5: bit 0=G17, bit 1=G18, bit 2=G19, bit 3=G20, bit 4=G21, bit 5=G22  (mask out bit 7)
// Byte 6: bit 0=Applet, bit 1=L1, bit 2=L2, bit 3=L3, bit 4=L4, bit 5=M1, bit 6=M2, bit 7=M3
// Byte 7: bit 0=MR, bit 1=G23, bit 2=G24  (mask out bit 7)
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class G13ReportParser : IParseHidReport, IParseHidAxes
{
    private static readonly (int ByteIndex, int BitMask, string KeyName)[] KeyMap =
    {
        // G-keys
        (3, 0x01, "G1"),
        (3, 0x02, "G2"),
        (3, 0x04, "G3"),
        (3, 0x08, "G4"),
        (3, 0x10, "G5"),
        (3, 0x20, "G6"),
        (3, 0x40, "G7"),
        (3, 0x80, "G8"),
        (4, 0x01, "G9"),
        (4, 0x02, "G10"),
        (4, 0x04, "G11"),
        (4, 0x08, "G12"),
        (4, 0x10, "G13"),
        (4, 0x20, "G14"),
        (4, 0x40, "G15"),
        (4, 0x80, "G16"),
        (5, 0x01, "G17"),
        (5, 0x02, "G18"),
        (5, 0x04, "G19"),
        (5, 0x08, "G20"),
        (5, 0x10, "G21"),
        (5, 0x20, "G22"),
        // Thumb buttons
        (7, 0x02, "G23"),
        (7, 0x04, "G24"),
        // LCD buttons
        (6, 0x01, "Applet"),
        (6, 0x02, "L1"),
        (6, 0x04, "L2"),
        (6, 0x08, "L3"),
        (6, 0x10, "L4"),
        // Mode keys
        (6, 0x20, "M1"),
        (6, 0x40, "M2"),
        (6, 0x80, "M3"),
        (7, 0x01, "MR"),
    };

    private const int ReportId = 0x01;
    private const int ReportLength = 8;
    private const int Byte5NoiseMask = 0x7F;
    private const int Byte7NoiseMask = 0x7F;

    private byte[] _previousReport = new byte[ReportLength];

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyboardType Device => KeyboardType.G13;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateState
    //
    // Advances the previous report to the current report.
    // Must be called after Parse and ParseAxes to prepare for the next report.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void UpdateState(byte[] report)
    {
        _previousReport = report;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Parse
    //
    // Compares the incoming report against the previous report to detect
    // key state transitions. Returns one HidKeyEventArgs per changed key.
    // Analog joystick changes are not returned here.
    //
    // report:  The raw report bytes from the device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<HidKeyEventArgs> Parse(byte[] report)
    {
        var results = new List<HidKeyEventArgs>();

        if ((report == null) || (report.Length < ReportLength))
        {
            DebugLog.Write(LogChannel.Input, $"G13ReportParser.Parse: invalid report length={report?.Length ?? 0}.");
            return results;
        }

        if (report[0] != ReportId)
        {
            return results;
        }

        // Mask out noise bits
        byte[] masked = (byte[])report.Clone();
        masked[5] &= Byte5NoiseMask;
        masked[7] &= Byte7NoiseMask;

        byte[] maskedPrevious = (byte[])_previousReport.Clone();
        maskedPrevious[5] &= Byte5NoiseMask;
        maskedPrevious[7] &= Byte7NoiseMask;

        foreach (var (byteIndex, bitMask, keyName) in KeyMap)
        {
            bool wasPressed = (maskedPrevious[byteIndex] & bitMask) != 0;
            bool isPressed = (masked[byteIndex] & bitMask) != 0;

            if (wasPressed != isPressed)
            {
                DebugLog.Write(LogChannel.Input, $"G13ReportParser.Parse: key='{keyName}' isPressed={isPressed}.");
                results.Add(new HidKeyEventArgs(keyName, isPressed));
            }
        }

        return results;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ParseAxes
    //
    // Detects changes in the analog joystick axes and returns axis events.
    // JoystickX is byte 1, JoystickY is byte 2, center value is 0x7F.
    //
    // report:  The raw report bytes from the device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<HidAxisEventArgs> ParseAxes(byte[] report)
    {
        var results = new List<HidAxisEventArgs>();

        if ((report == null) || (report.Length < ReportLength))
        {
            return results;
        }

        if (report[0] != ReportId)
        {
            return results;
        }

        byte currentX = report[1];
        byte currentY = report[2];
        byte previousX = _previousReport[1];
        byte previousY = _previousReport[2];

        if (currentX != previousX)
        {
            results.Add(new HidAxisEventArgs("JoystickX", currentX, previousX));
        }

        if (currentY != previousY)
        {
            results.Add(new HidAxisEventArgs("JoystickY", currentY, previousY));
        }

        return results;
    }
}
