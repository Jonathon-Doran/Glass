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
// HandleZoneEntry
//
// Handles OP_ZoneEntry packets.  Server-to-client packets contain NPC/mob
// spawn data with a null-terminated name at offset 0.  Client-to-server
// packets contain the player's own zone entry with a different layout.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleZoneEntry : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_ZoneEntry";
    private OpcodeHandle _handle;
    private PatchRegistry _registry;
    private PatchLevel _patchLevel;

    private readonly uint _nameId;
    private readonly uint _spawnId;
    private readonly uint _levelId;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneEntry (constructor)
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
    public HandleZoneEntry()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _handle = _registry.GetOpcodeHandle(_patchLevel, _opcodeName);

        _nameId = _registry.IndexOfField(_patchLevel, _handle, "name");
        _spawnId = _registry.IndexOfField(_patchLevel, _handle, "spawn_id");
        _levelId = _registry.IndexOfField(_patchLevel, _handle, "level");
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
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
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
    // HandleZoneToClient
    //
    // Processes zone-to-client OP_ZoneEntry.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        string name;
        uint spawn_id;
        uint level;

        FieldBag bag = _registry.Rent(_patchLevel, _handle);
        try
        {
            GlassContext.FieldExtractor.Extract(_patchLevel, _handle, data, bag);

            ReadOnlySpan<byte> nameBytes = bag.GetBytesAt(_nameId);
            name = Encoding.ASCII.GetString(nameBytes);
  
            spawn_id = bag.GetUIntAt(_spawnId);
            level = bag.GetUIntAt(_levelId);
        }
        finally
        {
            bag.Release();
        }

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] " + _opcodeName + " length=" + data.Length);
        DebugLog.Write(LogChannel.Opcodes, "name=\"" + name + "\"  id=(0x" + spawn_id.ToString("x4")+")");
        DebugLog.Write(LogChannel.Opcodes, "SpawnId= 0x" + spawn_id.ToString("x4"));
        DebugLog.Write(LogChannel.Opcodes, "Level=" + level + " (0x" + level.ToString("x4") + ")");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToZone
    //
    // Processes client-to-zone OP_ZoneEntry. 
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry.HandleClientToZone: "
            + _opcodeName + " length=" + data.Length);
    }
}