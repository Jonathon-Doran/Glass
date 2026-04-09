using System;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// SoeCrc
//
// Seeded CRC-16 calculation for SOE protocol packet validation.
// Uses the CRC-32 polynomial 0xEDB88320 with a 32-bit session key seed,
// then truncates to 16 bits.
//
// The lookup table is generated once at static initialization from the
// polynomial rather than hand-transcribed, eliminating transcription errors.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class SoeCrc
{
    private static readonly uint[] CrcTable = BuildTable();

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // BuildTable
    //
    // Generates the 256-entry CRC-32 lookup table from polynomial 0xEDB88320.
    // Called once at static initialization.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];

        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;

            for (int bit = 0; bit < 8; bit++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (crc >> 1) ^ 0xEDB88320;
                }
                else
                {
                    crc = crc >> 1;
                }
            }

            table[i] = crc;
        }

        return table;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Calculate
    //
    // Computes the seeded CRC-16 for packet validation.
    // The seed (session key) is CRC'd byte-by-byte first (little-endian order),
    // then the packet data.  The result is the lower 16 bits of the inverted CRC-32.
    //
    // data:    The packet bytes to checksum (excluding the trailing 2-byte CRC)
    // length:  The number of bytes to process from data
    // seed:    The 32-bit session key used as the CRC seed
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static ushort Calculate(ReadOnlySpan<byte> data, int length, uint seed)
    {
        uint crc = 0xFFFFFFFF;

        crc = (crc >> 8) ^ CrcTable[(byte)(seed ^ crc)];
        crc = (crc >> 8) ^ CrcTable[(byte)((seed >> 8) ^ crc)];
        crc = (crc >> 8) ^ CrcTable[(byte)((seed >> 16) ^ crc)];
        crc = (crc >> 8) ^ CrcTable[(byte)((seed >> 24) ^ crc)];

        for (int i = 0; i < length; i++)
        {
            crc = (crc >> 8) ^ CrcTable[(byte)(data[i] ^ crc)];
        }

        return (ushort)((crc ^ 0xFFFFFFFF) & 0xFFFF);
    }
}