using Glass.Core;
using Glass.Data.Models;

namespace Glass.Input;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// G15ReportParser
//
// Parses raw HID reports from the Logitech G15 keyboard (046D-C222).
// Report ID is 0x02, 9 bytes total.
// Maintains previous report state to detect press and release transitions.
//
// Bit map:
// Byte 1: bit 0 = G1,  bit 2 = G13
// Byte 2: bit 0 = G7,  bit 1 = G2,  bit 3 = G14
// Byte 3: bit 1 = G8,  bit 2 = G3,  bit 4 = G15
// Byte 4: bit 3 = G4,  bit 4 = G9,  bit 5 = G16  (mask out bit 0)
// Byte 5: bit 3 = G10, bit 4 = G5,  bit 6 = G17
// Byte 6: bit 4 = G11, bit 5 = G6
// Byte 7: bit 5 = G12
// Byte 8: bit 6 = G18
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class G15ReportParser : IParseHidReport
{
    private static readonly (int ByteIndex, int BitMask, string KeyName)[] KeyMap =
    {
        (1, 0x01, "G1"),
        (1, 0x04, "G13"),
        (2, 0x01, "G7"),
        (2, 0x02, "G2"),
        (2, 0x08, "G14"),
        (3, 0x02, "G8"),
        (3, 0x04, "G3"),
        (3, 0x10, "G15"),
        (4, 0x08, "G4"),
        (4, 0x10, "G9"),
        (4, 0x20, "G16"),
        (5, 0x08, "G10"),
        (5, 0x10, "G5"),
        (5, 0x40, "G17"),
        (6, 0x10, "G11"),
        (6, 0x20, "G6"),
        (7, 0x20, "G12"),
        (8, 0x40, "G18"),
    };

    private const int ReportId = 0x02;
    private const int ReportLength = 9;
    private const int Byte4NoiseMask = 0xFE;

    private byte[] _previousReport = new byte[ReportLength];

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyboardType Device => KeyboardType.G15;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Parse
    //
    // Compares the incoming report against the previous report to detect
    // key state transitions. Returns one HidKeyEventArgs per changed key.
    //
    // report:  The raw report bytes from the device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<HidKeyEventArgs> Parse(byte[] report)
    {
        var results = new List<HidKeyEventArgs>();

        if ((report == null) || (report.Length < ReportLength))
        {
            DebugLog.Write($"G15ReportParser.Parse: invalid report length={report?.Length ?? 0}.");
            return results;
        }

        if (report[0] != ReportId)
        {
            return results;
        }

        // Mask out noise bit in byte 4
        byte[] masked = (byte[])report.Clone();
        masked[4] &= Byte4NoiseMask;

        byte[] maskedPrevious = (byte[])_previousReport.Clone();
        maskedPrevious[4] &= Byte4NoiseMask;

        foreach (var (byteIndex, bitMask, keyName) in KeyMap)
        {
            bool wasPressed = (maskedPrevious[byteIndex] & bitMask) != 0;
            bool isPressed = (masked[byteIndex] & bitMask) != 0;

            if (wasPressed != isPressed)
            {
                DebugLog.Write($"G15ReportParser.Parse: key='{keyName}' isPressed={isPressed}.");
                results.Add(new HidKeyEventArgs(keyName, isPressed));
            }
        }

        _previousReport = report;

        return results;
    }
}