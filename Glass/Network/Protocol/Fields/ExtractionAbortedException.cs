using System;
namespace Glass.Network.Protocol.Fields;
///////////////////////////////////////////////////////////////////////////////////////////////
// ExtractionAbortedException
//
// Thrown by FieldExtractor guards when extraction becomes untrustworthy: an implausible
// resolved multiplicity, or recursion beyond any legitimate nesting depth.  Carries the
// location and cause so the boundary catch in Extract can log a diagnostic that brackets
// the failure.  Internal control flow only: thrown by guard checks, caught exclusively in
// Extract, never propagated to callers.
///////////////////////////////////////////////////////////////////////////////////////////////
public class ExtractionAbortedException : Exception
{
    public long BitOffset { get; }
    public string FieldName { get; }
    public ulong ResolvedCount { get; }
    public int Depth { get; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ExtractionAbortedException (constructor)
    //
    // message:        Human-readable cause, e.g. "multiplicity exceeds remaining payload".
    // bitOffset:      Absolute bit offset into the payload where the guard tripped.
    // fieldName:      The field or collection being extracted when the guard tripped.
    // resolvedCount:  The offending multiplicity, zero when the cause is not a count.
    // depth:          Recursion depth at the guard, zero when the cause is not depth.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ExtractionAbortedException(string message, long bitOffset, string fieldName,
        ulong resolvedCount, int depth)
        : base(message)
    {
        BitOffset = bitOffset;
        FieldName = fieldName;
        ResolvedCount = resolvedCount;
        Depth = depth;
    }
}
