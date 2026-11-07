using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;


namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleGroupJoin 
//
// Handles OP_GroupJoin messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleGroupJoin : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _groupId_Slot;
    private readonly SlotId _characterName_Slot;
    private readonly SlotId _characterLevel_Slot;
    private readonly SlotId _characterId_Slot;
    private readonly SlotId _secondCID_Slot;
    private readonly SlotId _Unknown1_Slot;
    private readonly SlotId _Unknown2_Slot;
    private readonly SlotId _Unknown3_Slot;
    private readonly SlotId _Unknown4_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleGroupJoin (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_GroupJoin from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_GroupJoin, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleGroupJoin(PatchLevel patchLevel)
        : base(patchLevel, "OP_Group_Join")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_Group_Join");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _groupId_Slot = _registry.IndexOfField(_collectionHandle, "GroupId");
        _characterName_Slot = _registry.IndexOfField(_collectionHandle, "Character_Name");
        _characterLevel_Slot = _registry.IndexOfField(_collectionHandle, "Level");
        _characterId_Slot = _registry.IndexOfField(_collectionHandle, "CharacterId");
        _secondCID_Slot = _registry.IndexOfField(_collectionHandle, "SecondCID");
        _Unknown1_Slot = _registry.IndexOfField(_collectionHandle, "Unknown1");
        _Unknown2_Slot = _registry.IndexOfField(_collectionHandle, "Unknown2");
        _Unknown3_Slot = _registry.IndexOfField(_collectionHandle, "Unknown3");
        _Unknown4_Slot = _registry.IndexOfField(_collectionHandle, "Unknown4");
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
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupJoin:  No RootGate", LogLevel.Error);
                return;
            }

            uint groupId = _extractor.GetUIntAt(_groupId_Slot);
            string characterName = _extractor.GetStringAt(_characterName_Slot);
            uint characterLevel = _extractor.GetUIntAt(_characterLevel_Slot);
            uint characterId = _extractor.GetUIntAt(_characterId_Slot);
            uint secondCID = _extractor.GetUIntAt(_secondCID_Slot);
            uint unknown1 = _extractor.GetUIntAt(_Unknown1_Slot);
            uint unknown2 = _extractor.GetUIntAt(_Unknown2_Slot);
            uint unknown3 = _extractor.GetUIntAt(_Unknown3_Slot);
            uint unknown4 = _extractor.GetUIntAt(_Unknown4_Slot);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_GroupJoin against the active patch and builds a display tree: a root node for
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

        uint groupId;
        string characterName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupJoin:  No RootGate", LogLevel.Error);
                return root;
            }

            groupId = FieldNodes.AddUIntNode(_extractor, _groupId_Slot, "Group ID", root, "X4");
            characterName = FieldNodes.AddStringNode(_extractor, _characterName_Slot, "Name", root);
            FieldNodes.AddUIntNode(_extractor, _characterLevel_Slot, "Level", root, "D");
            FieldNodes.AddUIntNode(_extractor, _characterId_Slot, "Character ID", root, "X4");
            FieldNodes.AddUIntNode(_extractor, _secondCID_Slot, "Second CID", root, "X4");
            FieldNodes.AddUIntNode(_extractor, _Unknown1_Slot, "Unknown 1", root);
            FieldNodes.AddUIntNode(_extractor, _Unknown2_Slot, "Unknown 2", root);
            FieldNodes.AddUIntNode(_extractor, _Unknown3_Slot, "Unknown 3", root);
            FieldNodes.AddUIntNode(_extractor, _Unknown4_Slot, "Unknown 4", root);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Group Join (" + characterName + " group 0x" + groupId.ToString("X4") + ")";
        return root;
    }
}
