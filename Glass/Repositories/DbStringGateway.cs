using Glass.Core.Logging;
using Microsoft.Data.Sqlite;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// DbStringType
//
// The string types of the client's database string file that are stored in the DbStrings table.  The
// values are the type numbers used in the file.  None means no type.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public enum DbStringType : uint
{
    None = 0,
    AAName = 1,
    AADescription = 4
}

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// DbStringGateway
//
// Database interface for the strings of the client's database string file.  Nothing is cached here;
// every read and write goes to the DbStrings table.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class DbStringGateway
{
    private static DbStringGateway? _instance = null;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static DbStringGateway Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new DbStringGateway();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DbStringGateway
    //
    // Private constructor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private DbStringGateway()
    {
        DebugLog.Write(LogChannel.Database, "DbStringGateway: singleton instance created.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LookupString
    //
    // Reads the text of one stored string from the database.
    //
    // type:  The string type.
    // id:    The string id within that type.
    //
    // Returns the text, or "unknown" when the type is None or no row has that type and id.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public string LookupString(DbStringType type, uint id)
    {
        if (type == DbStringType.None)
        {
            DebugLog.Write(LogChannel.Database, "DbStringGateway.LookupString: called with no string type for id " +
                id + ".", LogLevel.Warn);
            return "unknown";
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT text FROM DbStrings WHERE type = @type AND id = @id";
        cmd.Parameters.AddWithValue("@type", (uint)type);
        cmd.Parameters.AddWithValue("@id", id);

        object? result = cmd.ExecuteScalar();
        if ((result == null) || (result == DBNull.Value))
        {
            DebugLog.Write(LogChannel.Database, "DbStringGateway.LookupString: no row for type " + type +
                " id " + id + ".", LogLevel.Warn);
            return "unknown";
        }

        string text = (string)result;
        DebugLog.Write(LogChannel.Database, "DbStringGateway.LookupString: type " + type + " id " + id +
            " is '" + text + "'.", LogLevel.Trace);
        return text;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StoreStrings
    //
    // Stores the strings of one type in one transaction.  A string with no stored row is inserted.  A
    // string whose stored text differs is updated.  A string whose stored text matches is left alone.
    // Stored strings that are absent from the set are kept.  A failure rolls the transaction back and
    // leaves the previous rows in place.
    //
    // type:     The string type the set belongs to.
    // strings:  The text to store, keyed by string id.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void StoreStrings(DbStringType type, IReadOnlyDictionary<uint, string> strings)
    {
        if (type == DbStringType.None)
        {
            DebugLog.Write(LogChannel.Database, "DbStringGateway.StoreStrings: called with no string type, " +
                "nothing stored.", LogLevel.Warn);
            return;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteTransaction tx = conn.BeginTransaction();
        try
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO DbStrings (type, id, text)
                VALUES (@type, @id, @text)
                ON CONFLICT(type, id) DO UPDATE SET text = excluded.text
                WHERE DbStrings.text <> excluded.text";
            cmd.Parameters.AddWithValue("@type", (uint)type);
            SqliteParameter idParam = cmd.Parameters.Add("@id", SqliteType.Integer);
            SqliteParameter textParam = cmd.Parameters.Add("@text", SqliteType.Text);

            uint written = 0;
            uint unchanged = 0;

            foreach (KeyValuePair<uint, string> entry in strings)
            {
                idParam.Value = entry.Key;
                textParam.Value = entry.Value;
                int rowsChanged = cmd.ExecuteNonQuery();
                if (rowsChanged > 0)
                {
                    written++;
                }
                else
                {
                    unchanged++;
                }
            }

            tx.Commit();
            DebugLog.Write(LogChannel.Database, "DbStringGateway.StoreStrings: committed type " + type + ". " +
                written + " written, " + unchanged + " unchanged.", LogLevel.Trace);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            DebugLog.Write(LogChannel.Database, "DbStringGateway.StoreStrings: failed for type " + type +
                ", transaction rolled back: " + ex.Message, LogLevel.Error);
            throw;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadStrings
    //
    // Reads every stored string of one type from the database.
    //
    // type:  The string type to read.
    //
    // Returns the text keyed by string id; empty when the type is None or no rows have that type.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Dictionary<uint, string> LoadStrings(DbStringType type)
    {
        Dictionary<uint, string> strings = new Dictionary<uint, string>();

        if (type == DbStringType.None)
        {
            DebugLog.Write(LogChannel.Database, "DbStringGateway.LoadStrings: called with no string type.",
                LogLevel.Warn);
            return strings;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, text FROM DbStrings WHERE type = @type";
        cmd.Parameters.AddWithValue("@type", (uint)type);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            uint id = (uint)reader.GetInt64(0);
            strings[id] = reader.GetString(1);
        }

        if (strings.Count == 0)
        {
            DebugLog.Write(LogChannel.Database, "DbStringGateway.LoadStrings: no rows for type " + type + ".",
                LogLevel.Warn);
        }
        else
        {
            DebugLog.Write(LogChannel.Database, "DbStringGateway.LoadStrings: read " + strings.Count +
                " strings of type " + type + ".", LogLevel.Trace);
        }

        return strings;
    }
}