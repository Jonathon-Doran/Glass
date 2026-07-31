using System;
using System.Text;

namespace Inference.UI;

///////////////////////////////////////////////////////////////////////////////////////////////
// HexDumpFormatter
//
// Formats a packet payload as a hex dump in canonical "hexdump -C" format.
// Layout: an 8-hex-digit offset, two columns of 8
// bytes each separated by an extra space, then an ASCII gutter
// showing printable bytes and '.' for everything else.  Highlighting
// is not applied here; callers that need highlights overlay them
// against this output.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class HexDumpFormatter
{
    // The canonical "hexdump -C" line layout, as emitted by Format.  Consumers that map a byte
    // offset to a character offset within the formatted text derive their arithmetic from these,
    // so the layout is defined in exactly one place.  Offsets are character counts from the start
    // of a line; LineWidth includes the trailing newline, so line N begins at N * LineWidth.
    public const int BytesPerLine = 16;
    public const int HalfLineByteIndex = 8;
    public const int OffsetWidth = 8;
    public const int OffsetGapWidth = 2;
    public const int HexCellWidth = 3;
    public const int MidLineGapWidth = 1;
    public const int GutterBarWidth = 1;
    public const int NewlineWidth = 1;
    public const string OffsetFormat = "x8";

    public const int HexColumnOffset = OffsetWidth + OffsetGapWidth;

    public const int AsciiColumnOffset = HexColumnOffset + (BytesPerLine * HexCellWidth)
        + MidLineGapWidth + GutterBarWidth;

    public const int LineWidth = AsciiColumnOffset + BytesPerLine + GutterBarWidth + NewlineWidth;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Format
    //
    // Formats a payload span as hexdump -C output capped at maxBytes.
    // When the payload is longer than maxBytes, the trailing line
    // "[showing first N of M bytes]" is appended.  When maxBytes is
    // int.MaxValue the cap is treated as effectively unlimited and the
    // trailing line is suppressed.  Line geometry comes from the layout
    // constants, so every emitted line is exactly LineWidth characters
    // including its newline.
    //
    // payload:   The bytes to format.
    // maxBytes:  Maximum bytes to render.  Pass int.MaxValue for no cap.
    //
    // returns:   The formatted hex dump.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static string Format(ReadOnlySpan<byte> payload, uint maxBytes)
    {
        uint payloadLength = (uint)payload.Length;
        uint displayLength;
        if (maxBytes == int.MaxValue || payloadLength <= maxBytes)
        {
            displayLength = payloadLength;
        }
        else
        {
            displayLength = maxBytes;
        }

        StringBuilder sb = new StringBuilder((int)displayLength * 4 + 80);

        int offset = 0;
        while (offset < displayLength)
        {
            int bytesThisRow = Math.Min(BytesPerLine, (int)displayLength - offset);

            sb.Append(offset.ToString(OffsetFormat));
            for (int p = 0; p < OffsetGapWidth; p++)
            {
                sb.Append(' ');
            }

            for (int i = 0; i < BytesPerLine; i++)
            {
                if (i == HalfLineByteIndex)
                {
                    for (int p = 0; p < MidLineGapWidth; p++)
                    {
                        sb.Append(' ');
                    }
                }

                if (i < bytesThisRow)
                {
                    sb.Append(payload[offset + i].ToString("x2"));
                    sb.Append(' ');
                }
                else
                {
                    for (int p = 0; p < HexCellWidth; p++)
                    {
                        sb.Append(' ');
                    }
                }
            }

            sb.Append('|');
            for (int i = 0; i < bytesThisRow; i++)
            {
                byte b = payload[offset + i];
                char c;
                if (b >= 0x20 && b <= 0x7e)
                {
                    c = (char)b;
                }
                else
                {
                    c = '.';
                }
                sb.Append(c);
            }

            // Pad the ascii gutter to a full line's worth of columns when the row is short, so
            // every rendered line is the same width and the closing '|' aligns.
            for (int i = bytesThisRow; i < BytesPerLine; i++)
            {
                sb.Append(' ');
            }

            sb.Append('|');
            sb.Append('\n');

            offset = offset + BytesPerLine;
        }

        if (displayLength < payloadLength)
        {
            sb.Append("[showing first ");
            sb.Append(displayLength);
            sb.Append(" of ");
            sb.Append(payloadLength);
            sb.Append(" bytes]\n");
        }

        return sb.ToString();
    }
}
