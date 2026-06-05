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
// HandleNewZone
//
// Handles OP_NewZone packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleNewZone : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_NewZone";
    private OpcodeHandle _opcode;
    private PatchRegistry _registry;
    private PatchLevel _patchLevel;


    private readonly SlotId _shortNameSlot;
    private readonly SlotId _longNameSlot;
    private readonly SlotId _zoneIdSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleNewZone(constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_NewZone from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_NewZone, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleNewZone()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcode = GlassContext.PatchRegistry.GetOpcodeHandle(_patchLevel, _opcodeName);

        _shortNameSlot = _registry.IndexOfField(_patchLevel, _opcode, "short_name");
        _longNameSlot = _registry.IndexOfField(_patchLevel, _opcode, "long_name");
        _zoneIdSlot = _registry.IndexOfField(_patchLevel, _opcode, "zone_id");
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
    // Processes zone-to-client traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        string shortName;
        string longName;
        uint zoneId;

        FieldBag bag = _registry.Rent(_patchLevel, _opcode);
        try
        {
            GlassContext.FieldExtractor.Extract(_patchLevel, _opcode, data, bag);

            ReadOnlySpan<byte> snameBytes = bag.GetBytesAt(_shortNameSlot);
            shortName = Encoding.ASCII.GetString(snameBytes);

            ReadOnlySpan<byte> lnameBytes = bag.GetBytesAt(_longNameSlot);
            longName = Encoding.ASCII.GetString(lnameBytes);


            zoneId = bag.GetUIntAt(_zoneIdSlot);
        }
        finally
        {
            bag.Release();
        }

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " length=" + data.Length);
    }
}


