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
public class HandleCommonMessage : OpcodeHandler
{
    private CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _senderIdSlot;
    private readonly SlotId _channelIdSlot;
    private readonly SlotId _messageIdSlot;

    private static readonly byte ChannelShout = 0x03;
    private static readonly byte ChannelOoc = 0x05;
    private static readonly byte ChannelSay = 0x08;
    private static readonly byte ChannelCustomBase = 0x10;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleCommonMessage (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public HandleCommonMessage(PatchLevel patchLevel)
        :base(patchLevel, "OP_CommonMessage")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_CommonMessage");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _senderIdSlot = _registry.IndexOfField(_collectionHandle, "sender_name");
        _channelIdSlot = _registry.IndexOfField(_collectionHandle, "channel_id");
        _messageIdSlot = _registry.IndexOfField(_collectionHandle, "message_text");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamClientToZone:
                HandleClientToZone(data, metadata);
                break;
        }
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
        FieldExtractor extractor = GlassContext.FieldExtractor;

        string sender;
        uint channel;
        string message;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            sender = extractor.GetStringAt(_senderIdSlot);
            channel = extractor.GetUIntAt(_channelIdSlot);
            message = extractor.GetStringAt(_messageIdSlot);
        }
        finally
        {
            extractor.Release();
        }

        string channelName = GetChannelName(channel);

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " sender = " + sender + ", channel = " +
            channel + "(" + channelName + "), message = '" + message + "'");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_CommonMessage against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();
        string characterName = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            string sender = extractor.GetStringAt(_senderIdSlot);
            uint channel = extractor.GetUIntAt(_channelIdSlot);
            string message = extractor.GetStringAt(_messageIdSlot);

            FieldDisplayNode senderNode = new FieldDisplayNode("sender = " + sender);
            senderNode.AddByteRange(extractor.GetByteRangeFor(_senderIdSlot));
            root.AddChild(senderNode);

            FieldDisplayNode channelNode = new FieldDisplayNode("channel = " + channel);
            channelNode.AddByteRange(extractor.GetByteRangeFor(_channelIdSlot));
            root.AddChild(channelNode);

            FieldDisplayNode messageNode = new FieldDisplayNode("message = " + message);
            messageNode.AddByteRange(extractor.GetByteRangeFor(_messageIdSlot));
            root.AddChild(messageNode);
        }
        finally
        {
            extractor.Release();
        }

        root.Text = "CommonMessage (to " + characterName + ")";
        return root;
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

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveVersion
    //
    // Returns the opcode version for a packet.
    //
    // data:      The application payload.
    // metadata:  Packet metadata
    //
    // Returns:   The resolved version number.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override uint ResolveVersion(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamClientToZone:
                return 1;

            case SoeConstants.StreamId.StreamZoneToClient:
                return 2;
        }

        return 0;
    }
}