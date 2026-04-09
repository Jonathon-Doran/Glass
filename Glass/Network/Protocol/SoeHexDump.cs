using System;
using System.Text;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// SoeHexDump
//
// Formats binary data identically to the Python hexdump function:
// offset, hex (8+8 with double space), ASCII.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class SoeHexDump
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Format
    //
    // Formats binary data like hexdump -C.
    //
    // data:    The bytes to format
    // indent:  Prefix for each line (default 10 spaces to match Python)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string Format(ReadOnlySpan<byte> data, string indent = "")
    {
        if (data.Length == 0)
        {
            return indent + "(no data)";
        }

        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < data.Length; i += 16)
        {
            int remaining = data.Length - i;
            int chunkLen = remaining < 16 ? remaining : 16;

            if (i > 0)
            {
                sb.Append('\n');
            }

            sb.Append(indent);
            sb.Append(i.ToString("x8"));
            sb.Append("  ");

            // Hex part: first 8 bytes
            int firstHalf = chunkLen < 8 ? chunkLen : 8;
            for (int j = 0; j < firstHalf; j++)
            {
                if (j > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(data[i + j].ToString("x2"));
            }

            // Double space between halves
            sb.Append("  ");

            // Hex part: second 8 bytes
            if (chunkLen > 8)
            {
                for (int j = 8; j < chunkLen; j++)
                {
                    if (j > 8)
                    {
                        sb.Append(' ');
                    }
                    sb.Append(data[i + j].ToString("x2"));
                }
            }

            // Pad hex to 49 characters
            int hexLen = 0;
            if (chunkLen <= 8)
            {
                hexLen = (firstHalf * 3 - 1) + 2;
            }
            else
            {
                hexLen = (8 * 3 - 1) + 2 + ((chunkLen - 8) * 3 - 1);
            }
            for (int p = hexLen; p < 49; p++)
            {
                sb.Append(' ');
            }

            // ASCII part
            sb.Append(" |");
            for (int j = 0; j < chunkLen; j++)
            {
                byte b = data[i + j];
                if (b >= 32 && b < 127)
                {
                    sb.Append((char)b);
                }
                else
                {
                    sb.Append('.');
                }
            }
            sb.Append('|');
        }

        return sb.ToString();
    }
}