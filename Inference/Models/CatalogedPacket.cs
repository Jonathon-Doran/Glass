using Glass.Core.Logging;
using Glass.Core.Memory;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;

namespace Inference.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// CatalogedPacket
//
// One entry in PacketCatalog.  Holds the wire opcode, the packet metadata,
// a RetainedBuffer over a long-lived copy of the payload bytes, and the
// arrival index assigned at insertion.
//
// The arrival index is the position the packet held in the catalog's
// arrival-order list at the moment it was added.  It is stable for the
// session and survives filtering by PacketsFor, so callers that need to
// correlate a filtered result back to absolute arrival order can rely on
// it.  Ephemeral: the index is not persisted and would not survive a
// rebuild of the catalog from a saved capture.
//
// Held by value.  default(CatalogedPacket) is not meaningful; the
// Payload's RetainedBuffer will throw on access.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly struct CatalogedPacket
{
    public PacketMetadata Metadata { get; }
    public OpcodeValue Opcode { get; }
    public RetainedBuffer Payload { get; }
    public FieldDisplayNode? FieldTree { get; }
    public uint PacketIndex { get; init; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CatalogedPacket (constructor)
    //
    // Stores the metadata, wire opcode and retained payload, and builds the decoded field
    // tree from the payload at this moment so the tree reflects the state in force when the
    // packet arrived.  The arrival index is set separately via the init-only property.
    //
    // metadata:  Source/dest IP and port, timestamp, frame number, session,
    //            and channel.
    // opcode:    Wire opcode value.  Not resolved to a name here.
    // payload:   RetainedBuffer over the long-lived copy of the bytes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public CatalogedPacket(PacketMetadata metadata, OpcodeValue opcode, RetainedBuffer payload)
    {
        Metadata = metadata;
        Opcode = opcode;
        Payload = payload;
        FieldTree = OpcodeDispatch.Instance.Describe(payload.AsReadOnlySpan(), metadata);

        if (FieldTree == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "CatalogedPacket: no field tree for opcode 0x" + opcode
                + " frame " + metadata.FrameNumber, LogLevel.Trace);
        }
        else
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "CatalogedPacket: built field tree for opcode 0x" + opcode
                + " frame " + metadata.FrameNumber, LogLevel.Trace);
        }
    }
}