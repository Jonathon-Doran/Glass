using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.Data.Models;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleItemMove
//
// Handles OP_ItemMove messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleItemMove : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _unknown0Slot;

    private readonly SlotId _location1Slot;
    private readonly SlotId _subPosition1Slot;
    private readonly SlotId _augPosition1Slot;
    private readonly SlotId _unknownTrailer1Slot;
    private readonly SlotId _unknownTrailer2Slot;

    private readonly SlotId _location2Slot;
    private readonly SlotId _subPosition2Slot;
    private readonly SlotId _augPosition2Slot;
    private readonly SlotId _unknownTrailer3Slot;
    private readonly SlotId _unknownTrailer4Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleItemMove (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_ItemMove from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_ItemMove, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleItemMove(PatchLevel patchLevel)
        : base(patchLevel, "OP_ItemMove")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_ItemMove");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _unknown0Slot = _registry.IndexOfField(_collectionHandle, "Unknown_0");

        _location1Slot = _registry.IndexOfField(_collectionHandle, "Slot1");
        _subPosition1Slot = _registry.IndexOfField(_collectionHandle, "SubPosition1");
        _augPosition1Slot = _registry.IndexOfField(_collectionHandle, "AugPosition1");
        _unknownTrailer1Slot = _registry.IndexOfField(_collectionHandle, "Unknown_Trailer1");
        _unknownTrailer2Slot = _registry.IndexOfField(_collectionHandle, "Unknown_Trailer2");

        _location2Slot = _registry.IndexOfField(_collectionHandle, "Slot2");
        _subPosition2Slot = _registry.IndexOfField(_collectionHandle, "SubPosition2");
        _augPosition2Slot = _registry.IndexOfField(_collectionHandle, "AugPosition2");
        _unknownTrailer3Slot = _registry.IndexOfField(_collectionHandle, "Unknown_Trailer3");
        _unknownTrailer4Slot = _registry.IndexOfField(_collectionHandle, "Unknown_Trailer4");

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
            case SoeConstants.StreamId.StreamClientToZone:
                HandleClientToZone(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToZone
    //
    // Processes client-to-zone traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_ItemMove against the active patch and builds a display tree: a root node for
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
                DebugLog.Write(LogChannel.Opcodes, "ItemMove:  No RootGate", LogLevel.Error);
                return root;
            }
            //     public static string DescribeLocation(StorageSystem storageSystem, uint mainPosition, uint subPosition, uint augPosition)

            uint location1 = _extractor.GetUIntAt(_location1Slot);
            uint subPosition1 = _extractor.GetUIntAt(_subPosition1Slot);
            uint augPosition1 = _extractor.GetUIntAt(_augPosition1Slot);

            uint location2 = _extractor.GetUIntAt(_location2Slot);
            uint subPosition2 = _extractor.GetUIntAt(_subPosition2Slot);
            uint augPosition2 = _extractor.GetUIntAt(_augPosition2Slot);

            string LocationText1 = Character.DescribeLocation(StorageSystem.Carried,
                location1, subPosition1, augPosition1);
            string LocationText2 = Character.DescribeLocation(StorageSystem.Carried,
                location2, subPosition2, augPosition2);

            FieldNodes.AddUIntNode(_extractor, _unknown0Slot, "Unknown 0", root, "?");

            FieldDisplayNode loc1 = FieldNodes.AddLabeledNode(_extractor, _location1Slot, 
                "Source: " + LocationText1, root);
            loc1.AddByteRange(_extractor.GetByteRangeFor(_subPosition1Slot));
            loc1.AddByteRange(_extractor.GetByteRangeFor(_augPosition1Slot));

            FieldNodes.AddUIntNode(_extractor, _location1Slot, "Location 1", root, "D");
            FieldNodes.AddUIntNode(_extractor, _subPosition1Slot, "SubPosition 1", root, "D");
            FieldNodes.AddUIntNode(_extractor, _augPosition1Slot, "AugPosition 1", root, "D");
            FieldNodes.AddUIntNode(_extractor, _unknownTrailer1Slot, "Unknown Trailer 1", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknownTrailer2Slot, "Unknown Trailer 2", root, "?");

            FieldDisplayNode loc2 = FieldNodes.AddLabeledNode(_extractor, _location2Slot, 
                "Destination: " + LocationText2, root);
            loc2.AddByteRange(_extractor.GetByteRangeFor(_subPosition2Slot));
            loc2.AddByteRange(_extractor.GetByteRangeFor(_augPosition2Slot));

            FieldNodes.AddUIntNode(_extractor, _location2Slot, "Location 2", root, "D");
            FieldNodes.AddUIntNode(_extractor, _subPosition2Slot, "SubPosition 2", root, "D");
            FieldNodes.AddUIntNode(_extractor, _augPosition2Slot, "AugPosition 2", root, "D");
            FieldNodes.AddUIntNode(_extractor, _unknownTrailer3Slot, "Unknown Trailer 3", root, "?");
            FieldNodes.AddUIntNode(_extractor, _unknownTrailer4Slot, "Unknown Trailer 4", root, "?");
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "ItemMove";
        return root;
    }
}
