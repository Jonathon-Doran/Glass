using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;
using Glass.UI.ViewModels;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// WindowLayoutRepository
//
// Loads and caches all window layouts, their monitor assignments, and slot placements
// on construction. All read methods operate against the in-memory cache.
// Write methods (Create, Rename, Delete) update both the database and the cache.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class WindowLayoutRepository
{
    private readonly List<WindowLayout> _layouts;
    private readonly Dictionary<int, List<LayoutMonitorSettings>> _monitorCache;
    private readonly Dictionary<int, List<SlotPlacement>> _placementCache;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WindowLayoutRepository
    //
    // Loads all window layouts from the database, including their monitor assignments
    // and slot placements. After construction, all read operations are cache-only.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public WindowLayoutRepository()
    {
        _layouts = new List<WindowLayout>();
        _monitorCache = new Dictionary<int, List<LayoutMonitorSettings>>();
        _placementCache = new Dictionary<int, List<SlotPlacement>>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        LoadLayouts(conn);

        foreach (WindowLayout layout in _layouts)
        {
            _monitorCache[layout.Id] = LoadMonitors(conn, layout.Id);
            _placementCache[layout.Id] = LoadPlacements(conn, layout.Id);
            layout.Monitors = _monitorCache[layout.Id];
            layout.Slots = _placementCache[layout.Id];
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadLayouts
    //
    // Loads all window layout rows from the database into _layouts.
    // Called once during construction.
    //
    // conn:  An open database connection
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadLayouts(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, machine_id FROM WindowLayouts ORDER BY name";

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            WindowLayout layout = new WindowLayout
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                MachineId = reader.IsDBNull(2) ? null : reader.GetInt32(2)
            };

            _layouts.Add(layout);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadMonitors
    //
    // Loads all LayoutMonitors rows for the given layout from the database.
    // Called once per layout during construction.
    //
    // conn:      An open database connection
    // layoutId:  The layout to load monitors for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private List<LayoutMonitorSettings> LoadMonitors(SqliteConnection conn, int layoutId)
    {
        List<LayoutMonitorSettings> monitors = new List<LayoutMonitorSettings>();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, layout_id, monitor_id, layout_position, slot_width
            FROM LayoutMonitors
            WHERE layout_id = @layoutId
            ORDER BY layout_position";
        cmd.Parameters.AddWithValue("@layoutId", layoutId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            LayoutMonitorSettings settings = new LayoutMonitorSettings
            {
                Id = reader.GetInt32(0),
                LayoutId = reader.GetInt32(1),
                MonitorId = reader.GetInt32(2),
                LayoutPosition = reader.GetInt32(3),
                SlotWidth = reader.GetInt32(4)
            };

            monitors.Add(settings);
        }

        return monitors;
    }


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadPlacements
    //
    // Loads all SlotPlacements rows for the given layout from the database.
    // Called once per layout during construction.
    //
    // conn:      An open database connection
    // layoutId:  The layout to load slot placements for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private List<SlotPlacement> LoadPlacements(SqliteConnection conn, int layoutId)
    {
        List<SlotPlacement> placements = new List<SlotPlacement>();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, layout_id, monitor_id, slot_number, x, y, width, height
            FROM SlotPlacements
            WHERE layout_id = @layoutId
            ORDER BY slot_number";
        cmd.Parameters.AddWithValue("@layoutId", layoutId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            SlotPlacement placement = new SlotPlacement
            {
                Id = reader.GetInt32(0),
                LayoutId = reader.GetInt32(1),
                MonitorId = reader.GetInt32(2),
                SlotNumber = reader.GetInt32(3),
                X = reader.GetInt32(4),
                Y = reader.GetInt32(5),
                Width = reader.GetInt32(6),
                Height = reader.GetInt32(7)
            };

            placements.Add(placement);
        }

         return placements;
    }

    // Returns the next available layout name for a profile, e.g. "Layout3".
    public string GetNextLayoutName(int profileId)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM WindowLayouts WHERE profile_id = @id";
        cmd.Parameters.AddWithValue("@id", profileId);
        int count = Convert.ToInt32(cmd.ExecuteScalar());

        return $"Layout{count + 1}";
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save
    //
    // Saves a window layout and its LayoutMonitors. If a layout with the same name
    // already exists it is overwritten. Updates the profile's layout_id to point
    // to this layout. Returns the layout ID.
    //
    // profileRepo:  The profile repository providing profileId and machineId.
    // layoutName:   The name of the layout to create or overwrite.
    // slots:        The slot assignments for this profile.
    // monitors:     The monitor configurations used in this layout.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int Save(ProfileRepository profileRepo, string layoutName, List<SlotAssignment> slots, List<LayoutMonitorViewModel> monitors)
    {
        int profileId = profileRepo.GetId();
        int? machineId = profileRepo.GetMachineId();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();
        try
        {
            SqliteCommand checkCmd = conn.CreateCommand();
            checkCmd.Transaction = tx;
            checkCmd.CommandText = "SELECT id FROM WindowLayouts WHERE name = @name";
            checkCmd.Parameters.AddWithValue("@name", layoutName);
            object? existingId = checkCmd.ExecuteScalar();

            int layoutId;
            if (existingId != null)
            {
                layoutId = Convert.ToInt32(existingId);
 
                using SqliteCommand updateLayoutCmd = conn.CreateCommand();
                updateLayoutCmd.Transaction = tx;
                updateLayoutCmd.CommandText = "UPDATE WindowLayouts SET machine_id = @machineId WHERE id = @layoutId";
                updateLayoutCmd.Parameters.AddWithValue("@machineId", machineId.HasValue ? machineId.Value : DBNull.Value);
                updateLayoutCmd.Parameters.AddWithValue("@layoutId", layoutId);
                updateLayoutCmd.ExecuteNonQuery();
  
                // Delete and reinsert LayoutMonitors rows for this layout.
                // This handles the case where the user reduces the monitor count —
                // rows for removed monitors are cleaned up naturally by the delete.
                using SqliteCommand deleteLayoutMonitors = conn.CreateCommand();
                deleteLayoutMonitors.Transaction = tx;
                deleteLayoutMonitors.CommandText = "DELETE FROM LayoutMonitors WHERE layout_id = @layoutId";
                deleteLayoutMonitors.Parameters.AddWithValue("@layoutId", layoutId);
                deleteLayoutMonitors.ExecuteNonQuery();
             }
            else
            {
                using SqliteCommand insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"
                INSERT INTO WindowLayouts (name, machine_id)
                VALUES (@name, @machineId);
                SELECT last_insert_rowid();";
                insertCmd.Parameters.AddWithValue("@name", layoutName);
                insertCmd.Parameters.AddWithValue("@machineId", machineId.HasValue ? machineId.Value : DBNull.Value);
                layoutId = Convert.ToInt32(insertCmd.ExecuteScalar());
            }

            SqliteCommand profileCmd = conn.CreateCommand();
            profileCmd.Transaction = tx;
            profileCmd.CommandText = "UPDATE Profiles SET layout_id = @layoutId WHERE id = @profileId";
            profileCmd.Parameters.AddWithValue("@layoutId", layoutId);
            profileCmd.Parameters.AddWithValue("@profileId", profileId);
            profileCmd.ExecuteNonQuery();
 
            // TODO: populate pnp_id and serial once MonitorRepository.SyncFromHardware is implemented.
            int layoutPosition = 0;
            foreach (LayoutMonitorViewModel layoutMonitor in monitors)
            {
                layoutPosition++;
 
                using SqliteCommand monitorCmd = conn.CreateCommand();
                monitorCmd.Transaction = tx;
                monitorCmd.CommandText = @"
                    INSERT INTO Monitors (machine_id, adapter_name, pnp_id, serial, width, height)
                    VALUES (1, @adapterName, '', '', @width, @height)
                    ON CONFLICT(machine_id, adapter_name) DO UPDATE SET
                        width  = excluded.width,
                        height = excluded.height;
                    SELECT id FROM Monitors WHERE machine_id = 1 AND adapter_name = @adapterName";
                monitorCmd.Parameters.AddWithValue("@adapterName", layoutMonitor.Monitor.AdapterName);
                monitorCmd.Parameters.AddWithValue("@width", layoutMonitor.Monitor.Width);
                monitorCmd.Parameters.AddWithValue("@height", layoutMonitor.Monitor.Height);
                int monitorId = Convert.ToInt32(monitorCmd.ExecuteScalar());

                using SqliteCommand insertLayoutMonitor = conn.CreateCommand();
                insertLayoutMonitor.Transaction = tx;
                insertLayoutMonitor.CommandText = @"
                    INSERT INTO LayoutMonitors (layout_id, monitor_id, layout_position, slot_width)
                    VALUES (@layoutId, @monitorId, @layoutPosition, @slotWidth)";
                insertLayoutMonitor.Parameters.AddWithValue("@layoutId", layoutId);
                insertLayoutMonitor.Parameters.AddWithValue("@monitorId", monitorId);
                insertLayoutMonitor.Parameters.AddWithValue("@layoutPosition", layoutPosition);
                insertLayoutMonitor.Parameters.AddWithValue("@slotWidth", layoutMonitor.SlotWidth);
                insertLayoutMonitor.ExecuteNonQuery();
            }

            tx.Commit();
            return layoutId;
        }
        catch (Exception ex)
        {
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: exception: {ex.Message}, rolling back.");
            tx.Rollback();
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllLayouts
    //
    // Returns a read-only view of all cached window layouts.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<WindowLayout> GetAllLayouts()
    {
        return _layouts.AsReadOnly();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetLayoutById
    //
    // Returns the cached window layout with the given ID, or null if not found.
    //
    // layoutId:  The ID of the layout to retrieve
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public WindowLayout? GetLayoutById(int layoutId)
    {
        WindowLayout? layout = _layouts.FirstOrDefault(l => l.Id == layoutId);

        if (layout == null)
        {
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.GetLayoutById: layoutId={layoutId} not found.");
        }

        return layout;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSlotPlacements
    //
    // Returns the cached slot placements for the given layout ID.
    // Returns an empty list if the layout has no placements or is not found.
    //
    // layoutId:  The ID of the layout to retrieve slot placements for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<SlotPlacement> GetSlotPlacements(int layoutId)
    {
        if (!_placementCache.TryGetValue(layoutId, out List<SlotPlacement>? placements))
        {
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.GetSlotPlacements: layoutId={layoutId} not found in cache.");
            return new List<SlotPlacement>();
        }

        return placements.AsReadOnly();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveSlotPlacements
    //
    // Replaces all SlotPlacements rows for the given layout with the provided list.
    // Deletes existing rows first, then inserts the new ones.
    // Updates the in-memory cache to match.
    //
    // layoutId:   The layout to save placements for
    // placements: The slot placements to persist
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SaveSlotPlacements(int layoutId, List<SlotPlacement> placements)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();

        try
        {
            using SqliteCommand deleteCmd = conn.CreateCommand();
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM SlotPlacements WHERE layout_id = @layoutId";
            deleteCmd.Parameters.AddWithValue("@layoutId", layoutId);
            deleteCmd.ExecuteNonQuery();

            int slotNumber = 1;
            foreach (SlotPlacement placement in placements)
            {
                using SqliteCommand insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"
                    INSERT INTO SlotPlacements (layout_id, monitor_id, slot_number, x, y, width, height)
                    VALUES (@layoutId, @monitorId, @slotNumber, @x, @y, @width, @height)";
                insertCmd.Parameters.AddWithValue("@layoutId", layoutId);
                insertCmd.Parameters.AddWithValue("@monitorId", placement.MonitorId);
                insertCmd.Parameters.AddWithValue("@slotNumber", slotNumber);
                insertCmd.Parameters.AddWithValue("@x", placement.X);
                insertCmd.Parameters.AddWithValue("@y", placement.Y);
                insertCmd.Parameters.AddWithValue("@width", placement.Width);
                insertCmd.Parameters.AddWithValue("@height", placement.Height);
                insertCmd.ExecuteNonQuery();

                slotNumber++;
            }

            tx.Commit();

            // Update cache.
            _placementCache[layoutId] = placements;

            WindowLayout? layout = _layouts.FirstOrDefault(l => l.Id == layoutId);
            if (layout != null)
            {
                layout.Slots = placements;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.SaveSlotPlacements: exception: {ex.Message}, rolling back.");
            tx.Rollback();
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Create
    //
    // Inserts a new window layout into the database and adds it to the cache.
    // Returns the new layout ID.
    //
    // name:       The name for the new layout
    // machineId:  The machine this layout is intended for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int Create(string name, int machineId)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO WindowLayouts (name, machine_id)
            VALUES (@name, @machineId)";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@machineId", machineId);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT last_insert_rowid()";
        cmd.Parameters.Clear();
        int newId = Convert.ToInt32(cmd.ExecuteScalar());

        WindowLayout layout = new WindowLayout
        {
            Id = newId,
            Name = name,
            MachineId = machineId
        };

        _layouts.Add(layout);
        _monitorCache[newId] = new List<LayoutMonitorSettings>();
        _placementCache[newId] = new List<SlotPlacement>();

        return newId;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Rename
    //
    // Renames an existing window layout in the database and updates the cache.
    //
    // layoutId:  The ID of the layout to rename
    // newName:   The new name for the layout
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Rename(int layoutId, string newName)
    {
        WindowLayout? layout = _layouts.FirstOrDefault(l => l.Id == layoutId);

        if (layout == null)
        {
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Rename: layoutId={layoutId} not found in cache, aborting.");
            return;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE WindowLayouts SET name = @name WHERE id = @id";
        cmd.Parameters.AddWithValue("@name", newName);
        cmd.Parameters.AddWithValue("@id", layoutId);
        cmd.ExecuteNonQuery();

        string oldName = layout.Name;
        layout.Name = newName;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Delete
    //
    // Deletes a window layout from the database and removes it from the cache.
    // LayoutMonitors and SlotPlacements are removed automatically via ON DELETE CASCADE.
    //
    // layoutId:  The ID of the layout to delete
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Delete(int layoutId)
    {
        WindowLayout? layout = _layouts.FirstOrDefault(l => l.Id == layoutId);

        if (layout == null)
        {
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Delete: layoutId={layoutId} not found in cache, aborting.");
            return;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM WindowLayouts WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", layoutId);
        cmd.ExecuteNonQuery();

        _layouts.Remove(layout);
        _monitorCache.Remove(layoutId);
        _placementCache.Remove(layoutId);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetProfilesUsingLayout
    //
    // Returns all profiles that reference the given layout ID.
    // This is a live database query — not cached — as it is called infrequently
    // and only when the user is about to delete a layout.
    //
    // layoutId:  The ID of the layout to check
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Profile> GetProfilesUsingLayout(int layoutId)
    {
        List<Profile> profiles = new List<Profile>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, name
            FROM Profiles
            WHERE layout_id = @layoutId
            ORDER BY name";
        cmd.Parameters.AddWithValue("@layoutId", layoutId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Profile profile = new Profile
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            };

            profiles.Add(profile);
        }

        return profiles;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetLayoutMonitors
    //
    // Returns the cached monitor assignments for the given layout ID.
    // Returns an empty list if the layout has no monitors or is not found.
    //
    // layoutId:  The ID of the layout to retrieve monitors for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public IReadOnlyList<LayoutMonitorSettings> GetLayoutMonitors(int layoutId)
    {
        if (!_monitorCache.TryGetValue(layoutId, out List<LayoutMonitorSettings>? monitors))
        {
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.GetLayoutMonitors: layoutId={layoutId} not found in cache.");
            return new List<LayoutMonitorSettings>();
        }

        return monitors.AsReadOnly();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveLayoutMonitors
    //
    // Replaces all LayoutMonitors rows for the given layout with the provided list.
    // Deletes existing rows first, then inserts the new ones.
    // Updates the in-memory cache to match.
    //
    // layoutId:  The layout to save monitors for
    // monitors:  The monitor settings to persist
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SaveLayoutMonitors(int layoutId, List<LayoutMonitorSettings> monitors)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();

        try
        {
            using SqliteCommand deleteCmd = conn.CreateCommand();
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM LayoutMonitors WHERE layout_id = @layoutId";
            deleteCmd.Parameters.AddWithValue("@layoutId", layoutId);
            deleteCmd.ExecuteNonQuery();

            foreach (LayoutMonitorSettings settings in monitors)
            {
                using SqliteCommand insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"
                    INSERT INTO LayoutMonitors (layout_id, monitor_id, layout_position, slot_width)
                    VALUES (@layoutId, @monitorId, @layoutPosition, @slotWidth)";
                insertCmd.Parameters.AddWithValue("@layoutId", layoutId);
                insertCmd.Parameters.AddWithValue("@monitorId", settings.MonitorId);
                insertCmd.Parameters.AddWithValue("@layoutPosition", settings.LayoutPosition);
                insertCmd.Parameters.AddWithValue("@slotWidth", settings.SlotWidth);
                insertCmd.ExecuteNonQuery();
             }

            tx.Commit();

            // Update cache.
            _monitorCache[layoutId] = monitors;

            WindowLayout? layout = _layouts.FirstOrDefault(l => l.Id == layoutId);
            if (layout != null)
            {
                layout.Monitors = monitors;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.SaveLayoutMonitors: exception: {ex.Message}, rolling back.");
            tx.Rollback();
            throw;
        }
    }
}
