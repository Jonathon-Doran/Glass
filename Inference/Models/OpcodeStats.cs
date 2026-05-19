using static Glass.Network.Protocol.SoeConstants;

namespace Inference.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeStats
//
// Per-opcode aggregates maintained by PacketCatalog as packets arrive.
// Channel is set from the first packet seen for the opcode and never
// changes.  MinSize and MaxSize are updated on each subsequent packet when
// its length falls outside the current range.
//
// Held by value.  The catalog stores its own mutable copy inside a
// dictionary under its lock and updates the fields in place; StatsFor
// returns a copy of that record by value, so callers see a consistent
// snapshot without any lock contract.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct OpcodeStats
{
    public StreamId Channel;
    public int MinSize;
    public int MaxSize;
}
