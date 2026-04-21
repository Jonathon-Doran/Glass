using Glass.Core;
using System;

namespace Glass.Network.Protocol
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // BitReader
    //
    // Reads bit-packed fields from a byte buffer.  Bits are consumed MSB-first
    // within each byte, and bytes are read in order from the start of the buffer.
    //
    // Matches the EQ client's bit-reading convention (FUN_1405ac1f0 / FUN_1405ac160
    // in eqgame.exe).
    //
    // Signed values use sign-magnitude encoding, NOT two's complement:
    //   first bit  = sign (1 = negative, 0 = positive)
    //   remaining  = unsigned magnitude
    //   result     = magnitude * (sign ? -1 : +1)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public class BitReader
    {
        private readonly byte[] _buffer;
        private readonly int _totalBits;
        private int _bitPosition;

        ///////////////////////////////////////////////////////////////////////////////////////////
        // Constructor
        //
        // Wraps a byte array in a bit-reader.  The reader does NOT copy the buffer.
        //
        // buffer:  the byte array to read from
        ///////////////////////////////////////////////////////////////////////////////////////////
        public BitReader(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            _buffer = buffer;
            _totalBits = buffer.Length * 8;
            _bitPosition = 0;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////
        // BitPosition
        //
        // Current bit offset from the start of the buffer.  Useful for diagnostic logging.
        ///////////////////////////////////////////////////////////////////////////////////////////
        public int BitPosition
        {
            get
            {
                return _bitPosition;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////
        // BitsRemaining
        //
        // Number of bits left to read from the buffer.
        ///////////////////////////////////////////////////////////////////////////////////////////
        public int BitsRemaining
        {
            get
            {
                return _totalBits - _bitPosition;
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////
        // ReadUInt
        //
        // Reads an unsigned integer from the bit stream.  Bits are consumed MSB-first
        // within each byte.  The first bit read becomes the most significant bit of
        // the result.
        //
        // numBits:  number of bits to read (1 to 32)
        ///////////////////////////////////////////////////////////////////////////////////////////
        public uint ReadUInt(int numBits)
        {
            if (numBits < 1 || numBits > 32)
            {
                DebugLog.Write("BitReader.ReadUInt: invalid numBits=" + numBits);
                throw new ArgumentOutOfRangeException(nameof(numBits));
            }
            if (_bitPosition + numBits > _totalBits)
            {
                DebugLog.Write("BitReader.ReadUInt: underrun at bitPos="
                    + _bitPosition + " numBits=" + numBits + " totalBits=" + _totalBits);
                throw new InvalidOperationException("BitReader underrun");
            }

            uint result = 0;
            int bitsLeft = numBits;

            while (bitsLeft > 0)
            {
                int byteIndex = _bitPosition / 8;
                int bitInByte = _bitPosition % 8;
                int bitsInThisByte = 8 - bitInByte;
                int bitsToTake;
                if (bitsLeft < bitsInThisByte)
                {
                    bitsToTake = bitsLeft;
                }
                else
                {
                    bitsToTake = bitsInThisByte;
                }

                // Extract bitsToTake bits from the current byte, MSB-first
                int shift = bitsInThisByte - bitsToTake;
                uint mask = (uint)((1 << bitsToTake) - 1);
                uint chunk = ((uint)_buffer[byteIndex] >> shift) & mask;

                result = (result << bitsToTake) | chunk;
                _bitPosition += bitsToTake;
                bitsLeft -= bitsToTake;
            }

            return result;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////
        // ReadInt
        //
        // Reads a signed integer using sign-magnitude encoding.  First bit is the
        // sign (1 = negative), remaining bits are the unsigned magnitude.
        //
        // numBits:  total number of bits including sign (2 to 32)
        ///////////////////////////////////////////////////////////////////////////////////////////
        public int ReadInt(int numBits)
        {
            if (numBits < 2 || numBits > 32)
            {
                DebugLog.Write("BitReader.ReadInt: invalid numBits=" + numBits);
                throw new ArgumentOutOfRangeException(nameof(numBits));
            }

            uint signBit = ReadUInt(1);
            uint magnitude = ReadUInt(numBits - 1);

            int result;
            if (signBit != 0)
            {
                result = -(int)magnitude;
            }
            else
            {
                result = (int)magnitude;
            }

            return result;
        }
    }
}
