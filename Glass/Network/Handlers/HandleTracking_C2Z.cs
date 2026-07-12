///////////////////////////////////////////////////////////////////////////////////////////////
// HandleZoneSpawns
//
// Handles OP_ZoneSpawns packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Handlers;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System.Buffers.Binary;
using System.Data;
using System.Text;

public class HandleTracking_C2Z : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_Tracking_C2Z";
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchOpcode _opcodeHandled;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;
    private readonly GateDefinitionHandle _discriminator;

    private readonly SlotId _magicSlot;         // for ResolveVersion

    private readonly SlotId _spawnIdSlot;  
    private readonly SlotId _countSlot;
    private readonly SlotId _levelSlot;
    private readonly SlotId _nameSlot;

    private readonly uint TRACKING_MAGIC_NUMBER = 0x4f348bff;
    //private readonly bool _brief = false;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleTracking_C2Z (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_TrackingUpdate from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_TrackingUpdate, 
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleTracking_C2Z()
    {
        _registry =   GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        PatchOpcode baseOpcode = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _opcodeHandled = baseOpcode with { Version = 1 };
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Tracking_Entries");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _spawnIdSlot = _registry.IndexOfField(_collectionHandle, "spawn_id");
        _nameSlot = _registry.IndexOfField(_collectionHandle, "name");
        _levelSlot = _registry.IndexOfField(_collectionHandle, "level");

        CollectionHandle discriminatorCollection = _registry.GetCollectionHandle(_patchLevel, "Tracking_Discriminator");
        _magicSlot = _registry.IndexOfField(discriminatorCollection, "magic");
        _discriminator = _registry.GetGateDefinitionHandle(_patchLevel, "Gate_Tracking_Discriminator");

        _ = TRACKING_MAGIC_NUMBER;
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
        FieldExtractor extractor = GlassContext.FieldExtractor;

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);

            uint bagCount = extractor.BagCount(rootGate);
            DebugLog.Write(LogChannel.Opcodes, bagCount.ToString() + " bags seen in tracking packet", LogLevel.Info);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                extractor.EnterGate(rootGate, bagIndex);
                uint spawnId = extractor.GetUIntAt(_spawnIdSlot);
                uint level = extractor.GetUIntAt(_levelSlot);
                string name = extractor.GetStringAt(_nameSlot);
            }
        }
        finally
        {
            extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ResolveVersion
    //
    // Determine the packet version by examining the payload.  This is a little more complicated, as we need
    // to extract a collection. A minimal "discriminator" collection was created for this.
    //
    // data:  Slice of the application payload starting at the entry boundary.
    // metadata:  Packet metadata (timestamp, source/dest)
    //
    // Returns: The detected version number
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint ResolveVersion(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        uint version = 0;

        if (data.Length == 0)
        {
            return version;
        }

        try
        {
            GateHandle rootGate = extractor.Extract(_discriminator, data);
            uint magic = extractor.GetUIntAt(_magicSlot);

            if (magic == TRACKING_MAGIC_NUMBER)
            {
                version = 1;
            }
            else
            {
                version = 2;
            }
        }
        finally
        {
            extractor.Release();
        }

        return version;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}

