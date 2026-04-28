using Glass.Core.Logging;

namespace Glass.Network.Protocol.Fields;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldDefinitionExtensions
//
// Extension methods for working with FieldDefinition arrays.  Centralizes lookup logic
// that would otherwise be duplicated in every opcode handler.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class FieldDefinitionExtensions
{
    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IndexOfField
    //
    // Walks the array looking for a definition with the given name and returns its index,
    // or -1 if not found.  Called by handlers at construction time to cache field indices
    // for fast hot-path access; not used in the hot path itself.
    //
    // Returns -1 if the array is null (meaning GetFieldDefinitions returned null because
    // the opcode name was not in the active patch) or if the name was not found.
    //
    // Parameters:
    //   fields    - The array of FieldDefinition to search.  May be null.
    //   fieldName - The field_name column value to look up.
    //
    // Returns:
    //   The index into the array, or -1 if the array is null or the name is not present.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static int IndexOfField(this FieldDefinition[]? fields, string fieldName)
    {
        if (fields == null)
        {
            return -1;
        }

        for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
        {
            if (fields[fieldIndex].Name == fieldName)
            {
                return fieldIndex;
            }
        }

        DebugLog.Write(LogChannel.Network, "FieldDefinitionExtensions.IndexOfField: field '"
            + fieldName + "' not present in field definitions, returning -1");
        return -1;
    }
}
