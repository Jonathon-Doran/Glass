using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleCastBegin
//
// Handles OP_CastRequest packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleCastBegin : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _spellIdSlot;
    private readonly SlotId _casterIdSlot;
    private readonly SlotId _castTimeSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleCastBegin  (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleCastBegin(PatchLevel patchLevel)
        : base(patchLevel, "OP_CastBegin")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Cast Begin");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _spellIdSlot = _registry.IndexOfField(_collectionHandle, "Spell_ID");
        _casterIdSlot = _registry.IndexOfField(_collectionHandle, "Caster_ID");
        _castTimeSlot = _registry.IndexOfField(_collectionHandle, "Cast_Time_ms");
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
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneToClient
    //
    // Processes client-to-zone traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "CastRequest: metadata cannot be "
                + "mapped to a character.  Dropping mob data.", LogLevel.Warn);
            return;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            uint targetID = _extractor.GetUIntAt(_spellIdSlot);
            uint casterID = _extractor.GetUIntAt(_casterIdSlot);
            uint castTime = _extractor.GetUIntAt(_castTimeSlot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_CastBegin against the active patch and builds a display tree: a root node for
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
        string casterName;
        string spellName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandlCastBegin:  No RootGate", LogLevel.Error);
                return root;
            }

            SpellId spellID = (SpellId) _extractor.GetUIntAt(_spellIdSlot);
            SpawnId casterID = (SpawnId) _extractor.GetUIntAt(_casterIdSlot);
            spellName = SpellCatalog.Instance.LookupSpell(spellID);
            casterName = MobRepository.Instance.LookupSpawnName(zoneId, casterID);

            FieldNodes.AddLabeledNode(_extractor, _spellIdSlot, "Spell: " + spellName + " (" +
                spellID + ", 0x" + spellID.Value.ToString("X8") + ")", root);

            FieldNodes.AddLabeledNode(_extractor, _casterIdSlot, "Caster: " + casterName + " (" +
                 casterID + ", 0x" + casterID.Value.ToString("X4") + ")", root);

            FieldNodes.AddUIntNode(_extractor, _castTimeSlot, "Cast Time (ms)", root, "D");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Cast Begin (" + spellName + ")";
        return root;
    }
}


