using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using System;
using System.Buffers.Binary;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleDeath
//
// Handles OP_NpcMove packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleDeath : IHandleOpcodes
{
    private ushort _opcode = 0xc7c6;
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
        if (data.Length < 4)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " too short, length=" + data.Length);
            return;
        }

        uint spawnId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0));
        uint killerId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4));
        uint unk1 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8));
        uint unk2 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12));
        uint unk3 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16));
        uint unk4 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20));
        uint unk5 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24));

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " length=" + data.Length);
        DebugLog.Write(LogChannel.Opcodes, "Dead=" + spawnId + " (0x" + spawnId.ToString("x4") + ")");
        DebugLog.Write(LogChannel.Opcodes, "Killer=" + killerId + " (0x" + killerId.ToString("x4") + ")");
        DebugLog.Write(LogChannel.Opcodes, "Unk1=" + unk1 + " (0x" + unk1.ToString("x8") + ")");
        DebugLog.Write(LogChannel.Opcodes, "Unk2=" + unk2 + " (0x" + unk2.ToString("x8") + ")");
        DebugLog.Write(LogChannel.Opcodes, "Unk3=" + unk3 + " (0x" + unk3.ToString("x8") + ")");
        DebugLog.Write(LogChannel.Opcodes, "Unk4=" + unk4 + " (0x" + unk4.ToString("x8") + ")");
        DebugLog.Write(LogChannel.Opcodes, "Unk5=" + unk5 + " (0x" + unk5.ToString("x8") + ")");
    }
}
