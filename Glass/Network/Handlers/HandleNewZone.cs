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
public class HandleNewZone :OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _shortNameSlot;
    private readonly SlotId _longNameSlot;
    private readonly SlotId _zoneIdSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleNewZone(constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleNewZone(PatchLevel patchLevel)
        : base(patchLevel, "OP_NewZone")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel,  _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_NewZone");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _shortNameSlot = _registry.IndexOfField(_collectionHandle, "short_name");
        _longNameSlot = _registry.IndexOfField(_collectionHandle, "long_name");
        _zoneIdSlot = _registry.IndexOfField(_collectionHandle, "zone_id");
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
        string shortName;
        string longName;
        uint zoneId;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            shortName = _extractor.GetStringAt(_shortNameSlot);
            longName = _extractor.GetStringAt(_longNameSlot);
            zoneId = _extractor.GetUIntAt(_zoneIdSlot);
        }
        finally
        {
            _extractor.Release();
        }
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
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldDisplayNode root = new FieldDisplayNode();

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleNewZone:  No RootGate", LogLevel.Error);
                return root;
            }

            uint bagCount = _extractor.BagCount(rootGate);
            string shortName = _extractor.GetStringAt(_shortNameSlot);

            FieldNodes.AddStringNode(_extractor, _shortNameSlot, "Short Name", root);
            FieldNodes.AddStringNode(_extractor, _longNameSlot, "Long Name", root);
            FieldNodes.AddUIntNode(_extractor, _zoneIdSlot, "Zone ID", root);

            root.Text = OpcodeName + " (" + shortName + ")";
        }
        finally
        {
            _extractor.Release();
        }

        return root;
    }
}