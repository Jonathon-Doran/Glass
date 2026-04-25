using System.ComponentModel;
using static Glass.Network.Protocol.SoeConstants;

namespace Inference.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeEntry
//
// Represents a single row in the Opcodes grid.  Each entry tracks one
// unique combination of opcode + direction.  Properties that change at
// runtime (Count, MinSize, MaxSize) raise PropertyChanged so the grid
// updates in place.
///////////////////////////////////////////////////////////////////////////////////////////////
public class OpcodeEntry : INotifyPropertyChanged
{
    private int _count;
    private int _minSize;
    private int _maxSize;
    private string _name;

    public event PropertyChangedEventHandler? PropertyChanged;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeEntry (constructor)
    //
    // opcodeHex:   Formatted hex string for display (e.g. "0x9c2d")
    // direction:   "C2Z" or "Z2C"
    // initialSize: Payload length of the first packet seen
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeEntry(string opcodeHex, StreamId channel, int initialSize)
    {
        Opcode = opcodeHex;
        Channel = channel;
        _name = string.Empty;
        _count = 1;
        _minSize = initialSize;
        _maxSize = initialSize;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Opcode
    //
    // The hex-formatted opcode string.  Does not change after construction.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string Opcode { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Channel
    //
    // Stream this opcode arrived on.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public StreamId Channel { get; }

    public string ChannelAbbrev
    {
        get
        {
            return StreamAbbrev[Channel];
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Name
    //
    // Logical name of the opcode (e.g. "OP_ClientUpdate").  Empty until
    // a hypothesis is accepted by the operator.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string Name
    {
        get { return _name; }
        set
        {
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Count
    //
    // Number of packets seen for this opcode + direction.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int Count
    {
        get { return _count; }
        set
        {
            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // MinSize
    //
    // Smallest payload length seen for this opcode + direction.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int MinSize
    {
        get { return _minSize; }
        set
        {
            _minSize = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinSize)));
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // MaxSize
    //
    // Largest payload length seen for this opcode + direction.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public int MaxSize
    {
        get { return _maxSize; }
        set
        {
            _maxSize = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxSize)));
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RawOpcode
    //
    // The numeric opcode value.  Not displayed in the grid, but used for
    // dictionary lookups in the handler.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort RawOpcode { get; init; }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // RawDirection
    //
    // The numeric direction byte.  Not displayed in the grid, but used for
    // dictionary lookups in the handler.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public byte RawDirection { get; init; }
}