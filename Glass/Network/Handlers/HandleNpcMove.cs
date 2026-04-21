using System;
using System.Buffers.Binary;
using Glass.Core;
using Glass.Network.Protocol;
namespace Glass.Network.Handlers;
///////////////////////////////////////////////////////////////////////////////////////////////
// HandleNpcMove
//
// Handles OP_NpcMoveUpdate packets.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleNpcMove : IHandleOpcodes
{
    private ushort _opcode = 0x7c8c;
    private readonly string _opcodeName = "OP_NpcMoveUpdate";

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Opcode
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort Opcode
    {
        get
        {
            return _opcode;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeName
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string OpcodeName
    {
        get
        {
            return _opcodeName;
        }
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
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, int length,
                              byte direction, ushort opcode, PacketMetadata metadata)
    {
        if (direction == SoeConstants.DirectionServerToClient)
        {
            HandleServerToClient(data, length, metadata);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleServerToClient
    //
    // Processes zone-to-client traffic.  Decodes bit-packed position, heading, and
    // optional motion fields from the packet.
    //
    // data:    The application payload
    // length:  Length of the application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleServerToClient(ReadOnlySpan<byte> data, int length, PacketMetadata metadata)
    {
        if (length < 14)
        {
            DebugLog.Write(_opcodeName + " minimum length is 14, saw " + length);
            return;
        }

        // Read spawn_id as little-endian from raw bytes (not through BitReader).
        // The BitReader reads MSB-first which would byte-swap this field.
        ushort spawnId = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0));

        // Copy the payload to a byte array for BitReader, skipping the first 2 bytes
        // which were already consumed as spawn_id.
        byte[] buffer = new byte[length];
        data.CopyTo(buffer);
        BitReader reader = new BitReader(buffer);

        // Advance past the 16-bit spawn_id we already read
        reader.ReadUInt(16);

        // Second 16-bit field - purpose currently unknown
        uint secondField = reader.ReadUInt(16);

        // 6-bit flags controlling the conditional fields below
        uint flags = reader.ReadUInt(6);

        // Three 19-bit signed sign-magnitude coordinates, scaled by 8
        int rawX = reader.ReadInt(19);
        int rawY = reader.ReadInt(19);
        int rawZ = reader.ReadInt(19);

        double x = rawX / 8.0;
        double y = rawY / 8.0;
        double z = rawZ / 8.0;

        System.Text.StringBuilder optional = new System.Text.StringBuilder();

        // optional fields
        int? pitch = null;
        int? headingDelta = null;
        int? velocity = null;
        int? dy = null;
        int? dx = null;
        int? dz = null;

        // 12-bit signed heading.  I do not observe any negative headings being sent.  We are using 11 bits.
        int heading = reader.ReadInt(12);
        double headingDegrees = heading * 360.0 / 2048.0;

        // Conditional fields based on flag bits.
        if ((flags & 0x01) != 0)
        {
            if (reader.BitsRemaining < 12)
            {
                DebugLog.Write(_opcodeName + " underrun reading pitch, bits remaining=" + reader.BitsRemaining);
                return;
            }
            pitch = reader.ReadInt(12);
        }
        if ((flags & 0x02) != 0)
        {
            if (reader.BitsRemaining < 10)
            {
                DebugLog.Write(_opcodeName + " underrun reading headingDelta, bits remaining=" + reader.BitsRemaining);
                return;
            }
            headingDelta = reader.ReadInt(10);
        }
        if ((flags & 0x04) != 0)
        {
            if (reader.BitsRemaining < 10)
            {
                DebugLog.Write(_opcodeName + " underrun reading velocity, bits remaining=" + reader.BitsRemaining);
                return;
            }
            velocity = reader.ReadInt(10);
        }
        if ((flags & 0x08) != 0)
        {
            if (reader.BitsRemaining < 13)
            {
                DebugLog.Write(_opcodeName + " underrun reading dy, bits remaining=" + reader.BitsRemaining);
                return;
            }
            dy = reader.ReadInt(13);
        }
        if ((flags & 0x10) != 0)
        {
            if (reader.BitsRemaining < 13)
            {
                DebugLog.Write(_opcodeName + " underrun reading dx, bits remaining=" + reader.BitsRemaining);
                return;
            }
            dx = reader.ReadInt(13);
        }
        if ((flags & 0x20) != 0)
        {
            if (reader.BitsRemaining < 13)
            {
                DebugLog.Write(_opcodeName + " underrun reading dz, bits remaining=" + reader.BitsRemaining);
                return;
            }
            dz = reader.ReadInt(13);
        }

        if (pitch.HasValue)
        {
            optional.Append(" pitch=");
            optional.Append(pitch.Value);
        }
        if (headingDelta.HasValue)
        {
            optional.Append(" headingDelta=");
            optional.Append(headingDelta.Value);
        }
        if (velocity.HasValue)
        {
            optional.Append(" velocity=");
            optional.Append(velocity.Value);
        }
        if (dx.HasValue)
        {
            optional.Append(" dx=");
            optional.Append(dx.Value);
        }
        if (dy.HasValue)
        {
            optional.Append(" dy=");
            optional.Append(dy.Value);
        }
        if (dz.HasValue)
        {
            optional.Append(" dz=");
            optional.Append(dz.Value);
        }

        string timestamp = metadata.Timestamp.ToString("HH:mm:ss.fff");
        DebugLog.Write("[" + timestamp + "] " + _opcodeName
            + " SpawnId=0x" + spawnId.ToString("x4")
            + " pos=(" + x.ToString("F2") + "," + y.ToString("F2") + "," + z.ToString("F2") + ")"
            + " heading=" + headingDegrees.ToString("0.00")
            + optional.ToString());
    }
}