using System;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// SoeByteOrder
//
// Big-endian (network byte order) read helpers for SOE protocol fields.
// The SOE protocol stores multi-byte integers in big-endian order.
// These methods read from a ReadOnlySpan<byte> at a given offset.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class SoeByteOrder
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ReadUInt16
    //
    // Reads a 16-bit unsigned integer in big-endian byte order.
    //
    // data:    The buffer to read from
    // offset:  The byte offset to begin reading at
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
    {
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ReadUInt32
    //
    // Reads a 32-bit unsigned integer in big-endian byte order.
    //
    // data:    The buffer to read from
    // offset:  The byte offset to begin reading at
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        return ((uint)data[offset] << 24) |
               ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) |
               (uint)data[offset + 3];
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ReadFloat
    //
    // Reads a 32-bit IEEE 754 float in big-endian byte order.
    //
    // data:    The buffer to read from
    // offset:  The byte offset to begin reading at
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static float ReadFloat(ReadOnlySpan<byte> data, int offset)
    {
        uint raw = ReadUInt32(data, offset);
        return BitConverter.Int32BitsToSingle((int)raw);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IpToUInt32
    //
    // Converts a dotted-decimal IP address string to a 32-bit unsigned integer
    // in network byte order (big-endian).
    //
    // ip:  The IP address string (e.g. "10.146.79.19")
    //
    // Returns 0 if the input is null or malformed.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static uint IpToUInt32(string ip)
    {
        if (ip == null)
        {
            return 0;
        }

        string[] parts = ip.Split('.');

        if (parts.Length != 4)
        {
            return 0;
        }

        return (uint)((byte.Parse(parts[0]) << 24) |
                       (byte.Parse(parts[1]) << 16) |
                       (byte.Parse(parts[2]) << 8) |
                       byte.Parse(parts[3]));
    }
}