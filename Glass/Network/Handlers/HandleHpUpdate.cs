using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleHpUpdate
//
// Handles OP_HpUpdate messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleHpUpdate : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _playerIdSlot;
    private readonly SlotId _currentHPSlot;
    private readonly SlotId _maxHPSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleHpUpdate (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleHpUpdate(PatchLevel patchLevel)
        :base(patchLevel, "OP_HpUpdate")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel,  _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_HpUpdate");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _playerIdSlot = _registry.IndexOfField(_collectionHandle, "player_id");
        _currentHPSlot = _registry.IndexOfField(_collectionHandle, "current_hp");
        _maxHPSlot = _registry.IndexOfField(_collectionHandle, "max_hp");
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
        FieldExtractor extractor = GlassContext.FieldExtractor;
        Character? character = null;
        uint playerId;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            playerId = extractor.GetUIntAt(_playerIdSlot);
            character = CharacterRepository.Instance.GetById((int)playerId);

            if (character == null)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character with ID '" + playerId + "' in repository; fields not stored.");
                return;
            }

            character.MaxHP = extractor.GetUIntAt(_maxHPSlot);
            character.CurrentHP = extractor.GetUIntAt(_currentHPSlot);
        }
        finally
        {
            extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_HpUpdate against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();
        string characterName = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            uint playerId = extractor.GetUIntAt(_playerIdSlot);
            uint currentHP = extractor.GetUIntAt(_currentHPSlot);
            uint maxHP = extractor.GetUIntAt(_maxHPSlot);

            FieldDisplayNode playerNode = new FieldDisplayNode("player = " + characterName + " ("
                + playerId.ToString("X4") + ")");
            playerNode.AddByteRange(extractor.GetByteRangeFor(_playerIdSlot));
            root.AddChild(playerNode);

            FieldDisplayNode hpNode = new FieldDisplayNode("current HP = " + currentHP);
            hpNode.AddByteRange(extractor.GetByteRangeFor(_currentHPSlot));
            root.AddChild(hpNode);

            FieldDisplayNode maxHPNode = new FieldDisplayNode("max HP = " + maxHP);
            maxHPNode.AddByteRange(extractor.GetByteRangeFor(_maxHPSlot));
            root.AddChild(maxHPNode);
        }
        finally
        {
            extractor.Release();
        }

        root.Text = "HP Update";
        return root;
    }
}