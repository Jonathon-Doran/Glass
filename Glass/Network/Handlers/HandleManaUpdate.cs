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
// Handles OP_ManaUpdate packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleManaUpdate : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _playerIdSlot;
    private readonly SlotId _currentManaSlot;
    private readonly SlotId _maxManaSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleManaUpdate (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleManaUpdate(PatchLevel patchLevel)
        : base(patchLevel, "OP_ManaUpdate")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_ManaUpdate");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _playerIdSlot = _registry.IndexOfField(_collectionHandle, "player_id");
        _currentManaSlot = _registry.IndexOfField(_collectionHandle, "current_mana");
        _maxManaSlot = _registry.IndexOfField(_collectionHandle, "max_mana");
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
        Character? character = null;
        uint playerId;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            playerId = _extractor.GetUIntAt(_playerIdSlot);
            character = CharacterRepository.Instance.GetById((int)playerId);

            if (character == null)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character with ID '" + playerId + "' in repository; fields not stored.");
                return;
            }

            character.MaxMana = _extractor.GetUIntAt(_maxManaSlot);
            character.CurrentMana = _extractor.GetUIntAt(_currentManaSlot);
        }
        finally
        {
            _extractor.Release();
        }
   }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_ManaUpdate against the active patch and builds a display tree: a root node for
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
        ZoneId zoneId = GlassContext.SessionRegistry.ZoneFromMetadata(metadata);
        String characterName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            SpawnId playerId = (SpawnId) _extractor.GetUIntAt(_playerIdSlot);
            characterName = MobRepository.Instance.LookupSpawnName(zoneId, playerId);

            uint currentMana = _extractor.GetUIntAt(_currentManaSlot);
            uint maxMana = _extractor.GetUIntAt(_maxManaSlot);

            FieldNodes.AddLabeledNode(_extractor, _playerIdSlot, "Spawn: " + characterName + " (" +
                    playerId + ", 0x" + playerId.Value.ToString("X4") + ")", root);

            FieldNodes.AddUIntNode(_extractor, _currentManaSlot, "Current Mana", root, "D");
            FieldNodes.AddUIntNode(_extractor, _maxManaSlot, "Max Mana", root, "D");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Mana Update (" + characterName + ")";
        return root;
    }
}