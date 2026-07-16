using System.Collections.Generic;
using static Glass.Network.Protocol.SoeConstants;
namespace Inference.Models;
///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeStats
//
// Per-opcode aggregates for one wire value.  Channel, MinSize, MaxSize, and Count are
// maintained incrementally by PacketCatalog as packets arrive.  ModalSize, ModalFraction,
// DistinctSizeCount, SizeHistogram, and ChannelCounts describe the length and direction
// distributions and are filled by the catalog's cold-path Prepare pass; they are zero or
// null until Prepare runs.
///////////////////////////////////////////////////////////////////////////////////////////////
public struct OpcodeStats
{
    public int MinSize;
    public int MaxSize;
    public uint Count;
    public int ModalSize;                       // the most frequently observed packet length in bytes
    public double ModalFraction;                // share of packets at ModalSize, in the range 0.0 to 1.0
    public int DistinctSizeCount;               // number of different packet lengths seen; 1 means fixed size
    public double MeanSize;                     // arithmetic mean of packet lengths in bytes
    public double SizeVariance;                 // variance of packet lengths; high with a few large outliers
    public Dictionary<int, int>? SizeHistogram; // packet count per observed length; null until Prepare runs
    public Dictionary<StreamId, uint>? ChannelCounts; // packet count per channel; null until Prepare runs
}
