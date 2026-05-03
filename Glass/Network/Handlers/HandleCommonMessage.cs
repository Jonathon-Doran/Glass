using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Text;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleCommonMessage
//
// Handles OP_CommonMessage packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleCommonMessage : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_CommonMessage";

    private readonly FieldDefinition[]? _fields;
    private bool _nullFieldsObserved = false;
    private ushort _opcode;

    private static readonly byte ChannelShout = 0x03;
    private static readonly byte ChannelOoc = 0x05;
    private static readonly byte ChannelSay = 0x08;
    private static readonly byte ChannelCustomBase = 0x10;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleCommonMessage (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_CommonMessage from the
    // active patch via GlassContext.FieldExtractor.  Caches the index of each field the
    // handler reads so the hot path can access the bag by integer index without name lookup.
    //
    // If the active patch does not define OP_CommonMessage, GetOpcodeValue returns 0 and the
    // handler is effectively disabled — OpcodeDispatch refuses to register handlers with a
    // zero opcode, so this handler simply will not receive packets.  All field index lookups
    // resolve to -1 in that case but are never consulted. 

    public HandleCommonMessage()
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        _opcode = extractor.GetOpcodeValue(_opcodeName);
        _fields = extractor.GetFieldDefinitions(_opcodeName);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Opcode
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort Opcode
    {
        get { return _opcode; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeName
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string OpcodeName
    {
        get { return _opcodeName; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamZoneToClient:
                HandleZoneToClient(data, metadata);
                break;
            case SoeConstants.StreamId.StreamClientToZone:
                HandleClientToZone(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Extract
    //
    // Fills the supplied bag with field values decoded from data, using this handler's cached
    // field definitions.  Called by OpcodeDispatch.Extract on the cold path (e.g. the
    // Inference opcode log tab during refresh).  Handlers not yet refactored to use the
    // FieldExtractor may leave this empty; callers will see an empty bag.
    //
    // The caller owns the bag's lifetime — must Rent it before this call and Release it after.
    //
    // data:  The application payload
    // bag:   A bag rented by the caller; will be filled by this method
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Extract(ReadOnlySpan<byte> data, FieldBag bag)
    {

    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneToClient
    //
    // Processes zone-to-client traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        DebugLog.Write(LogChannel.Opcodes, _opcodeName);
        DebugLog.Write(LogChannel.Opcodes, "Server to Client");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToZone
    //
    // Processes client-to-zone OP_CommonMessage.
    //
    // Fixed structure:
    //   Offset 0x00 (0):   Sender name, null-terminated, 21-byte fixed field
    //   Offset 0x15 (21):  Channel ID byte
    //   Offset 0x16 (22):  12 bytes unknown (zeros observed)
    //   Offset 0x22 (34):  Message text, null-terminated
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " length=" + data.Length);

        if (data.Length < 35)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " too short, length=" + data.Length + ", minimum is 35.");
            return;
        }

        // Sender name: null-terminated within a 21-byte fixed field (offsets 0x00-0x14)
        int nullIndex = data.Slice(0, 21).IndexOf((byte)0x00);
        if (nullIndex < 0)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " no null terminator in sender name field.");
            return;
        }
        string senderName = Encoding.ASCII.GetString(data.Slice(0, nullIndex));
        DebugLog.Write(LogChannel.Opcodes, _opcodeName + " Sender=\"" + senderName + "\"");

        // Channel ID at offset 0x15
        byte channelId = data[21];
        string channelName = GetChannelName(channelId);
        DebugLog.Write(LogChannel.Opcodes, _opcodeName + " Channel=" + channelId
            + " (0x" + channelId.ToString("x2") + ") " + channelName);

        // Message text: null-terminated starting at offset 0x22
        ReadOnlySpan<byte> messageSpan = data.Slice(34);
        int messageNull = messageSpan.IndexOf((byte)0x00);
        if (messageNull < 0)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " no null terminator in message text.");
            return;
        }
        string messageText = Encoding.ASCII.GetString(messageSpan.Slice(0, messageNull));
        DebugLog.Write(LogChannel.Opcodes, _opcodeName + " Message=\"" + messageText + "\"");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetChannelName
    //
    // Returns a human-readable name for the channel ID byte.
    // Custom channels appear to start at base 0x10, so channel ID 0x14 = custom channel 4.
    // This base offset is speculative and needs confirmation from other custom channel captures.
    //
    // channelId:  The channel ID byte from offset 0x15
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private string GetChannelName(byte channelId)
    {
        if (channelId == ChannelShout)
        {
            return "/shout";
        }
        if (channelId == ChannelOoc)
        {
            return "/ooc";
        }
        if (channelId == ChannelSay)
        {
            return "/say";
        }
        if (channelId >= ChannelCustomBase)
        {
            int customNumber = channelId - ChannelCustomBase;
            return "/" + customNumber;
        }
        return "unknown(" + channelId.ToString("x2") + ")";
    }
}