using Microsoft.Data.Sqlite;
using System.IO;

namespace Glass.Data;


public class Database
{
    private static Database? _instance;


    private readonly string _connectionString;
    public static Database Instance => _instance ?? throw new InvalidOperationException("Database not initialized.");

    private Database(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }


    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Glass", "glass.db");

    public SqliteConnection Connect() => new SqliteConnection(_connectionString);

    public static void Create(string dbPath)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath)!);
        _instance = new Database(dbPath);
        _instance.Initialize();
    }

    public static void Open(string dbPath)
    {
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Database not found.", dbPath);
        _instance = new Database(dbPath);
        _instance.Initialize();
    }
    public void Initialize()
    {
        using var conn = Connect();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();

        ApplyMigrations(conn);
    }

    private void ApplyMigrations(SqliteConnection conn)
    {
        int version = GetSchemaVersion(conn);

        // Apply migrations in order
        if (version < 2)
        {
            ApplyMigration(conn, 2, Migration_002);
        }
        if (version < 3)
        {
            ApplyMigration(conn, 3, Migration_003);
        }
        if (version < 4)
        {
            ApplyMigration(conn, 4, Migration_004);
        }
    }

    private int GetSchemaVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM SchemaVersion";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private void ApplyMigration(SqliteConnection conn, int version, string sql)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO SchemaVersion (version) VALUES (@version)";
            cmd.Parameters.AddWithValue("@version", version);
            cmd.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private const string Migration_002 = @"
    ALTER TABLE Characters ADD COLUMN server TEXT NOT NULL DEFAULT 'Test';
";
    private const string Migration_003 = @"
    CREATE TABLE IF NOT EXISTS CharacterSetSlots (
        id                  INTEGER PRIMARY KEY,
        character_set_id    INTEGER NOT NULL REFERENCES CharacterSets(id),
        slot_number         INTEGER NOT NULL,
        character_id        INTEGER NOT NULL REFERENCES Characters(id),
        UNIQUE (character_set_id, slot_number),
        UNIQUE (character_set_id, character_id)
    );
";

    private const string Migration_004 = @"
    CREATE TABLE IF NOT EXISTS WindowLayouts_new (
        id                  INTEGER PRIMARY KEY,
        name                TEXT NOT NULL,
        character_set_id    INTEGER NOT NULL REFERENCES CharacterSets(id),
        machine_id          INTEGER REFERENCES Machines(id),
        monitor_fingerprint TEXT NOT NULL DEFAULT '',
        UNIQUE (character_set_id, machine_id, name)
    );
    INSERT INTO WindowLayouts_new SELECT * FROM WindowLayouts;
    DROP TABLE WindowLayouts;
    ALTER TABLE WindowLayouts_new RENAME TO WindowLayouts;
";
    private const string Schema = @"
        CREATE TABLE IF NOT EXISTS SchemaVersion (
            version     INTEGER NOT NULL,
            applied_at  TEXT NOT NULL DEFAULT (datetime('now'))
        );

        CREATE TABLE IF NOT EXISTS Machines (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL UNIQUE  -- system hostname
        );

        CREATE TABLE IF NOT EXISTS Monitors (
            id           INTEGER PRIMARY KEY,
            machine_id   INTEGER NOT NULL REFERENCES Machines(id),
            display_name TEXT NOT NULL,  -- e.g. \\.\DISPLAY2
            width        INTEGER NOT NULL,
            height       INTEGER NOT NULL,
            orientation  INTEGER NOT NULL DEFAULT 0  -- MonitorOrientation enum value
        );

        CREATE TABLE IF NOT EXISTS Characters (
            id          INTEGER PRIMARY KEY,
            name        TEXT NOT NULL UNIQUE,
            class       INTEGER NOT NULL,  -- EQClass enum value
            account_id  INTEGER NOT NULL,
            progression INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS RelayGroups (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL UNIQUE
        );

        CREATE TABLE IF NOT EXISTS CharacterRelayGroups (
            character_id    INTEGER NOT NULL REFERENCES Characters(id),
            relay_group_id  INTEGER NOT NULL REFERENCES RelayGroups(id),
            PRIMARY KEY (character_id, relay_group_id)
        );

        CREATE TABLE IF NOT EXISTS CharacterSets (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL UNIQUE
        );

        CREATE TABLE IF NOT EXISTS CharacterSetMembers (
            character_set_id    INTEGER NOT NULL REFERENCES CharacterSets(id),
            character_id        INTEGER NOT NULL REFERENCES Characters(id),
            PRIMARY KEY (character_set_id, character_id)
        );

        CREATE TABLE IF NOT EXISTS WindowLayouts (
            id                  INTEGER PRIMARY KEY,
            name                TEXT NOT NULL,
            character_set_id    INTEGER NOT NULL REFERENCES CharacterSets(id),
            machine_id          INTEGER NOT NULL REFERENCES Machines(id),
            monitor_fingerprint TEXT NOT NULL DEFAULT '',
            UNIQUE (character_set_id, machine_id, name)
        );

        CREATE TABLE IF NOT EXISTS CharacterPlacements (
            id               INTEGER PRIMARY KEY,
            window_layout_id INTEGER NOT NULL REFERENCES WindowLayouts(id),
            character_id     INTEGER NOT NULL REFERENCES Characters(id),
            x                INTEGER NOT NULL,
            y                INTEGER NOT NULL,
            width            INTEGER NOT NULL,
            height           INTEGER NOT NULL,
            UNIQUE (window_layout_id, character_id)
        );

        CREATE TABLE IF NOT EXISTS KeyPages (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL UNIQUE,
            device  TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS KeyBindings (
            id              INTEGER PRIMARY KEY,
            key_page_id     INTEGER NOT NULL REFERENCES KeyPages(id),
            key             TEXT NOT NULL,
            command_type    TEXT NOT NULL,
            relay_group_id  INTEGER REFERENCES RelayGroups(id),
            round_robin     INTEGER NOT NULL DEFAULT 0,
            params          TEXT,
            UNIQUE (key_page_id, key)
        );
    ";
}