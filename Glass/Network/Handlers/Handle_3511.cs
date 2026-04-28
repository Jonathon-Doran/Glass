using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using System;
using System.Buffers.Binary;

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
    // data:       The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        if (data.Length != 6)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " wrong size, should be 4, length=" + data.Length);
            return;
        }

        uint spawnId = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(0));
        Int32 unk1 = BinaryPrimitives.ReadInt32BigEndian(data.Slice(2));


        DebugLog.Write(LogChannel.Opcodes, _opcodeName);
        DebugLog.Write(LogChannel.Opcodes, "Spawn?=" + spawnId + " (0x" + spawnId.ToString("x4") + ")");
        DebugLog.Write(LogChannel.Opcodes, "Unk1 =" + unk1 + " (0x" + unk1.ToString("x4") + ")");
    }

}

