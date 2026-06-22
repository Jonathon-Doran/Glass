namespace Glass.UI;

///////////////////////////////////////////////////////////////////////////////////////////////
// ArgbColor
//
// A 32-bit color as packed ARGB: alpha in the high byte, then red, green, and blue.  Wraps the
// packed value so a color is a named type rather than a bare integer wherever colors are stored,
// compared, or used as a dictionary key.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct ArgbColor : System.IFormattable
{
    public readonly uint Value;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ArgbColor (constructor)
    //
    // Stores the packed ARGB value.
    //
    // value:  The packed ARGB value, alpha in the high byte through blue in the low byte.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ArgbColor(uint value)
    {
        Value = value;
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Formats the packed ARGB value as eight lowercase hex digits.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        return Value.ToString("x8");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Formats the packed ARGB value with the given numeric format string and provider, so an
    // explicit format such as "x8" or "X8" is honored.
    //
    // format:    Numeric format string applied to the packed value.
    // provider:  Format provider applied to the packed value.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public string ToString(string? format, System.IFormatProvider? provider)
    {
        return Value.ToString(format, provider);
    }
}