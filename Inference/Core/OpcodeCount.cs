namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeCount
//
// Pairs a wire opcode value with the number of captured packets observed for it.
// Returned by PacketRepository.OpcodesByCountDescending so callers can reason about
// both identity and frequency without a follow-up count lookup.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly struct OpcodeCount
{
    public readonly ushort OpcodeValue;
    public readonly int Count;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeCount
    //
    // Constructs an OpcodeCount with the given opcode value and packet count.
    //
    // Parameters:
    //   opcodeValue - The wire opcode value.
    //   count       - The number of captured packets observed for this opcode value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeCount(ushort opcodeValue, int count)
    {
        OpcodeValue = opcodeValue;
        Count = count;
    }
}
