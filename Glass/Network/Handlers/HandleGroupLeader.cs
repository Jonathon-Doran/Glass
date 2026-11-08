using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;


namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleGroupLeader
//
// Handles OP_GroupLeader messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleGroupLeader : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _groupId_Slot;
    private readonly SlotId _leaderName_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleGroupLeader (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_GroupLeader from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_GroupLeader, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleGroupLeader(PatchLevel patchLevel)
        : base(patchLevel, "OP_Group_Leader")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_Group_Leader");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _groupId_Slot = _registry.IndexOfField(_collectionHandle, "GroupId");
        _leaderName_Slot = _registry.IndexOfField(_collectionHandle, "Leader_Name");
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
    // Processes zone-to-client traffic.  Makes the named character the leader of the group with
    // the packet's server group id.  The leader name is converted to its persistent character id
    // first; a name with no known character id and a group that is not held each leave
    // everything unchanged.
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
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeader:  No RootGate", LogLevel.Error);
                return;
            }

            ServerGroupId serverGroupId = (ServerGroupId)_extractor.GetUIntAt(_groupId_Slot);
            string leaderName = _extractor.GetStringAt(_leaderName_Slot);

            if (CharacterRepository.Instance.TryGetCharacterIdByName(leaderName, out CharacterId leaderId) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeader.HandleZoneToClient: '" + leaderName
                    + "' has no known character id, leader of server group id 0x"
                    + serverGroupId.Value.ToString("X") + " not changed.", LogLevel.Warn);
                return;
            }

            if (GroupRepository.Instance.TryGetGroupByServerId(serverGroupId, out Group? group) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeader.HandleZoneToClient: server group id 0x"
                    + serverGroupId.Value.ToString("X") + " is not held, leader '" + leaderName + "' not set.",
                    LogLevel.Warn);
                return;
            }

            if (group.SetLeader(leaderId) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeader.HandleZoneToClient: groupId=" + group.Id
                    + " did not accept '" + leaderName + "' characterId=" + leaderId + " as leader.", LogLevel.Warn);
                return;
            }

            DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeader.HandleZoneToClient: '" + leaderName
                + "' characterId=" + leaderId + " is leader of groupId=" + group.Id + " (server group id 0x"
                + serverGroupId.Value.ToString("X") + ").", LogLevel.Trace);
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_GroupLeader against the active patch and builds a display tree: a root node for
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
        string leaderName;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeader:  No RootGate", LogLevel.Error);
                return root;
            }

            groupId = FieldNodes.AddUIntNode(_extractor, _groupId_Slot, "Group ID", root, "X4");
            leaderName = FieldNodes.AddStringNode(_extractor, _leaderName_Slot, "Name", root);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Group Leader (" + leaderName + " group 0x" + groupId.ToString("X4") + ")";
        return root;
    }
}
