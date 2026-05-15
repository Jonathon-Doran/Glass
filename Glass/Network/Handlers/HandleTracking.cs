///////////////////////////////////////////////////////////////////////////////////////////////
// HandleZoneSpawns
//
// Handles OP_ZoneSpawns packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System.Buffers.Binary;
using System.Text;

public class HandleTrackingUpdate : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_Tracking";
    private OpcodeHandle _handle;
    private PatchRegistry _registry;
    private PatchLevel _patchLevel;

    private readonly uint _magicId;         // for v1

    private readonly uint _spawnIdIndex;    // for v2
    private readonly uint _countId;
    private readonly uint _levelId;
    private readonly uint _nameId;

    private readonly uint TRACKING_MAGIC_NUMBER = 0x4f348bff;
    //private readonly bool _brief = false;
    private uint _fixedEntryLength;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleTrackingUpdate (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_TrackingUpdate from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_TrackingUpdate, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleTrackingUpdate()
    {
        _registry =   GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _handle =    _registry.GetOpcodeHandle(_patchLevel, _opcodeName);

        _magicId = _registry.IndexOfField(_patchLevel, _handle, "magic");
        _spawnIdIndex = _registry.IndexOfField(_patchLevel, _handle, "spawn_id");
        _countId = _registry.IndexOfField(_patchLevel, _handle, "count");
        _nameId = _registry.IndexOfField(_patchLevel, _handle, "name");
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
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneToClient
    //
    // Processes zone-to-client traffic.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        uint version = GetPayloadVersion(data);

        switch (version)
        {
            case 1:
                break;
            case 2:
                HandleV2Packet(data, metadata);
                break;
            default:
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleV2Packet
    //
    // Handles the variable length tracking packet.  This version is an array of variable length
    // structures.  The first entry has a count in the count field, subsequent entries in the array
    // have a zero there.  The helper ReadEntry extracts one entry out of the payload starting at
    // the supplied offset and reports how many bytes it consumed.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleV2Packet(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldBag bag = _registry.Rent(_patchLevel, _handle);
        try
        {
            GlassContext.FieldExtractor.Extract(_patchLevel, _handle, data, bag);
            uint recordCount = bag.GetUIntAt(_countId);
            DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
                + _opcodeName + ",  " + recordCount + " entries");
            uint offset = 0;
            uint index = 0;

            // TODO:  Hard-coded fixed length for now.
            _fixedEntryLength = bag.GetLengthAt(_countId) + bag.GetLengthAt(_spawnIdIndex) +
                bag.GetLengthAt(_levelId);
            _fixedEntryLength = 12;

            do
            {
                bag.Clear();
  
                uint consumed = ReadEntry(data.Slice((int) offset),  bag);
                if (consumed == 0)
                {
                    DebugLog.Write(LogChannel.Opcodes, _opcodeName + " failed to parse entry "
                        + index + " at offset " + offset + ", stopping");
                    break;
                }
                offset += consumed;
                index++;
                if (offset > data.Length)
                {
                    DebugLog.Write(LogChannel.Opcodes, _opcodeName + " offset " + offset
                        + " exceeds length " + data.Length + " after entry " + (index - 1));
                    break;
                }
            }
            while (index < recordCount);
        }
        finally
        {
            bag.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetPayloadVersion
    //
    // Examines the payload to determine the version number of the payload.
    //
    // Returns: Either 1 if the magic number for v1 is found, or 2 otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint GetPayloadVersion(ReadOnlySpan<byte> data)
    {
        FieldBag bag = _registry.Rent(_patchLevel, _handle);

        try
        {
            GlassContext.FieldExtractor.Extract(_patchLevel, _handle, data, bag);

            uint magic = bag.GetUIntAt(_magicId);

            if (magic == TRACKING_MAGIC_NUMBER)
            {
                return 1;
            }
        }
        finally
        {
            bag.Release();
        }

        return 2;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ReadEntry
    //
    // Extracts one variable-length tracking entry from the supplied slice using the
    // data-driven FieldExtractor.  The slice must begin at the entry boundary.  The
    // returned byte count is the precomputed fixed-portion length plus the actual
    // length of the variable-length name string plus one byte for the null terminator.
    //
    // data:  Slice of the application payload starting at the entry boundary.
    // bag:   Shared FieldBag rented by the caller and cleared before this call.
    //
    // Returns: Number of bytes consumed by this entry.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private uint ReadEntry(ReadOnlySpan<byte> data, FieldBag bag)
    {
        GlassContext.FieldExtractor.Extract(_patchLevel, _handle, data, bag);

        uint spawnId = bag.GetUIntAt(_spawnIdIndex);
        uint level = bag.GetUIntAt(_levelId);
        ReadOnlySpan<byte> nameBytes = bag.GetBytesAt(_nameId);
        string name = Encoding.ASCII.GetString(nameBytes);
        uint nameLength = bag.GetLengthAt(_nameId);

        DebugLog.Write(LogChannel.Opcodes, _opcodeName + " entry: spawn_id=0x" + spawnId.ToString("x4")
            + " level=" + level + " name='" + name + "'");

        return _fixedEntryLength + nameLength + 1;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToServer
    //
    // Processes client-to-zone
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToServer(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] " + _opcodeName + " length=" + data.Length + " client->zone");
        if (data.Length == 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "Request tracking data");
        }
    }

    private int FindNullTerminator(ReadOnlySpan<byte> data, uint length)
    {
        // Find the null terminator for the name string at offset 0
        int nullPos = -1;
        for (int i = 0; i < length; i++)
        {
            if (data[i] == 0)
            {
                nullPos = i;
                break;
            }
        }

        if (nullPos < 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleTracking.HandleServerToClient: "
                + _opcodeName + " no null terminator found, length=" + length);
            return -1;
        }

        return nullPos;
    }
}

