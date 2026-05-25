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

        string opcodeName = GlassContext.PatchRegistry.GetOpcodeName(
            GlassContext.CurrentPatchLevel, packet.Opcode);
        string opcodeHex = "0x" + packet.Opcode.ToString("x4");
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

        FieldTextBox.Text = ExtractFieldText(packet.Opcode, payload);
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
    // Runs the field extractor against the payload using the active
    // patch level and returns the formatted "name = value" text, one
    // binding per line.  Returns empty string when the opcode is not
    // present in the active patch (the registry returns a sentinel
    // handle of -1).
    //
    // opcode:   Wire opcode value.
    // payload:  Bytes to extract from.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static string ExtractFieldText(ushort opcode, ReadOnlySpan<byte> payload)
    {
        OpcodeHandle handle = GlassContext.PatchRegistry.GetOpcodeHandle(
            GlassContext.CurrentPatchLevel, opcode);
        if ((int)handle == -1)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "PacketDetailWindow.ExtractFieldText: opcode=0x" + opcode.ToString("x4")
                + " not in active patch, no fields available");
            return string.Empty;
        }

        FieldBag bag = GlassContext.PatchRegistry.Rent(GlassContext.CurrentPatchLevel, handle);
        try
        {
            GlassContext.FieldExtractor.Extract(GlassContext.CurrentPatchLevel, handle, payload, bag);

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
                "PacketDetailWindow.ExtractFieldText: opcode=0x" + opcode.ToString("x4")
                + " extracted " + bindingCount + " bindings");
            return sb.ToString();
        }
        finally
        {
            bag.Release();
        }
    }
}
