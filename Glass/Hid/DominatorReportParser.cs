using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;

namespace Glass.Input;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// DominatorReportParser
//
// Parses raw HID reports from the Dominator X36 (0483-5750).
// Report ID is 0x01, 6 bytes total.
// 36 keys (X-1..X-36) as a little-endian bitmap starting at byte 1, bit 0 = X-1.
// Byte 5 bits 4-7 are padding and always 0.
// Maintains previous report state to detect key press/release transitions.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class DominatorReportParser : IParseHidReport
{
    private const int ReportId = 0x01;
    private const int ReportLength = 6;
    private const int KeyCount = 36;
    private const int FirstKeyByte = 1;

    private byte[] _previousReport = new byte[ReportLength];

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyboardType Device => KeyboardType.DominatorX36;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Parse
    //
    // Compares the incoming report against the previous report to detect
    // key state transitions. Returns one HidKeyEventArgs per changed key.
    //
    // report:  The raw report bytes from the device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<HidKeyEventArgs> Parse(byte[] report)
    {
        List<HidKeyEventArgs> results = new List<HidKeyEventArgs>();

        if ((report == null) || (report.Length < ReportLength))
        {
            DebugLog.Write(LogChannel.Input, $"DominatorReportParser.Parse: invalid report length={report?.Length ?? 0}.", LogLevel.Warn);
            return results;
        }

        if (report[0] != ReportId)
        {
            return results;
        }

        for (int keyIndex = 0; keyIndex < KeyCount; keyIndex++)
        {
            int byteIndex = FirstKeyByte + (keyIndex / 8);
            int bitMask = 1 << (keyIndex % 8);

            bool wasPressed = (_previousReport[byteIndex] & bitMask) != 0;
            bool isPressed = (report[byteIndex] & bitMask) != 0;

            if (wasPressed != isPressed)
            {
                string keyName = $"X-{keyIndex + 1}";
                DebugLog.Write(LogChannel.Input, $"DominatorReportParser.Parse: key='{keyName}' isPressed={isPressed}.", LogLevel.Info);
                results.Add(new HidKeyEventArgs(keyName, isPressed));
            }
        }

        return results;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // UpdateState
    //
    // Advances the previous report to the current report.
    // Must be called after Parse to prepare for the next report.
    //
    // report:  The raw report bytes from the device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void UpdateState(byte[] report)
    {
        _previousReport = report;
    }
}
