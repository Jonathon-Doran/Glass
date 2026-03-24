using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// MachineRepository
//
// Provides access to machines and their device inventories.
// Machines are identified by hostname. GetOrCreate ensures the current
// machine always has a database entry.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class MachineRepository
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetOrCreate
    //
    // Returns the machine with the given hostname, creating it if it does not exist.
    // Returns the machine ID.
    //
    // hostname:  The machine hostname e.g. Environment.MachineName
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Machine GetOrCreate(string hostname)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT id, name FROM Machines WHERE name = @name";
        selectCmd.Parameters.AddWithValue("@name", hostname);

        using var reader = selectCmd.ExecuteReader();
        if (reader.Read())
        {
            var machine = new Machine
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            };
            reader.Close();
            machine.Devices = GetDevices(conn, machine.Id);
            DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.GetOrCreate: found id={machine.Id}.");
            return machine;
        }

        reader.Close();

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO Machines (name) VALUES (@name); SELECT last_insert_rowid();";
        insertCmd.Parameters.AddWithValue("@name", hostname);
        int id = Convert.ToInt32(insertCmd.ExecuteScalar());

        DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.GetOrCreate: created id={id}.");

        return new Machine { Id = id, Name = hostname };
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAll
    //
    // Returns all machines.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Machine> GetAll()
    {
        DebugLog.Write(DebugLog.Log_Database, "MachineRepository.GetAll: loading.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM Machines ORDER BY name";

        var machines = new List<Machine>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var machine = new Machine
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            };
            machines.Add(machine);
        }

        reader.Close();

        foreach (var machine in machines)
        {
            machine.Devices = GetDevices(conn, machine.Id);
        }

        DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.GetAll: found {machines.Count} machines.");
        return machines;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetById
    //
    // Returns the machine with the given ID, or null if not found.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Machine? GetById(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.GetById: id={id}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM Machines WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.GetById: id={id} not found.");
            return null;
        }

        var machine = new Machine
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1)
        };
        reader.Close();
        machine.Devices = GetDevices(conn, machine.Id);

        DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.GetById: found name='{machine.Name}' devices={machine.Devices.Count}.");
        return machine;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save
    //
    // Inserts or updates a machine. If Id is 0, inserts and updates Id.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Save(Machine machine)
    {
        DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.Save: name='{machine.Name}'.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        if (machine.Id == 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Machines (name) VALUES (@name); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@name", machine.Name);
            machine.Id = Convert.ToInt32(cmd.ExecuteScalar());
            DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.Save: inserted id={machine.Id}.");
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Machines SET name = @name WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", machine.Name);
            cmd.Parameters.AddWithValue("@id", machine.Id);
            cmd.ExecuteNonQuery();
            DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.Save: updated id={machine.Id}.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveDevices
    //
    // Replaces all device entries for the given machine.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SaveDevices(int machineId, List<MachineDevice> devices)
    {
        DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.SaveDevices: machineId={machineId} count={devices.Count}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var tx = conn.BeginTransaction();
        try
        {
            using var deleteCmd = conn.CreateCommand();
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM MachineDevices WHERE machine_id = @machineId";
            deleteCmd.Parameters.AddWithValue("@machineId", machineId);
            deleteCmd.ExecuteNonQuery();

            foreach (var device in devices)
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = @"
                    INSERT INTO MachineDevices (machine_id, keyboard_type, instance_count)
                    VALUES (@machineId, @keyboardType, @instanceCount)";
                insertCmd.Parameters.AddWithValue("@machineId", machineId);
                insertCmd.Parameters.AddWithValue("@keyboardType", device.KeyboardType.ToDeviceString());
                insertCmd.Parameters.AddWithValue("@instanceCount", device.InstanceCount);
                insertCmd.ExecuteNonQuery();
            }

            tx.Commit();
            DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.SaveDevices: committed.");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.SaveDevices: exception: {ex.Message}, rolling back.");
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Delete
    //
    // Deletes a machine and its device inventory.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Delete(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.Delete: id={id}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var tx = conn.BeginTransaction();
        try
        {
            using var deleteDevices = conn.CreateCommand();
            deleteDevices.Transaction = tx;
            deleteDevices.CommandText = "DELETE FROM MachineDevices WHERE machine_id = @id";
            deleteDevices.Parameters.AddWithValue("@id", id);
            deleteDevices.ExecuteNonQuery();

            using var deleteMachine = conn.CreateCommand();
            deleteMachine.Transaction = tx;
            deleteMachine.CommandText = "DELETE FROM Machines WHERE id = @id";
            deleteMachine.Parameters.AddWithValue("@id", id);
            deleteMachine.ExecuteNonQuery();

            tx.Commit();
            DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.Delete: deleted id={id}.");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(DebugLog.Log_Database, $"MachineRepository.Delete: exception: {ex.Message}, rolling back.");
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetDevices
    //
    // Returns all device entries for the given machine.
    // Uses an existing open connection.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static List<MachineDevice> GetDevices(SqliteConnection conn, int machineId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, machine_id, keyboard_type, instance_count FROM MachineDevices WHERE machine_id = @machineId ORDER BY keyboard_type";
        cmd.Parameters.AddWithValue("@machineId", machineId);

        var devices = new List<MachineDevice>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            devices.Add(new MachineDevice
            {
                Id = reader.GetInt32(0),
                MachineId = reader.GetInt32(1),
                KeyboardType = reader.GetString(2).ToKeyboardType(),
                InstanceCount = reader.GetInt32(3)
            });
        }

        return devices;
    }
}