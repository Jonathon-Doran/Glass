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
// Handles OP_MovementHistory messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleMovementHistory : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _xPosSlot;
    private readonly SlotId _yPosSlot;
    private readonly SlotId _zPosSlot;
    private readonly SlotId _timestampSlot;
    private readonly SlotId _movestateSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleMovementHistory (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleMovementHistory(PatchLevel patchLevel)
        : base(patchLevel, "OP_MovementHistory")
    {
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
        Character? character = GlassContext.SessionRegistry.GetConnection(metadata).Character;

        float xPos;
        float yPos;
        float zPos;
        uint moveState;
        uint timestamp;

        // possibly too early to map the character
        if (character == null)
        {
            return;
        }

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            uint bagCount = _extractor.BagCount(rootGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(rootGate, bagIndex);

                xPos = _extractor.GetFloatAt(_xPosSlot);
                yPos = _extractor.GetFloatAt(_yPosSlot);
                zPos = _extractor.GetFloatAt(_zPosSlot);
                moveState = _extractor.GetUIntAt(_movestateSlot);
                timestamp = _extractor.GetUIntAt(_timestampSlot);
                // movementState seems 2 when standing still, 1 when moving.   And 2 appears mid-movement during duplicate position

                // timestamp is a 16-bit unsigned timer that wraps under normal use
                // I assume each zone has its own timer
            }
        }
        finally
        {
            _extractor.Release();
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
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        FieldDisplayNode root = new FieldDisplayNode();

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            uint bagCount = _extractor.BagCount(rootGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(rootGate, bagIndex);

                float xPos = _extractor.GetFloatAt(_xPosSlot);
                float yPos = _extractor.GetFloatAt(_yPosSlot);
                float zPos = _extractor.GetFloatAt(_zPosSlot);
                uint timestamp = _extractor.GetUIntAt(_timestampSlot);
                uint movestate = _extractor.GetUIntAt(_movestateSlot);

                FieldDisplayNode positionNode = new FieldDisplayNode("Position: (" +
                    xPos.ToString("F2") + "," + yPos.ToString("F2") + "," + zPos.ToString("F2") + ")");

                positionNode.AddByteRange(_extractor.GetByteRangeFor(_xPosSlot));
                positionNode.AddByteRange(_extractor.GetByteRangeFor(_yPosSlot));
                positionNode.AddByteRange(_extractor.GetByteRangeFor(_zPosSlot));
                root.AddChild(positionNode);

                FieldNodes.AddUIntNode(_extractor, _timestampSlot, "Timestamp", root);
                FieldNodes.AddUIntNode(_extractor, _movestateSlot, "MoveState", root);
            }
        }
        finally
        {
            _extractor.Release();
        }

        root.Text = "Movement History";
        return root;
    }
}