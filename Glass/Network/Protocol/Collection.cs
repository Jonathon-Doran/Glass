using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// Collection
//
// One collection's load-time data: its name, its ordered field definitions, and the two
// bag-sizing quantities derived from them.  A collection is a named ordered list of
// fields; arrays and nesting are expressed by gates among the fields, not here.
//
// SlotCount and ArenaEstimate are computed once when the collection is built and read
// directly by the bag-sizing path, so renting a bag for a collection needs no walk of the
// field array.  SlotCount is the field count.  ArenaEstimate is a generous upper bound on
// the arena bytes a bag for this collection may need, sized from the count of string-typed
// fields.
//
// PatchLevel records which patch load produced this collection, so a handle resolved
// against the wrong patch is detectable.
///////////////////////////////////////////////////////////////////////////////////////////////

public struct Collection
{
    private readonly string _name;
    private FieldDefinition[] _fields;
    private ushort _slotCount;
    private uint _arenaEstimate;
    private readonly PatchLevel _patchLevel;
    private const uint NameArenaAllowance = 32;
    private const uint StringArenaAllowance = 64;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Collection (constructor)
    //
    // Builds a collection with its name and originating patch level.  The field
    // definitions and the bag-sizing quantities derived from them are set later by
    // SetFields, once the fields have been loaded for this collection's handle.
    //
    // name:        The collection's name.
    // patchLevel:  The patch load that produced this collection.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public Collection(string name, PatchLevel patchLevel)
    {
        _name = name;
        _patchLevel = patchLevel;
        _fields = Array.Empty<FieldDefinition>();
        _slotCount = 0;
        _arenaEstimate = 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Fields
    //
    // The collection's ordered field definitions.  Setting them computes the two
    // bag-sizing quantities in a single walk: SlotCount becomes the field count;
    // ArenaEstimate becomes the count of string-typed fields times a per-string allowance
    // of 64 bytes — a deliberate over-estimate of the arena a bag for this collection may
    // need.
    //
    // Must be set through the collection array element directly
    // (_collections[handle].Fields = ...); set on a copy, the computed values are lost.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public FieldDefinition[] Fields
    {
        get
        {
            return _fields;
        }

        set
        {
            _fields = value;

            uint stringFieldCount = 0;
            for (int fieldIndex = 0; fieldIndex < value.Length; fieldIndex++)
            {
                FieldEncoding encoding = value[fieldIndex].Encoding;
                if ((encoding == FieldEncoding.StringNullTerminated)
                    || (encoding == FieldEncoding.StringLengthPrefixed))
                {
                    stringFieldCount++;
                }
            }

            _slotCount = (ushort)value.Length;
            _arenaEstimate = ((uint)value.Length * NameArenaAllowance)
                            + (stringFieldCount * StringArenaAllowance);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Name
    //
    // The collection's name.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string Name
    {
        get { return _name; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SlotCount
    //
    // The number of slots a bag for this collection requires: the field count.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ushort SlotCount
    {
        get { return _slotCount; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ArenaEstimate
    //
    // The recommended arena size in bytes for a bag for this collection: the count of
    // string-typed fields times a per-string allowance.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public uint ArenaEstimate
    {
        get { return _arenaEstimate; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Renders the collection as its name.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        return _name;
    }
}
