using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Runtime.InteropServices;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleMovementHistory
//
// Handles Unknown_d9d9 packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleMovementHistory : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_MovementHistory";
    private readonly PatchOpcode _opcodeHandled;
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;
    private readonly GateDefinitionHandle _top_level_gate;

    private bool _tooSmallObserved = false;
    private bool _oddSizeObserved = false;
    private bool _characterNotFound = false;

    private readonly SlotId _xPosSlot;
    private readonly SlotId _yPosSlot;
    private readonly SlotId _zPosSlot;
    private readonly SlotId _timestampSlot;
    private readonly SlotId _movestateSlot;


    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleMovementHistory (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_MovementHistory from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_MovementHistory, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleMovementHistory()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel,  _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_MovementHistory");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _xPosSlot = _registry.IndexOfField(_collectionHandle, "x_pos");
        _yPosSlot = _registry.IndexOfField(_collectionHandle, "y_pos");
        _zPosSlot = _registry.IndexOfField(_collectionHandle, "z_pos");
        _timestampSlot = _registry.IndexOfField(_collectionHandle, "timestamp");
        _movestateSlot = _registry.IndexOfField(_collectionHandle, "move_state");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        if (_tooSmallObserved)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " had at least one undersized packet");
        }
        if (_oddSizeObserved)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " had at least one odd-size packet");
        }
        if (_characterNotFound)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " could not find one character from metadata");
        }
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
            case SoeConstants.StreamId.StreamClientToZone:
                HandleClientToZone(data, metadata);
                break;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToServer
    //
    // Processes OP_MovementHistory client-to-zone packets.
    // The payload is an array of 17-byte MovementHistoryEntry structures followed by
    // a single trailing byte of unknown purpose.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        float xPos;
        float yPos;
        float zPos;
        uint moveState;
        uint timestamp;

        if (character == null)
        {
            _characterNotFound = true;
            return;
        }

        try
        {
            GateHandle rootGate = extractor.Extract(_top_level_gate, data);
            uint bagCount = extractor.BagCount(rootGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                extractor.EnterGate(rootGate, bagIndex);

                xPos = extractor.GetFloatAt(_xPosSlot);
                yPos = extractor.GetFloatAt(_yPosSlot);
                zPos = extractor.GetFloatAt(_zPosSlot);
                moveState = extractor.GetUIntAt(_movestateSlot);
                timestamp = extractor.GetUIntAt(_timestampSlot);
                // movementState seems 2 when standing still, 1 when moving.   And 2 appears mid-movement during duplicate position

                // timestamp is a 16-bit unsigned timer that wraps under normal use
                // I assume each zone has its own timer
            }
        }
        finally
        {
            extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_MovementHistory against the active patch and builds a display tree: a root node for
    // the collection with one leaf child per field each carrying its payload byte range.
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

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                extractor.EnterGate(rootGate, bagIndex);

                float xPos = extractor.GetFloatAt(_xPosSlot);
                float yPos = extractor.GetFloatAt(_yPosSlot);
                float zPos = extractor.GetFloatAt(_zPosSlot);
                uint timestamp = extractor.GetUIntAt(_timestampSlot);
                uint movestate = extractor.GetUIntAt(_movestateSlot);

                FieldDisplayNode positionNode = new FieldDisplayNode("Position: (" +
                    xPos.ToString("F2") + "," + yPos.ToString("F2") + "," + zPos.ToString("F2") + ")");

                positionNode.AddByteRange(extractor.GetByteRangeFor(_xPosSlot));
                positionNode.AddByteRange(extractor.GetByteRangeFor(_yPosSlot));
                positionNode.AddByteRange(extractor.GetByteRangeFor(_zPosSlot));
                root.AddChild(positionNode);

                FieldDisplayNode timestampNode = new FieldDisplayNode("Timestamp: " + timestamp);
                timestampNode.AddByteRange(extractor.GetByteRangeFor(_timestampSlot));
                root.AddChild(timestampNode);

                FieldDisplayNode movestateNode = new FieldDisplayNode("MoveState: " + movestate);
                movestateNode.AddByteRange(extractor.GetByteRangeFor(_movestateSlot));
                root.AddChild(movestateNode);
            }
        }
        finally
        {
            extractor.Release();
        }

        root.Text = "Movement History";
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



