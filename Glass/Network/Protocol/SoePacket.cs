using System;
using Glass.Core;

namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// SoePacket
//
// Mirrors EQProtocolPacket from packetformat.h/cpp.
// Parses a raw SOE protocol packet on construction (_init_parse),
// and provides Decode() for decompression.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SoePacket
{
    private byte[] _packet;
    private int _length;
    private ushort _netOp;
    private byte _flags;
    private int _payloadOffset;
    private int _payloadLength;
    private int _rawPayloadOffset;
    private int _rawPayloadLength;
    private ushort _arqSeq;
    private bool _subpacket;
    private bool _decoded;
    private byte[]? _decompressed;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SoePacket (constructor)
    //
    // Mirrors _init_parse from packetformat.cpp lines 148-218.
    //
    // data:        The raw packet bytes
    // length:      Total length of the packet
    // subpacket:   True if extracted from an OP_Combined container
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SoePacket(ReadOnlySpan<byte> data, int length, bool subpacket)
    {
        _packet = data.Slice(0, length).ToArray();
        _length = length;
        _subpacket = subpacket;
        _decompressed = null;
        _arqSeq = 0;
        _flags = 0;
        _payloadOffset = 0;
        _payloadLength = 0;
        _rawPayloadOffset = 0;
        _rawPayloadLength = 0;
        _decoded = false;

        // _init_parse
        _netOp = (ushort)(_packet[0] | (_packet[1] << 8));

        if (!HasFlags())
        {
            _flags = 0;
            _rawPayloadOffset = 2;

            int crcSize = HasCRC() ? 2 : 0;
            _rawPayloadLength = _length - 2 - crcSize;

            _payloadOffset = _rawPayloadOffset;
            _payloadLength = _rawPayloadLength;
            _decoded = true;
        }
        else
        {
            if (SoeConstants.IsAppOpcode(_netOp))
            {
                _flags = _packet[1];
                _netOp = (ushort)((_packet[2] << 8) | _packet[0]);
                _rawPayloadOffset = 3;

                int crcSize = HasCRC() ? 2 : 0;
                _rawPayloadLength = _length - 2 - 1 - crcSize;

                if ((_flags & SoeConstants.FLAG_COMPRESSED) == 0)
                {
                    _payloadLength = _rawPayloadLength;
                    _payloadOffset = _rawPayloadOffset;
                    _decoded = true;
                }
                else
                {
                    _decoded = false;
                }
            }
            else
            {
                if (_packet[2] == SoeConstants.FLAG_COMPRESSED ||
                    _packet[2] == SoeConstants.FLAG_UNCOMPRESSED)
                {
                    _flags = _packet[2];
                    _rawPayloadOffset = 3;

                    int crcSize = HasCRC() ? 2 : 0;
                    _rawPayloadLength = _length - 2 - 1 - crcSize;

                    if ((_flags & SoeConstants.FLAG_COMPRESSED) == 0)
                    {
                        _payloadLength = _rawPayloadLength;
                        _payloadOffset = _rawPayloadOffset;
                        _decoded = true;
                    }
                    else
                    {
                        _decoded = false;
                    }
                }
                else
                {
                    _flags = 0;
                    _rawPayloadOffset = 2;

                    int crcSize = HasCRC() ? 2 : 0;
                    _rawPayloadLength = _length - 2 - crcSize;

                    _payloadOffset = _rawPayloadOffset;
                    _payloadLength = _rawPayloadLength;
                    _decoded = true;
                }
            }
        }

        if (HasArqSeq() && _decoded)
        {
            _arqSeq = SoeByteOrder.ReadUInt16(_packet, _payloadOffset);
            _payloadOffset += 2;
            _payloadLength -= 2;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Decode
    //
    // Mirrors packetformat.cpp lines 222-298.
    // Decompresses if FLAG_COMPRESSED is set.
    // Returns false if decompression fails.
    //
    // decompressor:  The decompressor instance to use
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool Decode(SoeDecompressor decompressor)
    {
        if (_decoded)
        {
            return true;
        }

        if (HasFlags() && (_flags & SoeConstants.FLAG_COMPRESSED) != 0)
        {
            int compLen;
            int compStart;

            if (SoeConstants.IsAppOpcode(_netOp))
            {
                int crcSize = HasCRC() ? 2 : 0;
                compLen = _length - 1 - 1 - crcSize;
                compStart = 2;
            }
            else
            {
                int crcSize = HasCRC() ? 2 : 0;
                compLen = _length - 2 - 1 - crcSize;
                compStart = 3;
            }

            ReadOnlySpan<byte> compData = new ReadOnlySpan<byte>(_packet, compStart, compLen);

            if (!decompressor.Decompress(compData, out ReadOnlySpan<byte> decompressedSpan))
            {
                DebugLog.Write(DebugLog.Log_Network,
                    "WARNING: Uncompress failed for packet op 0x"
                    + _netOp.ToString("x4") + ", flags 0x"
                    + _flags.ToString("x2"));
                DebugLog.Write(DebugLog.Log_Network,
                    "  Raw: " + BitConverter.ToString(
                        _packet, 0, Math.Min(_length, 64))
                        .Replace("-", " ").ToLower());
                return false;
            }

            _decompressed = decompressedSpan.ToArray();
            _rawPayloadLength = _decompressed.Length;


            if (SoeConstants.IsAppOpcode(_netOp))
            {
                _netOp = (ushort)((_decompressed[0] << 8) | _packet[0]);
                _payloadOffset = 1;
                _payloadLength = _rawPayloadLength - 1;
            }
            else
            {
                _payloadOffset = 0;
                _payloadLength = _rawPayloadLength;
            }

            if (HasArqSeq())
            {
                _arqSeq = SoeByteOrder.ReadUInt16(_decompressed, _payloadOffset);
                _payloadOffset += 2;
                _payloadLength -= 2;
            }

            _decoded = true;
        }

        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HasFlags
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool HasFlags()
    {
        return SoeConstants.HasFlags(_netOp, _subpacket);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HasCRC
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool HasCRC()
    {
        return SoeConstants.HasCrc(_netOp, _subpacket);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HasArqSeq
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public bool HasArqSeq()
    {
        return SoeConstants.HasArqSeq(_netOp);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Payload
    //
    // Returns the decoded payload.  If decompressed, returns from the
    // decompressed buffer.  Otherwise returns from the raw packet.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ReadOnlySpan<byte> Payload()
    {
        if (_decompressed != null)
        {
            return new ReadOnlySpan<byte>(_decompressed, _payloadOffset, _payloadLength);
        }
        return new ReadOnlySpan<byte>(_packet, _payloadOffset, _payloadLength);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RawPacket
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ReadOnlySpan<byte> RawPacket()
    {
        return new ReadOnlySpan<byte>(_packet, 0, _length);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Properties
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort NetOpCode { get { return _netOp; } }
    public byte Flags { get { return _flags; } }
    public ushort ArqSeq { get { return _arqSeq; } }
    public int PayloadLength { get { return _payloadLength; } }
    public int RawPacketLength { get { return _length; } }
    public bool IsSubpacket { get { return _subpacket; } }
    public bool IsDecoded { get { return _decoded; } }
}