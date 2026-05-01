using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
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
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
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
            case SoeConstants.StreamId.StreamWorldToClient:
                HandleWorldToClient(data, metadata);
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
    // HandleWorldToClient
    //
    // Processes world-to-client traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleWorldToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        int nullIndex = data.Slice(0, data.Length).IndexOf((byte)0x00);
        int stringLength = (nullIndex >= 0) ? nullIndex : data.Length;
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
