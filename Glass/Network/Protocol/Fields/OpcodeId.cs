namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeId
//
// Identifies an opcode within a patch by numeric opcode value and version.  Used as the
// dictionary key for per-opcode metadata caches in PatchData.
//
// A patch can contain multiple versions of the same opcode; PatchOpcode's UNIQUE
// constraint is on (patch_date, server_type, opcode_name, version), so the numeric
// value alone does not identify an opcode.  This struct carries both pieces and
// provides value equality so it can be used as a dictionary key without boxing.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly struct OpcodeId : IEquatable<OpcodeId>
{
    public readonly ushort Opcode;
    public readonly int Version;

    public OpcodeId(ushort opcode, int version = 1)
    {
        Opcode = opcode;
        Version = version;
    }

    public bool Equals(OpcodeId other)
    {
        return Opcode == other.Opcode && Version == other.Version;
    }

    public override bool Equals(object? obj)
    {
        if (obj is OpcodeId other)
        {
            return Equals(other);
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Opcode, Version);
    }
}
