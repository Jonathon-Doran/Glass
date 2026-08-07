using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System.Net.Http.Headers;
using System.Security.Policy;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleAggro
//
// Handles OP_Aggro messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleAggro: OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _playerIdSlot;
    private readonly SlotId _entryGateSlot;

    private readonly SlotId _positionSlot;
    private readonly SlotId _valueSlot;


    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleAggro (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_Aggro from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_Aggro, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleAggro(PatchLevel patchLevel)
        : base(patchLevel, "OP_Aggro")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Aggro");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        CollectionHandle entryCollection = _registry.GetCollectionHandle(_patchLevel, "Aggro_Entry");

        _playerIdSlot = _registry.IndexOfField(_collectionHandle, "Player-ID");
        _entryGateSlot = _registry.IndexOfField(_collectionHandle, "Gate_Aggro_Entry");

        _positionSlot = _registry.IndexOfField(entryCollection, "Slot");
        _valueSlot = _registry.IndexOfField(entryCollection, "Value");
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
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            uint playerId = _extractor.GetUIntAt(_playerIdSlot);

            GateHandle entryGate = _extractor.GetGateAt(_entryGateSlot);
            if (entryGate.Exists == true)
            {
                uint entryCount = _extractor.BagCount(entryGate);
                for (uint i = 0; i < entryCount; i++)
                {
                    _extractor.EnterGate(entryGate, i);
                    uint slot = _extractor.GetUIntAt(_positionSlot);
                    uint value = _extractor.GetUIntAt(_valueSlot);
                }
            }
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_Aggro against the active patch and builds a display tree: a root node for
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

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "Aggro:  No RootGate", LogLevel.Error);
                return root;
            }

            GateHandle entryGate = _extractor.GetGateAt(_entryGateSlot);
            if (entryGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "Aggro:  No EntryGate", LogLevel.Error);
                return root;
            }

            if (_extractor.IsPresent(_playerIdSlot))
            {
                SpawnId playerId = (SpawnId)_extractor.GetUIntAt(_playerIdSlot);
                string playerName = MobRepository.Instance.LookupSpawnName(zoneId, playerId); ;


                FieldNodes.AddLabeledNode(_extractor, _playerIdSlot, "Secondary: " + playerName + " (" +
                    playerId + ", 0x" + playerId.Value.ToString("X4") + ")", root);
            }

            uint entryCount = _extractor.BagCount(entryGate);
            for (uint i = 0; i < entryCount; i++)
            {
                _extractor.EnterGate(entryGate, i);

                FieldDisplayNode entryNode = new FieldDisplayNode();
                FieldNodes.AddUIntNode(_extractor, _positionSlot, "Slot", entryNode, "D");
                FieldNodes.AddUIntNode(_extractor, _valueSlot, "Value", entryNode, "D");
                root.AddChild(entryNode);
                entryNode.Text = i.ToString();
            }
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Aggro";
        return root;
    }
}
