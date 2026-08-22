namespace Glass.World;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Id<TTag>
//
// Strongly typed wrapper for a uint identifier. The tag type parameter carries no data; it exists only to
// make two identifier kinds distinct types to the compiler, so an identifier of one kind cannot be passed
// where another kind is expected. Value uint.MaxValue is reserved to mean "no identifier"; None exposes
// that reserved value and Exists tests against it. Conversion to uint is implicit; construction from a
// uint is explicit.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public readonly record struct Id<TTag>(uint Value)
{
    public const uint NoneValue = uint.MaxValue;

    public static Id<TTag> None => new Id<TTag>(NoneValue);

    public bool Exists => Value != NoneValue;

    public static implicit operator uint(Id<TTag> id) => id.Value;
    public static explicit operator Id<TTag>(uint value) => new Id<TTag>(value);

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Formats the identifier as the tag type name followed by the numeric value, or "None" for the
    // reserved no-identifier value, for use in log messages.
    //
    // Returns the formatted string.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        if (!Exists)
        {
            return "None";
        }
        return Value.ToString();
    }
}

// Tag types for the world-domain identifiers. Empty by design; see Id<TTag>.
// World domain.
public readonly struct MobTag { }                  // Glass-assigned mob record identifier, unique per process lifetime.
public readonly struct ZoneTag { }                 // Zone identifier.
public readonly struct SpawnTag { }                // Server-assigned spawn identifier, meaningful only within one zone.

// Protocol domain.
public readonly struct OpcodeTag { }               // Issued by PatchData at load time; not interchangeable across patch levels.
public readonly struct CollectionTag { }           // Issued by PatchData at load time; not interchangeable across patch levels.
public readonly struct CollectionIndexTag { }      // Per-PatchData index; not a global identity.
public readonly struct GateTag { }                 // Valid only until the owning extraction's gate list is reset.
public readonly struct GateDefinitionTag { }       // Issued by PatchData at load time; not interchangeable across patch levels.
public readonly struct BagTag { }                  // Valid only until the owning GateTree is reset.
public readonly struct SlotTag { }                 // Field bag slot identifier.
public readonly struct SpellTag { }                // Spell identifier from the client's spell data file.
public readonly struct SpellCategoryTag { }        // Spell category identifier from the database string file.
public readonly struct MessageIndexTag { }         // Arrival position of a message within a capture.
public readonly struct ItemTag { }                 // Item identifier from the item serialization.
public readonly struct ItemInstanceTag { }         // Database-assigned item instance identifier.