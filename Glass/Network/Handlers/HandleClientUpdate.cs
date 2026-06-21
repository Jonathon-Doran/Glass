using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System.Buffers.Binary;
using System.Printing;
using System.Xml.Linq;


namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleClientUpdate
//
// Handles OP_ClientUpdate packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleClientUpdate : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_ClientUpdate";
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchOpcode _opcodeHandled;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
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
    // Resolves the wire opcode and loads the field definitions for OP_ClientUpdate from the
    // active patch via GlassContext.FieldExtractor.  Caches the index of each field the
    // handler reads so the hot path can access the bag by integer index without name lookup.
    //
    // If the active patch does not define OP_ClientUpdate, GetOpcodeValue returns 0 and the
    // handler is effectively disabled — OpcodeDispatch refuses to register handlers with a
    // zero opcode, so this handler simply will not receive packets.  All field index lookups
    // resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleClientUpdate()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
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
    // data:       The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
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

            DebugLog.Write(LogChannel.Opcodes, "CU rootgate is " + rootGate + ", bags = " +
                bagCount);
            if (character == null)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character with id '" + id + "' in repository; fields not stored.");
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
    // ResolveVersion
    //
    // Returns the opcode version for a packet.
    //
    // data:      The application payload.
    // metadata:  Packet metadata
    //
    // Returns:   The resolved version number.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint ResolveVersion(ReadOnlySpan<byte> data, PacketMetadata metadata)
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

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}
