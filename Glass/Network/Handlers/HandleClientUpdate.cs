using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleClientUpdate
//
// Handles OP_ClientUpdate packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleClientUpdate : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _sequenceSlot;
    private readonly SlotId _playerIdSlot;
    private readonly SlotId _xPosSlot;
    private readonly SlotId _yPosSlot;
    private readonly SlotId _zPosSlot;
    private readonly SlotId _headingSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientUpdate(constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleClientUpdate(PatchLevel patchLevel)
        : base(patchLevel, "OP_ClientUpdate")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        // handles of collections that we expect
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_ClientUpdate");

        _sequenceSlot = _registry.IndexOfField(_collectionHandle, "sequence");
        _playerIdSlot = _registry.IndexOfField(_collectionHandle, "player_id");
        _xPosSlot = _registry.IndexOfField(_collectionHandle, "x_pos");
        _yPosSlot = _registry.IndexOfField(_collectionHandle, "y_pos");
        _zPosSlot = _registry.IndexOfField(_collectionHandle, "z_pos");
        _headingSlot = _registry.IndexOfField(_collectionHandle, "heading");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:       The application payload
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
    // Processes client-to-zone
    //
    // data:    The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            // look up the character associated with the session receiving the packet
            int id = GlassContext.SessionRegistry.GetConnection(metadata).CharacterId;
            Character? character = CharacterRepository.Instance.GetById(id);

            string characterName = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);

            uint bagCount = extractor.BagCount(rootGate);

            if (character == null)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character with id '" + id + "' in repository; fields not stored.", LogLevel.Error);
                return;
            }
            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                extractor.EnterGate(rootGate, bagIndex);
                character.XPos = extractor.GetFloatAt(_xPosSlot);
                character.YPos = extractor.GetFloatAt(_yPosSlot);
                character.ZPos = extractor.GetFloatAt(_zPosSlot);

                uint sequence = extractor.GetUIntAt(_sequenceSlot);
                uint playerId = extractor.GetUIntAt(_playerIdSlot);

                // Note on heading:  measured as 160-degrees per second to within 0.2%.  One degree is 6.25ms of keypress.  
                character.Heading = extractor.GetUIntAt(_headingSlot) / 8192.0f * 360.0f;
            
                DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] " + _opcodeName);
                DebugLog.Write(LogChannel.Opcodes, "Player " + playerId + " (" + characterName + ", 0x" + playerId.ToString("x4") + ") sequence " + sequence);
                DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + " ID: " + playerId.ToString("x4") + " Position:  (" + character.XPos + "," + character.YPos + "," + character.ZPos + ")");
                DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + " Heading is " + character.Heading.ToString() + " degrees");
            }
        }
        finally
        {
            extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_ClientUpdate against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        ZoneId zoneId = GlassContext.SessionRegistry.ZoneFromMetadata(metadata);
        FieldDisplayNode root = new FieldDisplayNode();
        string characterName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleClientUpdate:  No RootGate", LogLevel.Error);
                return root;
            }



            float XPos = _extractor.GetFloatAt(_xPosSlot);
            float YPos = _extractor.GetFloatAt(_yPosSlot);
            float ZPos = _extractor.GetFloatAt(_zPosSlot);
            uint sequence = _extractor.GetUIntAt(_sequenceSlot);
            SpawnId playerId = (SpawnId) _extractor.GetUIntAt(_playerIdSlot);
            // Note on heading:  measured as 160-degrees per second to within 0.2%.  One degree is 6.25ms of keypress.  
            float Heading = _extractor.GetUIntAt(_headingSlot) / 8192.0f * 360.0f;
            characterName = MobRepository.Instance.LookupSpawnName(zoneId, playerId);

            FieldNodes.AddLabeledNode(_extractor, _playerIdSlot, "Character: " + characterName + " (" +
                 playerId + ", 0x" + playerId.Value.ToString("X4") + ")", root);

            FieldDisplayNode positionNode = new FieldDisplayNode();
            root.AddChild(positionNode);

            positionNode.Text = "position = (" + XPos.ToString("F2") + "," +
                YPos.ToString("F2") + "," + ZPos.ToString("F2") + ")";

            FieldDisplayNode xNode = new FieldDisplayNode("x = " + XPos);
            xNode.AddByteRange(_extractor.GetByteRangeFor(_xPosSlot));
            positionNode.AddChild(xNode);

            FieldDisplayNode yNode = new FieldDisplayNode("y = " + YPos);
            yNode.AddByteRange(_extractor.GetByteRangeFor(_yPosSlot));
            positionNode.AddChild(yNode);

            FieldDisplayNode zNode = new FieldDisplayNode("z = " + ZPos);
            zNode.AddByteRange(_extractor.GetByteRangeFor(_zPosSlot));
            positionNode.AddChild(zNode);

            positionNode.AddByteRange(_extractor.GetByteRangeFor(_xPosSlot));
            positionNode.AddByteRange(_extractor.GetByteRangeFor(_yPosSlot));
            positionNode.AddByteRange(_extractor.GetByteRangeFor(_zPosSlot));


            FieldDisplayNode headingNode = new FieldDisplayNode("heading (degrees) = " + Heading);
            headingNode.AddByteRange(_extractor.GetByteRangeFor(_headingSlot));
            root.AddChild(headingNode);

            FieldDisplayNode sequenceNode = new FieldDisplayNode("sequence = " + sequence);
            sequenceNode.AddByteRange(_extractor.GetByteRangeFor(_sequenceSlot));
            root.AddChild(sequenceNode);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "ClientUpdate (" + characterName + ")";
        return root;
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
