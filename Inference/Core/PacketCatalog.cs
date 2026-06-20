using Glass.Core;
using Glass.Core.Logging;
using Glass.Core.Memory;
using Glass.Network.Protocol;
using Inference.Models;
using System;
using System.Collections.Generic;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// PacketCatalog
//
// Indexes every captured application-level packet seen this session.
// Subscribes to PacketBus and, for each delivered payload, asks the
// supplied RetainedBufferPool for a long-lived copy and records a
// CatalogedPacket holding the opcode, metadata, and retained payload.
//
// The catalog records what came off the wire.  It is patch-level-agnostic:
// opcode values stored are wire values, not handles, and no opcode names are
// resolved here.  Callers that need a name or a decoded field set use
// PatchRegistry separately.  This keeps the catalog valid across patch level
// changes; an older capture can still be queried after switching patch
// levels.
//
// HandleAppPacket runs on the bus delivery thread (capture or pcap reader).
// Read methods may be called from any thread; they take the same lock as the
// writer.  Read callers that need to iterate at length should copy to a
// local list to avoid holding the lock.
///////////////////////////////////////////////////////////////////////////////////////////////
public class PacketCatalog
{
    private readonly RetainedBufferPool _retainedBufferPool;
    private readonly object _lock;
    private readonly List<CatalogedPacket> _packets;
    private readonly Dictionary<OpcodeValue, List<CatalogedPacket>> _byOpcode;
    private readonly Dictionary<OpcodeValue, OpcodeStats> _stats;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PacketCatalog (constructor)
    //
    // Initializes the catalog and subscribes its packet handler to the PacketBus.
    // The catalog remains subscribed for its entire lifetime.
    //
    // retainedBufferPool:  The pool the catalog calls Retain on for each
    //                      delivered payload.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public PacketCatalog(RetainedBufferPool retainedBufferPool)
    {
        _retainedBufferPool = retainedBufferPool;
        _lock = new object();
        _packets = new List<CatalogedPacket>();
        _byOpcode = new Dictionary<OpcodeValue, List<CatalogedPacket>>();
        _stats = new Dictionary<OpcodeValue, OpcodeStats>();
        GlassContext.PacketBus.Subscribe(HandleAppPacket);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HandleAppPacket
    //
    // PacketBus delivery target.  Retains a long-lived copy of the payload
    // via RetainedBufferPool and records the packet in both the arrival-order
    // list and the per-opcode bucket.  Per-opcode aggregates (channel, min
    // size, max size) are maintained in a parallel stats dictionary, updated
    // incrementally as packets arrive.
    //
    // Channel is captured from the first packet for the opcode and never
    // changes.  Min and max are updated on each subsequent packet when its
    // length falls outside the current range.  Because OpcodeStats is a
    // value type, the update branch reads the current record into a local,
    // mutates the local, and assigns it back to the dictionary.
    //
    // Runs on the bus delivery thread.  Retain is called outside the lock
    // because it has no shared state with the catalog's collections; only
    // the collection mutations need serialization against concurrent
    // readers.
    //
    // data:      Application payload, opcode bytes already stripped.  Valid
    //            only for the duration of this call.
    // metadata:  Source/dest IP and port, timestamp, frame number, session,
    //            and channel.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void HandleAppPacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        OpcodeValue wireValue = metadata.WireValue;

        int length = data.Length;
        RetainedBuffer payload = _retainedBufferPool.Retain(data);
        CatalogedPacket cataloged;
        lock (_lock)
        {

            OpcodeValue opcode = metadata.Opcode.Value;
            cataloged = new CatalogedPacket(metadata, opcode, payload)
            {
                PacketIndex = (uint)_packets.Count,
            };
            _packets.Add(cataloged);

            List<CatalogedPacket>? bucket;
            if (!_byOpcode.TryGetValue(wireValue, out bucket))
            {
                bucket = new List<CatalogedPacket>();
                _byOpcode[wireValue] = bucket;

                OpcodeStats stats = new OpcodeStats();
                stats.Channel = metadata.Channel;
                stats.MinSize = length;
                stats.MaxSize = length;
                _stats[wireValue] = stats;
            }
            else
            {
                OpcodeStats stats = _stats[wireValue];
                if (length < stats.MinSize)
                {
                    stats.MinSize = length;
                }
                if (length > stats.MaxSize)
                {
                    stats.MaxSize = length;
                }
                _stats[wireValue] = stats;
            }
            bucket.Add(cataloged);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PacketsFor
    //
    // Returns packets recorded for the given opcode, optionally filtered by
    // channel and session.  Results are in arrival order within the opcode's
    // bucket and capped at maxResults.  The returned list is a fresh copy;
    // the caller may iterate it without holding the catalog lock.
    //
    // No payload truncation: each CatalogedPacket carries its full retained
    // payload.  Callers that want to display only the first N bytes take a
    // span of that length from the payload at render time.
    //
    // opcode:      Wire opcode value to retrieve.  Required.
    // channel:     Optional channel filter.  Null matches any channel.
    // sessionId:   Optional session filter.  Null matches any session.
    // maxResults:  Maximum number of packets to return.  Use int.MaxValue
    //              for no cap.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<CatalogedPacket> PacketsFor(OpcodeValue opcode,
        SoeConstants.StreamId? channel, int? sessionId, int maxResults)
    {
        List<CatalogedPacket> results = new List<CatalogedPacket>();

        lock (_lock)
        {
            List<CatalogedPacket>? bucket;
            if (!_byOpcode.TryGetValue(opcode, out bucket))
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PacketCatalog.PacketsFor: no bucket for opcode=0x"
                    + opcode, LogLevel.Warn);
                return results;
            }

            for (int i = 0; i < bucket.Count; i++)
            {
                CatalogedPacket packet = bucket[i];

                if (channel != null && packet.Metadata.Channel != channel.Value)
                {
                    continue;
                }
                if (sessionId != null && packet.Metadata.SessionId != sessionId.Value)
                {
                    continue;
                }

                results.Add(packet);
                if (results.Count >= maxResults)
                {
                    break;
                }
            }
        }

        return results;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // StatsFor
    //
    // Returns the per-opcode aggregates (channel, min size, max size) for
    // the given opcode, or null if the opcode has never been seen.  The
    // returned reference is the live record updated by HandleAppPacket;
    // callers must take the catalog lock around field reads to see a
    // consistent snapshot.
    //
    // opcode:  Wire opcode value to query.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeStats? StatsFor(OpcodeValue opcode)
    {
        lock (_lock)
        {
            OpcodeStats stats;
            if (!_stats.TryGetValue(opcode, out stats))
            {
                return null;
            }
            return stats;
        }
    }


    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountFor
    //
    // Returns the number of packets recorded for the given opcode.  Zero if
    // the opcode has never been seen.  Cheap: reads bucket.Count under the
    // lock, no walk and no copy.
    //
    // opcode:  Wire opcode value to query.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public int CountFor(OpcodeValue opcode)
    {
        lock (_lock)
        {
            List<CatalogedPacket>? bucket;
            if (!_byOpcode.TryGetValue(opcode, out bucket))
            {
                return 0;
            }
            return bucket.Count;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // KnownOpcodes
    //
    // Returns a snapshot of the wire opcode values seen at least once this
    // session.  The snapshot is taken under the catalog lock and returned as
    // a fresh array; the caller may iterate it without holding the lock.
    // Order is unspecified.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeValue[] KnownOpcodes()
    {
        lock (_lock)
        {
            OpcodeValue[] snapshot = new OpcodeValue[_byOpcode.Count];
            _byOpcode.Keys.CopyTo(snapshot, 0);
            return snapshot;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PacketAt
    //
    // Returns the CatalogedPacket recorded at the given arrival index, or
    // null when the index is out of range.  Arrival indices are assigned
    // in HandleAppPacket as the value of _packets.Count at insertion;
    // nothing ever removes from _packets, so the index is a direct
    // position in the arrival-order list.
    //
    // packetIndex:  Arrival index to retrieve.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public CatalogedPacket? PacketAt(uint packetIndex)
    {
        lock (_lock)
        {
            if (packetIndex >= _packets.Count)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PacketCatalog.PacketAt: index " + packetIndex
                    + " out of range (count=" + _packets.Count + ")", LogLevel.Error);
                return null;
            }
            return _packets[(int)packetIndex];
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Removes every cataloged packet, every per-opcode bucket, and every
    // per-opcode stats record.  Takes the catalog lock for the duration of
    // the operation.
    ///////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        lock (_lock)
        {
            _packets.Clear();
            _byOpcode.Clear();
            _stats.Clear();
        }
        DebugLog.Write(LogChannel.InferenceDebug, "PacketCatalog.Clear: cleared", LogLevel.Trace);
    }
}