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

    private const uint ChannelGuild = 0x00;
    private const uint ChannelShout = 0x03;
    private const uint ChannelOoc = 0x05;
    private const uint ChannelSay = 0x08;
    private const uint ChannelCustomBase = 0x10;

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
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

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
        string sender;
        uint channel;
        string message;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleCommonMessage:  No RootGate", LogLevel.Error);
                return;
            }

            sender = _extractor.GetStringAt(_senderIdSlot);
            channel = _extractor.GetUIntAt(_channelIdSlot);
            message = _extractor.GetStringAt(_messageIdSlot);
        }
        finally
        {
            _extractor.Release();
        }

        string channelName = GetChannelName(channel);
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
        FieldDisplayNode root = new FieldDisplayNode();

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            uint channel = _extractor.GetUIntAt(_channelIdSlot);
            string message = _extractor.GetStringAt(_messageIdSlot);

            FieldNodes.AddStringNode(_extractor, _senderIdSlot, "Sender", root);
            FieldNodes.AddLabeledNode(_extractor, _channelIdSlot, "Channel: " + GetChannelName(channel), root);

            FieldDisplayNode messageNode = new FieldDisplayNode("message = " + message);
            messageNode.AddByteRange(_extractor.GetByteRangeFor(_messageIdSlot));
            root.AddChild(messageNode);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "CommonMessage (to " + metadata.CharacterName + ")";
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
        if (channelId > ChannelCustomBase)
        {
            uint customNumber = channelId - ChannelCustomBase;
            return "/" + customNumber;
        }

        switch (channelId)
        {
            case ChannelGuild:
                return "/guild";
            case ChannelSay:
                return "/say";
            case ChannelOoc:
                return "/ooc";
            case ChannelShout:
                return "/shout";
            default:
                return "unknown(" + channelId.ToString("x2") + ")";
        }
    }
}