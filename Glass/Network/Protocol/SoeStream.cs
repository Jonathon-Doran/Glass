using System;
using System.Collections.Generic;
using Glass.Core;
using Glass.Core.Logging;
using static Glass.Network.Protocol.SoeConstants;
namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// SoeStream
//
// Processes one direction of one connection type in the SOE protocol.
// Handles decompression, ARQ sequencing, out-of-order packet caching,
// fragment reassembly, and OP_Combined/OP_AppCombined unpacking.
//
// Emits decoded application-level opcodes via the OnAppPacket delegate.
//
// One instance exists per direction per connection type per client.
// For example, a single EQ client has four streams:
//   client->world, world->client, client->zone, zone->client.
///////////////////////////////////////////////////////////////////////////////////////////////
public class SoeStream : IDisposable
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CachedPacket
    //
    // Stores an out-of-order packet's net opcode alongside its payload
    // so that ProcessCache can route to the correct handler (OP_Packet
    // vs OP_Oversized) when the packet's turn arrives.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private struct CachedPacket
    {
        public ushort NetOpcode;
        public byte[] Payload;
        public PacketMetadata Metadata;
    }

    // ---------------------------------------------------------------------------
    // Delegate for dispatched application-level opcodes
    // ---------------------------------------------------------------------------
    public delegate void AppPacketHandler(ReadOnlySpan<byte> data,
                                          ushort opcode,
                                          PacketMetadata metadata);

    // ---------------------------------------------------------------------------
    // Delegate for session key distribution
    // ---------------------------------------------------------------------------
    public delegate void SessionKeyHandler(uint sessionId,
                                            StreamId streamId,
                                            uint sessionKey);

    // ---------------------------------------------------------------------------
    // Delegate for session close notification
    // ---------------------------------------------------------------------------
    public delegate void SessionCloseHandler(uint sessionId, StreamId streamId);

    // ---------------------------------------------------------------------------
    // Delegate for client lock-on (zone2client only)
    // ---------------------------------------------------------------------------
    public delegate void LockOnClientHandler(int serverPort,
                                              int clientPort,
                                              uint clientAddr);

    // ---------------------------------------------------------------------------
    // Stream identity
    // ---------------------------------------------------------------------------
    private readonly StreamId _streamId;
    private readonly byte _direction;
    private readonly string _name;

    // ---------------------------------------------------------------------------
    // ARQ sequencing
    // ---------------------------------------------------------------------------
    private bool _arqSeqFound;
    private ushort _arqSeqExpected;
    private readonly int _arqSeqGiveUp;
    private readonly Dictionary<ushort, CachedPacket> _arqCache;
    private int _arqCacheHighWater;

    // ---------------------------------------------------------------------------
    // Fragment reassembly
    // ---------------------------------------------------------------------------
    private byte[]? _fragmentBuffer;
    private int _fragmentTotalLength;
    private int _fragmentDataSize;

    // ---------------------------------------------------------------------------
    // Session state
    // ---------------------------------------------------------------------------
    private uint _sessionId;
    private uint _sessionKey;
    private int _sessionClientPort;
    private uint _sessionClientIP;
    private uint _maxLength;
    private int _sessionTrackingEnabled;

    // ---------------------------------------------------------------------------
    // Decompression
    // ---------------------------------------------------------------------------
    private SoeDecompressor? _decompressor;

    // ---------------------------------------------------------------------------
    // Statistics
    // ---------------------------------------------------------------------------
    private long _packetCount;
    private readonly Dictionary<ushort, int> _opcodeCount;

    // ---------------------------------------------------------------------------
    // Callbacks
    // ---------------------------------------------------------------------------
    public AppPacketHandler? OnAppPacket;
    public SessionKeyHandler? OnSessionKey;
    public SessionCloseHandler? OnClosing;
    public LockOnClientHandler? OnLockOnClient;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SoeStream (constructor)
    //
    // streamId:       One of SoeConstants.StreamClient2World, etc.
    // direction:      SoeConstants.DirectionClient or DirectionServer
    // arqSeqGiveUp:   Number of cached out-of-order packets before skipping ahead
    // name:           Human-readable name for logging
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public SoeStream(StreamId streamId, byte direction, int arqSeqGiveUp, string name)
    {
        _streamId = streamId;
        _direction = direction;
        _arqSeqGiveUp = arqSeqGiveUp;
        _name = name;

        _arqSeqExpected = 0;
        _arqSeqFound = false;
        _arqCache = new Dictionary<ushort, CachedPacket>();
        _arqCacheHighWater = 0;

        _fragmentBuffer = null;
        _fragmentTotalLength = 0;
        _fragmentDataSize = 0;

        _sessionId = 0;
        _sessionKey = 0;
        _sessionClientPort = 0;
        _sessionClientIP = 0;
        _maxLength = 0;
        _sessionTrackingEnabled = 0;

        _decompressor = new SoeDecompressor();

        _packetCount = 0;
        _opcodeCount = new Dictionary<ushort, int>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Reset
    //
    // Resets all stream state to initial values.  Called on session disconnect.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Reset()
    {
        _arqCache.Clear();
        _arqSeqExpected = 0;
        _arqSeqFound = false;

        _fragmentBuffer = null;
        _fragmentTotalLength = 0;
        _fragmentDataSize = 0;

        _sessionClientPort = 0;
        _sessionClientIP = 0;
        _sessionId = 0;
        _sessionKey = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Releases the decompressor and clears all buffers.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Dispose()
    {
        if (_decompressor != null)
        {
            _decompressor.Dispose();
            _decompressor = null;
        }

        _arqCache.Clear();
        _fragmentBuffer = null;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Entry point for incoming packets from the router.  Mirrors Python
    // handlePacket exactly: construct packet (runs _init_parse), print
    // diagnostics, call decode, call processPacket, drain cache.
    //
    // rawData:      The complete UDP payload including the 2-byte net opcode
    // metadata:     Packet metadata (source/dest IP and port, timestamp, framenumber)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> rawData, PacketMetadata metadata)
    {
        _packetCount++;

        if (rawData.Length < 2)
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "SoeStream.HandlePacket: packet too short ("
                + rawData.Length + " bytes), dropping");
            return;
        }

        SoePacket packet = new SoePacket(rawData, rawData.Length, false);

        // Diagnostic output — mirrors Python handlePacket
        DebugLog.Write(LogChannel.LowNetwork,
            "========================================================================");
        DebugLog.Write(LogChannel.LowNetwork,
            "Frame " + metadata.FrameNumber + " Packet #" + _packetCount
            + " on stream " + SoeConstants.StreamNames[_streamId]);
        DebugLog.Write(LogChannel.LowNetwork,
            metadata.SourceIp + ":" + metadata.SourcePort + " -> "
            + metadata.DestIp + ":" + metadata.DestPort + " len=" + rawData.Length + " bytes");
        DebugLog.Write(LogChannel.LowNetwork, "");
        DebugLog.WriteMultiline(LogChannel.LowNetwork,
            SoeHexDump.Format(packet.RawPacket()));
        DebugLog.Write(LogChannel.LowNetwork,
             "========================================================================");
        DebugLog.Write(LogChannel.LowNetwork, "");
        DebugLog.Write(LogChannel.LowNetwork,
            "          Stream: " + SoeConstants.StreamNames[_streamId]);
        DebugLog.Write(LogChannel.LowNetwork,
            "          Net Opcode: 0x" + packet.NetOpCode.ToString("x4") + " -> "
            + SoeConstants.GetNetOpcodeName(packet.NetOpCode));
        DebugLog.Write(LogChannel.LowNetwork,
            "          Payload Size: " + packet.PayloadLength + " bytes");

        if (packet.HasFlags())
        {
            string compStr = (packet.Flags & SoeConstants.FLAG_COMPRESSED) != 0
                ? "compressed" : "not compressed";
            DebugLog.Write(LogChannel.LowNetwork,
                "          Flags: 0x" + packet.Flags.ToString("x2")
                + " (" + compStr + ")");
            DebugLog.Write(LogChannel.LowNetwork, "");
        }

        if (packet.HasArqSeq() && packet.IsDecoded)
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "            Seq: " + packet.ArqSeq.ToString("x4")
                + " (expecting " + _arqSeqExpected.ToString("x4") + ")");
        }

        // decode (decompress)
        if (_decompressor == null)
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "          Decompressor is null, dropping");
            return;
        }

        if (!packet.Decode(_decompressor))
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "WARNING: Packet decode failed for stream "
                + SoeConstants.StreamNames[_streamId] + " (" + _streamId + "), "
                + "op " + packet.NetOpCode.ToString("x4") + ", "
                + "flags " + packet.Flags.ToString("x2") + " packet dropped.");
            return;
        }

        // processPacket
        ReadOnlySpan<byte> payload = packet.Payload();
        bool hasArq = packet.HasArqSeq();

        ProcessPacket(packet.NetOpCode, payload, packet.ArqSeq, hasArq, false, metadata);

        // processCache
        if (_arqCache.Count > 0)
        {
            ProcessCache();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessPacket
    //
    // Main dispatch for decoded packets.  Routes by net opcode to the
    // appropriate handler.
    //
    // netOpcode:    The decoded net opcode
    // payload:      The payload after opcode, flags, CRC, and ARQ have been stripped
    // arqSeq:       The ARQ sequence number (valid only if hasArq is true)
    // hasArq:       True if this packet type carries an ARQ sequence
    // isSubpacket:  True if this packet was extracted from an OP_Combined container
    // metadata:     Packet metadata (source/dest IP and port, timestamp, framenumber)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ProcessPacket(ushort netOpcode, ReadOnlySpan<byte> payload,
                                   ushort arqSeq, bool hasArq, bool isSubpacket,
                                   PacketMetadata metadata)
    {
        DebugLog.Write(LogChannel.LowNetwork,
            "          [processPacket] netop=0x" + netOpcode.ToString("x4")
            + " subpacket=" + isSubpacket
            + " payloadLen=" + payload.Length);

        if (SoeConstants.IsAppOpcode(netOpcode))
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "          [processPacket] APP opcode on wire: 0x"
                + netOpcode.ToString("x4"));
            DispatchAppPacket(payload, payload.Length, netOpcode, metadata);
            return;
        }

        if (netOpcode == SoeConstants.OP_Combined)
        {
            ProcessCombined(payload, metadata);
        }
        else if (netOpcode == SoeConstants.OP_AppCombined)
        {
            ProcessAppCombined(payload, metadata);
        }
        else if (netOpcode == SoeConstants.OP_Packet)
        {
            ProcessSequenced(payload, arqSeq, metadata);
        }
        else if (netOpcode == SoeConstants.OP_Oversized)
        {
            ProcessOversized(payload, arqSeq, metadata);
        }
        else if (netOpcode == SoeConstants.OP_SessionRequest)
        {
            ProcessSessionRequest(payload, metadata);
        }
        else if (netOpcode == SoeConstants.OP_SessionResponse)
        {
            ProcessSessionResponse(payload, metadata);
        }
        else if (netOpcode == SoeConstants.OP_SessionDisconnect)
        {
            ProcessSessionDisconnect(payload);
        }
        else if (netOpcode == SoeConstants.OP_Ack ||
                 netOpcode == SoeConstants.OP_AckFuture ||
                 netOpcode == SoeConstants.OP_AckAfterDisconnect)
        {
            // No-op
        }
        else if (netOpcode == SoeConstants.OP_KeepAlive ||
                 netOpcode == SoeConstants.OP_SessionStatRequest ||
                 netOpcode == SoeConstants.OP_SessionStatResponse)
        {
            // No-op
        }
        else
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "EQPacket: Unhandled net opcode " + netOpcode.ToString("x4")
                + ", stream " + StreamNames[_streamId]
                + ", size " + payload.Length);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // DispatchAppPacket
    //
    // Final delivery point for decoded application-level opcodes.
    // Invokes the OnAppPacket callback if set.
    //
    // data:    The application payload (after opcode bytes have been stripped)
    // length:  Length of the application payload
    // opcode:  The application-level opcode
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void DispatchAppPacket(ReadOnlySpan<byte> data, int length, ushort opcode, PacketMetadata metadata)
    {
        DebugLog.Write(LogChannel.LowNetwork, "");
        DebugLog.Write(LogChannel.LowNetwork, "Opcode: 0x" + opcode.ToString("x4"));

        DebugLog.Write(LogChannel.LowNetwork,
            "          [dispatchPacket] stream=" + StreamNames[_streamId]
            + " opCode=0x" + opcode.ToString("x4") + " len=" + length);

        DebugLog.WriteMultiline(LogChannel.LowNetwork,
            SoeHexDump.Format(data.Slice(0, length), "          "));

        if (_opcodeCount.ContainsKey(opcode))
        {
            _opcodeCount[opcode]++;
        }
        else
        {
            _opcodeCount[opcode] = 1;
        }

        if (OnAppPacket != null)
        {
            OnAppPacket(data, opcode, metadata);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeCount
    //
    // Returns the dictionary of dispatched opcode counts.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyDictionary<ushort, int> OpcodeCount
    {
        get { return _opcodeCount; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessCombined
    //
    // Unpacks an OP_Combined packet, which contains multiple sub-packets
    // concatenated with a 1-byte length prefix each.
    //
    // payload:  The combined packet payload after opcode/flags/CRC have been stripped
    // metadata:     Packet metadata (source/dest IP and port, timestamp, framenumber)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ProcessCombined(ReadOnlySpan<byte> payload, PacketMetadata metadata)
    {
        int pos = 0;
        int end = payload.Length;
        int subNum = 0;

        while (pos < end)
        {
            int subpacketLength = payload[pos];
            pos++;

            if (subpacketLength == 0)
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "  Combined: zero-length sub-packet at pos " + pos + ", breaking");
                break;
            }

            if (pos + subpacketLength > end)
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "  Combined: sub-packet length " + subpacketLength
                    + " exceeds remaining " + (end - pos) + " bytes at pos "
                    + pos + ", breaking");
                break;
            }

            if (subpacketLength < 2)
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "  Combined: sub-packet length " + subpacketLength
                    + " too short for opcode, skipping");
                pos += subpacketLength;
                continue;
            }

            ushort subOpCode = (ushort)(payload[pos] | (payload[pos + 1] << 8));
            subNum++;


            DebugLog.Write(LogChannel.LowNetwork, "          ");
            DebugLog.Write(LogChannel.LowNetwork, "          ----------");
            DebugLog.Write(LogChannel.LowNetwork, "          ");
            DebugLog.Write(LogChannel.LowNetwork,
                "          Sub-packet #" + subNum + " (" + subpacketLength + " bytes)"
                + " opcode=0x" + subOpCode.ToString("x4"));

            if (subOpCode == 0)
            {
                if (subpacketLength < 3)
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "  Combined: 3-byte opcode but only " + subpacketLength
                        + " bytes, skipping");
                    pos += subpacketLength;
                    continue;
                }
                subOpCode = (ushort)(payload[pos + 1] | (payload[pos + 2] << 8));
                DispatchAppPacket(payload.Slice(pos + 3, subpacketLength - 3),
                                  subpacketLength - 3, subOpCode, metadata);
            }
            else if (SoeConstants.IsNetOpcode(subOpCode))
            {
                ReadOnlySpan<byte> subData = payload.Slice(pos, subpacketLength);
                ProcessSubpacket(subData, metadata);
            }
            else
            {
                DispatchAppPacket(payload.Slice(pos + 2, subpacketLength - 2),
                                  subpacketLength - 2, subOpCode, metadata);
            }

            DebugLog.Write(LogChannel.LowNetwork, "\n");
            pos += subpacketLength;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessAppCombined
    //
    // Unpacks an OP_AppCombined packet.  Similar to OP_Combined but uses a
    // different length encoding: 0xFF as the length byte signals a 2-byte
    // big-endian length follows.
    //
    // payload:  The app-combined payload after opcode/flags/CRC have been stripped
    // metadata:     Packet metadata (source/dest IP and port, timestamp, framenumber)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ProcessAppCombined(ReadOnlySpan<byte> payload, PacketMetadata metadata)
    {
        int pos = 0;
        int end = payload.Length;
        int subNum = 0;

        DebugLog.Write(LogChannel.LowNetwork, "          ProcessAppCombined");

        while (pos < end)
        {
            int subpacketLength = payload[pos];
            pos++;

            if (subpacketLength != 0xFF)
            {
                if (pos + subpacketLength > end)
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "  AppCombined: sub-packet overflows, breaking");
                    break;
                }

                if (subpacketLength < 2)
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "  AppCombined: sub-packet too short, skipping");
                    pos += subpacketLength;
                    continue;
                }

                ushort subOpCode = (ushort)(payload[pos] | (payload[pos + 1] << 8));
                int dataPos = pos;
                int actualLen = subpacketLength;

                if (subOpCode == 0)
                {
                    dataPos += 1;
                    actualLen -= 1;
                    if (actualLen < 2)
                    {
                        DebugLog.Write(LogChannel.LowNetwork,
                            "  AppCombined: extended opcode too short, skipping");
                        pos += subpacketLength;
                        continue;
                    }
                    subOpCode = (ushort)(payload[dataPos] | (payload[dataPos + 1] << 8));
                }

                subNum++;
                DebugLog.Write(LogChannel.LowNetwork, "          ");

                DebugLog.Write(LogChannel.LowNetwork,
                    "          Sub-packet #" + subNum + " (" + subpacketLength + " bytes)"
                    + " opcode=0x" + subOpCode.ToString("x4"));

                DispatchAppPacket(payload.Slice(dataPos + 2, actualLen - 2),
                                  actualLen - 2, subOpCode, metadata);

                pos += subpacketLength;
            }
            else
            {
                if (pos + 2 > end)
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "  AppCombined: long-form length overflows, breaking");
                    break;
                }

                int longLength = SoeByteOrder.ReadUInt16(payload, pos);
                pos += 2;

                if (pos + longLength > end)
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "  AppCombined: long sub-packet overflows, breaking");
                    break;
                }

                if (longLength < 2)
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "  AppCombined: long sub-packet too short, skipping");
                    pos += longLength;
                    continue;
                }

                ushort subOpCode = (ushort)(payload[pos] | (payload[pos + 1] << 8));
                int dataPos = pos;
                int actualLen = longLength;

                if (subOpCode == 0)
                {
                    dataPos += 1;
                    actualLen -= 1;
                    if (actualLen < 2)
                    {
                        DebugLog.Write(LogChannel.LowNetwork,
                            "  AppCombined: long extended opcode too short, skipping");
                        pos += longLength;
                        continue;
                    }
                    subOpCode = (ushort)(payload[dataPos] | (payload[dataPos + 1] << 8));
                }

                subNum++;
                DebugLog.Write(LogChannel.LowNetwork,
                    "           Sub-packet #" + subNum + " (" + longLength + " bytes, long form)"
                    + " opcode=0x" + subOpCode.ToString("x4"));


                DispatchAppPacket(payload.Slice(dataPos + 2, actualLen - 2),
                                  actualLen - 2, subOpCode, metadata);

                pos += longLength;
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessSequenced
    //
    // Handles OP_Packet — a sequenced packet carrying a single app-level
    // sub-packet.  If the sequence matches expected, processes immediately.
    // If future (within wrap cutoff), caches for later.  If past, drops.
    //
    // payload:  The payload after ARQ sequence has been stripped
    // arqSeq:   The ARQ sequence number for this packet
    // metadata:     Packet metadata (source/dest IP and port, timestamp, framenumber)
    ///////////////////////////////////////////////////////////////////////////////////////////////

    private void ProcessSequenced(ReadOnlySpan<byte> payload, ushort arqSeq, PacketMetadata metadata)
    {
        if (!_arqSeqFound)
        {
            _arqSeqExpected = arqSeq;
            _arqSeqFound = true;
            DebugLog.Write(LogChannel.LowNetwork,
                "          Sequenced: latching ARQ sequence to "
                + arqSeq.ToString("x4"));
        }

        if (arqSeq == _arqSeqExpected)
        {
            _arqSeqExpected = (ushort)((_arqSeqExpected + 1) & 0xFFFF);

            if (payload.Length < 2)
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "WARNING: Sequenced packet with payload too short for opcode");
                return;
            }

            ushort subOpCode = (ushort)(payload[0] | (payload[1] << 8));

            DebugLog.Write(LogChannel.LowNetwork,
                "          Sequenced: seq=" + arqSeq.ToString("x4")
                + " subOpCode=0x" + subOpCode.ToString("x4"));

            if (subOpCode == 0)
            {
                if (payload.Length < 3)
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "WARNING: Sequenced packet with payload too short for extended opcode");
                    return;
                }
                subOpCode = (ushort)(payload[1] | (payload[2] << 8));
                DispatchAppPacket(payload.Slice(3), payload.Length - 3, subOpCode, metadata);
            }
            else if (SoeConstants.IsNetOpcode(subOpCode))
            {
                ProcessSubpacket(payload, metadata);
            }
            else
            {
                DispatchAppPacket(payload.Slice(2), payload.Length - 2, subOpCode, metadata);
            }
        }
        else if (IsSequenceFuture(arqSeq))
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "  Sequenced: seq=" + arqSeq.ToString("x4")
                + " out of order (expecting " + _arqSeqExpected.ToString("x4")
                + "), caching");
            CachePacket(arqSeq, SoeConstants.OP_Packet, payload, metadata);
        }
        else
        {
            // pass
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessOversized
    //
    // Handles OP_Oversized — a sequenced fragment.  First fragment carries a
    // 4-byte big-endian total length.  Subsequent fragments carry pure data.
    // When the fragment is complete, the reassembled payload is dispatched.
    //
    // payload:  The payload after ARQ sequence has been stripped
    // arqSeq:   The ARQ sequence number for this fragment
    // metadata:     Packet metadata (source/dest IP and port, timestamp, framenumber)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ProcessOversized(ReadOnlySpan<byte> payload, ushort arqSeq, PacketMetadata metadata)
    {
        if (!_arqSeqFound)
        {
            _arqSeqExpected = arqSeq;
            _arqSeqFound = true;
            DebugLog.Write(LogChannel.LowNetwork,
                "  Fragment: latching ARQ sequence to "
                + arqSeq.ToString("x4"));
        }

        if (arqSeq == _arqSeqExpected)
        {
            _arqSeqExpected = (ushort)((_arqSeqExpected + 1) & 0xFFFF);

            if (_fragmentDataSize == 0)
            {
                // First fragment — read the total length
                if (payload.Length < 4)
                {
                    DebugLog.Write(LogChannel.LowNetwork, "WARNING: Oversized first fragment too short for total length");
                    return;
                }

                _fragmentTotalLength = (int)SoeByteOrder.ReadUInt32(payload, 0);

                if (_fragmentTotalLength == 0)
                {
                    ushort fragOp = 0;
                    if (payload.Length >= 6)
                    {
                        fragOp = (ushort)(payload[4] | (payload[5] << 8));
                    }
                    DebugLog.Write(LogChannel.LowNetwork, "WARNING: Oversized packet fragment requested buffer of size 0"
                        + " on stream " + _streamId
                        + " OpCode " + fragOp.ToString("x4")
                        + " seq " + arqSeq.ToString("x4"));
                    return;
                }

                if (_fragmentTotalLength > 2 * 1024 * 1024)
                {
                    DebugLog.Write(LogChannel.LowNetwork,"WARNING: Unusually large fragment total: "
                        + _fragmentTotalLength + " bytes");
                }

                int fragDataLen = payload.Length - 4;
                if (_fragmentTotalLength < fragDataLen)
                {
                    DebugLog.Write(LogChannel.LowNetwork, "WARNING: Oversized first fragment declares total=" + _fragmentTotalLength
                        + " but payload contains " + fragDataLen + " bytes. Treating as invalid, resetting.");
                    FragmentReset();
                    return;
                }

                _fragmentBuffer = new byte[_fragmentTotalLength];

                ReadOnlySpan<byte> fragData = payload.Slice(4);
                int fragLen = fragData.Length;
                fragData.CopyTo(new Span<byte>(_fragmentBuffer, 0, fragLen));
                _fragmentDataSize = fragLen;

                DebugLog.Write(LogChannel.LowNetwork,
                    "          Fragment: seq=" + arqSeq.ToString("x4")
                    + " size=" + payload.Length + " "
                    + _fragmentDataSize + "/" + _fragmentTotalLength + " bytes");
            }
            else
            {
                // Subsequent fragment
                int fragLen = payload.Length;

                if (_fragmentDataSize + fragLen > _fragmentTotalLength)
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "FATAL: ProcessOversized: buffer overflow"
                        + " seq " + arqSeq.ToString("x4")
                        + " stream " + _streamId
                        + ". Buffer is size " + _fragmentTotalLength
                        + ", filled to " + _fragmentDataSize
                        + ", tried to add " + fragLen + " more!");
                    FragmentReset();
                    return;
                }

                DebugLog.Write(LogChannel.LowNetwork,
                    "ProcessOversized subsequent: _fragmentBuffer="
                    + (_fragmentBuffer == null ? "NULL" : "len=" + _fragmentBuffer.Length)
                    + " _fragmentDataSize=" + _fragmentDataSize
                    + " _fragmentTotalLength=" + _fragmentTotalLength
                    + " fragLen=" + fragLen
                    + " arqSeq=" + arqSeq.ToString("x4"));

                payload.CopyTo(new Span<byte>(_fragmentBuffer, _fragmentDataSize, fragLen));
                _fragmentDataSize += fragLen;

                DebugLog.Write(LogChannel.LowNetwork,
                    "          Fragment: seq=" + arqSeq.ToString("x4")
                    + " size=" + payload.Length + " "
                    + _fragmentDataSize + "/" + _fragmentTotalLength + " bytes");
            }

            // Check for completion
            if (_fragmentDataSize == _fragmentTotalLength)
            {
                ReadOnlySpan<byte> fragPayload = new ReadOnlySpan<byte>(
                    _fragmentBuffer, 0, _fragmentDataSize);

                if (fragPayload.Length < 2)
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "WARNING: Fragment complete but data too short for opcode");
                    FragmentReset();
                    return;
                }

                ushort fragOpCode = (ushort)(fragPayload[0] | (fragPayload[1] << 8));

                DebugLog.Write(LogChannel.LowNetwork,
                    "          Fragment COMPLETE: " + _fragmentDataSize + " bytes"
                    + " opcode=0x" + fragOpCode.ToString("x4"));

                if (fragOpCode == 0)
                {
                    if (fragPayload.Length < 3)
                    {
                        DebugLog.Write(LogChannel.LowNetwork,
                            "WARNING: Fragment complete but data too short"
                            + " for extended opcode");
                        FragmentReset();
                        return;
                    }
                    fragOpCode = (ushort)(fragPayload[1] | (fragPayload[2] << 8));
                    DebugLog.Write(LogChannel.LowNetwork,
                        "dispatching complete fragment with new fragOpCode "
                        + fragOpCode.ToString("x4"));
                    DispatchAppPacket(fragPayload.Slice(3),
                                      _fragmentDataSize - 3, fragOpCode, metadata);
                }
                else if (SoeConstants.IsNetOpcode(fragOpCode))
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "dispatching complete fragment via processPacket");
                    ProcessSubpacket(fragPayload, metadata);
                }
                else
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "dispatching complete fragment with fragOpcode "
                        + fragOpCode.ToString("x4") + " via dispatchPacket");
                    DispatchAppPacket(fragPayload.Slice(2),
                                      _fragmentDataSize - 2, fragOpCode, metadata);
                }

                FragmentReset();
            }
        }
        else if (IsSequenceFuture(arqSeq))
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "  Fragment: seq=" + arqSeq.ToString("x4")
                + " out of order (expecting " + _arqSeqExpected.ToString("x4")
                + "), caching");
            CachePacket(arqSeq, SoeConstants.OP_Oversized, payload, metadata);
        }
        else
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "          Fragment: seq=" + arqSeq.ToString("x4")
                + " in the past (expecting " + _arqSeqExpected.ToString("x4")
                + "), dropping");
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessSubpacket
    //
    // Processes a net-opcode sub-packet extracted from OP_Combined or
    // a completed fragment.  Constructs a SoePacket with subpacket=true
    // which means no flags, no CRC, no ARQ per _init_parse.
    //
    // data:  The complete sub-packet including its 2-byte net opcode
    // metadata:     Packet metadata (source/dest IP and port, timestamp, framenumber)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ProcessSubpacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        if (data.Length < 2)
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "  Subpacket: too short");
            return;
        }

        SoePacket spacket = new SoePacket(data, data.Length, true);

        ProcessPacket(spacket.NetOpCode, spacket.Payload(), spacket.ArqSeq,
                      spacket.HasArqSeq(), true, metadata);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CachePacket
    //
    // Stores an out-of-order packet in the ARQ cache for later processing.
    // The payload is copied into a new byte array since the source span
    // may be transient.  The net opcode is stored alongside so that
    // ProcessCache knows whether to route to ProcessSequenced or
    // ProcessOversized.
    //
    // arqSeq:     The ARQ sequence number of the packet
    // netOpcode:  The net opcode (OP_Packet or OP_Oversized)
    // payload:    The packet payload to cache (ARQ already stripped)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void CachePacket(ushort arqSeq, ushort netOpcode, ReadOnlySpan<byte> payload, PacketMetadata metadata)
    {
        if (!_arqCache.ContainsKey(arqSeq))
        {
            CachedPacket cached;
            cached.NetOpcode = netOpcode;
            cached.Payload = payload.ToArray();
            cached.Metadata = metadata;

            _arqCache[arqSeq] = cached;

            if (_arqCache.Count > _arqCacheHighWater)
            {
                _arqCacheHighWater = _arqCache.Count;
                DebugLog.Write(LogChannel.LowNetwork,
                    "  Cache: new high water mark: " + _arqCacheHighWater);
            }
        }
        else
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "  Cache: seq=0x" + arqSeq.ToString("x4")
                + " already cached, ignoring duplicate");
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessCache
    //
    // Drains sequentially-available packets from the ARQ cache.
    // Routes each cached packet to the correct handler based on its
    // stored net opcode.  If the cache grows beyond the give-up
    // threshold, skips ahead to the next available sequence.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ProcessCache()
    {
        if (_arqCache.Count >= _arqSeqGiveUp)
        {
            while (!_arqCache.ContainsKey(_arqSeqExpected))
            {
                DebugLog.Write(LogChannel.LowNetwork,
                    "SEQ: Giving up on finding arq "
                    + _arqSeqExpected.ToString("x4") + " in stream "
                    + StreamNames[_streamId] + " cache, skipping!");
                _arqSeqExpected = (ushort)((_arqSeqExpected + 1) & 0xFFFF);
            }
        }
        while (_arqCache.TryGetValue(_arqSeqExpected, out CachedPacket cached))
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "  Cache: processing arq " + _arqSeqExpected.ToString("x4")
                + " on stream " + StreamNames[_streamId]);
            _arqCache.Remove(_arqSeqExpected);
            ReadOnlySpan<byte> payload = new ReadOnlySpan<byte>(cached.Payload);
            if (cached.NetOpcode == SoeConstants.OP_Oversized)
            {
                ProcessOversized(payload, _arqSeqExpected, cached.Metadata);
            }
            else
            {
                ProcessSequenced(payload, _arqSeqExpected, cached.Metadata);
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsSequenceFuture
    //
    // Returns true if the given ARQ sequence is ahead of the expected
    // sequence but within the wrap cutoff window.  Mirrors the logic
    // from ShowEQ's packetstream.cpp lines 857-858.
    //
    // seq:  The ARQ sequence to test
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private bool IsSequenceFuture(ushort seq)
    {
        if (seq > _arqSeqExpected &&
            seq < ((_arqSeqExpected + SoeConstants.ArqSeqWrapCutoff) & 0xFFFF))
        {
            return true;
        }

        if (_arqSeqExpected >= SoeConstants.ArqSeqWrapCutoff &&
            seq < _arqSeqExpected - SoeConstants.ArqSeqWrapCutoff)
        {
            return true;
        }

        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // FragmentReset
    //
    // Resets the fragment reassembly state.  Does not release the buffer
    // for zone streams — it will be reused for the next fragment sequence.
    // For world streams the buffer is released since world traffic goes
    // quiet after login.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void FragmentReset()
    {
        _fragmentDataSize = 0;
        _fragmentTotalLength = 0;

        if (_streamId == StreamId.StreamClientToWorld ||
            _streamId == StreamId.StreamWorldToClient)
        {
            _fragmentBuffer = null;

            DebugLog.Write(LogChannel.LowNetwork,
                "  Fragment: world stream, buffer released");
        }
        else
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "  Fragment: zone stream, buffer retained");
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessSessionRequest
    //
    // Handles OP_SessionRequest.  Extracts session ID and max packet length.
    // Resets the ARQ sequence to 0.
    //
    // payload:     The session request payload
    // metadata:    Packet metadata (source/dest IP/port, timestamp, framenumber)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ProcessSessionRequest(ReadOnlySpan<byte> payload, PacketMetadata metadata)
    {
        if (payload.Length != SoeConstants.SizeOfSessionRequest)
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "EQPacket: SessionRequest packet with invalid size "
                + payload.Length);
            return;
        }

        _sessionId = SoeByteOrder.ReadUInt32(payload, 4);
        _maxLength = SoeByteOrder.ReadUInt32(payload, 8);

        DebugLog.Write(LogChannel.LowNetwork,
            "EQPacket: SessionRequest found, stream "
            + SoeConstants.StreamNames[_streamId] + " (" + _streamId + "), "
            + "sessionId " + _sessionId.ToString("x8")
            + ", maxLength " + _maxLength);

        _arqSeqExpected = 0;
        _arqSeqFound = true;

        if (_sessionTrackingEnabled != 0)
        {
            _sessionClientPort = metadata.SourcePort;
            _sessionClientIP = SoeByteOrder.IpToUInt32(metadata.SourceIp);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessSessionResponse
    //
    // Handles OP_SessionResponse.  Extracts session ID, session key, and
    // max packet length.  Distributes the session key via the OnSessionKey
    // callback.  Resets the ARQ sequence to 0.
    //
    // payload:     The session response payload
    // metadata:    Packet metadata (source/dest IP/Port, timestamp, framenumber)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ProcessSessionResponse(ReadOnlySpan<byte> payload, PacketMetadata metadata)
    {
        if (payload.Length != SoeConstants.SizeOfSessionResponse)
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "EQPacket: SessionResponse packet with invalid size "
                + payload.Length);
            return;
        }

        _sessionId = SoeByteOrder.ReadUInt32(payload, 0);
        _sessionKey = SoeByteOrder.ReadUInt32(payload, 4);
        _maxLength = SoeByteOrder.ReadUInt32(payload, 11);

        DebugLog.Write(LogChannel.LowNetwork,
            "EQPacket: SessionResponse found, stream "
            + StreamNames[_streamId] + " (" + _streamId + "), "
            + "sessionId " + _sessionId.ToString("x8")
            + ", maxLength " + _maxLength
            + ", key " + _sessionKey.ToString("x8"));

        if (OnSessionKey != null)
        {
            OnSessionKey(_sessionId, _streamId, _sessionKey);
        }

        _arqSeqExpected = 0;
        _arqSeqFound = true;

        if (_sessionTrackingEnabled != 0)
        {
            _sessionClientPort = metadata.DestPort;
            _sessionClientIP = SoeByteOrder.IpToUInt32(metadata.DestIp);

            if (_streamId == StreamId.StreamWorldToClient)
            {
                _sessionTrackingEnabled = 1;
            }
            else if (_streamId == StreamId.StreamZoneToClient)
            {
                _sessionTrackingEnabled = 2;

                if (OnLockOnClient != null)
                {
                    OnLockOnClient(metadata.SourcePort, metadata.DestPort, _sessionClientIP);
                }
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ProcessSessionDisconnect
    //
    // Handles OP_SessionDisconnect.  Validates the session ID if session
    // tracking is enabled, then resets the stream and notifies via
    // the OnClosing callback.
    //
    // payload:  The session disconnect payload
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void ProcessSessionDisconnect(ReadOnlySpan<byte> payload)
    {
        if (_sessionTrackingEnabled != 0)
        {
            if (payload.Length >= 5)
            {
                uint disconnectedSessionId = SoeByteOrder.ReadUInt32(payload, 1);

                if (_sessionId != disconnectedSessionId)
                {
                    DebugLog.Write(LogChannel.LowNetwork,
                        "EQPacket: SessionDisconnect for session "
                        + disconnectedSessionId.ToString("x8")
                        + " does not match our session "
                        + _sessionId.ToString("x8") + ", ignoring");
                    return;
                }
            }
        }

        DebugLog.Write(LogChannel.LowNetwork,
            "EQPacket: SessionDisconnect found, stream "
            + SoeConstants.StreamNames[_streamId] + " (" + _streamId + ")");

        _arqSeqExpected = 0;
        _arqCache.Clear();

        if (_sessionTrackingEnabled != 0)
        {
            _sessionTrackingEnabled = 1;
            _sessionClientPort = 0;
            _sessionClientIP = 0;
        }

        if (OnClosing != null)
        {
            OnClosing(_sessionId, _streamId);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ReceiveSessionKey
    //
    // Called when another stream on the same client receives a session key.
    // Accepts the key if the session ID matches and the source is a
    // different stream.
    //
    // sessionId:   The session ID the key belongs to
    // fromStream:  The stream ID that discovered the key
    // sessionKey:  The session key value
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void ReceiveSessionKey(uint sessionId, StreamId fromStream, uint sessionKey)
    {
        if (fromStream != _streamId && _sessionId == sessionId)
        {
            _sessionKey = sessionKey;

            DebugLog.Write(LogChannel.LowNetwork,
                "SoeStream: Received key " + sessionKey.ToString("x8")
                + " for session " + _sessionId
                + " on stream " + StreamNames[_streamId]
                + " (" + _streamId + ")"
                + " from stream " + StreamNames[fromStream]
                + " (" + fromStream + ")");
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Close
    //
    // Called when any stream on the same client disconnects.  Resets this
    // stream if the session ID matches.
    //
    // sessionId:         The session ID that disconnected
    // fromStream:        The stream ID that disconnected
    // sessionTracking:   The session tracking state to restore
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Close(uint sessionId, StreamId fromStream, int sessionTracking)
    {
        if (sessionId == _sessionId)
        {
            DebugLog.Write(LogChannel.LowNetwork,
                "SoeStream.Close [" + _name + "]: closing for session 0x"
                + sessionId.ToString("x8"));

            Reset();
            _sessionTrackingEnabled = sessionTracking;
        }
    }

    // ---------------------------------------------------------------------------
    // Properties for EqClient access
    // ---------------------------------------------------------------------------

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Channel
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public StreamId Channel
    {
        get { return _streamId; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SessionId
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint SessionId
    {
        get { return _sessionId; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SessionKey
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public uint SessionKey
    {
        get { return _sessionKey; }
        set { _sessionKey = value; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // SessionTrackingEnabled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int SessionTrackingEnabled
    {
        get { return _sessionTrackingEnabled; }
        set { _sessionTrackingEnabled = value; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // PacketCount
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public long PacketCount
    {
        get { return _packetCount; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // ArqCacheHighWater
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int ArqCacheHighWater
    {
        get { return _arqCacheHighWater; }
    }
}