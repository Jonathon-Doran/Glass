using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleCastRequest
//
// Handles OP_CastRequest packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleCastRequest: OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _gemSlot;
    private readonly SlotId _spellIdSlot;
    private readonly SlotId _targetIdSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleCastRequest  (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleCastRequest(PatchLevel patchLevel)
        : base(patchLevel, "OP_CastRequest")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Cast Request");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _gemSlot = _registry.IndexOfField(_collectionHandle, "Gem");
        _spellIdSlot = _registry.IndexOfField(_collectionHandle, "Spell_ID");
        _targetIdSlot = _registry.IndexOfField(_collectionHandle, "Target_ID");
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
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "CastRequest: metadata cannot be "
                + "mapped to a character.  Dropping mob data.", LogLevel.Warn);
            return;
        }

        uint gem, spellId, targetId;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            gem = _extractor.GetUIntAt(_gemSlot);
            spellId = _extractor.GetUIntAt(_spellIdSlot);
            targetId = _extractor.GetUIntAt(_targetIdSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_CastRequest against the active patch and builds a display tree: a root node for
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
        string targetName;
        string spellName;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "CastRequest: metadata cannot be "
                + "mapped to a character.", LogLevel.Warn);
            root.Text = "Target <Unknown>";
            return root;
        }
        if (character.CurrentZone == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "CastRequest: no current zone "
                + "for caster.", LogLevel.Warn);
            root.Text = "Target <Unknown>";
            return root;
        }
        zoneId = character.CurrentZone.Value;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            uint spellID = _extractor.GetUIntAt(_spellIdSlot);
            spellName = SpellCatalog.Instance.LookupSpell(spellID);

            FieldNodes.AddUIntNode(_extractor, _gemSlot, "Spell Gem", root, "D");
            FieldNodes.AddLabeledNode(_extractor, _spellIdSlot, "Spell: " + spellName + " (" +
                spellID + ", 0x" + spellID.ToString("X8") + ")", root);

            uint targetID = FieldNodes.AddUIntNode(_extractor, _targetIdSlot, "Target ID", root, "X4");
           
            if (!MobRepository.Instance.TryGetBySpawnId((ZoneId) zoneId, (SpawnId) targetID, out Spawn? spawn))
            {
                DebugLog.Write(LogChannel.Opcodes, "CastRequest: targetId=" + targetID
                    + " unknown.", LogLevel.Trace);
                targetName = "<unknown>";
            }
            else
            {
                targetName = spawn.Name!;
            }
            FieldNodes.AddLabeledNode(_extractor, _targetIdSlot, "Cast Target: " + targetName, root);

        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Cast Request (" + targetName + ")";
        return root;
    }
}

