///////////////////////////////////////////////////////////////////////////////////////////////
// HandleTracking_C2Z
//
// Handles OP_Tracking_C2Z packets. (Client-to-Zone)
///////////////////////////////////////////////////////////////////////////////////////////////
using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Handlers;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;


public class HandleTracking_C2Z : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;
    private readonly GateDefinitionHandle _discriminator;

    private readonly SlotId _magicSlot;         // for ResolveVersion

    private readonly SlotId _spawnIdSlot;  
    private readonly SlotId _levelSlot;
    private readonly SlotId _nameSlot;

    private readonly uint TRACKING_MAGIC_NUMBER = 0x4f348bff;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleTracking_C2Z (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleTracking_C2Z(PatchLevel patchLevel)
        : base (patchLevel, "OP_Tracking_C2Z")
    {
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
    // Processes zone-to-client traffic.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);

            uint bagCount = _extractor.BagCount(rootGate);
            DebugLog.Write(LogChannel.Opcodes, bagCount.ToString() + " bags seen in tracking packet", LogLevel.Info);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(rootGate, bagIndex);
                uint spawnId = _extractor.GetUIntAt(_spawnIdSlot);
                uint level = _extractor.GetUIntAt(_levelSlot);
                string name = _extractor.GetStringAt(_nameSlot);
            }
        }
        finally
        {
            _extractor.Release();
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
    public override uint ResolveVersion(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        uint version = 0;

        if (data.Length == 0)
        {
            return version;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_discriminator, data);
            uint magic = _extractor.GetUIntAt(_magicSlot);

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
            _extractor.Release();
        }

        return version;
    }
}

