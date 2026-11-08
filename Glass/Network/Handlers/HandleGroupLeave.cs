using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;


namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleGroupLeave
//
// Handles OP_GroupLeave messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleGroupLeave : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _groupId_Slot;
    private readonly SlotId _characterName_Slot;
    private readonly SlotId _Unknown1_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleGroupLeave(constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_GroupLeave from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_GroupLeave, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleGroupLeave(PatchLevel patchLevel)
        : base(patchLevel, "OP_Group_Leave")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        if (_opcodeHandled == PatchOpcode.None)
        {
            // happens when replaying captures before the opcode was added
            return;
        }

        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_Group_Leave");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _groupId_Slot = _registry.IndexOfField(_collectionHandle, "GroupId");
        _characterName_Slot = _registry.IndexOfField(_collectionHandle, "Character_Name");
        _Unknown1_Slot = _registry.IndexOfField(_collectionHandle, "Unknown1");
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
    // Processes zone-to-client traffic.  Removes the leaving character from the group with the
    // packet's server group id.  The character name is converted to its persistent character id
    // first; a name with no known character id, a group that is not held, and a character not
    // in that group each leave everything unchanged.
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
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeave:  No RootGate", LogLevel.Error);
                return;
            }

            ServerGroupId serverGroupId = (ServerGroupId)_extractor.GetUIntAt(_groupId_Slot);
            string characterName = _extractor.GetStringAt(_characterName_Slot);

            if (CharacterRepository.Instance.TryGetCharacterIdByName(characterName, out CharacterId characterId) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeave.HandleZoneToClient: '" + characterName
                    + "' has no known character id, not removed from server group id 0x"
                    + serverGroupId.Value.ToString("X") + ".", LogLevel.Warn);
                return;
            }

            if (GroupRepository.Instance.TryGetGroupByServerId(serverGroupId, out Group? group) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeave.HandleZoneToClient: server group id 0x"
                    + serverGroupId.Value.ToString("X") + " is not held, '" + characterName + "' not removed.",
                    LogLevel.Warn);
                return;
            }

            if (group.HasMember(characterId) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeave.HandleZoneToClient: '" + characterName
                    + "' characterId=" + characterId + " is not in groupId=" + group.Id + ", nothing changed.",
                    LogLevel.Trace);
                return;
            }

            if (GroupRepository.Instance.RemoveMember(characterId) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeave.HandleZoneToClient: could not remove '"
                    + characterName + "' characterId=" + characterId + " from groupId=" + group.Id + ".",
                    LogLevel.Error);
                return;
            }

            DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeave.HandleZoneToClient: removed '" + characterName
                + "' characterId=" + characterId + " from groupId=" + group.Id + " (server group id 0x"
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
    // Extracts OP_GroupLeave against the active patch and builds a display tree: a root node for
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
                DebugLog.Write(LogChannel.Opcodes, "HandleGroupLeave:  No RootGate", LogLevel.Error);
                return root;
            }

            groupId = FieldNodes.AddUIntNode(_extractor, _groupId_Slot, "Group ID", root, "X4");
            characterName = FieldNodes.AddStringNode(_extractor, _characterName_Slot, "Name", root);
            FieldNodes.AddUIntNode(_extractor, _Unknown1_Slot, "Unknown 1", root);
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Group Leave (" + characterName + " group 0x" + groupId.ToString("X4") + ")";
        return root;
    }
}
