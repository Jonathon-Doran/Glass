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
    ///////////////////////////////////////////////////////////////////////////////////////////
    // Format
    //
    // Formats a payload span as hexdump -C output capped at maxBytes.
    // When the payload is longer than maxBytes, the trailing line
    // "[showing first N of M bytes]" is appended.  When maxBytes is
    // int.MaxValue the cap is treated as effectively unlimited and the
    // trailing line is suppressed.
    //
    // payload:   The bytes to format.
    // maxBytes:  Maximum bytes to render.  Pass int.MaxValue for no cap.
    //
    // returns:   The formatted hex dump.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static string Format(ReadOnlySpan<byte> payload, uint maxBytes)
    {
        uint payloadLength = (uint) payload.Length;
        uint displayLength;
        if (maxBytes == int.MaxValue || payloadLength <= maxBytes)
        {
            displayLength = payloadLength;
        }
        else
        {
            displayLength = maxBytes;
        }

        StringBuilder sb = new StringBuilder((int) displayLength * 4 + 80);

        int offset = 0;
        while (offset < displayLength)
        {
            int bytesThisRow = Math.Min(16, (int)displayLength - offset);

            sb.Append(offset.ToString("x8"));
            sb.Append("  ");

            for (int i = 0; i < 16; i++)
            {
                if (i == 8)
                {
                    sb.Append(' ');
                }

                if (i < bytesThisRow)
                {
                    sb.Append(payload[offset + i].ToString("x2"));
                    sb.Append(' ');
                }
                else
                {
                    sb.Append("   ");
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

            // Pad the ascii gutter to a full 16 columns when the row is short, so every rendered
            // line is the same width and the closing '|' aligns.
            for (int i = bytesThisRow; i < 16; i++)
            {
                sb.Append(' ');
            }

            sb.Append('|');
            sb.Append('\n');

            offset = offset + 16;
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
