using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Text;
using static Glass.Network.Protocol.SoeConstants;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleSetChatServer
//
// Handles OP_SetChatServer -- World server tells clients what chat server to use 
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleSetChatServer : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_SetChatServer";
    private readonly PatchOpcode _opcodeHandled;
    private readonly OpcodeHandle _opcode;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;

    private readonly SlotId _payloadSlot;
    private readonly SlotId _chatServerSlot;
    private readonly SlotId _chatPortSlot;
    private readonly SlotId _serverCharacterIdSlot;

    // These are the positions of strings in the CSV text
    // Positions are stored in the field bit_position column.
    private readonly uint _serverCsvIndex;
    private readonly uint _portCsvIndex;
    private readonly uint _characterCsvIndex;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleSetChatServer  (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_SetChatServer  from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_SetChatServer , GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleSetChatServer()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _opcode = _registry.GetOpcodeHandle(_patchLevel, _opcodeName);

        _payloadSlot =         _registry.IndexOfField(_patchLevel, _opcode, "csv_payload");
        _chatServerSlot =      _registry.IndexOfField(_patchLevel, _opcode, "chat_server");
        _chatPortSlot =        _registry.IndexOfField(_patchLevel, _opcode, "chat_port");
        _serverCharacterIdSlot = _registry.IndexOfField(_patchLevel, _opcode, "server_character");

        _serverCsvIndex = _registry.GetFieldPosition(_patchLevel, _opcode, _chatServerSlot);
        _portCsvIndex = _registry.GetFieldPosition(_patchLevel, _opcode, _chatPortSlot);
        _characterCsvIndex = _registry.GetFieldPosition(_patchLevel, _opcode, _serverCharacterIdSlot);
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
            case SoeConstants.StreamId.StreamWorldToClient:
                HandleWorldToClient(data, metadata);
                break;
        }
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
        string payload;

        FieldBag bag = _registry.Rent(_opcodeHandled);
        try
        {
            GlassContext.FieldExtractor.Extract(_patchLevel, _opcode, data, bag);

            ReadOnlySpan<byte> payloadBytes = bag.GetBytesAt(_payloadSlot);
            payload = Encoding.ASCII.GetString(payloadBytes);
        }
        finally
        {
            bag.Release();
        }

        string[] csvFields = payload.Split(',');

        if (csvFields.Length < 4)
        {
            DebugLog.Write(LogChannel.Inference, "HandleSetChatServer: malformed payload, field count="
                + csvFields.Length + " raw='" + payload + "'");
            return;
        }

        string chatServer = csvFields[_serverCsvIndex];
        string chatPort = csvFields[_portCsvIndex];
        string serverDotCharacter = csvFields[_characterCsvIndex];

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
            + " chatPort=" + chatPort);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}
