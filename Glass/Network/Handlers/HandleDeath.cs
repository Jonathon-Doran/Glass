using System;
using System.Buffers.Binary;
using Glass.Core;
using Glass.Network.Protocol;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleDeath
//
// Handles OP_NpcMove packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleDeath : IHandleOpcodes
{
    private ushort _opcode = 0xcf7b;
    private readonly string _opcodeName = "OP_Death";

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
        if (length < 4)
        {
            DebugLog.Write(_opcodeName + " too short, length=" + length);
            return;
        }

        uint spawnId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(0));
        uint killerId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4));
        uint unk1 = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8));
        uint unk2 = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(12));
        uint unk3 = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16));
        uint unk4 = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20));
        uint unk5 = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(24));

        DebugLog.Write(_opcodeName);
        DebugLog.Write("Dead=" + spawnId + " (0x" + spawnId.ToString("x4") + ")");
        DebugLog.Write("Killer=" + killerId + " (0x" + killerId.ToString("x4") + ")");
        DebugLog.Write("Unk1=" + unk1 + " (0x" + unk1.ToString("x8") + ")");
        DebugLog.Write("Unk2=" + unk2 + " (0x" + unk2.ToString("x8") + ")");
        DebugLog.Write("Unk3=" + unk3 + " (0x" + unk3.ToString("x8") + ")");
        DebugLog.Write("Unk4=" + unk4 + " (0x" + unk4.ToString("x8") + ")");
        DebugLog.Write("Unk5=" + unk5 + " (0x" + unk5.ToString("x8") + ")");
    }

}
