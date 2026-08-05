using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleSpellAction 
//
// Handles OP_SpellAction  packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleSpellAction : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _spellGemSlot;
    private readonly SlotId _spellIDSlot;
    private readonly SlotId _actionSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleSpellAction  (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleSpellAction(PatchLevel patchLevel)
        : base(patchLevel, "OP_SpellAction")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_SpellAction");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _spellGemSlot = _registry.IndexOfField(_collectionHandle, "Slot");
        _spellIDSlot = _registry.IndexOfField(_collectionHandle, "SpellID");
        _actionSlot = _registry.IndexOfField(_collectionHandle, "Action");
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
            case SoeConstants.StreamId.StreamZoneToClient:
                HandleZoneToClient(data, metadata);
                break;
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
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "SpellAction: metadata cannot be "
                + "mapped to a character.", LogLevel.Warn);
            return;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            uint spellGem = _extractor.GetUIntAt(_spellGemSlot);
            uint spellID = _extractor.GetUIntAt(_spellGemSlot);
            uint action = _extractor.GetUIntAt(_actionSlot);
        }
        finally
        {
            _extractor.Release();
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
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "SpellAction: metadata cannot be "
                + "mapped to a character.", LogLevel.Warn);
            return;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            uint spellGem = _extractor.GetUIntAt(_spellGemSlot);
            uint spellID = _extractor.GetUIntAt(_spellGemSlot);
            uint action = _extractor.GetUIntAt(_actionSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_SpellAction  against the active patch and builds a display tree: a root node for
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

        FieldDisplayNode root = new FieldDisplayNode();
        uint? zoneId;
        string spellName;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "SpellAction: metadata cannot be "
                + "mapped to a character.", LogLevel.Warn);
            root.Text = "Target <Unknown>";
            return root;
        }
        if (character.CurrentZone == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "SpellAction: no current zone "
                + "for caster.", LogLevel.Warn);
            root.Text = "Target <Unknown>";
            return root;
        }
        zoneId = character.CurrentZone.Value;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            uint spellID = _extractor.GetUIntAt(_spellIDSlot);
            spellName = SpellCatalog.Instance.LookupSpell(spellID);

            FieldNodes.AddLabeledNode(_extractor, _spellIDSlot, "Spell: " + spellName + " (" +
                spellID + ", 0x" + spellID.ToString("X4") + ")", root);
            FieldNodes.AddUIntNode(_extractor, _spellGemSlot, "Spell Gem", root, "D");
            FieldNodes.AddUIntNode(_extractor, _actionSlot, "Action", root, "D");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "SpellGem Action (" + spellName + ")";
        return root;
    }
}



