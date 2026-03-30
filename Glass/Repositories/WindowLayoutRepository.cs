using Glass.Core;
using Glass.Data.Models;
using Glass.UI.ViewModels;
using Microsoft.Data.Sqlite;
using System.Windows;

namespace Glass.Data.Repositories;

// Handles persistence of window layouts and character placements.
public class WindowLayoutRepository
{
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

        DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: profileId={profileId} name='{layoutName}' slots={slots.Count} monitors={monitors.Count}.");

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
                DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: overwriting existing layoutId={layoutId}.");

                using SqliteCommand updateLayoutCmd = conn.CreateCommand();
                updateLayoutCmd.Transaction = tx;
                updateLayoutCmd.CommandText = "UPDATE WindowLayouts SET machine_id = @machineId WHERE id = @layoutId";
                updateLayoutCmd.Parameters.AddWithValue("@machineId", machineId.HasValue ? machineId.Value : DBNull.Value);
                updateLayoutCmd.Parameters.AddWithValue("@layoutId", layoutId);
                updateLayoutCmd.ExecuteNonQuery();
                DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: updated machine_id={machineId} for layoutId={layoutId}.");

                // Delete and reinsert LayoutMonitors rows for this layout.
                // This handles the case where the user reduces the monitor count —
                // rows for removed monitors are cleaned up naturally by the delete.
                using SqliteCommand deleteLayoutMonitors = conn.CreateCommand();
                deleteLayoutMonitors.Transaction = tx;
                deleteLayoutMonitors.CommandText = "DELETE FROM LayoutMonitors WHERE layout_id = @layoutId";
                deleteLayoutMonitors.Parameters.AddWithValue("@layoutId", layoutId);
                deleteLayoutMonitors.ExecuteNonQuery();
                DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: cleared LayoutMonitors for layoutId={layoutId}.");
            }
            else
            {
                DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: inserting new layout name='{layoutName}'.");

                using SqliteCommand insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"
                INSERT INTO WindowLayouts (name, machine_id)
                VALUES (@name, @machineId);
                SELECT last_insert_rowid();";
                insertCmd.Parameters.AddWithValue("@name", layoutName);
                insertCmd.Parameters.AddWithValue("@machineId", machineId.HasValue ? machineId.Value : DBNull.Value);
                layoutId = Convert.ToInt32(insertCmd.ExecuteScalar());
                DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: inserted layoutId={layoutId} machineId={machineId}.");
            }

            SqliteCommand profileCmd = conn.CreateCommand();
            profileCmd.Transaction = tx;
            profileCmd.CommandText = "UPDATE Profiles SET layout_id = @layoutId WHERE id = @profileId";
            profileCmd.Parameters.AddWithValue("@layoutId", layoutId);
            profileCmd.Parameters.AddWithValue("@profileId", profileId);
            profileCmd.ExecuteNonQuery();
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: updated profile layoutId={layoutId}.");

            // TODO: populate pnp_id and serial once MonitorRepository.SyncFromHardware is implemented.
            int layoutPosition = 0;
            foreach (LayoutMonitorViewModel layoutMonitor in monitors)
            {
                layoutPosition++;
                DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: upserting monitor '{layoutMonitor.Monitor.AdapterName}' {layoutMonitor.Monitor.Width}x{layoutMonitor.Monitor.Height}.");

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
                DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: monitorId={monitorId} layoutPosition={layoutPosition}.");

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
                DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: inserted LayoutMonitors layoutPosition={layoutPosition} slotWidth={layoutMonitor.SlotWidth}.");
            }

            tx.Commit();
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.Save: committed layoutId={layoutId}.");
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
    // GetLayoutById
    //
    // Returns the WindowLayout with the given ID, or null if not found.
    //
    // layoutId:  The layout to query.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public WindowLayout? GetLayoutById(int layoutId)
    {
        DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.GetLayoutById: layoutId={layoutId}.");

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, machine_id FROM WindowLayouts WHERE id = @layoutId";
        cmd.Parameters.AddWithValue("@layoutId", layoutId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.GetLayoutById: layoutId={layoutId} not found.");
            return null;
        }

        WindowLayout layout = new WindowLayout
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            MachineId = reader.IsDBNull(2) ? null : reader.GetInt32(2)
        };

        DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.GetLayoutById: found name='{layout.Name}' machineId={layout.MachineId}.");
        return layout;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetLayoutMonitors
    //
    // Returns all LayoutMonitorSettings with associated Monitor data for the given layout ID,
    // ordered by layout position.
    //
    // layoutId:  The layout to query.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<(LayoutMonitorSettings Settings, Glass.Data.Models.Monitor Monitor)> GetLayoutMonitors(int layoutId)
    {
        List<(LayoutMonitorSettings Settings, Glass.Data.Models.Monitor Monitor)> results
            = new List<(LayoutMonitorSettings Settings, Glass.Data.Models.Monitor Monitor)>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                lm.id, lm.layout_id, lm.monitor_id, lm.layout_position, lm.slot_width,
                m.id, m.machine_id, m.adapter_name, m.pnp_id, m.serial, m.width, m.height
            FROM LayoutMonitors lm
            JOIN Monitors m ON m.id = lm.monitor_id
            WHERE lm.layout_id = @layoutId
            ORDER BY lm.layout_position";
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

            Glass.Data.Models.Monitor monitor = new Glass.Data.Models.Monitor
            {
                Id = reader.GetInt32(5),
                MachineId = reader.GetInt32(6),
                AdapterName = reader.GetString(7),
                PnpId = reader.GetString(8),
                Serial = reader.GetString(9),
                Width = reader.GetInt32(10),
                Height = reader.GetInt32(11)
            };

            results.Add((settings, monitor));
        }

       return results;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSlotPlacements
    //
    // Returns slot placements for the given layout ID, computed from LayoutMonitors.
    // Slot positions are derived from each monitor's dimensions and slot width.
    // Primary monitors (layout_position = 1) are skipped — they host full-size windows.
    // Returns an empty list if no layout monitors are defined.
    //
    // layoutId:  The layout to compute slot placements for.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<SlotPlacement> GetSlotPlacements(int layoutId)
    {
        DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.GetSlotPlacements: layoutId={layoutId}.");

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT lm.layout_position, lm.slot_width, m.width, m.height
            FROM LayoutMonitors lm
            JOIN Monitors m ON m.id = lm.monitor_id
            WHERE lm.layout_id = @layoutId
            AND lm.layout_position > 1
            ORDER BY lm.layout_position";
        cmd.Parameters.AddWithValue("@layoutId", layoutId);

        List<SlotPlacement> placements = new List<SlotPlacement>();
        int slotNumber = 0;

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int layoutPosition = reader.GetInt32(0);
            int slotWidth = reader.GetInt32(1);
            int monitorWidth = reader.GetInt32(2);
            int monitorHeight = reader.GetInt32(3);
            int slotHeight = (int)(slotWidth / LayoutConstants.AspectRatio);

            int columns = monitorWidth / (slotWidth + LayoutConstants.HorizontalMargin);
            int rows = monitorHeight / (slotHeight + LayoutConstants.VerticalMargin);

            double horizontalMargin = (monitorWidth - (columns * slotWidth)) / (double)columns;
            double verticalMargin = (monitorHeight - (rows * slotHeight)) / (double)rows;

            DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.GetSlotPlacements: layoutPosition={layoutPosition} {monitorWidth}x{monitorHeight} slotWidth={slotWidth} columns={columns} rows={rows}.");

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    slotNumber++;
                    int x = (int)Math.Round(col * (slotWidth + horizontalMargin));
                    int y = (int)Math.Round(row * (slotHeight + verticalMargin));

                    placements.Add(new SlotPlacement
                    {
                        SlotNumber = slotNumber,
                        X = x,
                        Y = y,
                        Width = slotWidth,
                        Height = slotHeight
                    });

                    DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.GetSlotPlacements: slot={slotNumber} x={x} y={y} w={slotWidth} h={slotHeight}.");
                }
            }
        }

        DebugLog.Write(DebugLog.Log_Database, $"WindowLayoutRepository.GetSlotPlacements: {placements.Count} placements computed.");
        return placements;
    }
}
