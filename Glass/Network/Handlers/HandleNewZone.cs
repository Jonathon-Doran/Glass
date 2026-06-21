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
    private readonly PatchOpcode _opcodeHandled;
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;

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
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel,  _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_NewZone");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _shortNameSlot = _registry.IndexOfField(_collectionHandle, "short_name");
        _longNameSlot = _registry.IndexOfField(_collectionHandle, "long_name");
        _zoneIdSlot = _registry.IndexOfField(_collectionHandle, "zone_id");
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
        FieldExtractor extractor = GlassContext.FieldExtractor;
        string shortName;
        string longName;
        uint zoneId;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            shortName = extractor.GetStringAt(_shortNameSlot);
            longName = extractor.GetStringAt(_longNameSlot);
            zoneId = extractor.GetUIntAt(_zoneIdSlot);
        }
        finally
        {
            extractor.Release();
        }

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
                    + _opcodeName + " length=" + data.Length);
        DebugLog.Write(LogChannel.Opcodes, "Entered " + longName + "(" + shortName + "), zoneId="
            + zoneId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_NewZone against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field (short_name, long_name, zone_id), each
    // carrying its payload byte range.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldDisplayNode root = new FieldDisplayNode();

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            uint bagCount = extractor.BagCount(rootGate);

            DebugLog.Write(LogChannel.Fields,
                "HandleNewZone.Describe: bagCount=" + bagCount, LogLevel.Trace);

            if (bagCount == 0)
            {
                DebugLog.Write(LogChannel.Fields,
                    "HandleNewZone.Describe: no bags extracted", LogLevel.Warn);
                return root;
            }

            extractor.EnterGate(rootGate, 0);

            string shortName = extractor.GetStringAt(_shortNameSlot);
            string longName = extractor.GetStringAt(_longNameSlot);
            uint zoneId = extractor.GetUIntAt(_zoneIdSlot);

            FieldDisplayNode shortNameNode = new FieldDisplayNode("short_name = " + shortName);
            shortNameNode.AddByteRange(extractor.GetByteRangeFor(_shortNameSlot));
            root.AddChild(shortNameNode);

            FieldDisplayNode longNameNode = new FieldDisplayNode("long_name = " + longName);
            longNameNode.AddByteRange(extractor.GetByteRangeFor(_longNameSlot));
            root.AddChild(longNameNode);

            FieldDisplayNode zoneIdNode = new FieldDisplayNode("zone_id = " + zoneId);
            zoneIdNode.AddByteRange(extractor.GetByteRangeFor(_zoneIdSlot));
            root.AddChild(zoneIdNode);

            root.Text = OpcodeName + " (" + shortName + ")";

            DebugLog.Write(LogChannel.Fields,
                "HandleNewZone.Describe: built root with " + root.Children.Count + " children", LogLevel.Trace);
        }
        finally
        {
            extractor.Release();
        }

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