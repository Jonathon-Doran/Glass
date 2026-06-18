using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Inference.Models;
using Inference.UI;
using System;
using System.Text;
using System.Windows;
using static Glass.Network.Protocol.SoeConstants;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// PacketDetailWindow
//
// Modeless display window for a single captured packet.  Snapshots the
// payload and metadata at construction and renders a header strip with
// the packet's identifying details, a left pane with the field
// decoding produced by the field extractor against the active patch
// level, and a right pane with the full uncapped hex dump.  The two
// panes are independent ScrollViewers separated by a GridSplitter.
// The contents are read-only but selectable so the user can copy text
// out of either pane.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class PacketDetailWindow : Window
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // PacketDetailWindow (constructor)
    //
    // Captures the packet's identifying details into the header strip,
    // runs the field extractor against the active patch level and
    // writes the formatted result into the left pane, and formats the
    // full payload as an uncapped hex dump into the right pane.  Rows
    // whose opcode is not in the active patch leave the field pane
    // empty; the hex pane is always populated.
    //
    // packet:  The cataloged packet to display.  Retained by the
    //          window's bindings for the lifetime of the window.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public PacketDetailWindow(CatalogedPacket packet)
    {
        InitializeComponent();

        ReadOnlySpan<byte> payload = packet.Payload.AsReadOnlySpan();
        int payloadLength = payload.Length;

        // note on version:  This usage is ok because we do not need to know the exact version
        // when obtaining the opcode name.  V1's output is identical to all other versions.

        PatchOpcode patchOpcode = new PatchOpcode(GlassContext.CurrentPatchLevel, packet.Opcode);
        string opcodeName = GlassContext.PatchRegistry.GetOpcodeName(patchOpcode);
        string opcodeHex = "0x" + packet.Opcode;
        string characterName = ResolveCharacterName(packet.Metadata.SessionId);

        Title = "Packet " + packet.PacketIndex + " — " + opcodeName;

        HeaderPacketIndex.Text = packet.PacketIndex.ToString();
        HeaderTimestamp.Text = packet.Metadata.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
        HeaderLength.Text = payloadLength.ToString() + " bytes";
        HeaderOpcode.Text = opcodeHex + "  " + opcodeName;
        HeaderChannel.Text = StreamAbbrev[packet.Metadata.Channel];
        HeaderCharacter.Text = characterName;

        DebugLog.Write(LogChannel.InferenceDebug,
            "PacketDetailWindow: opening packetIndex=" + packet.PacketIndex
            + " opcode=" + opcodeHex + " (" + opcodeName + ")"
            + " length=" + payloadLength);

        FieldTextBox.Text = ExtractFieldText(packet.Metadata.Opcode, payload);
        HexDumpTextBox.Text = HexDumpFormatter.Format(payload, int.MaxValue);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolveCharacterName
    //
    // Returns the character name associated with the given session id by
    // asking the SessionRegistry directly.  Returns empty string when no
    // session is associated with the packet (sessionId negative) or when
    // the registry has no name for that session.
    //
    // sessionId:  Session id from the packet's metadata.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string ResolveCharacterName(int sessionId)
    {
        if (sessionId < 0)
        {
            return string.Empty;
        }

        string? name = GlassContext.SessionRegistry.CharacterNameFromSession(sessionId);
        if (name == null)
        {
            return string.Empty;
        }

        return name;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExtractFieldText
    //
    // Runs the field extractor against the payload using the active patch level and returns
    // the formatted "name = value" text, one binding per line.  Returns empty string when the
    // opcode is not present in the active patch.
    //
    // For opcode 0x7E9B the payload carries a count-prefixed run of items, each beginning with
    // an ItemString.  Each ItemString offset is found and the "Inventory Top-Level" collection
    // is extracted against the payload sliced at that offset, so each item decodes from the
    // start of its own bytes.  Items are emitted in wire order, separated by a header line
    // carrying the item ordinal and byte offset.  For every other opcode the collection is
    // extracted once against the whole payload.
    //
    // patchOpcode:  The packet's versioned opcode.
    // payload:      Bytes to extract from.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string ExtractFieldText(PatchOpcode patchOpcode, ReadOnlySpan<byte> payload)
    {
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;
        PatchRegistry registry = GlassContext.PatchRegistry;

        string opcodeName = registry.GetOpcodeName(patchOpcode);
        CollectionHandle collectionHandle = registry.GetOpcodeCollection(patchOpcode);

        if (!collectionHandle.Exists)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PacketDetailWindow.ExtractFieldText: opcode=0x" + patchOpcode
                + " not in active patch, no fields available");
            return string.Empty;
        }

        if (patchOpcode.Value.Value == 0x7E9B)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PacketDetailWindow.ExtractFieldText: opcode=0x7E9B, segmenting on ItemString");
            return ExtractInventoryItems(patchLevel, collectionHandle, patchOpcode, payload);
        }

        /*
                FieldBag bag = registry.Rent(collectionHandle);
                try
                {
                    GlassContext.FieldExtractor.ExtractCollection(patchLevel, collectionHandle, payload, bag);

                    StringBuilder sb = new StringBuilder();
                    BagWalker walker = bag.Walk();
                    FieldBinding? binding = walker.Next();
                    int bindingCount = 0;
                    while (binding != null)
                    {
                        FieldBinding b = binding.Value;
                        sb.Append(b.Name);
                        sb.Append(" = ");
                        sb.Append(b.Value);
                        sb.Append('\n');
                        binding = walker.Next();
                        bindingCount++;
                    }

                    DebugLog.Write(LogChannel.InferenceDebug,
                        "PacketDetailWindow.ExtractFieldText: opcode=0x" + patchOpcode
                        + " extracted " + bindingCount + " bindings");
                    return sb.ToString();
                }
                finally
                {
                    bag.Release();
                }
        */
        return "";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExtractInventoryItems
    //
    // Decodes the items in a 0x7E9B inventory payload by segmenting on the ItemString that
    // begins each item.  Scans the payload for each 16-byte ItemString, slices the payload at
    // that offset, and extracts the given collection against the slice so each item decodes
    // from the start of its own bytes.  Bindings for each item are appended under a header line
    // carrying the item ordinal and the byte offset of its ItemString.  Items appear in wire
    // order.
    //
    // patchLevel:   The active patch level.
    // collection:   The collection extracted against each item slice.
    // patchOpcode:  The packet's versioned opcode, used to rent a bag.
    // payload:      The full inventory payload.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string ExtractInventoryItems(PatchLevel patchLevel, CollectionHandle collection,
        PatchOpcode patchOpcode, ReadOnlySpan<byte> payload)
    {
        /*
        StringBuilder sb = new StringBuilder();
        int itemOrdinal = 0;
        int scanOffset = 0;

        while (scanOffset < payload.Length)
        {
            int anchorOffset = FindNextItemString(payload, scanOffset);
            if (anchorOffset < 0)
            {
                break;
            }

            sb.Append("---- item ");
            sb.Append(itemOrdinal);
            sb.Append(" at 0x");
            sb.Append(anchorOffset.ToString("x"));
            sb.Append(" ----\n");

            ReadOnlySpan<byte> itemSlice = payload.Slice(anchorOffset);

            FieldBag bag = GlassContext.PatchRegistry.Rent(collection);
            try
            {
                GlassContext.FieldExtractor.ExtractCollection(patchLevel, collection, itemSlice, bag);

                BagWalker walker = bag.Walk();
                FieldBinding? binding = walker.Next();
                while (binding != null)
                {
                    FieldBinding b = binding.Value;
                    sb.Append(b.Name);
                    sb.Append(" = ");
                    sb.Append(b.Value);
                    sb.Append('\n');
                    binding = walker.Next();
                }
            }
            finally
            {
                bag.Release();
            }

            itemOrdinal++;
            scanOffset = anchorOffset + 16;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PacketDetailWindow.ExtractInventoryItems: segmented " + itemOrdinal + " items");
        return sb.ToString();
        */

        return "";
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FindNextItemString
    //
    // Scans the payload forward from startOffset for the next item-key string: a run of exactly
    // sixteen printable ASCII bytes (0x20 through 0x7E) that contains no space, is immediately
    // followed by a non-printable byte, and is preceded by a non-printable byte or the payload
    // start.  This is the on-wire signature of the inventory item-key string (the "D9" string),
    // sixteen characters terminated by a null.
    //
    // The no-space rule separates the item-key serials from sixteen-character item display names
    // that also appear in the payload; display names contain spaces, serials do not.
    //
    // Returns the byte offset of the first character of the matched run, or -1 when no further
    // match exists from startOffset to the end of the payload.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static int FindNextItemString(ReadOnlySpan<byte> payload, int startOffset)
    {
        DebugLog.Write(LogChannel.InferenceDebug,
            "PacketDetailWindow.FindNextItemString: scanning from offset 0x"
            + startOffset.ToString("x") + " in payload of " + payload.Length + " bytes");

        if (startOffset < 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PacketDetailWindow.FindNextItemString: negative startOffset, returning -1");
            return -1;
        }

        int scanOffset = startOffset;
        while (scanOffset + 16 <= payload.Length)
        {
            bool runIsAllPrintable = true;
            bool runHasSpace = false;
            for (int runIndex = 0; runIndex < 16; runIndex++)
            {
                byte candidate = payload[scanOffset + runIndex];
                if (candidate < 0x20 || candidate > 0x7E)
                {
                    runIsAllPrintable = false;
                    break;
                }
                if (candidate == 0x20)
                {
                    runHasSpace = true;
                }
            }

            if (runIsAllPrintable == false)
            {
                scanOffset = scanOffset + 1;
                continue;
            }

            if (runHasSpace == true)
            {
                scanOffset = scanOffset + 1;
                continue;
            }

            int followingIndex = scanOffset + 16;
            bool boundedByNonPrintable;
            if (followingIndex >= payload.Length)
            {
                boundedByNonPrintable = true;
            }
            else
            {
                byte following = payload[followingIndex];
                boundedByNonPrintable = following < 0x20 || following > 0x7E;
            }

            if (boundedByNonPrintable == false)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PacketDetailWindow.FindNextItemString: 16-char run at 0x"
                    + scanOffset.ToString("x") + " is followed by a printable byte, run is longer than 16, skipping");
                scanOffset = scanOffset + 1;
                continue;
            }

            bool precededByNonPrintable;
            if (scanOffset == 0)
            {
                precededByNonPrintable = true;
            }
            else
            {
                byte preceding = payload[scanOffset - 1];
                precededByNonPrintable = preceding < 0x20 || preceding > 0x7E;
            }

            if (precededByNonPrintable == false)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PacketDetailWindow.FindNextItemString: 16-char run at 0x"
                    + scanOffset.ToString("x") + " is preceded by a printable byte, not a run start, skipping");
                scanOffset = scanOffset + 1;
                continue;
            }

            DebugLog.Write(LogChannel.InferenceDebug,
                "PacketDetailWindow.FindNextItemString: matched item key at 0x"
                + scanOffset.ToString("x"));
            return scanOffset;
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "PacketDetailWindow.FindNextItemString: no further item key found from 0x"
            + startOffset.ToString("x"));
        return -1;
    }
}
