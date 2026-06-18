using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleManaUpdate
//
// Handles OP_PlayerProfile packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleManaUpdate : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_ManaUpdate";
    private readonly PatchOpcode _opcodeHandled;
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;

    private readonly SlotId _playerIdSlot;
    private readonly SlotId _currentManaSlot;
    private readonly SlotId _maxManaSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleManaUpdate (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_ManaUpdate from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_ManaUpdate, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleManaUpdate()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_ManaUpdate");

        _playerIdSlot = _registry.IndexOfField(_collectionHandle, "player_id");
        _currentManaSlot = _registry.IndexOfField(_collectionHandle, "current_mana");
        _maxManaSlot = _registry.IndexOfField(_collectionHandle, "max_mana");
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
            case SoeConstants.StreamId.StreamZoneToClient:
                HandleZoneToClient(data, metadata);
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
/*        Character? character = null;

        FieldBag bag = _registry.Rent(_collectionHandle);
        try
        {
            GlassContext.FieldExtractor.ExtractCollection(_patchLevel, _collectionHandle, data, bag);

            uint playerId = bag.GetUIntAt(_playerIdSlot);

            character = CharacterRepository.Instance.GetById((int)playerId);

            if (character == null)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character with ID '" + playerId + "' in repository; fields not stored.");
                return;
            }

            character.MaxMana = bag.GetUIntAt(_maxManaSlot);
            character.CurrentMana = bag.GetUIntAt(_currentManaSlot);
        }
        finally
        {
            bag.Release();
        }

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " length=" + data.Length);
        DebugLog.Write(LogChannel.Opcodes, "Mana at " + character.CurrentMana + " / " + character.MaxMana);
*/    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}


