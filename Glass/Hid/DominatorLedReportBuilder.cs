using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;

namespace Glass.Input;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// DominatorLedReportBuilder
//
// Builds LED row-paint OUT reports for one Dominator X36 (0483-5750) instance.
// Holds the current color of every key so a single changed key can be
// reconciled against its unchanged row-mates. One instance per physical
// device — not shared across multiple Dominators.
//
// Report ID is 0x02, 20 bytes total: report ID, row index, then 6 columns
// of (R, G, B) in column order 0-5.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class DominatorLedReportBuilder : IBuildLedReport
{
    private static readonly (string KeyName, int Row, int Col)[] KeyMap =
    {
        ("X-1", 0, 0),  ("X-2", 0, 1),  ("X-3", 0, 2),  ("X-4", 0, 3),  ("X-5", 0, 4),  ("X-6", 0, 5),
        ("X-7", 1, 0),  ("X-8", 1, 1),  ("X-9", 1, 2),  ("X-10", 1, 3), ("X-11", 1, 4), ("X-12", 1, 5),
        ("X-13", 2, 0), ("X-14", 2, 1), ("X-15", 2, 2), ("X-16", 2, 3), ("X-17", 2, 4), ("X-18", 2, 5),
        ("X-19", 3, 0), ("X-20", 3, 1), ("X-21", 3, 2), ("X-22", 3, 3), ("X-23", 3, 4), ("X-24", 3, 5),
        ("X-25", 4, 0), ("X-26", 4, 1), ("X-27", 4, 2), ("X-28", 4, 3), ("X-29", 4, 4), ("X-30", 4, 5),
        ("X-31", 5, 0), ("X-32", 5, 1), ("X-33", 5, 2), ("X-34", 5, 3), ("X-35", 5, 4), ("X-36", 5, 5),
    };

    private const int ReportId = 0x02;
    private const int ReportLength = 20;
    private const int RowCount = 6;
    private const int ColumnCount = 6;

    private readonly Dictionary<string, (int Row, int Col)> _keyPositions;
    private readonly (byte R, byte G, byte B)[,] _currentColors = new (byte, byte, byte)[RowCount, ColumnCount];

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DominatorLedReportBuilder
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public DominatorLedReportBuilder()
    {
        _keyPositions = new Dictionary<string, (int Row, int Col)>();

        foreach (var (keyName, row, col) in KeyMap)
        {
            _keyPositions[keyName] = (row, col);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Device
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public KeyboardType Device => KeyboardType.DominatorX36;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetColors
    //
    // Updates the given keys' colors and returns the raw OUT report(s) needed
    // to make the device reflect just this change. Unknown key names are
    // logged and skipped.
    //
    // changedColors:  One or more key names mapped to their new (R, G, B) color
    // Returns:         The raw report(s) to send, one per affected row
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<byte[]> SetColors(IReadOnlyDictionary<string, (byte R, byte G, byte B)> changedColors)
    {
        HashSet<int> touchedRows = new HashSet<int>();

        foreach (var (keyName, color) in changedColors)
        {
            if (!_keyPositions.TryGetValue(keyName, out (int Row, int Col) position))
            {
                DebugLog.Write(LogChannel.Input, $"DominatorLedReportBuilder.SetColors: unknown key='{keyName}', ignoring.", LogLevel.Warn);
                continue;
            }

            _currentColors[position.Row, position.Col] = color;
            touchedRows.Add(position.Row);

            DebugLog.Write(LogChannel.Input, $"DominatorLedReportBuilder.SetColors: key='{keyName}' row={position.Row} col={position.Col} color=({color.R},{color.G},{color.B}).", LogLevel.Trace);
        }

        List<byte[]> reports = new List<byte[]>();

        foreach (int row in touchedRows)
        {
            reports.Add(BuildRowReport(row));
        }

        return reports;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // BuildRowReport
    //
    // Builds the 20-byte row-paint report for the given row from the current
    // color cache.
    //
    // row:      The row index to paint, 0-5
    // Returns:  The raw report bytes, including the leading report ID byte
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private byte[] BuildRowReport(int row)
    {
        byte[] report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = (byte)row;

        int offset = 2;
        for (int col = 0; col < ColumnCount; col++)
        {
            (byte R, byte G, byte B) color = _currentColors[row, col];
            report[offset++] = color.R;
            report[offset++] = color.G;
            report[offset++] = color.B;
        }

        return report;
    }
}