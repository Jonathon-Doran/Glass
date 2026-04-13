using System;
using System.Buffers.Binary;
using Glass.Core;
using Glass.Network.Protocol;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// Handle_3511
//
// Handles Unknown_3511 packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class Handle_3511 : IHandleOpcodes
{
    private ushort _opcode = 0x3511;
    private readonly string _opcodeName = "OP_Unknown_3511";

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
    // data:       The application payload
    // length:     Length of the application payload
    // direction:  Direction byte
    // opcode:     The application-level opcode
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, int length,
                              byte direction, ushort opcode, PacketMetadata metadata)
    {
        if (direction == SoeConstants.DirectionServerToClient)
        {
            HandleServerToClient(data, length);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleServerToClient
    //
    // Processes zone-to-client traffic
    //
    // data:    The application payload
    // length:  Length of the application payload
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleServerToClient(ReadOnlySpan<byte> data, int length)
    {
        if (length != 6)
        {
            DebugLog.Write(_opcodeName + " wrong size, should be 4, length=" + length);
            return;
        }

        uint spawnId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(0));
        Int32 unk1 = BinaryPrimitives.ReadInt32BigEndian(data.Slice(2));


        DebugLog.Write(_opcodeName);
        DebugLog.Write("Spawn?=" + spawnId + " (0x" + spawnId.ToString("x4") + ")");
        DebugLog.Write("Unk1 =" + unk1 + " (0x" + unk1.ToString("x4") + ")");
    }

}

