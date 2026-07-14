using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Security.Policy;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleStartCast
//
// Handles OP_StartCast packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleStartCast: IHandleOpcodes
{
    private readonly string _opcodeName = "OP_StartCast";
    private readonly PatchOpcode _opcodeHandled;
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _gemSlot;
    private readonly SlotId _spellIdSlot;
    private readonly SlotId _targetIdSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleStartCast  (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_StartCast  from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_StartCast , GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleStartCast()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Start Cast");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _gemSlot = _registry.IndexOfField(_collectionHandle, "Gem");
        _spellIdSlot = _registry.IndexOfField(_collectionHandle, "Spell_ID");
        _targetIdSlot = _registry.IndexOfField(_collectionHandle, "Target_ID");
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
    // metadata:   Packet metadata (timestamp, source/dest)
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

        uint gem, spellId, targetId;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            gem = extractor.GetUIntAt(_gemSlot);
            spellId = extractor.GetUIntAt(_spellIdSlot);
            targetId = extractor.GetUIntAt(_targetIdSlot);
        }
        finally
        {
            extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_StartCast against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();
        uint? zoneId;
        string targetName;

        if (character == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "StartCast: metadata cannot be "
                + "mapped to a character.", LogLevel.Warn);
            root.Text = "Target <Unknown>";
            return root;
        }
        if (character.CurrentZone == null)
        {
            DebugLog.Write(LogChannel.Opcodes, "StartCast: no current zone "
                + "for caster.", LogLevel.Warn);
            root.Text = "Target <Unknown>";
            return root;
        }
        zoneId = character.CurrentZone.Value;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            FieldNodes.AddUIntNode(extractor, _gemSlot, "Spell Gem", root, "D");
            FieldNodes.AddUIntNode(extractor, _spellIdSlot, "Spell ID", root, "X8");
            uint targetID = FieldNodes.AddUIntNode(extractor, _targetIdSlot, "Target ID", root, "X4");
           
            if (!MobRepository.Instance.TryGetBySpawnId(zoneId, targetID, out Spawn? spawn))
            {
                DebugLog.Write(LogChannel.Opcodes, "StartCast: targetId=" + targetID
                    + " unknown.", LogLevel.Trace);
                targetName = "<unknown>";
            }
            else
            {
                targetName = spawn.Name!;
            }
            FieldNodes.AddLabeledNode(extractor, _targetIdSlot, "Target: " + targetName, root);

        }
        finally
        {
            extractor.Release();
        }

        root.Text = "Target (" + targetName + ")";
        return root;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}

