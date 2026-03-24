using Microsoft.Data.Sqlite;
using System.IO;
using System.Windows.Shapes;
using Glass.Core;
using System.Data;

namespace Glass.Data;


public class Database
{
    private static Database? _instance;


    private readonly string _connectionString;
    public static Database Instance => _instance ?? throw new InvalidOperationException("Database not initialized.");
    public static bool IsInitialized => _instance != null;

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

        int version = _instance.GetSchemaVersion();

        DebugLog.Write(DebugLog.Log_Database, $"Databae open, version {version}");
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
        int version = GetSchemaVersion();

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
        if (version < 5)
        {
            ApplyMigration(conn, 5, Migration_005);
        }
        if (version < 6)
        {
            ApplyMigration(conn, 6, Migration_006);
        }
        if (version < 7)
        {
            ApplyMigration(conn, 7, Migration_007);
        }
        if (version < 8)
        {
            ApplyMigration(conn, 8, Migration_008);
        }
        if (version < 9)
        {
            ApplyMigration(conn, 9, Migration_009);
        }
        if (version < 10)
        {
            ApplyMigration(conn, 10, Migration_010);
        }
        if (version < 11)
        {
            using var pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 11, Migration_011);

            using var pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 12)
        {
            using var pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 12, Migration_012);

            using var pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 13)
        {
            using var pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 13, Migration_013);

            using var pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 14)
        {
            ApplyMigration(conn, 14, Migration_014);
        }
    }

    private int GetSchemaVersion()
    {
        using var conn = Connect();
        conn.Open();

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

    private const string Migration_005 = @"
    ALTER TABLE KeyBindings RENAME COLUMN params TO action;
    ";
    private const string Migration_006 = @"
    ALTER TABLE CharacterSets ADD COLUMN start_page_id INTEGER REFERENCES KeyPages(id);
    ";

    private const string Migration_007 = @"
    CREATE TABLE IF NOT EXISTS Commands (
        id      INTEGER PRIMARY KEY,
        name    TEXT NOT NULL UNIQUE
    );

    CREATE TABLE IF NOT EXISTS CommandSteps (
        id          INTEGER PRIMARY KEY,
        command_id  INTEGER NOT NULL REFERENCES Commands(id),
        sequence    INTEGER NOT NULL,
        type        TEXT NOT NULL,
        value       TEXT NOT NULL,
        delay_ms    INTEGER NOT NULL DEFAULT 0,
        UNIQUE (command_id, sequence)
    );

    CREATE TABLE IF NOT EXISTS KeyBindings_new (
        id          INTEGER PRIMARY KEY,
        key_page_id INTEGER NOT NULL REFERENCES KeyPages(id),
        key         TEXT NOT NULL,
        command_id  INTEGER REFERENCES Commands(id),
        target      TEXT NOT NULL DEFAULT 'self',
        round_robin INTEGER NOT NULL DEFAULT 0,
        UNIQUE (key_page_id, key)
    );

    INSERT INTO KeyBindings_new (id, key_page_id, key, round_robin)
    SELECT id, key_page_id, key, round_robin FROM KeyBindings;

    DROP TABLE KeyBindings;

    ALTER TABLE KeyBindings_new RENAME TO KeyBindings;
    ";

    private const string Migration_008 = @"
    CREATE TABLE IF NOT EXISTS KeyAliases (
        id      INTEGER PRIMARY KEY,
        name    TEXT NOT NULL UNIQUE,
        value   TEXT NOT NULL
    );
    ";

    private const string Migration_009 = @"
    CREATE TABLE IF NOT EXISTS KeyBindings_new (
        id              INTEGER PRIMARY KEY,
        key_page_id     INTEGER NOT NULL REFERENCES KeyPages(id),
        key             TEXT NOT NULL,
        command_id      INTEGER REFERENCES Commands(id),
        target          INTEGER NOT NULL DEFAULT -1,
        relay_group_id  INTEGER REFERENCES RelayGroups(id),
        round_robin     INTEGER NOT NULL DEFAULT 0,
        label           TEXT,
        UNIQUE (key_page_id, key)
    );

    INSERT INTO KeyBindings_new (id, key_page_id, key, command_id, target, relay_group_id, round_robin)
    SELECT
        kb.id,
        kb.key_page_id,
        kb.key,
        kb.command_id,
        CASE kb.target
            WHEN 'Self'           THEN 0
            WHEN 'All Characters' THEN 1
            WHEN 'All Others'     THEN 2
            ELSE                       3
        END,
        CASE kb.target
            WHEN 'Self'           THEN NULL
            WHEN 'All Characters' THEN NULL
            WHEN 'All Others'     THEN NULL
            ELSE (SELECT id FROM RelayGroups WHERE name = kb.target)
        END,
        kb.round_robin
    FROM KeyBindings kb;

    DROP TABLE KeyBindings;

    ALTER TABLE KeyBindings_new RENAME TO KeyBindings;
";

    private const string Migration_010 = @"
    CREATE TABLE IF NOT EXISTS Monitors_new (
        id           INTEGER PRIMARY KEY,
        machine_id   INTEGER NOT NULL REFERENCES Machines(id),
        display_name TEXT NOT NULL,
        width        INTEGER NOT NULL,
        height       INTEGER NOT NULL,
        orientation  INTEGER NOT NULL DEFAULT 0,
        UNIQUE (machine_id, display_name)
    );

    INSERT OR IGNORE INTO Monitors_new SELECT * FROM Monitors;

    DROP TABLE Monitors;

    ALTER TABLE Monitors_new RENAME TO Monitors;
";

    private const string Migration_011 = @"
    PRAGMA foreign_keys = OFF;

    CREATE TABLE IF NOT EXISTS ProfilePages (
        id                  INTEGER PRIMARY KEY,
        character_set_id    INTEGER NOT NULL REFERENCES CharacterSets(id),
        key_page_id         INTEGER NOT NULL REFERENCES KeyPages(id),
        is_start_page       INTEGER NOT NULL DEFAULT 0,
        UNIQUE (character_set_id, key_page_id)
    );

    INSERT INTO ProfilePages (character_set_id, key_page_id, is_start_page)
    SELECT id, start_page_id, 1
    FROM CharacterSets
    WHERE start_page_id IS NOT NULL;

    CREATE TABLE CharacterSets_new (
        id      INTEGER PRIMARY KEY,
        name    TEXT NOT NULL UNIQUE
    );

    INSERT INTO CharacterSets_new SELECT id, name FROM CharacterSets;
    DROP TABLE CharacterSets;
    ALTER TABLE CharacterSets_new RENAME TO CharacterSets;

    PRAGMA foreign_keys = ON;
";

    private const string Migration_012 = @"
    ALTER TABLE CharacterSets RENAME TO Profiles;
    ALTER TABLE CharacterSetSlots RENAME TO ProfileSlots;
    ALTER TABLE CharacterSetMembers RENAME TO ProfileMembers;

    CREATE TABLE ProfileSlots_new (
        id          INTEGER PRIMARY KEY,
        profile_id  INTEGER NOT NULL REFERENCES Profiles(id),
        slot_number INTEGER NOT NULL,
        character_id INTEGER NOT NULL REFERENCES Characters(id),
        UNIQUE (profile_id, slot_number),
        UNIQUE (profile_id, character_id)
    );

    INSERT INTO ProfileSlots_new SELECT id, character_set_id, slot_number, character_id FROM ProfileSlots;
    DROP TABLE ProfileSlots;
    ALTER TABLE ProfileSlots_new RENAME TO ProfileSlots;
";

    private const string Migration_013 = @"
    DROP TABLE IF EXISTS CharacterSetMembers;
    DROP TABLE IF EXISTS CharacterSets;

    CREATE TABLE WindowLayouts_new (
        id                  INTEGER PRIMARY KEY,
        name                TEXT NOT NULL,
        profile_id          INTEGER NOT NULL REFERENCES Profiles(id),
        machine_id          INTEGER NOT NULL REFERENCES Machines(id),
        monitor_fingerprint TEXT NOT NULL DEFAULT '',
        UNIQUE (profile_id, machine_id, name)
    );
    INSERT INTO WindowLayouts_new SELECT id, name, character_set_id, machine_id, monitor_fingerprint FROM WindowLayouts;
    DROP TABLE WindowLayouts;
    ALTER TABLE WindowLayouts_new RENAME TO WindowLayouts;

    CREATE TABLE ProfilePages_new (
        id                  INTEGER PRIMARY KEY,
        profile_id          INTEGER NOT NULL REFERENCES Profiles(id),
        key_page_id         INTEGER NOT NULL REFERENCES KeyPages(id),
        is_start_page       INTEGER NOT NULL DEFAULT 0,
        UNIQUE (profile_id, key_page_id)
    );
    INSERT INTO ProfilePages_new SELECT id, character_set_id, key_page_id, is_start_page FROM ProfilePages;
    DROP TABLE ProfilePages;
    ALTER TABLE ProfilePages_new RENAME TO ProfilePages;

    CREATE TABLE ProfileMembers_new (
        profile_id      INTEGER NOT NULL REFERENCES Profiles(id),
        character_id    INTEGER NOT NULL REFERENCES Characters(id),
        PRIMARY KEY (profile_id, character_id)
    );
    INSERT INTO ProfileMembers_new SELECT character_set_id, character_id FROM ProfileMembers;
    DROP TABLE ProfileMembers;
    ALTER TABLE ProfileMembers_new RENAME TO ProfileMembers;

";
    private const string Migration_014 = @"
    CREATE TABLE IF NOT EXISTS MachineDevices (
        id              INTEGER PRIMARY KEY,
        machine_id      INTEGER NOT NULL REFERENCES Machines(id),
        keyboard_type   TEXT NOT NULL,
        instance_count  INTEGER NOT NULL DEFAULT 1,
        UNIQUE (machine_id, keyboard_type)
    );

    ALTER TABLE Profiles ADD COLUMN machine_id INTEGER REFERENCES Machines(id);
";
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

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