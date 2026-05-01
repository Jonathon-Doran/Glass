using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleMobUpdate
//
// Handles OP_NpcMove packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleMobUpdate : IHandleOpcodes
{
    private ushort _opcode = 0x73da;
    private readonly string _opcodeName = "OP_MobUpdate";

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
    // Opcode
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort Opcode
    {
        get { return _opcode; }
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
    // Extract
    //
    // Fills the supplied bag with field values decoded from data, using this handler's cached
    // field definitions.  Called by OpcodeDispatch.Extract on the cold path (e.g. the
    // Inference opcode log tab during refresh).  Handlers not yet refactored to use the
    // FieldExtractor may leave this empty; callers will see an empty bag.
    //
    // The caller owns the bag's lifetime — must Rent it before this call and Release it after.
    //
    // data:  The application payload
    // bag:   A bag rented by the caller; will be filled by this method
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Extract(ReadOnlySpan<byte> data, FieldBag bag)
    {

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
        ushort spawnId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0));
        ushort unknown02 = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2));
        ulong packed = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(4));
        short headingRaw = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(12));

        // -----------------------------------------------------------
        // Extract 19-bit fixed-point coordinates (3 fractional bits)
        // -----------------------------------------------------------
        const ulong MASK_19BIT = 0x7FFFF;
        const float FIXED_POINT_DIVISOR = 8.0f;

        ulong rawX = packed & MASK_19BIT;
        ulong rawY = (packed >> 45) & MASK_19BIT;
        ulong rawZ = (packed >> 19) & MASK_19BIT;


        // -----------------------------------------------------------
        // Sign-extend 19-bit values to handle negative coordinates
        // -----------------------------------------------------------
        int signedX = (rawX & 0x40000) != 0 ? (int)(rawX | 0xFFF80000) : (int)rawX;
        int signedZ = (rawZ & 0x40000) != 0 ? (int)(rawZ | 0xFFF80000) : (int)rawZ;
        int signedY = (rawY & 0x40000) != 0 ? (int)(rawY | 0xFFF80000) : (int)rawY;

        float x = signedX / FIXED_POINT_DIVISOR;
        float z = signedZ / FIXED_POINT_DIVISOR;
        float y = signedY / FIXED_POINT_DIVISOR;



        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " spawnid: " + spawnId.ToString("x4") + " at ({x:F2},{y:F2},{z:F2}");
    }
}