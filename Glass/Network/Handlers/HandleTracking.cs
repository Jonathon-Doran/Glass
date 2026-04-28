///////////////////////////////////////////////////////////////////////////////////////////////
// HandleZoneSpawns
//
// Handles OP_ZoneSpawns packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using System.Buffers.Binary;

public class HandleTrackingUpdate : IHandleOpcodes
{
    private ushort _opcode = 0x7604;
    private readonly string _opcodeName = "OP_Tracking";

    private bool brief = true;

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
    // Processes zone-to-client traffic.  The packet is either a cooldown-control packet
    // (identified by magic 0x4f348bff in the first 4 bytes of entry[0]) or an array of
    // variable-length spawn records.
    //
    // Every entry has the same 12-byte fixed header followed by a null-terminated name:
    //   Offset 0-1:  count (ushort LE) — only meaningful on entry[0], zero on entries 1..N-1
    //   Offset 2-3:  spawnId (ushort BE)
    //   Offset 4-7:  unknown2 (int BE)
    //   Offset 8-11: unknown3 (uint BE)
    //   Offset 12+:  name (null-terminated ASCII)
    //
    // The count field overlaps what EQ's client reads as a 32-bit spawnId, so the magic
    // number 0x4f348bff (big-endian) occupies the same 4 bytes as count+spawnId and
    // signals a cooldown control packet instead of an array.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data,  PacketMetadata metadata)
    {
        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " length=" + data.Length + " zone->client");

        if (data.Length < 4)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " too short for magic check, length=" + data.Length);
            return;
        }

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(data);

        // Magic number overloads the first 4 bytes (normally count+spawnId) and signals
        // that this packet controls the cooldown for spawn requests rather than
        // carrying an array of spawn records.
        if (magic == 0x4f348bff)
        {
            DebugLog.Write(LogChannel.Opcodes, "magic=0x" + magic.ToString("x8"));

            if (data.Length < 8)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + " cooldown packet too short, length=" + data.Length);
                return;
            }

            float cooldown = BinaryPrimitives.ReadSingleLittleEndian(data.Slice(4));
            DebugLog.Write(LogChannel.Opcodes, "cooldown=" + cooldown.ToString());
            return;
        }

        if (data.Length < 2)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " too short for count, length=" + data.Length);
            return;
        }

        // count lives in the first 2 bytes of entry[0] — it tells us how many entries
        // follow (including entry[0] itself).  On entries 1..N-1 these same 2 bytes
        // are zero padding.
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0));

        if (count == 0)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " count is zero, nothing to parse");
            return;
        }

        int offset = 0;
        int parsedCount = 0;

        for (int i = 0; i < count; i++)
        {
            int consumed = ParseSpawnEntry(data, offset, data.Length, i);
            if (consumed <= 0)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + " failed to parse entry " + i
                    + " at offset " + offset + ", stopping");
                break;
            }

            offset += consumed;
            parsedCount++;

            if (offset > data.Length)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + " offset " + offset
                    + " exceeds length " + data.Length + " after entry " + i);
                break;
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ParseSpawnEntry
    //
    // Parses a single variable-length spawn entry at the given offset.  Every entry has
    // the same 12-byte fixed header followed by a null-terminated ASCII name:
    //
    //   Offset 0-1:  count field (ushort LE) — only valid on entry[0], zero otherwise;
    //                read and logged but otherwise ignored here, since the outer loop
    //                owns the count semantics
    //   Offset 2-3:  spawnId (ushort BE)
    //   Offset 4-7:  unknown2 (int BE)
    //   Offset 8-11: unknown3 (uint BE)
    //   Offset 12+:  name (null-terminated ASCII)
    //
    // data:        The full application payload
    // offset:      Byte offset of this entry within data
    // totalLength: Total length of the application payload
    // index:       Zero-based index of this entry (for logging)
    //
    // Returns: Number of bytes consumed by this entry, or -1 on parse failure.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private int ParseSpawnEntry(ReadOnlySpan<byte> data, int offset, int totalLength, int index)
    {
        const int HeaderSize = 12;

        if (offset + HeaderSize > totalLength)
        {
            DebugLog.Write(LogChannel.Opcodes, "ParseSpawnEntry: entry " + index + " header runs past end, offset="
                + offset + ", need " + HeaderSize + ", have " + (totalLength - offset));
            return -1;
        }

        ReadOnlySpan<byte> entry = data.Slice(offset);

        ushort countField = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(0));
        ushort spawnId = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(2));
        int unknown2 = BinaryPrimitives.ReadInt32LittleEndian(entry.Slice(4));
        byte unknownByte8 = entry[8];
        byte unknownByte9 = entry[9];
        byte level = entry[10];
        byte flag11 = entry[11];

        if (!brief)
        {
            DebugLog.Write(LogChannel.Opcodes, "spawn=" + spawnId + " (0x" + spawnId.ToString("x4") + ")");
            DebugLog.Write(LogChannel.Opcodes, "countField=" + countField + " (0x" + countField.ToString("x4") + ")");
            DebugLog.Write(LogChannel.Opcodes, "unknown2=" + unknown2 + " (0x" + unknown2.ToString("x8") + ")");

            DebugLog.Write(LogChannel.Opcodes, "unknownByte8=" + unknownByte8 + " (0x" + unknownByte8.ToString("x2") + ")");
            DebugLog.Write(LogChannel.Opcodes, "unknownByte9=" + unknownByte9 + " (0x" + unknownByte9.ToString("x2") + ")");
            DebugLog.Write(LogChannel.Opcodes, "level=" + level + " (0x" + level.ToString("x2") + ")");
            DebugLog.Write(LogChannel.Opcodes, "flag=" + flag11 + " (0x" + flag11.ToString("x2") + ")");
        }

        int nameRegionLength = totalLength - offset - HeaderSize;
        if (nameRegionLength <= 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "ParseSpawnEntry: entry " + index
                + " no bytes available for name, nameRegionLength=" + nameRegionLength);
            return -1;
        }

        int terminator = FindNullTerminator(entry.Slice(HeaderSize), nameRegionLength);
        if (terminator == -1)
        {
            DebugLog.Write(LogChannel.Opcodes, "ParseSpawnEntry: entry " + index
                + " no null terminator after name, nameRegionLength=" + nameRegionLength);
            return -1;
        }

        string name = System.Text.Encoding.ASCII.GetString(entry.Slice(HeaderSize, terminator));
        DebugLog.Write(LogChannel.Opcodes, "spawn " + spawnId.ToString("x4") + " \"" + name + "\"");
        DebugLog.Write(LogChannel.Opcodes, "");

        // Total entry size = 12-byte header + name bytes + 1 null terminator
        return HeaderSize + terminator + 1;


    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToServer
    //
    // Processes client-to-zone
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToServer(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] " + _opcodeName + " length=" + data.Length + " client->zone");
        if (data.Length == 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "Request tracking data");
        }
    }

    private int FindNullTerminator(ReadOnlySpan<byte> data, int length)
    {
        // Find the null terminator for the name string at offset 0
        int nullPos = -1;
        for (int i = 0; i < length; i++)
        {
            if (data[i] == 0)
            {
                nullPos = i;
                break;
            }
        }

        if (nullPos < 0)
        {
            DebugLog.Write(LogChannel.Opcodes, "HandleTracking.HandleServerToClient: "
                + _opcodeName + " no null terminator found, length=" + length);
            return -1;
        }

        return nullPos;
    }
}

