using Glass.Network.Protocol.Fields;
using Glass.Network.Protocol;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldNodes
//
// Static helpers that build FieldDisplayNodes for basic field types.  Each helper reads a
// field from the supplied extractor's active bag, formats it, attaches the slot's byte
// range, and adds the new node beneath the supplied parent.  All state is passed in; the
// helpers hold none.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class FieldNodes
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddUIntNode
    //
    // Builds a display node for a uint field and adds it beneath the supplied parent.  The
    // field's value is read from the extractor's active bag, formatted per the format string,
    // and the slot's byte range is attached to the new node.
    //
    // extractor:  The extractor whose active bag holds the field.
    // slotId:     The slot to extract.
    // label:      The label to use in the new display node.
    // parent:     The display node's parent.
    // format:     Numeric format string.  "?" renders hex and decimal together; a format
    //             beginning with 'X' renders hex with a 0x prefix; any other format is passed
    //             to ToString directly.  Defaults to "X".
    ///////////////////////////////////////////////////////////////////////////////////////////

    public static void AddUIntNode(FieldExtractor extractor, SlotId slotId, string label,
        FieldDisplayNode parent, string format = "X")
    {
        uint value = extractor.GetUIntAt(slotId);
        string valueString;
        if (format == "?")
        {
            valueString = "0x" + value.ToString("X") + " (" + value.ToString("D") + ")";
        }
        else if (format[0] == 'X')
        {
            valueString = "0x" + value.ToString(format);
        }
        else
        {
            valueString = value.ToString(format);
        }
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": " + valueString);
        newNode.AddByteRange(extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddIntNode
    //
    // Builds a display node for an int field and adds it beneath the supplied parent.  The
    // field's value is read from the extractor's active bag, formatted per the format string,
    // and the slot's byte range is attached to the new node.
    //
    // extractor:  The extractor whose active bag holds the field.
    // slotId:     The slot to extract.
    // label:      The label to use in the new display node.
    // parent:     The display node's parent.
    // format:     Numeric format string.  "?" renders hex and decimal together; a format
    //             beginning with 'X' renders hex with a 0x prefix; any other format is passed
    //             to ToString directly.  Defaults to "X".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void AddIntNode(FieldExtractor extractor, SlotId slotId, string label,
        FieldDisplayNode parent, string format = "X")
    {
        int value = extractor.GetIntAt(slotId);
        string valueString;
        if (format == "?")
        {
            valueString = "0x" + value.ToString("X") + " (" + value.ToString("D") + ")";
        }
        else if (format[0] == 'X')
        {
            valueString = "0x" + value.ToString(format);
        }
        else
        {
            valueString = value.ToString(format);
        }
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": " + valueString);
        newNode.AddByteRange(extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddFloatNode
    //
    // Builds a display node for a float field and adds it beneath the supplied parent.  The
    // field's value is read from the extractor's active bag, formatted per the format string,
    // and the slot's byte range is attached to the new node.
    //
    // extractor:  The extractor whose active bag holds the field.
    // slotId:     The slot to extract.
    // label:      The label to use in the new display node.
    // parent:     The display node's parent.
    // format:     Numeric format string passed to ToString.  Defaults to "F".
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void AddFloatNode(FieldExtractor extractor, SlotId slotId, string label,
        FieldDisplayNode parent, string format = "F")
    {
        float value = extractor.GetFloatAt(slotId);
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": " + value.ToString(format));
        newNode.AddByteRange(extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AddStringNode
    //
    // Builds a display node for a string field and adds it beneath the supplied parent.  The
    // field's value is read from the extractor's active bag, rendered in double quotes, and
    // the slot's byte range is attached to the new node.
    //
    // extractor:  The extractor whose active bag holds the field.
    // slotId:     The slot to extract.
    // label:      The label to use in the new display node.
    // parent:     The display node's parent.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static void AddStringNode(FieldExtractor extractor, SlotId slotId, string label,
        FieldDisplayNode parent)
    {
        string value = extractor.GetStringAt(slotId);
        FieldDisplayNode newNode = new FieldDisplayNode(label + ": \"" + value + "\"");
        newNode.AddByteRange(extractor.GetByteRangeFor(slotId));
        parent.AddChild(newNode);
    }
}