using Glass.Core.Logging;

namespace Glass.Data.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// ItemLocation
//
// The location of one item: storage system, main position, sub position, and
// aug position.  Sub and aug positions are 0xFFFF when absent.  Immutable, and
// usable as a dictionary key.
//
// A position is valid only when _exists is true.  Only the public constructor
// sets it, and only when the storage value is a member of StorageSystem.  None
// is the named invalid position, with every part set to NoneValue.  An
// uninitialized ItemLocation is also invalid (_exists false, parts zero).  All
// invalid positions compare equal to each other and never equal a valid one.
///////////////////////////////////////////////////////////////////////////////////////////////
public readonly struct ItemLocation : IEquatable<ItemLocation>
{
    // Value of a sub or aug position that is absent
    public const uint Absent = 0xFFFF;

    // Reserved value held in every part of None
    public const uint NoneValue = uint.MaxValue;

    // The invalid position: every part is NoneValue and _exists is false
    public static ItemLocation None => new ItemLocation(NoneValue);

    // True only for a position built by the public constructor with a known storage value
    private readonly bool _exists;

    public StorageSystem Storage { get; }
    public uint MainPosition { get; }
    public uint SubPosition { get; }
    public uint AugPosition { get; }

    public bool Exists => _exists;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ItemLocation (constructor)
    //
    // Builds a position from its four parts.  The position is valid only if storage
    // is a member of StorageSystem; otherwise it is left invalid and a warning is
    // logged.
    //
    // storage:       The storage system holding the item.
    // mainPosition:  The main position within the storage system.
    // subPosition:   The position within a container, or Absent.
    // augPosition:   The augment position within an item, or Absent.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public ItemLocation(StorageSystem storage, uint mainPosition, uint subPosition, uint augPosition)
    {
        Storage = storage;
        MainPosition = mainPosition;
        SubPosition = subPosition;
        AugPosition = augPosition;

        if (Enum.IsDefined(storage) == false)
        {
            _exists = false;
            DebugLog.Write(LogChannel.General, "ItemLocation: unknown storage value " + (uint)storage +
                " (main " + mainPosition + ", sub " + subPosition + ", aug " + augPosition +
                "); position is invalid", LogLevel.Warn);
            return;
        }

        _exists = true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ItemLocation (constructor)
    //
    // Builds an invalid position, with every part set to fill and _exists false.
    // There is no way to produce a valid position through this constructor.
    //
    // fill:  The value stored in every part.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private ItemLocation(uint fill)
    {
        Storage = (StorageSystem)fill;
        MainPosition = fill;
        SubPosition = fill;
        AugPosition = fill;
        _exists = false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Equals
    //
    // Compares this position with another position.  Two invalid positions are
    // equal; an invalid position never equals a valid one; two valid positions are
    // equal when all four parts are equal.
    //
    // other:    The position to compare with.
    // Returns:  True if the positions are equal.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool Equals(ItemLocation other)
    {
        if (_exists == false || other._exists == false)
        {
            return _exists == other._exists;
        }

        return Storage == other.Storage &&
               MainPosition == other.MainPosition &&
               SubPosition == other.SubPosition &&
               AugPosition == other.AugPosition;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Equals
    //
    // Compares this position with an object.
    //
    // obj:      The object to compare with.
    // Returns:  True if obj is an ItemLocation equal to this one.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override bool Equals(object? obj)
    {
        if (obj is ItemLocation other)
        {
            return Equals(other);
        }

        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GetHashCode
    //
    // Returns a hash of this position.  All invalid positions hash to zero; a valid
    // position hashes all four parts.
    //
    // Returns:  The hash code.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override int GetHashCode()
    {
        if (_exists == false)
        {
            return 0;
        }

        return HashCode.Combine(Storage, MainPosition, SubPosition, AugPosition);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToString
    //
    // Returns this position as text for log messages: "None" when invalid, otherwise
    // the four parts separated by slashes.
    //
    // Returns:  The position as text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public override string ToString()
    {
        if (_exists == false)
        {
            return "None";
        }

        return Storage + "/" + MainPosition + "/" + SubPosition + "/" + AugPosition;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // operator ==
    //
    // Compares two positions using Equals.
    //
    // left:     The first position.
    // right:    The second position.
    // Returns:  True if the positions are equal.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static bool operator ==(ItemLocation left, ItemLocation right)
    {
        return left.Equals(right);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // operator !=
    //
    // Compares two positions using Equals.
    //
    // left:     The first position.
    // right:    The second position.
    // Returns:  True if the positions are not equal.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static bool operator !=(ItemLocation left, ItemLocation right)
    {
        return left.Equals(right) == false;
    }
}