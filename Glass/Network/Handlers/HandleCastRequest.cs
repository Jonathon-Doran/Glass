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
        ZoneId zoneId = GlassContext.SessionRegistry.ZoneFromMetadata(metadata);

        FieldDisplayNode root = new FieldDisplayNode();
        string targetName;
        string spellName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleCastRequest:  No RootGate", LogLevel.Error);
                return root;
            }

            uint spellID = _extractor.GetUIntAt(_spellIdSlot);
            spellName = SpellCatalog.Instance.LookupSpell(spellID);

            FieldNodes.AddLabeledNode(_extractor, _spellIdSlot, "Spell: " + spellName + " (" +
                spellID + ", 0x" + spellID.ToString("X8") + ")", root);

            SpawnId targetID = (SpawnId) FieldNodes.AddUIntNode(_extractor, _targetIdSlot, "Target ID", root, "X4");

            FieldNodes.AddUIntNode(_extractor, _gemSlot, "Spell Gem", root, "D");
            targetName = MobRepository.Instance.LookupSpawnName(zoneId, targetID);

            FieldNodes.AddLabeledNode(_extractor, _targetIdSlot, "Target: " + targetName + " (" +
                    targetID + ", 0x" + targetID.Value.ToString("X4") + ")", root);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Cast Request (" + spellName + ")";
        return root;
    }
}