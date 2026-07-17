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
    private List<OpcodeValue> _byCountDescending;
    private readonly Dictionary<int, int> _pooledSizeHistogram;
    private readonly Dictionary<SoeConstants.StreamId, uint> _pooledChannelCounts;
    private readonly Dictionary<SoeConstants.StreamId, int> _pureChannelOpcodeCounts;

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
        _byCountDescending = new List<OpcodeValue>();
        _pooledSizeHistogram = new Dictionary<int, int>();
        _pooledChannelCounts = new Dictionary<SoeConstants.StreamId, uint>();
        _pureChannelOpcodeCounts = new Dictionary<SoeConstants.StreamId, int>();
        GlassContext.PacketBus.Subscribe(HandleAppPacket);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PureChannelOpcodeCounts
    //
    // Returns, per channel, the number of opcodes whose packets all rode that channel, as
    // computed by the last Prepare call.  Opcodes seen on more than one channel are counted
    // by no channel.  Empty until Prepare has run.  The returned dictionary is a fresh copy;
    // the caller may iterate it without holding the catalog lock.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public Dictionary<SoeConstants.StreamId, int> PureChannelOpcodeCounts()
    {
        lock (_lock)
        {
            return new Dictionary<SoeConstants.StreamId, int>(_pureChannelOpcodeCounts);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PooledSizeHistogram
    //
    // Returns the packet count per observed length across all opcodes, as accumulated by the
    // last Prepare call.  Empty until Prepare has run.  The returned dictionary is a fresh
    // copy; the caller may iterate it without holding the catalog lock.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public Dictionary<int, int> PooledSizeHistogram()
    {
        lock (_lock)
        {
            return new Dictionary<int, int>(_pooledSizeHistogram);
        }
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // PooledChannelCounts
    //
    // Returns the packet count per channel across all opcodes, as accumulated by the last
    // Prepare call.  Empty until Prepare has run.  The returned dictionary is a fresh copy;
    // the caller may iterate it without holding the catalog lock.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public Dictionary<SoeConstants.StreamId, uint> PooledChannelCounts()
    {
        lock (_lock)
        {
            return new Dictionary<SoeConstants.StreamId, uint>(_pooledChannelCounts);
        }
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

            // new opcode
            if (!_byOpcode.TryGetValue(wireValue, out bucket))
            {
                bucket = new List<CatalogedPacket>();
                _byOpcode[wireValue] = bucket;

                OpcodeStats stats = new OpcodeStats();
                stats.MinSize = length;
                stats.MaxSize = length;
                stats.Count = 1;
                stats.LargestPacketIndex = cataloged.PacketIndex;
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
                    stats.LargestPacketIndex = cataloged.PacketIndex;
                }

                stats.Count++;
                _stats[wireValue] = stats;
            }
            bucket.Add(cataloged);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // LogSizeHistogram
    //
    // Writes the given opcode's size histogram to the log as C# dictionary initializer
    // lines, sorted by length ascending, for transcription into a baseline table.
    // Requires Prepare to have run; logs a warning and writes nothing when the opcode has
    // no histogram.
    //
    // opcode:  Wire opcode value whose histogram to write.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void LogSizeHistogram(OpcodeValue opcode)
    {
        lock (_lock)
        {
            OpcodeStats stats;
            if (!_stats.TryGetValue(opcode, out stats)
                || stats.SizeHistogram == null || stats.SizeHistogram.Count == 0)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PacketCatalog.LogSizeHistogram: no histogram for opcode 0x" + opcode
                    + " (Prepare not run, or opcode unseen)", LogLevel.Warn);
                return;
            }

            List<int> lengths = new List<int>(stats.SizeHistogram.Keys);
            lengths.Sort();
            DebugLog.Write(LogChannel.Opcodes,
                "PacketCatalog.LogSizeHistogram: opcode 0x" + opcode + ", "
                + lengths.Count + " lengths, " + stats.Count + " messages total", LogLevel.Info);
            for (int lengthIndex = 0; lengthIndex < lengths.Count; lengthIndex++)
            {
                int length = lengths[lengthIndex];
                DebugLog.Write(LogChannel.Opcodes,
                    "    { " + length + ", " + stats.SizeHistogram[length] + " },", LogLevel.Info);
            }
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
    // OpcodesByCountDescending
    //
    // Returns the wire values ordered by packet count, highest first, as computed by the
    // last Prepare call.  Empty until Prepare has run.  The returned list is a fresh copy.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public List<OpcodeValue> OpcodesByCountDescending()
    {
        lock (_lock)
        {
            return new List<OpcodeValue>(_byCountDescending);
        }
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
    // CountAt
    //
    // Returns the packet count of the opcode at the given position in count-descending order,
    // or zero if the position is out of range.  Position 0 is the highest-count opcode.
    // Valid after Prepare has run.
    //
    // rank:  Zero-based position in count-descending order.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint CountAt(int rank)
    {
        lock (_lock)
        {
            if (rank < 0 || rank >= _byCountDescending.Count)
            {
                return 0;
            }

            OpcodeValue wireValue = _byCountDescending[rank];
            return (uint) _byOpcode[wireValue].Count;
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
    // Prepare
    //
    // Cold-path pass that computes the length-distribution fields (ModalSize, ModalFraction,
    // DistinctSizeCount, MeanSize, SizeVariance, SizeHistogram) and the per-channel packet
    // counts (ChannelCounts) for every opcode from its retained packets.  The same pass
    // accumulates the pooled size histogram, the pooled channel counts, and the per-channel
    // tally of single-channel opcodes; all pooled tables are rebuilt from scratch on every
    // call.  Intended to run once after capture has stopped and before analysis begins;
    // running it while packets are still arriving produces stats that do not match later
    // arrivals.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Prepare()
    {
        DebugLog.Write(LogChannel.InferenceDebug, "PacketCatalog.Prepare: starting", LogLevel.Trace);
        lock (_lock)
        {
            _pooledSizeHistogram.Clear();
            _pooledChannelCounts.Clear();
            _pureChannelOpcodeCounts.Clear();
            foreach (KeyValuePair<OpcodeValue, List<CatalogedPacket>> entry in _byOpcode)
            {
                OpcodeValue wireValue = entry.Key;
                List<CatalogedPacket> bucket = entry.Value;
                Dictionary<int, int> lengthCounts = new Dictionary<int, int>();
                Dictionary<SoeConstants.StreamId, uint> channelCounts =
                    new Dictionary<SoeConstants.StreamId, uint>();
                long lengthSum = 0;
                long lengthSquaredSum = 0;
                for (int i = 0; i < bucket.Count; i++)
                {
                    int length = bucket[i].Payload.Length;
                    int seen;
                    if (lengthCounts.TryGetValue(length, out seen))
                    {
                        lengthCounts[length] = seen + 1;
                    }
                    else
                    {
                        lengthCounts[length] = 1;
                    }
                    lengthSum += length;
                    lengthSquaredSum += (long)length * length;
                    SoeConstants.StreamId channel = bucket[i].Metadata.Channel;
                    uint channelSeen;
                    if (channelCounts.TryGetValue(channel, out channelSeen))
                    {
                        channelCounts[channel] = channelSeen + 1;
                    }
                    else
                    {
                        channelCounts[channel] = 1;
                    }
                }
                foreach (KeyValuePair<int, int> lengthEntry in lengthCounts)
                {
                    int pooledSeen;
                    if (_pooledSizeHistogram.TryGetValue(lengthEntry.Key, out pooledSeen))
                    {
                        _pooledSizeHistogram[lengthEntry.Key] = pooledSeen + lengthEntry.Value;
                    }
                    else
                    {
                        _pooledSizeHistogram[lengthEntry.Key] = lengthEntry.Value;
                    }
                }
                foreach (KeyValuePair<SoeConstants.StreamId, uint> channelEntry in channelCounts)
                {
                    uint pooledChannelSeen;
                    if (_pooledChannelCounts.TryGetValue(channelEntry.Key, out pooledChannelSeen))
                    {
                        _pooledChannelCounts[channelEntry.Key] = pooledChannelSeen + channelEntry.Value;
                    }
                    else
                    {
                        _pooledChannelCounts[channelEntry.Key] = channelEntry.Value;
                    }
                }
                // an opcode whose packets all rode one channel counts toward that channel's
                // pure-opcode tally; mixed opcodes are counted by no channel
                if (channelCounts.Count == 1)
                {
                    foreach (KeyValuePair<SoeConstants.StreamId, uint> soleEntry in channelCounts)
                    {
                        int pureSeen;
                        if (_pureChannelOpcodeCounts.TryGetValue(soleEntry.Key, out pureSeen))
                        {
                            _pureChannelOpcodeCounts[soleEntry.Key] = pureSeen + 1;
                        }
                        else
                        {
                            _pureChannelOpcodeCounts[soleEntry.Key] = 1;
                        }
                    }
                }
                int modalSize = 0;
                int modalCount = 0;
                foreach (KeyValuePair<int, int> lengthEntry in lengthCounts)
                {
                    if (lengthEntry.Value > modalCount)
                    {
                        modalCount = lengthEntry.Value;
                        modalSize = lengthEntry.Key;
                    }
                }
                double mean = bucket.Count > 0 ? (double)lengthSum / bucket.Count : 0.0;
                double variance = bucket.Count > 0
                    ? ((double)lengthSquaredSum / bucket.Count) - (mean * mean)
                    : 0.0;
                if (variance < 0.0)
                {
                    variance = 0.0;
                }
                OpcodeStats stats = _stats[wireValue];
                stats.ModalSize = modalSize;
                stats.ModalFraction = bucket.Count > 0 ? (double)modalCount / bucket.Count : 0.0;
                stats.DistinctSizeCount = lengthCounts.Count;
                stats.MeanSize = mean;
                stats.SizeVariance = variance;
                stats.SizeHistogram = lengthCounts;
                stats.ChannelCounts = channelCounts;
                _stats[wireValue] = stats;
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PacketCatalog.Prepare: wireValue=0x" + wireValue
                    + " modal=" + modalSize + " frac=" + stats.ModalFraction.ToString("F3")
                    + " distinct=" + stats.DistinctSizeCount
                    + " channels=" + channelCounts.Count, LogLevel.Trace);
            }
            _byCountDescending = new List<OpcodeValue>(_byOpcode.Keys);
            _byCountDescending.Sort((a, b) => _byOpcode[b].Count.CompareTo(_byOpcode[a].Count));
            foreach (KeyValuePair<SoeConstants.StreamId, int> pureEntry in _pureChannelOpcodeCounts)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "PacketCatalog.Prepare: pure channel " + pureEntry.Key
                    + " = " + pureEntry.Value + " opcodes", LogLevel.Info);
            }
            DebugLog.Write(LogChannel.InferenceDebug,
                "PacketCatalog.Prepare: sorted " + _byCountDescending.Count + " opcodes, pooled "
                + _pooledSizeHistogram.Count + " lengths, " + _pooledChannelCounts.Count
                + " channels", LogLevel.Info);
        }
        DebugLog.Write(LogChannel.InferenceDebug, "PacketCatalog.Prepare: done", LogLevel.Trace);

        OpcodeValue toDebug = new OpcodeValue(0x917c);
        LogSizeHistogram(toDebug);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // HasPackets
    //
    // Returns true if at least one packet has been cataloged this session.  Reads the
    // arrival-order list count under the lock; no allocation.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool HasPackets()
    {
        lock (_lock)
        {
            return _packets.Count > 0;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TotalPacketCount
    //
    // Returns the total number of packets cataloged across all opcodes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ulong TotalPacketCount()
    {
        lock (_lock)
        {
            return (ulong)_packets.Count;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Removes every cataloged packet, every per-opcode bucket, every per-opcode stats
    // record, and both pooled background tables.  Takes the catalog lock for the duration
    // of the operation.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        lock (_lock)
        {
            _packets.Clear();
            _byOpcode.Clear();
            _stats.Clear();
            _byCountDescending.Clear();
            _pooledSizeHistogram.Clear();
            _pooledChannelCounts.Clear();
            _pureChannelOpcodeCounts.Clear();
        }
        DebugLog.Write(LogChannel.InferenceDebug, "PacketCatalog.Clear: cleared", LogLevel.Trace);
    }
}