using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Policy;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleCastResolution
//
// Handles OP_CastResolution packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleCastResolution: OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _manaSlot;
    private readonly SlotId _spellIdSlot;
    private readonly SlotId _unknown2Slot;
    private readonly SlotId _unknown4Slot;
    private readonly SlotId _resolutionSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleStartCast  (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleCastResolution(PatchLevel patchLevel)
        : base(patchLevel, "OP_Cast_Resolution")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Cast Resolution");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _manaSlot = _registry.IndexOfField(_collectionHandle, "New_Mana");
        _spellIdSlot = _registry.IndexOfField(_collectionHandle, "Spell_ID");
        _resolutionSlot = _registry.IndexOfField(_collectionHandle, "Resolution");
        _unknown2Slot = _registry.IndexOfField(_collectionHandle, "Unknown_2");
        _unknown4Slot = _registry.IndexOfField(_collectionHandle, "Unknown_4");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:       The application payload
    // metadata:   Packet metadata (timestamp, source/dest)
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
    // Processes client-to-zone traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "StartCast: metadata cannot be "
                + "mapped to a character.  Dropping mob data.", LogLevel.Warn);
            return;
        }

        uint mana, spell_id, unknown_2, unknown_4;
        int resolution;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            mana = extractor.GetUIntAt(_manaSlot);
            spell_id = extractor.GetUIntAt(_spellIdSlot);
            unknown_2 = extractor.GetUIntAt(_unknown2Slot);
            unknown_4 = extractor.GetUIntAt(_unknown4Slot);
            resolution = extractor.GetIntAt(_resolutionSlot);
        }
        finally
        {
            extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_CastResolution against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();
     
        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            FieldNodes.AddUIntNode(extractor, _manaSlot, "New Mana", root, "D");
            FieldNodes.AddUIntNode(extractor, _spellIdSlot, "Spell ID", root);
            FieldNodes.AddUIntNode(extractor, _unknown2Slot, "Unknown 2", root);
            FieldNodes.AddUIntNode(extractor, _unknown4Slot, "Unknown 4", root);
            FieldNodes.AddIntNode(extractor, _resolutionSlot, "Resolution", root);
        }
        finally
        {
            extractor.Release();
        }

        root.Text = "Cast Resolution";
        return root;
    }
}

