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
    // Walks the list looking for a definition with the given name and returns its index,
    // or -1 if not found.  Called by handlers at construction time to cache field indices
    // for fast hot-path access; not used in the hot path itself.
    //
    // Returns -1 if the list is null (meaning GetFields returned null because the opcode
    // name was not in the patch) or if the name was not found.
    //
    // The list parameter is IReadOnlyList rather than an array.  Handlers cache the value
    // returned by FieldExtractor.GetFields, which is the same reference held in
    // PatchData's per-opcode cache.
    //
    // Parameters:
    //   fields    - The field definitions to search.  May be null.
    //   fieldName - The field_name column value to look up.
    //
    // Returns:
    //   The index into the list, or -1 if the list is null or the name is not present.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static int IndexOfField(this IReadOnlyList<FieldDefinition>? fields, string fieldName)
    {
        if (fields == null)
        {
            return -1;
        }

        for (int fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
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
