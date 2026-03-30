using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

using Monitor = Glass.Data.Models.Monitor;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// MonitorRepository
//
// Handles persistence of Monitor records.
// Monitors represent physical displays attached to a machine.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class MonitorRepository
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetFirstMonitor
    //
    // Returns the width and height of the first monitor ordered by id,
    // or null if none exist.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public (int Width, int Height)? GetFirstMonitor()
    {
        DebugLog.Write(DebugLog.Log_Database, "MonitorRepository.GetFirstMonitor: loading.");

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT width, height
            FROM Monitors
            ORDER BY id
            LIMIT 1";

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            DebugLog.Write(DebugLog.Log_Database, "MonitorRepository.GetFirstMonitor: no monitors found.");
            return null;
        }

        int width = reader.GetInt32(0);
        int height = reader.GetInt32(1);
        DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.GetFirstMonitor: found {width}x{height}.");
        return (width, height);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetForMachine
    //
    // Returns all monitors for the given machine ID, ordered by adapter name.
    //
    // machineId:  The machine to query.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Monitor> GetForMachine(int machineId)
    {
        DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.GetForMachine: machineId={machineId}.");

        List<Monitor> monitors = new List<Monitor>();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, machine_id, adapter_name, pnp_id, serial, width, height
            FROM Monitors
            WHERE machine_id = @machineId
            ORDER BY adapter_name";
        cmd.Parameters.AddWithValue("@machineId", machineId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            Monitor monitor = new Monitor
            {
                Id = reader.GetInt32(0),
                MachineId = reader.GetInt32(1),
                AdapterName = reader.GetString(2),
                PnpId = reader.GetString(3),
                Serial = reader.GetString(4),
                Width = reader.GetInt32(5),
                Height = reader.GetInt32(6)
            };
            monitors.Add(monitor);
            DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.GetForMachine: id={monitor.Id} adapter='{monitor.AdapterName}' pnpId='{monitor.PnpId}' serial='{monitor.Serial}' {monitor.Width}x{monitor.Height}.");
        }

        DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.GetForMachine: {monitors.Count} monitors found.");
        return monitors;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SyncFromHardware
    //
    // Enumerates physical monitors via MonitorInfoHelper and upserts them into
    // the database for the given machine. Returns a list of change descriptions
    // if any monitors have changed since the last sync, or an empty list if
    // everything matches.
    //
    // machineId:  The machine to sync monitors for.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<string> SyncFromHardware(int machineId)
    {
        DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.SyncFromHardware: machineId={machineId}.");

        List<string> changes = new List<string>();

        // Enumerate logical monitors (resolution, DPI).
        Dictionary<string, (int Width, int Height, float DpiScale)> logical
            = new Dictionary<string, (int Width, int Height, float DpiScale)>();

        MonitorInfoHelper.EnumerateMonitors((hMonitor, dpiScale, deviceName, width, height) =>
        {
            logical[deviceName] = (width, height, dpiScale);
            DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.SyncFromHardware: logical adapter='{deviceName}' {width}x{height} dpi={dpiScale:F2}.");
        });

        // Enumerate display devices (PnP ID per adapter).
        Dictionary<string, List<DisplayDeviceInfo>> devices = MonitorInfoHelper.EnumerateDisplayDevices();

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();
        try
        {
            foreach (KeyValuePair<string, (int Width, int Height, float DpiScale)> pair in logical)
            {
                string adapterName = pair.Key;
                int width = pair.Value.Width;
                int height = pair.Value.Height;

                // Extract PnP ID from EnumDisplayDevices results.
                string pnpId = string.Empty;
                // TODO: populate serial from EDID registry once MonitorInfoHelper.ReadEdidSerial is implemented.
                string serial = string.Empty;

                if (devices.TryGetValue(adapterName, out List<DisplayDeviceInfo>? monitorDevices) &&
                    (monitorDevices.Count > 0))
                {
                    // PnP ID is the segment between MONITOR\ and the GUID in the DeviceID string.
                    // e.g. MONITOR\SAM1015\{4d36e96e...}\0002 → SAM1015
                    string deviceId = monitorDevices[0].MonitorID;
                    string[] parts = deviceId.Split('\\');
                    if (parts.Length >= 2)
                    {
                        pnpId = parts[1];
                    }
                }

                DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.SyncFromHardware: upserting adapter='{adapterName}' pnpId='{pnpId}' {width}x{height}.");

                // Check for existing record.
                using SqliteCommand checkCmd = conn.CreateCommand();
                checkCmd.Transaction = tx;
                checkCmd.CommandText = @"
                    SELECT id, pnp_id, serial, width, height
                    FROM Monitors
                    WHERE machine_id = @machineId AND adapter_name = @adapterName";
                checkCmd.Parameters.AddWithValue("@machineId", machineId);
                checkCmd.Parameters.AddWithValue("@adapterName", adapterName);

                using SqliteDataReader reader = checkCmd.ExecuteReader();
                if (reader.Read())
                {
                    string existingPnpId = reader.GetString(1);
                    string existingSerial = reader.GetString(2);
                    int existingWidth = reader.GetInt32(3);
                    int existingHeight = reader.GetInt32(4);

                    bool pnpChanged = existingPnpId != pnpId;
                    bool resolutionChanged = (existingWidth != width) || (existingHeight != height);

                    if (pnpChanged)
                    {
                        string change = $"{adapterName}: hardware changed from {existingPnpId} to {pnpId}.";
                        changes.Add(change);
                        DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.SyncFromHardware: {change}");
                    }

                    if (resolutionChanged)
                    {
                        string change = $"{adapterName}: resolution changed from {existingWidth}x{existingHeight} to {width}x{height}.";
                        changes.Add(change);
                        DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.SyncFromHardware: {change}");
                    }

                    reader.Close();

                    using SqliteCommand updateCmd = conn.CreateCommand();
                    updateCmd.Transaction = tx;
                    updateCmd.CommandText = @"
                        UPDATE Monitors
                        SET pnp_id = @pnpId, serial = @serial, width = @width, height = @height
                        WHERE machine_id = @machineId AND adapter_name = @adapterName";
                    updateCmd.Parameters.AddWithValue("@pnpId", pnpId);
                    updateCmd.Parameters.AddWithValue("@serial", serial);
                    updateCmd.Parameters.AddWithValue("@width", width);
                    updateCmd.Parameters.AddWithValue("@height", height);
                    updateCmd.Parameters.AddWithValue("@machineId", machineId);
                    updateCmd.Parameters.AddWithValue("@adapterName", adapterName);
                    updateCmd.ExecuteNonQuery();
                    DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.SyncFromHardware: updated adapter='{adapterName}'.");
                }
                else
                {
                    reader.Close();

                    using SqliteCommand insertCmd = conn.CreateCommand();
                    insertCmd.Transaction = tx;
                    insertCmd.CommandText = @"
                        INSERT INTO Monitors (machine_id, adapter_name, pnp_id, serial, width, height)
                        VALUES (@machineId, @adapterName, @pnpId, @serial, @width, @height)";
                    insertCmd.Parameters.AddWithValue("@machineId", machineId);
                    insertCmd.Parameters.AddWithValue("@adapterName", adapterName);
                    insertCmd.Parameters.AddWithValue("@pnpId", pnpId);
                    insertCmd.Parameters.AddWithValue("@serial", serial);
                    insertCmd.Parameters.AddWithValue("@width", width);
                    insertCmd.Parameters.AddWithValue("@height", height);
                    insertCmd.ExecuteNonQuery();
                    DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.SyncFromHardware: inserted new monitor adapter='{adapterName}'.");
                }
            }

            tx.Commit();
            DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.SyncFromHardware: committed. {changes.Count} change(s) detected.");
        }
        catch (Exception ex)
        {
            DebugLog.Write(DebugLog.Log_Database, $"MonitorRepository.SyncFromHardware: exception: {ex.Message}, rolling back.");
            tx.Rollback();
            throw;
        }

        return changes;
    }
}