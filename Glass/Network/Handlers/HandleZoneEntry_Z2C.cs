using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Text;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleZoneEntry_Z2C
//
// Handles OP_ZoneEntry packets.  Server-to-client packets contain NPC/mob
// spawn data with a null-terminated name at offset 0.  Client-to-server
// packets contain the player's own zone entry with a different layout.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleZoneEntry_Z2C : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_ZoneEntry_Z2C";
    private readonly PatchOpcode _opcodeHandled;
    private readonly CollectionHandle _collectionHandle;
    private PatchRegistry _registry;
    private PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _nameSlot;
    private readonly SlotId _spawnIdSlot;
    private readonly SlotId _levelSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneEntry_Z2C (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_ZoneEntry from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_ZoneEntry, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleZoneEntry_Z2C()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_ZoneEntryV1");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _nameSlot = _registry.IndexOfField(_collectionHandle, "name");
        _spawnIdSlot = _registry.IndexOfField(_collectionHandle, "spawn_id");
        _levelSlot = _registry.IndexOfField(_collectionHandle, "level");
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
    // Processes zone-to-client OP_ZoneEntry.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        string name;
        uint spawn_id;
        uint level;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            name = extractor.GetStringAt(_nameSlot);
            spawn_id = extractor.GetUIntAt(_spawnIdSlot);
            level = extractor.GetUIntAt(_levelSlot);
            
        }
        finally
        {
            extractor.Release();
        }
        /*
        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] " + _opcodeName + " length=" + data.Length);
        DebugLog.Write(LogChannel.Opcodes, "name=\"" + name + "\"  id=(0x" + spawn_id.ToString("x4")+")");
        DebugLog.Write(LogChannel.Opcodes, "SpawnId= 0x" + spawn_id.ToString("x4"));
        DebugLog.Write(LogChannel.Opcodes, "Level=" + level + " (0x" + level.ToString("x4") + ")");
 */
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveVersion
    //
    // Returns the opcode version for a packet.
    //
    // data:      The application payload.
    // metadata:  Packet metadata
    //
    // Returns:   The resolved version number.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint ResolveVersion(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamZoneToClient:
                return 1;

            case SoeConstants.StreamId.StreamClientToZone:
                return 2;
        }

        return 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}