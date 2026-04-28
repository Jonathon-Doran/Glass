using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
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
    private ushort _opcode = 0xdd47;
    private readonly string _opcodeName = "OP_CommonMessage";

    private static readonly byte ChannelShout = 0x03;
    private static readonly byte ChannelOoc = 0x05;
    private static readonly byte ChannelSay = 0x08;
    private static readonly byte ChannelCustomBase = 0x10;

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