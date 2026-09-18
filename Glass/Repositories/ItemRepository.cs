using Glass.Core.Logging;
using Glass.Data.Models;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ItemRepository
//
// In-memory repository of ItemRecord definitions, keyed by item id.  One record exists per item id and
// is shared by every instance of that item on every character.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class ItemRepository
{
    private static ItemRepository? _instance = null;

    private readonly Dictionary<ItemId, ItemRecord> _recordsById;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access with an empty cache.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static ItemRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new ItemRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ItemRepository
    //
    // Private constructor.  Initializes an empty record cache.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private ItemRepository()
    {
        _recordsById = new Dictionary<ItemId, ItemRecord>();
        DebugLog.Write(LogChannel.Inventory, "ItemRepository: singleton instance created with empty cache.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Add
    //
    // Stores an item definition keyed by its Id.  A record whose Id is None is rejected.  When a record
    // with the same Id is already stored, the stored record is kept and the given record is discarded.
    //
    // record:  The definition to store.  Its Id must exist.
    //
    // Returns true if the record was stored, false if it was rejected or a record with its Id was
    // already stored.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool Add(ItemRecord record)
    {
        if (record.Id.Exists == false)
        {
            DebugLog.Write(LogChannel.Inventory, "ItemRepository.Add: record '" + record.Name +
                "' has no Id; not stored.", LogLevel.Warn);
            return false;
        }

        if (_recordsById.ContainsKey(record.Id))
        {
            DebugLog.Write(LogChannel.Inventory, "ItemRepository.Add: '" + record.Name + "' (" + record.Id +
                ") already stored; existing record kept.", LogLevel.Trace);
            return false;
        }

        _recordsById[record.Id] = record;
        DebugLog.Write(LogChannel.Inventory, "ItemRepository.Add: stored '" + record.Name + "' (" + record.Id +
            "), " + _recordsById.Count + " records held.", LogLevel.Trace);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGet
    //
    // Looks up the item definition stored under the given Id.
    //
    // itemId:  Id of the definition to look up.
    // record:  Receives the definition if found, null otherwise.
    //
    // Returns true if the definition was found, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGet(ItemId itemId, [NotNullWhen(true)] out ItemRecord? record)
    {
        if (itemId.Exists == false)
        {
            record = null;
            DebugLog.Write(LogChannel.Inventory, "ItemRepository.TryGet: itemId is None.", LogLevel.Warn);
            return false;
        }

        if (_recordsById.TryGetValue(itemId, out record) == false)
        {
            DebugLog.Write(LogChannel.Inventory, "ItemRepository.TryGet: " + itemId + " not stored.", LogLevel.Trace);
            return false;
        }

        DebugLog.Write(LogChannel.Inventory, "ItemRepository.TryGet: found '" + record.Name + "' (" + itemId + ").",
            LogLevel.Trace);
        return true;
    }
}