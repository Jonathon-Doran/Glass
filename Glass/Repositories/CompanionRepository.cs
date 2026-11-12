using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.World;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// CompanionRepository
//
// In-memory repository of the companions seen this run, held per zone in a dictionary keyed by spawn id.
// Nothing is read from or written to the database.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class CompanionRepository
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;

    private static CompanionRepository? _instance = null;

    private readonly Dictionary<ZoneId, Dictionary<SpawnId, Companion>> _companionsByZone;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access with an empty table.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static CompanionRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new CompanionRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CompanionRepository
    //
    // Private constructor.  Initializes an empty table.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private CompanionRepository()
    {
        _companionsByZone = new Dictionary<ZoneId, Dictionary<SpawnId, Companion>>();
        DebugLog.Write(LogChannel.Database, "CompanionRepository: singleton instance created with an empty table.",
            defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddCompanion
    //
    // Stores a companion under its zone and spawn id.  The table for the companion's zone is created on
    // first use.  A companion whose zone id or spawn id is None is rejected, and a zone that already holds
    // a companion under the spawn id keeps the existing one; in either case nothing is stored.
    //
    // companion:  The companion to store.  ZoneId, SpawnId and any known field values are set by the caller.
    //
    // Returns true if the companion was stored, false if it was rejected.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool AddCompanion(Companion companion)
    {
        if ((companion.ZoneId.Exists == false) || (companion.SpawnId.Exists == false))
        {
            DebugLog.Write(LogChannel.Database, "CompanionRepository.AddCompanion: '" + companion.Name + "' zoneId="
                + companion.ZoneId + " spawnId=" + companion.SpawnId + " is not a valid placement, not stored.",
                LogLevel.Error);
            return false;
        }

        if (_companionsByZone.TryGetValue(companion.ZoneId, out Dictionary<SpawnId, Companion>? zoneCompanions) == false)
        {
            zoneCompanions = new Dictionary<SpawnId, Companion>();
            _companionsByZone[companion.ZoneId] = zoneCompanions;
            DebugLog.Write(LogChannel.Database, "CompanionRepository.AddCompanion: created table for zoneId="
                + companion.ZoneId + ".", defaultLoglevel);
        }

        if (zoneCompanions.TryGetValue(companion.SpawnId, out Companion? existing))
        {
            DebugLog.Write(LogChannel.Database, "CompanionRepository.AddCompanion: zoneId=" + companion.ZoneId
                + " spawnId=" + companion.SpawnId + " already holds '" + existing.Name + "', '" + companion.Name
                + "' not stored.", LogLevel.Warn);
            return false;
        }

        zoneCompanions[companion.SpawnId] = companion;

        DebugLog.Write(LogChannel.Database, "CompanionRepository.AddCompanion: stored '" + companion.Name
            + "' at zoneId=" + companion.ZoneId + " spawnId=" + companion.SpawnId + ", owner spawnId="
            + companion.OwnerSpawnId + ".", defaultLoglevel);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGetCompanion
    //
    // Looks up the companion held under the given zone and spawn id.
    //
    // zoneId:     The zone the spawn id is valid in.
    // spawnId:    The server assigned spawn id within that zone.
    // companion:  Receives the companion if found, null otherwise.
    //
    // Returns true if a companion is held there, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGetCompanion(ZoneId zoneId, SpawnId spawnId, [NotNullWhen(true)] out Companion? companion)
    {
        companion = null;

        if ((_companionsByZone.TryGetValue(zoneId, out Dictionary<SpawnId, Companion>? zoneCompanions))
            && (zoneCompanions.TryGetValue(spawnId, out companion)))
        {
            return true;
        }

        DebugLog.Write(LogChannel.Database, "CompanionRepository.TryGetCompanion: no companion at zoneId=" + zoneId
            + " spawnId=" + spawnId + ".", defaultLoglevel);
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetZoneCompanions
    //
    // Returns the companions held for one zone, in no particular order.
    //
    // zoneId:  The zone whose companions are returned.
    //
    // Returns a new list of the zone's companions; empty when the zone has no companion table.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Companion> GetZoneCompanions(ZoneId zoneId)
    {
        List<Companion> companions = new List<Companion>();

        if (_companionsByZone.TryGetValue(zoneId, out Dictionary<SpawnId, Companion>? zoneCompanions))
        {
            companions.AddRange(zoneCompanions.Values);
        }

        DebugLog.Write(LogChannel.Database, "CompanionRepository.GetZoneCompanions: zoneId=" + zoneId
            + " returning " + companions.Count + " companions.", defaultLoglevel);
        return companions;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LinkOwner
    //
    // Sets the given character as the owner of every companion in the zone whose owner spawn id equals the
    // character's spawn id.  A zone with no companion table links nothing.
    //
    // zoneId:  The zone the character has been placed in.
    // owner:   The placed character.  Its SpawnId is the spawn id it holds in that zone.
    //
    // Returns the number of companions linked to the character.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public uint LinkOwner(ZoneId zoneId, Character owner)
    {
        if (_companionsByZone.TryGetValue(zoneId, out Dictionary<SpawnId, Companion>? zoneCompanions) == false)
        {
            DebugLog.Write(LogChannel.Database, "CompanionRepository.LinkOwner: zoneId=" + zoneId
                + " has no companions, nothing linked to '" + owner.Name + "'.", defaultLoglevel);
            return 0;
        }

        uint linked = 0;
        foreach (Companion companion in zoneCompanions.Values)
        {
            if (companion.OwnerSpawnId != owner.SpawnId)
            {
                continue;
            }

            companion.Owner = owner;
            companion.OwnerId = owner.CharacterId;
            linked++;

            DebugLog.Write(LogChannel.Database, "CompanionRepository.LinkOwner: '" + companion.Name + "' spawnId=0x"
                + companion.SpawnId.Value.ToString("X4") + " linked to owner '" + owner.Name + "'.", defaultLoglevel);
        }

        DebugLog.Write(LogChannel.Database, "CompanionRepository.LinkOwner: zoneId=" + zoneId + " linked "
            + linked + " companions to '" + owner.Name + "'.", defaultLoglevel);
        return linked;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearZoneCompanions
    //
    // Removes the given zone's companion table, discarding every companion held under it.  A zone id that
    // is None or has no table does nothing.
    //
    // zoneId:  The zone whose companion table is removed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void ClearZoneCompanions(ZoneId zoneId)
    {
        if (zoneId.Exists == false)
        {
            DebugLog.Write(LogChannel.Database, "CompanionRepository.ClearZoneCompanions: zoneId is None.",
                LogLevel.Warn);
            return;
        }

        if (_companionsByZone.TryGetValue(zoneId, out Dictionary<SpawnId, Companion>? zoneCompanions) == false)
        {
            DebugLog.Write(LogChannel.Database, "CompanionRepository.ClearZoneCompanions: zoneId=" + zoneId
                + " has no table.", defaultLoglevel);
            return;
        }

        uint discarded = (uint)zoneCompanions.Count;
        _companionsByZone.Remove(zoneId);

        DebugLog.Write(LogChannel.Database, "CompanionRepository.ClearZoneCompanions: zoneId=" + zoneId
            + " discarded " + discarded + " companions, table removed.", defaultLoglevel);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Removes every zone's companion table, discarding every companion held.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        uint zones = (uint)_companionsByZone.Count;
        _companionsByZone.Clear();

        DebugLog.Write(LogChannel.Database, "CompanionRepository.Clear: removed " + zones + " zone tables.",
            defaultLoglevel);
    }
}