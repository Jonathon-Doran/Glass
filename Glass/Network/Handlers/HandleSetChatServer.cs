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
public class HandleSetChatServer : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

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
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleSetChatServer(PatchLevel patchLevel)
        : base(patchLevel, "OP_SetChatServer")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_SetChatServer");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _payloadSlot =         _registry.IndexOfField(_collectionHandle, "csv_payload");
        _chatServerSlot =      _registry.IndexOfField(_collectionHandle, "chat_server");
        _chatPortSlot =        _registry.IndexOfField(_collectionHandle, "chat_port");
        _serverCharacterIdSlot = _registry.IndexOfField(_collectionHandle, "server_character");

        _serverCsvIndex = _registry.GetFieldPosition(_collectionHandle, _chatServerSlot);
        _portCsvIndex = _registry.GetFieldPosition(_collectionHandle, _chatPortSlot);
        _characterCsvIndex = _registry.GetFieldPosition(_collectionHandle, _serverCharacterIdSlot);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
    // metadata:  Message metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
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

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            payload = _extractor.GetStringAt(_payloadSlot);
        }
        finally
        {
            _extractor.Release();
        }

        string[] csvFields = payload.Split(',');

        if (csvFields.Length < 4)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleSetChatServer: malformed payload, field count="
                + csvFields.Length + " raw='" + payload + "'");
            return;
        }

        string chatServer = csvFields[_serverCsvIndex];
        string chatPort = csvFields[_portCsvIndex];
        string serverDotCharacter = csvFields[_characterCsvIndex];

        int dotIndex = serverDotCharacter.IndexOf('.');

        if (dotIndex < 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleSetChatServer: no dot in server.character field: '"
                + serverDotCharacter + "'");
            return;
        }

        string serverName = serverDotCharacter.Substring(0, dotIndex);
        string characterName = serverDotCharacter.Substring(dotIndex + 1);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_Death against the active patch and builds a display tree: a root node for
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
            string payload = _extractor.GetStringAt(_payloadSlot);
            string[] csvFields = payload.Split(',');

            if (csvFields.Length < 4)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleSetChatServer: malformed payload, field count="
                    + csvFields.Length + " raw='" + payload + "'", LogLevel.Error);
                return root;
            }

            string chatServer = csvFields[_serverCsvIndex];
            string chatPort = csvFields[_portCsvIndex];
            string serverDotCharacter = csvFields[_characterCsvIndex];

            FieldDisplayNode serverNode = new FieldDisplayNode("Chat Server: " + chatServer);
            serverNode.AddByteRange(_extractor.GetByteRangeFor(_payloadSlot));
            root.AddChild(serverNode);

            FieldDisplayNode portNode = new FieldDisplayNode("Port: " + chatPort);
            portNode.AddByteRange(_extractor.GetByteRangeFor(_payloadSlot));
            root.AddChild(portNode);

            FieldDisplayNode charNode = new FieldDisplayNode("Character: " + serverDotCharacter);
            charNode.AddByteRange(_extractor.GetByteRangeFor(_payloadSlot));
            root.AddChild(charNode);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Set Chat Server";
        return root;
    }
}
