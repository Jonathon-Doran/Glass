using Glass.Core;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// CommandRepository
//
// Provides access to commands and their steps in the database.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class CommandRepository
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetAllCommands
    //
    // Returns all commands ordered alphabetically by name, with steps populated.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Command> GetAllCommands()
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, label FROM Commands ORDER BY name";

        List<Command> commands = new List<Command>();
        using (SqliteDataReader reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                commands.Add(new Command
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Label = reader.GetString(2)
                });
            }
        }

        foreach (Command command in commands)
        {
            command.Steps = GetSteps(conn, command.Id);
        }

        return commands;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetCommand
    //
    // Returns the command with the given ID including its steps, or null if not found.
    //
    // id:  The command ID to look up
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Command? GetCommand(int id)
    {
        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, label FROM Commands WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.GetCommand: id={id} not found.");
            return null;
        }

        var command = new Command
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Label = reader.GetString(2)
        };
        reader.Close();

        command.Steps = GetSteps(conn, id);

        return command;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveCommand
    //
    // Inserts or updates a command. If the command has an ID of 0, inserts a new record
    // and updates the command's ID. Otherwise updates the existing record.
    // Steps are not saved here — use SaveStep for that.
    //
    // command:  The command to persist
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SaveCommand(Command command)
    {
        DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.SaveCommand: name='{command.Name}'.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        if (command.Id == 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Commands (name, label) VALUES (@name, @label); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@name", command.Name);
            cmd.Parameters.AddWithValue("@label", command.Label);
            command.Id = Convert.ToInt32(cmd.ExecuteScalar());
            DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.SaveCommand: inserted. id={command.Id}.");
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Commands SET name = @name, label = @label WHERE id = @id";
            cmd.Parameters.AddWithValue("@name", command.Name);
            cmd.Parameters.AddWithValue("@label", command.Label);
            cmd.Parameters.AddWithValue("@id", command.Id);
            cmd.ExecuteNonQuery();
            DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.SaveCommand: updated. id={command.Id}.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteCommand
    //
    // Deletes the command with the given ID and all its steps.
    //
    // id:  The command ID to delete
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void DeleteCommand(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.DeleteCommand: id={id}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var tx = conn.BeginTransaction();
        try
        {
            using var deleteSteps = conn.CreateCommand();
            deleteSteps.Transaction = tx;
            deleteSteps.CommandText = "DELETE FROM CommandSteps WHERE command_id = @id";
            deleteSteps.Parameters.AddWithValue("@id", id);
            deleteSteps.ExecuteNonQuery();

            using var deleteCmd = conn.CreateCommand();
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = "DELETE FROM Commands WHERE id = @id";
            deleteCmd.Parameters.AddWithValue("@id", id);
            deleteCmd.ExecuteNonQuery();

            tx.Commit();
            DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.DeleteCommand: deleted. id={id}.");
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.DeleteCommand: exception: {ex.Message}, rolling back.");
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveStep
    //
    // Inserts or updates a command step. If the step has an ID of 0, inserts a new record
    // and updates the step's ID. Otherwise updates the existing record.
    //
    // step:  The step to persist
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void SaveStep(CommandStep step)
    {
        DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.SaveStep: command_id={step.CommandId} sequence={step.Sequence} type='{step.Type}' value='{step.Value}' delay_ms={step.DelayMs}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        if (step.Id == 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO CommandSteps (command_id, sequence, type, value, delay_ms, press_type)
                VALUES (@commandId, @sequence, @type, @value, @delayMs, @pressType);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@commandId", step.CommandId);
            cmd.Parameters.AddWithValue("@sequence", step.Sequence);
            cmd.Parameters.AddWithValue("@type", step.Type);
            cmd.Parameters.AddWithValue("@value", step.Value);
            cmd.Parameters.AddWithValue("@delayMs", step.DelayMs);
            cmd.Parameters.AddWithValue("@pressType", step.PressType);
            step.Id = Convert.ToInt32(cmd.ExecuteScalar());
            DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.SaveStep: inserted. id={step.Id}.");
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE CommandSteps
                SET sequence = @sequence, type = @type, value = @value, delay_ms = @delayMs, press_type = @pressType
                WHERE id = @id";
            cmd.Parameters.AddWithValue("@sequence", step.Sequence);
            cmd.Parameters.AddWithValue("@type", step.Type);
            cmd.Parameters.AddWithValue("@value", step.Value);
            cmd.Parameters.AddWithValue("@delayMs", step.DelayMs);
            cmd.Parameters.AddWithValue("@pressType", step.PressType);
            cmd.Parameters.AddWithValue("@id", step.Id);
            cmd.ExecuteNonQuery();
            DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.SaveStep: updated. id={step.Id}.");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteStep
    //
    // Deletes the command step with the given ID.
    //
    // id:  The step ID to delete
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void DeleteStep(int id)
    {
        DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.DeleteStep: id={id}.");

        using var conn = Database.Instance.Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CommandSteps WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();

        DebugLog.Write(DebugLog.Log_Database, $"CommandRepository.DeleteStep: deleted. id={id}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetSteps
    //
    // Returns all steps for the given command ID ordered by sequence.
    //
    // conn:       An open database connection to reuse
    // commandId:  The command whose steps to load
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private List<CommandStep> GetSteps(SqliteConnection conn, int commandId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
                SELECT id, command_id, sequence, type, value, delay_ms, press_type
                FROM CommandSteps
                WHERE command_id = @commandId
                ORDER BY sequence";
        cmd.Parameters.AddWithValue("@commandId", commandId);

        var steps = new List<CommandStep>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            steps.Add(new CommandStep
            {
                Id = reader.GetInt32(0),
                CommandId = reader.GetInt32(1),
                Sequence = reader.GetInt32(2),
                Type = reader.GetString(3),
                Value = reader.GetString(4),
                DelayMs = reader.GetInt32(5),
                PressType = reader.GetString(6)
            });
        }

        return steps;
    }
}