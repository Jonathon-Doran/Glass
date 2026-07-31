using Glass.Core.Logging;
using Glass.Network.Protocol.Fields;
using Glass.UI;
using Inference.UI;
using System;
using System.Collections.Generic;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// HexDumpSearch
//
// Query scanning and hex-dump geometry shared by every consumer that searches a packet payload
// and paints the result over a hex dump.  Holds no state; each method takes everything it needs
// as a parameter so the same logic serves the opcode trace's per-row dumps and the packet detail
// window's single dump.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class HexDumpSearch
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // FindMatches
    //
    // Scans the leading scanLength bytes of the payload for both byte forms of a query and
    // returns every match as a half-open byte range, in ascending order of start offset.  Both
    // forms are tested at each byte position rather than in separate passes, so the returned
    // order is ascending regardless of which form produced a match; a caller that walks matches
    // in order would otherwise step backwards.  A position matching both forms yields two
    // ranges.  The hex form compares exactly; the ASCII form folds 'A'-'Z' to 'a'-'z' on both
    // sides.  A null or empty pattern is skipped, and a pattern longer than the remaining bytes
    // cannot match at that position.
    //
    // payload:       The bytes to scan.
    // scanLength:    How many leading bytes of the payload are in scope, which may be fewer than
    //                the payload holds when the dump is capped.
    // hexPattern:    The query's parsed hex byte form, or null when the query is not hex.
    // asciiPattern:  The query's ASCII byte form, or null when there is no query.
    //
    // Returns the matching byte ranges in ascending start order, empty when nothing matches.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static List<ByteRange> FindMatches(
        ReadOnlySpan<byte> payload,
        int scanLength,
        byte[]? hexPattern,
        byte[]? asciiPattern)
    {
        List<ByteRange> matches = new List<ByteRange>();

        if (scanLength <= 0)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "HexDumpSearch.FindMatches: scanLength " + scanLength + ", nothing to scan",
                LogLevel.Trace);
            return matches;
        }

        if (scanLength > payload.Length)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "HexDumpSearch.FindMatches: scanLength " + scanLength
                + " exceeds payload length " + payload.Length + ", clamping", LogLevel.Warn);
            scanLength = payload.Length;
        }

        if ((hexPattern == null || hexPattern.Length == 0)
            && (asciiPattern == null || asciiPattern.Length == 0))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "HexDumpSearch.FindMatches: no usable pattern, nothing to scan", LogLevel.Trace);
            return matches;
        }

        for (int scanPos = 0; scanPos < scanLength; scanPos++)
        {
            for (int patternIndex = 0; patternIndex < 2; patternIndex++)
            {
                byte[]? pattern;
                bool caseInsensitive;
                if (patternIndex == 0)
                {
                    pattern = hexPattern;
                    caseInsensitive = false;
                }
                else
                {
                    pattern = asciiPattern;
                    caseInsensitive = true;
                }

                if (pattern == null || pattern.Length == 0)
                {
                    continue;
                }

                if (scanPos + pattern.Length > scanLength)
                {
                    continue;
                }

                bool matched = true;
                for (int q = 0; q < pattern.Length; q++)
                {
                    byte payloadByte = payload[scanPos + q];
                    byte queryByte = pattern[q];
                    if (caseInsensitive)
                    {
                        if (payloadByte >= (byte)'A' && payloadByte <= (byte)'Z')
                        {
                            payloadByte = (byte)(payloadByte + 32);
                        }
                        if (queryByte >= (byte)'A' && queryByte <= (byte)'Z')
                        {
                            queryByte = (byte)(queryByte + 32);
                        }
                    }
                    if (payloadByte != queryByte)
                    {
                        matched = false;
                        break;
                    }
                }

                if (!matched)
                {
                    continue;
                }

                matches.Add(new ByteRange((uint)scanPos, (uint)(scanPos + pattern.Length)));

                DebugLog.Write(LogChannel.Opcodes,
                    "HexDumpSearch.FindMatches: pattern " + patternIndex + " matched at byte "
                    + scanPos + " for " + pattern.Length + " byte(s)", LogLevel.Trace);
            }
        }

        DebugLog.Write(LogChannel.Opcodes,
            "HexDumpSearch.FindMatches: " + matches.Count + " match(es) over " + scanLength
            + " scanned byte(s)", LogLevel.Trace);

        return matches;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildSpans
    //
    // Converts a matched byte range into the highlight spans that cover it in a formatted hex
    // dump.  A match confined to one dump line yields two spans, one over its hex cells and one
    // over its ASCII gutter characters; a match crossing a line boundary yields that pair for
    // each line it touches, because the dump interleaves the two columns per line and a single
    // span cannot straddle them.  Character offsets are absolute within the dump text, derived
    // from the layout constants, so a caller can apply them directly.
    //
    // The hex span stops at the last cell's final digit rather than including that cell's
    // trailing space, and widens by the mid-line gap when the covered cells straddle the halfway
    // point, so the painted region matches exactly the digits of the matched bytes.
    //
    // match:       The matched byte range, half-open.
    // color:       Color to record on every emitted span.
    // generation:  Highlight generation to stamp on every emitted span.
    //
    // Returns the spans covering the match, empty when the range is empty.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static List<HighlightSpan> BuildSpans(ByteRange match, ArgbColor color, uint generation)
    {
        List<HighlightSpan> spans = new List<HighlightSpan>();

        if (match.End <= match.Start)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "HexDumpSearch.BuildSpans: empty range start=" + match.Start + " end=" + match.End
                + ", no spans", LogLevel.Warn);
            return spans;
        }

        int matchStart = (int)match.Start;
        int matchEnd = (int)match.End;

        int firstLine = matchStart / HexDumpFormatter.BytesPerLine;
        int lastLine = (matchEnd - 1) / HexDumpFormatter.BytesPerLine;

        for (int lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
        {
            int lineByteStart = lineIndex * HexDumpFormatter.BytesPerLine;
            int lineByteEnd = lineByteStart + HexDumpFormatter.BytesPerLine;

            int sliceStart = Math.Max(matchStart, lineByteStart);
            int sliceEnd = Math.Min(matchEnd, lineByteEnd);
            int sliceLength = sliceEnd - sliceStart;

            int withinLineByteIndex = sliceStart - lineByteStart;

            int hexStartInLine = HexDumpFormatter.HexColumnOffset
                + (withinLineByteIndex * HexDumpFormatter.HexCellWidth);
            if (withinLineByteIndex >= HexDumpFormatter.HalfLineByteIndex)
            {
                hexStartInLine += HexDumpFormatter.MidLineGapWidth;
            }

            int hexLength = (sliceLength * HexDumpFormatter.HexCellWidth) - 1;

            int sliceLastByteIndex = withinLineByteIndex + sliceLength - 1;
            if (withinLineByteIndex < HexDumpFormatter.HalfLineByteIndex
                && sliceLastByteIndex >= HexDumpFormatter.HalfLineByteIndex)
            {
                hexLength += HexDumpFormatter.MidLineGapWidth;
            }

            int lineStartInString = lineIndex * HexDumpFormatter.LineWidth;

            spans.Add(new HighlightSpan(
                lineStartInString + hexStartInLine, hexLength, color, generation));

            int asciiStartInLine = HexDumpFormatter.AsciiColumnOffset + withinLineByteIndex;
            spans.Add(new HighlightSpan(
                lineStartInString + asciiStartInLine, sliceLength, color, generation));

            DebugLog.Write(LogChannel.Opcodes,
                "HexDumpSearch.BuildSpans: line " + lineIndex + " covers byte(s) " + sliceStart
                + "-" + (sliceEnd - 1) + ", hex span at " + (lineStartInString + hexStartInLine)
                + " length " + hexLength + ", ascii span at "
                + (lineStartInString + asciiStartInLine) + " length " + sliceLength,
                LogLevel.Trace);
        }

        DebugLog.Write(LogChannel.Opcodes,
            "HexDumpSearch.BuildSpans: bytes " + matchStart + "-" + (matchEnd - 1) + " over line(s) "
            + firstLine + "-" + lastLine + " produced " + spans.Count + " span(s)", LogLevel.Trace);

        return spans;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TryParseHexQuery
    //
    // Determines whether a query string should be interpreted as a hex byte sequence.  The rule
    // is strict: the query must contain at least two whitespace-separated tokens, and every token
    // must be exactly two hexadecimal digits.  Single-token inputs like "BEEF" are not hex — they
    // fall through to the caller's ASCII interpretation.  This avoids the ambiguity where a
    // four-letter word could be a hex value or could be a real word in a packet payload.
    //
    // The query is normalized by splitting on whitespace and ignoring empty tokens, so multiple
    // spaces between bytes are tolerated.
    //
    // query:  The raw user input from a find bar.
    //
    // Returns a byte array containing the parsed hex bytes, or null if the query is not a valid
    // hex sequence.  An empty or whitespace-only query returns null.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static byte[]? TryParseHexQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "HexDumpSearch.TryParseHexQuery: empty query", LogLevel.Error);
            return null;
        }

        string[] tokens = query.Split(
            new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length < 2)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "HexDumpSearch.TryParseHexQuery: single-token query '" + query
                + "' treated as ASCII", LogLevel.Warn);
            return null;
        }

        byte[] result = new byte[tokens.Length];

        for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            string token = tokens[tokenIndex];

            if (token.Length != 2)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HexDumpSearch.TryParseHexQuery: token '" + token
                    + "' is not 2 chars, query treated as ASCII", LogLevel.Warn);
                return null;
            }

            byte parsed;
            if (!byte.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out parsed))
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HexDumpSearch.TryParseHexQuery: token '" + token
                    + "' is not hex, query treated as ASCII", LogLevel.Warn);
                return null;
            }

            result[tokenIndex] = parsed;
        }

        DebugLog.Write(LogChannel.Opcodes,
            "HexDumpSearch.TryParseHexQuery: parsed " + tokens.Length + " hex bytes",
            LogLevel.Trace);
        return result;
    }
}