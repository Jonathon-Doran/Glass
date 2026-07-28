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

            character.MaxMana = extractor.GetUIntAt(_maxManaSlot);
            character.CurrentMana = extractor.GetUIntAt(_currentManaSlot);
        }
        finally
        {
            extractor.Release();
        }

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " length=" + data.Length);
        DebugLog.Write(LogChannel.Opcodes, "Mana at " + character.CurrentMana + " / " + character.MaxMana);
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
        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();
        string characterName = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            uint playerId = extractor.GetUIntAt(_playerIdSlot);
            uint currentMana = extractor.GetUIntAt(_currentManaSlot);
            uint maxMana = extractor.GetUIntAt(_maxManaSlot);

            FieldDisplayNode playerIdNode = new FieldDisplayNode("playerId = 0x" + playerId.ToString("X4"));
            playerIdNode.AddByteRange(extractor.GetByteRangeFor(_playerIdSlot));
            root.AddChild(playerIdNode);

            FieldDisplayNode currentManaNode = new FieldDisplayNode("Current Mana = " + currentMana);
            currentManaNode.AddByteRange(extractor.GetByteRangeFor(_currentManaSlot));
            root.AddChild(currentManaNode);

            FieldDisplayNode maxManaNode = new FieldDisplayNode("Maximum Mana = " + maxMana);
            maxManaNode.AddByteRange(extractor.GetByteRangeFor(_maxManaSlot));
            root.AddChild(maxManaNode);
        }
        finally
        {
            extractor.Release();
        }

        root.Text = "Mana Update (" + characterName + ")";
        return root;
    }
}