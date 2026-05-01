using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleHpUpdate
//
// Handles OP_PlayerProfile packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleHpUpdate : IHandleOpcodes
{
    private ushort _opcode = 0x8f45;
    private readonly string _opcodeName = "OP_HpUpdate";

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
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
        if (data.Length < 18)
        {
            DebugLog.Write(LogChannel.Opcodes, "HPUpdate packet less than 18 bytes.  This is unusual");
            return;
        }

        int playerId = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(0));
        int currentHP = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(2));
        int maxHP = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(10));

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " length=" + data.Length);
        DebugLog.Write(LogChannel.Opcodes, "HP at " + currentHP + " / " + maxHP);
    }
}


