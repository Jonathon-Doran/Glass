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
    private OpcodeHandle _opcode;
    private PatchRegistry _registry;
    private PatchLevel _patchLevel;

    private readonly int _senderId;
    private readonly int _channelId;
    private readonly int _messageId;

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
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcode = _registry.GetOpcodeHandle(_patchLevel, _opcodeName);

        _senderId = _registry.IndexOfField(_patchLevel, _opcode, "sender_name");
        _channelId = _registry.IndexOfField(_patchLevel, _opcode, "channel_id");
        _messageId = _registry.IndexOfField(_patchLevel, _opcode, "message_text");
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
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        string sender;
        uint channel;
        string message;

        FieldBag bag = _registry.Rent(_patchLevel, _opcode);
        try
        {
            GlassContext.FieldExtractor.Extract(_patchLevel, _opcode, data, bag);

            ReadOnlySpan<byte> senderBytes = bag.GetBytesAt(_senderId);
            sender = Encoding.ASCII.GetString(senderBytes);
            channel = bag.GetUIntAt(_channelId);

            ReadOnlySpan<byte> messageBytes = bag.GetBytesAt(_messageId);
            message = Encoding.ASCII.GetString(messageBytes);
        }
        finally
        {
            bag.Release();
        }
        string channelName = GetChannelName(channel);

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " sender = " + sender + ", channel = " +
            channel + "(" + channelName + "), message = '" + message + "'");
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
    private string GetChannelName(uint channelId)
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
            uint customNumber = channelId - ChannelCustomBase;
            return "/" + customNumber;
        }
        return "unknown(" + channelId.ToString("x2") + ")";
    }
}