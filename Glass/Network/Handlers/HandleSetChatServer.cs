using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using System;
using System.Buffers.Binary;
using static Glass.Network.Protocol.SoeConstants;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleSetChatServer
//
// Handles OP_SetChatServer -- World server tells clients what chat server to use 
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleSetChatServer : IHandleOpcodes
{
    private ushort _opcode = 0x9cfc;
    private readonly string _opcodeName = "OP_SetChatServer";

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
    // data:       The application payload
    // length:     Length of the application payload
    // direction:  Direction byte
    // opcode:     The application-level opcode
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, int length,
                              byte direction, ushort opcode, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamWorldToClient:
                HandleWorldToClient(data, length, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleWorldToClient
    //
    // Processes world-to-client traffic
    //
    // data:    The application payload
    // length:  Length of the application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleWorldToClient(ReadOnlySpan<byte> data, int length, PacketMetadata metadata)
    {
        int nullIndex = data.Slice(0, length).IndexOf((byte)0x00);
        int stringLength = (nullIndex >= 0) ? nullIndex : length;
        string payload = System.Text.Encoding.ASCII.GetString(data.Slice(0, stringLength));

        string[] fields = payload.Split(',');

        if (fields.Length < 4)
        {
            DebugLog.Write(LogChannel.Inference, "HandleSetChatServer: malformed payload, field count="
                + fields.Length + " raw='" + payload + "'");
            return;
        }

        string chatServer = fields[0];
        string chatPort = fields[1];
        string serverDotCharacter = fields[2];
        string authToken = fields[3];

        int dotIndex = serverDotCharacter.IndexOf('.');

        if (dotIndex < 0)
        {
            DebugLog.Write(LogChannel.Inference, "HandleSetChatServer: no dot in server.character field: '"
                + serverDotCharacter + "'");
            return;
        }

        string serverName = serverDotCharacter.Substring(0, dotIndex);
        string characterName = serverDotCharacter.Substring(dotIndex + 1);

        DebugLog.Write(LogChannel.Inference, "HandleSetChatServer: server=" + serverName
            + " character=" + characterName
            + " chatServer=" + chatServer
            + " chatPort=" + chatPort
            + " port=" + metadata.SourcePort + "->" + metadata.DestPort);


    }

}
