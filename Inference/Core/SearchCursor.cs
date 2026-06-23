namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// SearchCursor
//
// Index into OpcodeTracePresenter's unified List<SearchMatch>, naming the match the find
// navigation is currently parked on.
//
// Wraps a uint so method signatures and field definitions distinguish a cursor from other
// integers; implicit conversion to uint keeps list indexing clean.  Construction from a raw
// uint is explicit, keeping the origin of every cursor deliberate.
//
// Cursor value uint.MaxValue is reserved to mean "inactive": no search is positioned and the
// cursor is parked on no match.  A cursor is valid only against the match list it was advanced
// over and only until that list is rebuilt by the next search.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct SearchCursor(uint Value)
{
    public static implicit operator uint(SearchCursor cursor) => cursor.Value;
    public static explicit operator SearchCursor(uint value) => new(value);
    public const uint InactiveValue = uint.MaxValue;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Inactive
    //
    // The reserved cursor meaning no search is positioned on any match.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static SearchCursor Inactive
    {
        get { return new SearchCursor(InactiveValue); }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Active
    //
    // True when the cursor is parked on a match.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Active
    {
        get { return Value != InactiveValue; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Renders the cursor as its numeric value, or "Inactive" for the sentinel.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        if (Active == false)
        {
            return "Inactive";
        }
        return Value.ToString();
    }
}