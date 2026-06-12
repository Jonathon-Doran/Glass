using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System.Buffers.Binary;
using System.Printing;


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
    private readonly GateDefinition _top_level_gate;

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
        _collectionHandle = _registry.GetOpcodeCollection(_patchLevel, _opcodeName);

        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);
        DebugLog.Write(LogChannel.Fields, "top level gate: " + _top_level_gate);

        _sequenceSlot = _registry.IndexOfField(_patchLevel, _collectionHandle, "sequence");
        _playerIdSlot = _registry.IndexOfField(_patchLevel, _collectionHandle, "player_id");
        _xPosSlot = _registry.IndexOfField(_patchLevel, _collectionHandle, "x_pos");
        _yPosSlot = _registry.IndexOfField(_patchLevel, _collectionHandle, "y_pos");
        _zPosSlot = _registry.IndexOfField(_patchLevel, _collectionHandle, "z_pos");
        _headingSlot = _registry.IndexOfField(_patchLevel, _collectionHandle, "heading");
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
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldBag bag = _registry.Rent(_opcodeHandled);

        try
        {
            GlassContext.FieldExtractor.Extract(_patchLevel, _collectionHandle, data, bag);

            int id = GlassContext.SessionRegistry.GetConnection(metadata).CharacterId;
            Character? character = CharacterRepository.Instance.GetById(id);
            if (character == null)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character with id '" + id + "' in repository; fields not stored.");
                return;
            }

            character.XPos = bag.GetFloatAt(_xPosSlot);
            character.YPos = bag.GetFloatAt(_yPosSlot);
            character.ZPos = bag.GetFloatAt(_zPosSlot);

            uint sequence = bag.GetUIntAt(_sequenceSlot);
            uint playerId = bag.GetUIntAt(_playerIdSlot);

            // Note on heading:  measured as 160-degrees per second to within 0.2%.  One degree is 6.25ms of keypress.  
            character.Heading = bag.GetUIntAt(_headingSlot) / 8192.0f * 360.0f;

            string name = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);

            DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] " + _opcodeName);
            DebugLog.Write(LogChannel.Opcodes, "Player " + playerId + " (" + name + ", 0x" + playerId.ToString("x4") + ") sequence " + sequence);
            DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + " ID: " + playerId.ToString("x4") + " Position:  (" + character.XPos + "," + character.YPos + "," + character.ZPos + ")");
            DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + " Heading is " + character.Heading.ToString() + " degrees");
        }
        finally
        {
            bag.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}
