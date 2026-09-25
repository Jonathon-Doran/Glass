using Glass.Core;
using Glass.Core.Logging;
using Microsoft.Data.Sqlite;
using System.Data;
using System.IO;
using System.Windows.Shapes;

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
    }
    public void Initialize()
    {
        using var conn = Connect();
        conn.Open();
        using SqliteCommand journalCmd = conn.CreateCommand();
        journalCmd.CommandText = "PRAGMA journal_mode = WAL";
        object? journalMode = journalCmd.ExecuteScalar();
        DebugLog.Write(LogChannel.Database, "Database.Initialize: journal_mode is " + journalMode, LogLevel.Trace);

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
        if (version < 15)
        {
            ApplyMigration(conn, 15, Migration_015);
        }
        if (version < 16)
        {
            using var pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 16, Migration_016);

            using var pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 17)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 17, Migration_017);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 18)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 18, Migration_018);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 19)
        {
            ApplyMigration(conn, 19, Migration_019);
        }
        if (version < 20)
        {
            ApplyMigration(conn, 20, Migration_020);
        }
        if (version < 21)
        {
            ApplyMigration(conn, 21, Migration_021);
        }
        if (version < 22)
        {
            ApplyMigration(conn, 22, Migration_022);
        }
        if (version < 23)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 23, Migration_023);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 24)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 24, Migration_024);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 25)
        {
            ApplyMigration(conn, 25, Migration_025);
        }
        if (version < 26)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 26, Migration_026);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 27)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 27, Migration_027);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 28)
        {
            ApplyMigration(conn, 28, Migration_028);
        }
        if (version < 29)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 29, Migration_029);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 30)
        {
            ApplyMigration(conn, 30, Migration_030);
        }
        if (version < 31)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 31, Migration_031);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 32)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 32, Migration_032);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 33)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 33, Migration_033);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 34)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 34, Migration_034);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 35)
        {
            ApplyMigration(conn, 35, Migration_035);
        }
        if (version < 36)
        {
            ApplyMigration(conn, 36, Migration_036);
        }
        if (version < 37)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 37, Migration_037);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 38)
        {
            ApplyMigration(conn, 38, Migration_038);
        }
        if (version < 39)
        {
            ApplyMigration(conn, 39, Migration_039);
        }
        if (version < 40)
        {
            ApplyMigration(conn, 40, Migration_040);
        }
        if (version < 41)
        {
            ApplyMigration(conn, 41, Migration_041);
        }
        if (version < 42)
        {
            ApplyMigration(conn, 42, Migration_042);
        }
        if (version < 43)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 43, Migration_043);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 44)
        {
            ApplyMigration(conn, 44, Migration_044);
        }
        if (version < 45)
        {
            ApplyMigration(conn, 45, Migration_045);
        }
        if (version < 46)
        {
            ApplyMigration(conn, 46, Migration_046);
        }
        if (version < 47)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 47, Migration_047);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 48)
        {
            ApplyMigration(conn, 48, Migration_048);
        }
        if (version < 49)
        {
            ApplyMigration(conn, 49, Migration_049);
        }
        if (version < 50)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 50, Migration_050);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 51)
        {
            ApplyMigration(conn, 51, Migration_051);
        }
        if (version < 52)
        {
            ApplyMigration(conn, 52, Migration_052);
        }
        if (version < 53)
        {
            ApplyMigration(conn, 53, Migration_053);
        }
        if (version < 54)
        {
            ApplyMigration(conn, 54, Migration_054);
        }
        if (version < 55)
        {
            ApplyMigration(conn, 55, Migration_055);
        }
        if (version < 56)
        {
            ApplyMigration(conn, 56, Migration_056);
        }
        if (version < 57)
        {
            ApplyMigration(conn, 57, Migration_057);
        }
        if (version < 58)
        {
            ApplyMigration(conn, 58, Migration_058);
        }
        if (version < 59)
        {
            ApplyMigration(conn, 59, Migration_059);
        }
        if (version < 60)
        {
            ApplyMigration(conn, 60, Migration_060);
        }
        if (version < 61)
        {
            ApplyMigration(conn, 61, Migration_061);
        }

        if (version < 62)
        {
            ApplyMigration(conn, 62, Migration_062);
        }

        if (version < 63)
        {
            ApplyMigration(conn, 63, Migration_063);
        }

        if (version < 64)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 64, Migration_064);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
        }
        if (version < 65)
        {
            using SqliteCommand pragmaOff = conn.CreateCommand();
            pragmaOff.CommandText = "PRAGMA foreign_keys = OFF";
            pragmaOff.ExecuteNonQuery();

            ApplyMigration(conn, 65, Migration_065);

            using SqliteCommand pragmaOn = conn.CreateCommand();
            pragmaOn.CommandText = "PRAGMA foreign_keys = ON";
            pragmaOn.ExecuteNonQuery();
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

    private const string Migration_015 = @"
        ALTER TABLE Commands ADD COLUMN short_name TEXT NOT NULL DEFAULT '';
    ";

    private const string Migration_016 = @"
        CREATE TABLE IF NOT EXISTS KeyPages_new (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL,
            device  TEXT NOT NULL,
            UNIQUE (name, device)
        );

        INSERT INTO KeyPages_new (id, name, device)
        SELECT id, name, device FROM KeyPages;

        DROP TABLE KeyPages;

        ALTER TABLE KeyPages_new RENAME TO KeyPages;
        ";

    private const string Migration_017 = @"
        -- Rebuild KeyBindings: collapse relay_group_id into target, drop label
        CREATE TABLE KeyBindings_new (
            id          INTEGER PRIMARY KEY,
            key_page_id INTEGER NOT NULL REFERENCES KeyPages(id),
            key         TEXT NOT NULL,
            command_id  INTEGER REFERENCES Commands(id),
            target      INTEGER NOT NULL DEFAULT 0,
            round_robin INTEGER NOT NULL DEFAULT 0,
            UNIQUE (key_page_id, key)
        );

        INSERT INTO KeyBindings_new (id, key_page_id, key, command_id, target, round_robin)
        SELECT
            id,
            key_page_id,
            key,
            command_id,
            CASE
                WHEN target = 3 AND relay_group_id IS NOT NULL THEN relay_group_id
                ELSE target
            END,
            round_robin
        FROM KeyBindings;

        DROP TABLE KeyBindings;
        ALTER TABLE KeyBindings_new RENAME TO KeyBindings;

        -- Delete spurious special-case relay groups
        DELETE FROM CharacterRelayGroups WHERE relay_group_id IN (
            SELECT id FROM RelayGroups WHERE name IN ('All Characters', 'All Others')
        );
        DELETE FROM RelayGroups WHERE name IN ('All Characters', 'All Others');

        -- Rebuild RelayGroups with clean sorted IDs starting at 4
        CREATE TABLE RelayGroups_new (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL UNIQUE
        );

        INSERT INTO RelayGroups_new (id, name) VALUES
            ( 4, 'Dotters'),
            ( 5, 'Enchanters'),
            ( 6, 'Evacs'),
            ( 7, 'Hasters'),
            ( 8, 'Mages'),
            ( 9, 'Mezzers'),
            (10, 'Nukers'),
            (11, 'Patch Healers'),
            (12, 'Pet Users'),
            (13, 'Prime Healers'),
            (14, 'Rooters'),
            (15, 'Shadowknights'),
            (16, 'Shamen'),
            (17, 'Snares'),
            (18, 'Stunners'),
            (19, 'Tanks'),
            (20, 'Wizards');

        DROP TABLE RelayGroups;
        ALTER TABLE RelayGroups_new RENAME TO RelayGroups;
    ";

    private const string Migration_018 = @"
        CREATE TABLE RelayGroups_new (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL UNIQUE
        );

        INSERT INTO RelayGroups_new (id, name) VALUES
            ( 4, 'Debuffers'),
            ( 5, 'Dotters'),
            ( 6, 'Enchanters'),
            ( 7, 'Evacs'),
            ( 8, 'Hasters'),
            ( 9, 'Mages'),
            (10, 'Mezzers'),
            (11, 'Nukers'),
            (12, 'Patch Healers'),
            (13, 'Pet Users'),
            (14, 'Prime Healers'),
            (15, 'Rooters'),
            (16, 'Shadowknights'),
            (17, 'Shamen'),
            (18, 'Slowers'),
            (19, 'Snares'),
            (20, 'Stunners'),
            (21, 'Tanks'),
            (22, 'Wizards');

        DROP TABLE RelayGroups;
        ALTER TABLE RelayGroups_new RENAME TO RelayGroups;
    ";

    private const string Migration_019 = @"
        ALTER TABLE KeyBindings ADD COLUMN label TEXT;
        ALTER TABLE KeyBindings ADD COLUMN trigger_on INTEGER NOT NULL DEFAULT 0;
    ";

    private const string Migration_020 = @"
        ALTER TABLE CommandSteps ADD COLUMN press_type TEXT NOT NULL DEFAULT 'press';
    ";

    private const string Migration_021 = @"
        ALTER TABLE KeyBindings ADD COLUMN key_type INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE KeyBindings ADD COLUMN repeat_interval_ms INTEGER NOT NULL DEFAULT 1000;
    ";

    private const string Migration_022 = @"
        CREATE TABLE IF NOT EXISTS VideoSources (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL UNIQUE,
            x       INTEGER NOT NULL,
            y       INTEGER NOT NULL,
            width   INTEGER NOT NULL,
            height  INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS VideoDestinations (
            id          INTEGER PRIMARY KEY,
            profile_id  INTEGER NOT NULL REFERENCES Profiles(id) ON DELETE CASCADE,
            source_id   INTEGER NOT NULL REFERENCES VideoSources(id) ON DELETE CASCADE,
            x           INTEGER NOT NULL,
            y           INTEGER NOT NULL,
            width       INTEGER NOT NULL,
            height      INTEGER NOT NULL,
            UNIQUE (profile_id, source_id)
        );
    ";

    private const string Migration_023 = @"
        ALTER TABLE Monitors RENAME TO Monitors_old;

        CREATE TABLE Monitors (
            id           INTEGER PRIMARY KEY,
            machine_id   INTEGER NOT NULL REFERENCES Machines(id),
            adapter_name TEXT NOT NULL,
            pnp_id       TEXT NOT NULL DEFAULT '',
            serial       TEXT NOT NULL DEFAULT '',
            width        INTEGER NOT NULL,
            height       INTEGER NOT NULL,
            UNIQUE (machine_id, adapter_name)
        );

        INSERT INTO Monitors (id, machine_id, adapter_name, pnp_id, serial, width, height)
        SELECT id, machine_id, display_name, '', '', width, height
        FROM Monitors_old;

        DROP TABLE Monitors_old;

        CREATE TABLE LayoutMonitors (
            id              INTEGER PRIMARY KEY,
            layout_id       INTEGER NOT NULL REFERENCES WindowLayouts(id) ON DELETE CASCADE,
            monitor_id      INTEGER NOT NULL REFERENCES Monitors(id),
            layout_position INTEGER NOT NULL,
            slot_width      INTEGER NOT NULL,
            UNIQUE (layout_id, monitor_id),
            UNIQUE (layout_id, layout_position)
        );
    ";

    private const string Migration_024 = @"
        ALTER TABLE WindowLayouts RENAME TO WindowLayouts_old;

        CREATE TABLE WindowLayouts (
            id                  INTEGER PRIMARY KEY,
            name                TEXT NOT NULL UNIQUE,
            machine_id          INTEGER REFERENCES Machines(id),
            monitor_fingerprint TEXT NOT NULL DEFAULT ''
        );

        INSERT INTO WindowLayouts (id, name, machine_id, monitor_fingerprint)
        SELECT
            id,
            name || '-' || id,
            machine_id,
            monitor_fingerprint
        FROM WindowLayouts_old;

        ALTER TABLE Profiles ADD COLUMN layout_id INTEGER REFERENCES WindowLayouts(id);

        UPDATE Profiles
        SET layout_id = (
            SELECT id FROM WindowLayouts_old WHERE profile_id = Profiles.id LIMIT 1
        );

        DROP TABLE WindowLayouts_old;
    ";

    private const string Migration_025 = @"
        DROP TABLE IF EXISTS CharacterPlacements;
    ";

    private const string Migration_026 = @"
        DROP TABLE IF EXISTS LayoutMonitors;
        DROP TABLE IF EXISTS CharacterPlacements;

        CREATE TABLE LayoutMonitors (
            id              INTEGER PRIMARY KEY,
            layout_id       INTEGER NOT NULL REFERENCES WindowLayouts(id) ON DELETE CASCADE,
            monitor_id      INTEGER NOT NULL REFERENCES Monitors(id),
            layout_position INTEGER NOT NULL,
            slot_width      INTEGER NOT NULL,
            UNIQUE (layout_id, monitor_id),
            UNIQUE (layout_id, layout_position)
        );
    ";

    private const string Migration_027 = @"
        CREATE TABLE WindowLayouts_new (
            id         INTEGER PRIMARY KEY,
            name       TEXT NOT NULL UNIQUE,
            machine_id INTEGER REFERENCES Machines(id)
        );

        INSERT INTO WindowLayouts_new (id, name, machine_id)
        SELECT id, name, machine_id
        FROM WindowLayouts;

        DROP TABLE WindowLayouts;
        ALTER TABLE WindowLayouts_new RENAME TO WindowLayouts;
    ";

    private const string Migration_028 = @"
        CREATE TABLE SlotPlacements (
            id          INTEGER PRIMARY KEY,
            layout_id   INTEGER NOT NULL REFERENCES WindowLayouts(id) ON DELETE CASCADE,
            monitor_id  INTEGER NOT NULL REFERENCES Monitors(id),
            slot_number INTEGER NOT NULL,
            x           INTEGER NOT NULL,
            y           INTEGER NOT NULL,
            width       INTEGER NOT NULL,
            height      INTEGER NOT NULL,
            UNIQUE (layout_id, slot_number)
        );

        DROP TABLE CharacterPlacements;
    ";

    private const string Migration_029 = @"
        CREATE TABLE VideoDestinations_new (
            id          INTEGER PRIMARY KEY,
            profile_id  INTEGER NOT NULL REFERENCES Profiles(id) ON DELETE CASCADE,
            source_id   INTEGER NOT NULL REFERENCES VideoSources(id) ON DELETE CASCADE,
            x           INTEGER NOT NULL,
            y           INTEGER NOT NULL,
            width       INTEGER NOT NULL,
            height      INTEGER NOT NULL
        );

        INSERT INTO VideoDestinations_new SELECT * FROM VideoDestinations;
        DROP TABLE VideoDestinations;
        ALTER TABLE VideoDestinations_new RENAME TO VideoDestinations;
    ";

    private const string Migration_030 = @"
        ALTER TABLE VideoDestinations ADD COLUMN name TEXT NOT NULL DEFAULT '';
    ";

    private const string Migration_031 = @"
        CREATE TABLE UISkins (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL UNIQUE
        );

        INSERT INTO UISkins (name) VALUES ('DefaultUI');
        INSERT INTO UISkins (name) VALUES ('SparxHD');
        INSERT INTO UISkins (name) VALUES ('Flame (4K)');

        ALTER TABLE Profiles ADD COLUMN ui_skin_id INTEGER REFERENCES UISkins(id);
    
        CREATE TABLE VideoSources_new (
            id          INTEGER PRIMARY KEY,
            name        TEXT NOT NULL,
            ui_skin_id  INTEGER NOT NULL REFERENCES UISkins(id),
            x           INTEGER NOT NULL,
            y           INTEGER NOT NULL,
            width       INTEGER NOT NULL,
            height      INTEGER NOT NULL,
            UNIQUE (name, ui_skin_id)
        );

        INSERT INTO VideoSources_new (id, name, ui_skin_id, x, y, width, height)
        SELECT id, name, 1, x, y, width, height FROM VideoSources;

        DROP TABLE VideoSources;
        ALTER TABLE VideoSources_new RENAME TO VideoSources;

        CREATE TABLE VideoDestinations_new (
            id      INTEGER PRIMARY KEY,
            name    TEXT NOT NULL UNIQUE,
            x       INTEGER NOT NULL,
            y       INTEGER NOT NULL,
            width   INTEGER NOT NULL,
            height  INTEGER NOT NULL
        );

        DROP TABLE VideoDestinations;
        ALTER TABLE VideoDestinations_new RENAME TO VideoDestinations;
    ";

    private const string Migration_032 = @"
        CREATE TABLE VideoDestinations_new (
            id          INTEGER PRIMARY KEY,
            name        TEXT NOT NULL,
            ui_skin_id  INTEGER NOT NULL REFERENCES UISkins(id),
            x           INTEGER NOT NULL,
            y           INTEGER NOT NULL,
            width       INTEGER NOT NULL,
            height      INTEGER NOT NULL,
            UNIQUE (name, ui_skin_id)
        );

        INSERT INTO VideoDestinations_new (id, name, ui_skin_id, x, y, width, height)
        SELECT id, name, 1, x, y, width, height FROM VideoDestinations;

        DROP TABLE VideoDestinations;
        ALTER TABLE VideoDestinations_new RENAME TO VideoDestinations;
    ";

    private const string Migration_033 = @"
        ALTER TABLE WindowLayouts ADD COLUMN ui_skin_id INTEGER REFERENCES UISkins(id);

        CREATE TABLE Profiles_new (
            id          INTEGER PRIMARY KEY,
            name        TEXT NOT NULL UNIQUE,
            machine_id  INTEGER REFERENCES Machines(id),
            layout_id   INTEGER REFERENCES WindowLayouts(id)
        );

        INSERT INTO Profiles_new (id, name, machine_id, layout_id)
        SELECT id, name, machine_id, layout_id FROM Profiles;

        DROP TABLE Profiles;
        ALTER TABLE Profiles_new RENAME TO Profiles;
    ";

    private const string Migration_034 = @"
        ALTER TABLE Commands RENAME COLUMN short_name TO label;
    ";

    private const string Migration_035 = @"
        CREATE TABLE PatchOpcode (
            id              INTEGER PRIMARY KEY,
            patch_date      TEXT NOT NULL,
            server_type     TEXT NOT NULL,
            opcode_value    INTEGER NOT NULL,
            opcode_name     TEXT NOT NULL,
            direction       INTEGER NOT NULL,
            byte_length     INTEGER,
            UNIQUE (patch_date, server_type, opcode_value, direction)
        );

        CREATE TABLE PacketField (
            id              INTEGER PRIMARY KEY,
            patch_opcode_id INTEGER NOT NULL REFERENCES PatchOpcode(id),
            field_name      TEXT NOT NULL,
            bit_offset      INTEGER NOT NULL,
            bit_length      INTEGER NOT NULL,
            encoding        TEXT NOT NULL,
            UNIQUE (patch_opcode_id, field_name)
        );

        CREATE TABLE PacketOptionalGroup (
            id                  INTEGER PRIMARY KEY,
            patch_opcode_id     INTEGER NOT NULL REFERENCES PatchOpcode(id),
            bit_offset          INTEGER NOT NULL,
            flags_bit_length    INTEGER NOT NULL,
            UNIQUE (patch_opcode_id, bit_offset)
        );

        CREATE TABLE PacketOptionalField (
            id              INTEGER PRIMARY KEY,
            group_id        INTEGER NOT NULL REFERENCES PacketOptionalGroup(id),
            flag_mask       INTEGER NOT NULL,
            sequence_order  INTEGER NOT NULL,
            field_name      TEXT NOT NULL,
            bit_length      INTEGER NOT NULL,
            encoding        TEXT NOT NULL,
            UNIQUE (group_id, sequence_order)
        );
    ";

    private const string Migration_036 = @"
        ALTER TABLE Profiles ADD COLUMN ServerType TEXT NOT NULL DEFAULT '';
        ALTER TABLE Profiles ADD COLUMN Server TEXT NOT NULL DEFAULT '';
    ";


    private const string Migration_037 = @"
        CREATE TABLE PatchOpcode_new
        (
            id              INTEGER PRIMARY KEY,
            patch_date      TEXT NOT NULL,
            server_type     TEXT NOT NULL,
            opcode_value    INTEGER NOT NULL,
            opcode_name     TEXT NOT NULL,
            version         INTEGER NOT NULL DEFAULT 1,
            byte_length     INTEGER,
            UNIQUE (patch_date, server_type, opcode_value, opcode_name, version)
        );

        INSERT INTO PatchOpcode_new
            (id, patch_date, server_type, opcode_value, opcode_name, version, byte_length)
        SELECT
            id, patch_date, server_type, opcode_value, opcode_name, 1, byte_length
        FROM PatchOpcode;

        CREATE TABLE PatchOpcodeChannel_temp
        (
            patch_opcode_id INTEGER NOT NULL,
            channel         TEXT NOT NULL
        );

        INSERT INTO PatchOpcodeChannel_temp (patch_opcode_id, channel)
        SELECT id, 'C2Z' FROM PatchOpcode WHERE direction = 0;

        INSERT INTO PatchOpcodeChannel_temp (patch_opcode_id, channel)
        SELECT id, 'Z2C' FROM PatchOpcode WHERE direction = 1;

        DROP TABLE PatchOpcode;

        ALTER TABLE PatchOpcode_new RENAME TO PatchOpcode;

        CREATE TABLE PatchOpcodeChannel
        (
            id              INTEGER PRIMARY KEY,
            patch_opcode_id INTEGER NOT NULL REFERENCES PatchOpcode(id),
            channel         TEXT NOT NULL,
            UNIQUE (patch_opcode_id, channel)
        );

        INSERT INTO PatchOpcodeChannel (patch_opcode_id, channel)
        SELECT patch_opcode_id, channel FROM PatchOpcodeChannel_temp;

        DROP TABLE PatchOpcodeChannel_temp;
    ";

    private const string Migration_038 = @"
        ALTER TABLE Characters ADD COLUMN level INTEGER;
        ALTER TABLE Characters ADD COLUMN practice_points INTEGER;
        ALTER TABLE Characters ADD COLUMN max_hp INTEGER;
        ALTER TABLE Characters ADD COLUMN max_mana INTEGER;
        ALTER TABLE Characters ADD COLUMN strength INTEGER;
        ALTER TABLE Characters ADD COLUMN stamina INTEGER;
        ALTER TABLE Characters ADD COLUMN charisma INTEGER;
        ALTER TABLE Characters ADD COLUMN dexterity INTEGER;
        ALTER TABLE Characters ADD COLUMN intelligence INTEGER;
        ALTER TABLE Characters ADD COLUMN agility INTEGER;
        ALTER TABLE Characters ADD COLUMN wisdom INTEGER;
        ALTER TABLE Characters ADD COLUMN platinum INTEGER;
        ALTER TABLE Characters ADD COLUMN gold INTEGER;
        ALTER TABLE Characters ADD COLUMN silver INTEGER;
        ALTER TABLE Characters ADD COLUMN copper INTEGER;
    ";

    private const string Migration_039 = @"
        ALTER TABLE Characters ADD COLUMN x_position REAL;
        ALTER TABLE Characters ADD COLUMN y_position REAL;
        ALTER TABLE Characters ADD COLUMN z_position REAL;
        ALTER TABLE Characters ADD COLUMN heading_degrees REAL;
        ALTER TABLE Characters ADD COLUMN current_mana INTEGER;
        ALTER TABLE Characters ADD COLUMN current_hp INTEGER;
    ";

    private const string Migration_040 = @"
        ALTER TABLE PacketOptionalGroup ADD COLUMN flag_field_name TEXT NOT NULL DEFAULT '';
        UPDATE PacketOptionalGroup SET flag_field_name = 'flags';
    ";

    private const string Migration_041 = @"
        ALTER TABLE PacketField ADD COLUMN divisor REAL NOT NULL DEFAULT 1.0;
        ALTER TABLE PacketOptionalField ADD COLUMN divisor REAL NOT NULL DEFAULT 1.0;
    ";

    private const string Migration_042 = @"
        ALTER TABLE PacketField ADD COLUMN relative_to TEXT;
    ";

    private const string Migration_043 = @"
        CREATE TABLE Characters_new (
            id              INTEGER PRIMARY KEY,
            name            TEXT NOT NULL,
            class           INTEGER NOT NULL,
            account_id      INTEGER NOT NULL,
            progression     INTEGER NOT NULL DEFAULT 0,
            server          TEXT NOT NULL DEFAULT 'Test',
            level           INTEGER,
            practice_points INTEGER,
            max_hp          INTEGER,
            max_mana        INTEGER,
            strength        INTEGER,
            stamina         INTEGER,
            charisma        INTEGER,
            dexterity       INTEGER,
            intelligence    INTEGER,
            agility         INTEGER,
            wisdom          INTEGER,
            platinum        INTEGER,
            gold            INTEGER,
            silver          INTEGER,
            copper          INTEGER,
            x_position      REAL,
            y_position      REAL,
            z_position      REAL,
            heading_degrees REAL,
            current_mana    INTEGER,
            current_hp      INTEGER,
            UNIQUE (name, server)
        );

        INSERT INTO Characters_new (
            id, name, class, account_id, progression, server,
            level, practice_points, max_hp, max_mana,
            strength, stamina, charisma, dexterity, intelligence, agility, wisdom,
            platinum, gold, silver, copper,
            x_position, y_position, z_position, heading_degrees,
            current_mana, current_hp
        )
        SELECT
            id, name, class, account_id, progression, server,
            level, practice_points, max_hp, max_mana,
            strength, stamina, charisma, dexterity, intelligence, agility, wisdom,
            platinum, gold, silver, copper,
            x_position, y_position, z_position, heading_degrees,
            current_mana, current_hp
        FROM Characters;

        DROP TABLE Characters;

        ALTER TABLE Characters_new RENAME TO Characters;
    ";

    private const string Migration_044 = @"
        ALTER TABLE PacketOptionalGroup ADD COLUMN name TEXT NOT NULL DEFAULT '';
    ";

    private const string Migration_045 = @"
        CREATE TABLE FieldCollection (
            id          INTEGER PRIMARY KEY,
            name        TEXT NOT NULL,
            version     INTEGER NOT NULL DEFAULT 1,
            patch_date  TEXT NOT NULL,
            server_type TEXT NOT NULL,
            UNIQUE (patch_date, server_type, name)
        );

        INSERT INTO FieldCollection (id, name, version, patch_date, server_type)
        SELECT
            id,
            opcode_name || 'V' || version,
            version,
            patch_date,
            server_type
        FROM PatchOpcode;
    ";

    private const string Migration_046 = @"
        CREATE TABLE FieldCollection_new (
            id          INTEGER PRIMARY KEY,
            name        TEXT NOT NULL,
            patch_date  TEXT NOT NULL,
            server_type TEXT NOT NULL,
            UNIQUE (patch_date, server_type, name)
        );

        INSERT INTO FieldCollection_new (id, name, patch_date, server_type)
        SELECT id, name, patch_date, server_type
        FROM FieldCollection;

        DROP TABLE FieldCollection;
        ALTER TABLE FieldCollection_new RENAME TO FieldCollection;
    ";

    private const string Migration_047 = @"
        CREATE TABLE PacketField_new (
            id              INTEGER PRIMARY KEY,
            patch_date      TEXT NOT NULL,
            server_type     TEXT NOT NULL,
            collection_name TEXT NOT NULL,
            field_name      TEXT NOT NULL,
            bit_offset      INTEGER NOT NULL,
            bit_length      INTEGER NOT NULL,
            encoding        TEXT NOT NULL,
            divisor         REAL NOT NULL DEFAULT 1.0,
            relative_to     TEXT,
            UNIQUE (patch_date, server_type, collection_name, field_name)
        );

        INSERT INTO PacketField_new
            (patch_date, server_type, collection_name, field_name,
             bit_offset, bit_length, encoding, divisor, relative_to)
        SELECT
            fc.patch_date, fc.server_type, fc.name, pf.field_name,
            pf.bit_offset, pf.bit_length, pf.encoding, pf.divisor, pf.relative_to
        FROM PacketField pf
        JOIN FieldCollection fc ON fc.id = pf.patch_opcode_id
        ORDER BY fc.patch_date, fc.server_type, fc.name, pf.bit_offset;

        DROP TABLE PacketField;
        ALTER TABLE PacketField_new RENAME TO PacketField;
    ";

    private const string Migration_048 = @"
        ALTER TABLE PatchOpcode ADD COLUMN collection_name TEXT NOT NULL DEFAULT '';

        UPDATE PatchOpcode
        SET collection_name = opcode_name;

        UPDATE PatchOpcode
        SET collection_name = 'OP_ZoneEntryV1'
        WHERE opcode_name = 'OP_ZoneEntry' AND version = 1;

        UPDATE PatchOpcode
        SET collection_name = 'OP_ZoneEntryV2'
        WHERE opcode_name = 'OP_ZoneEntry' AND version = 2;
    ";

    private const string Migration_049 = @"
        CREATE TABLE Gate (
            id                  INTEGER PRIMARY KEY,
            name                TEXT NOT NULL,
            kind                TEXT NOT NULL,
            child_collection    TEXT NOT NULL,
            field_name          TEXT,
            patch_date          TEXT NOT NULL,
            server_type         TEXT NOT NULL,
            UNIQUE (patch_date, server_type, name)
        );
    ";

    private const string Migration_050 = @"
        INSERT INTO Gate (patch_date, server_type, name, kind, child_collection, field_name)
        SELECT DISTINCT
            patch_date,
            server_type,
            'Gate_' || collection_name,
            'Always',
            collection_name,
            NULL
        FROM PatchOpcode;

        CREATE TABLE PatchOpcode_new (
            id              INTEGER PRIMARY KEY,
            patch_date      TEXT NOT NULL,
            server_type     TEXT NOT NULL,
            opcode_value    INTEGER NOT NULL,
            opcode_name     TEXT NOT NULL,
            version         INTEGER NOT NULL DEFAULT 1,
            byte_length     INTEGER,
            gate_name       TEXT NOT NULL DEFAULT '',
            UNIQUE (patch_date, server_type, opcode_value, opcode_name, version)
        );

        INSERT INTO PatchOpcode_new
            (id, patch_date, server_type, opcode_value, opcode_name, version, byte_length, gate_name)
        SELECT
            id, patch_date, server_type, opcode_value, opcode_name, version, byte_length,
            'Gate_' || collection_name
        FROM PatchOpcode;

        DROP TABLE PatchOpcode;
        ALTER TABLE PatchOpcode_new RENAME TO PatchOpcode;
    ";
    
    private const string Migration_051 = @"
        ALTER TABLE Gate RENAME TO Multiplicity;
    ";

    private const string Migration_052 = @"
        ALTER TABLE PacketField ADD COLUMN predicate TEXT;
    ";
    private const string Migration_053 = @"
        ALTER TABLE Multiplicity RENAME TO Gate;
    ";
    private const string Migration_054 = @"
        ALTER TABLE PacketField ADD COLUMN sequence INTEGER;
    ";
    private const string Migration_055 = @"
        ALTER TABLE Gate ADD COLUMN count INTEGER;
    ";
    private const string Migration_056 = @"
        ALTER TABLE PacketField ADD COLUMN blob_byte_count INTEGER NOT NULL DEFAULT 0;
    ";
    private const string Migration_057 = @"
        ALTER TABLE PacketField DROP COLUMN blob_byte_count;
    ";
    private const string Migration_058 = @"
        ALTER TABLE Characters ADD COLUMN current_zone INTEGER;
    ";
    private const string Migration_059 = @"
        DROP TABLE IF EXISTS PacketOptionalField;
        DROP TABLE IF EXISTS PacketOptionalGroup;
    ";
    private const string Migration_060 = @"
        DROP TABLE IF EXISTS PatchOpcodeChannel;
    ";

    private const string Migration_061 = @"
        CREATE TABLE PatchLevel (
            id          INTEGER PRIMARY KEY,
            patch_date  TEXT NOT NULL,
            server_type TEXT NOT NULL,
            UNIQUE (patch_date, server_type)
        );

        INSERT INTO PatchLevel (patch_date, server_type)
        SELECT DISTINCT patch_date, server_type FROM PatchOpcode;
    ";

    private const string Migration_062 = @"
        CREATE TABLE IF NOT EXISTS ItemRecords (
            id                INTEGER PRIMARY KEY,  -- wire item id
            name              TEXT NOT NULL,
            lore              TEXT NOT NULL DEFAULT '',
            lore_group        INTEGER NOT NULL DEFAULT 0,
            item_type         INTEGER NOT NULL DEFAULT 0,
            item_type2        INTEGER NOT NULL DEFAULT 0,
            class_mask        INTEGER NOT NULL DEFAULT 0,
            race_mask         INTEGER NOT NULL DEFAULT 0,
            usable_slot_mask  INTEGER NOT NULL DEFAULT 0,
            required_level    INTEGER NOT NULL DEFAULT 0,
            recommended_level INTEGER NOT NULL DEFAULT 0,
            tradeskill        INTEGER NOT NULL DEFAULT 0,
            food_drink_value  INTEGER NOT NULL DEFAULT 0,
            plus_strength     INTEGER NOT NULL DEFAULT 0,
            plus_stamina      INTEGER NOT NULL DEFAULT 0,
            plus_agility      INTEGER NOT NULL DEFAULT 0,
            plus_dexterity    INTEGER NOT NULL DEFAULT 0,
            plus_charisma     INTEGER NOT NULL DEFAULT 0,
            plus_intelligence INTEGER NOT NULL DEFAULT 0,
            plus_wisdom       INTEGER NOT NULL DEFAULT 0,
            plus_hp           INTEGER NOT NULL DEFAULT 0,
            plus_mana         INTEGER NOT NULL DEFAULT 0,
            plus_endurance    INTEGER NOT NULL DEFAULT 0,
            plus_ac           INTEGER NOT NULL DEFAULT 0,
            plus_attack       INTEGER NOT NULL DEFAULT 0,
            hp_regen          INTEGER NOT NULL DEFAULT 0,
            mana_regen        INTEGER NOT NULL DEFAULT 0,
            heroic_strength     INTEGER NOT NULL DEFAULT 0,
            heroic_stamina      INTEGER NOT NULL DEFAULT 0,
            heroic_agility      INTEGER NOT NULL DEFAULT 0,
            heroic_dexterity    INTEGER NOT NULL DEFAULT 0,
            heroic_charisma     INTEGER NOT NULL DEFAULT 0,
            heroic_intelligence INTEGER NOT NULL DEFAULT 0,
            heroic_wisdom       INTEGER NOT NULL DEFAULT 0,
            save_cold         INTEGER NOT NULL DEFAULT 0,
            save_disease      INTEGER NOT NULL DEFAULT 0,
            save_poison       INTEGER NOT NULL DEFAULT 0,
            save_magic        INTEGER NOT NULL DEFAULT 0,
            save_fire         INTEGER NOT NULL DEFAULT 0,
            skill_mod_skill   INTEGER NOT NULL DEFAULT 0,
            skill_mod_percent INTEGER NOT NULL DEFAULT 0,
            skill_mod_max     INTEGER NOT NULL DEFAULT 0,
            weapon_delay      INTEGER NOT NULL DEFAULT 0,
            base_damage       INTEGER NOT NULL DEFAULT 0,
            backstab_damage   INTEGER NOT NULL DEFAULT 0,
            weapon_range      INTEGER NOT NULL DEFAULT 0,
            bag_slots         INTEGER NOT NULL DEFAULT 0,
            bag_content_size  INTEGER NOT NULL DEFAULT 0,
            bag_weight_reduction INTEGER NOT NULL DEFAULT 0,
            weight            INTEGER NOT NULL DEFAULT 0,
            size              INTEGER NOT NULL DEFAULT 0,
            cost              INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS ItemEffects (
            item_id          INTEGER NOT NULL REFERENCES ItemRecords(id),
            spell_id         INTEGER NOT NULL,
            name             TEXT NOT NULL DEFAULT '',
            effect_type      INTEGER NOT NULL DEFAULT 0,
            level            INTEGER NOT NULL DEFAULT 0,
            cast_as_level    INTEGER NOT NULL DEFAULT 0,
            max_charges      INTEGER NOT NULL DEFAULT 0,
            cast_time_ms     INTEGER NOT NULL DEFAULT 0,
            recast_time_s    INTEGER NOT NULL DEFAULT 0,
            recast_type      INTEGER NOT NULL DEFAULT 0,
            recast_delay_s   INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS ItemInstances (
            id                INTEGER PRIMARY KEY,
            character_id      INTEGER NOT NULL REFERENCES Characters(id),
            item_id           INTEGER NOT NULL REFERENCES ItemRecords(id),
            parent_id         INTEGER REFERENCES ItemInstances(id),  -- NULL for top level
            storage           INTEGER NOT NULL,
            main_position     INTEGER NOT NULL,
            sub_position      INTEGER NOT NULL,
            aug_position      INTEGER NOT NULL,
            stack_size        INTEGER NOT NULL DEFAULT 0,
            remaining_charges INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX IF NOT EXISTS idx_iteminstances_character ON ItemInstances(character_id);
    ";

    private const string Migration_063 = @"
        CREATE UNIQUE INDEX IF NOT EXISTS idx_iteminstances_position
            ON ItemInstances(character_id, storage, main_position, sub_position, aug_position);

        CREATE INDEX IF NOT EXISTS idx_itemeffects_item ON ItemEffects(item_id);
    ";


    private const string Migration_064 = @"
        DROP TABLE IF EXISTS ItemRecords;

        CREATE TABLE ItemRecords (
            id_string               TEXT    NOT NULL DEFAULT '',        -- 1
            container_type          INTEGER NOT NULL DEFAULT 0,         -- 3
            unknown_7               INTEGER NOT NULL DEFAULT 0,         -- 7
            unknown_8               INTEGER NOT NULL DEFAULT 0,         -- 8
            unknown_9               INTEGER NOT NULL DEFAULT 0,         -- 9
            unknown_10              INTEGER NOT NULL DEFAULT 0,         -- 10
            unknown_11              INTEGER NOT NULL DEFAULT 0,         -- 11
            unknown_14              INTEGER NOT NULL DEFAULT 0,         -- 14
            unknown_15              INTEGER NOT NULL DEFAULT 0,         -- 15
            unknown_16              INTEGER NOT NULL DEFAULT 0,         -- 16
            unknown_17              INTEGER NOT NULL DEFAULT 0,         -- 17
            is_evolving_item        INTEGER NOT NULL DEFAULT 0,         -- 18
            unknown_20              INTEGER NOT NULL DEFAULT 0,         -- 20
            unknown_21              INTEGER NOT NULL DEFAULT 0,         -- 21
            unknown_22              INTEGER NOT NULL DEFAULT 0,         -- 22
            unknown_23              INTEGER NOT NULL DEFAULT 0,         -- 23
            unknown_24              INTEGER NOT NULL DEFAULT 0,         -- 24
            unknown_25              INTEGER NOT NULL DEFAULT 0,         -- 25
            unknown_27              INTEGER NOT NULL DEFAULT 0,         -- 27
            unknown_28              INTEGER NOT NULL DEFAULT 0,         -- 28
            item_type2              INTEGER NOT NULL DEFAULT 0,         -- 29
            name                    TEXT    NOT NULL,                   -- 30
            lore                    TEXT    NOT NULL DEFAULT '',        -- 31
            it_file                 INTEGER NOT NULL DEFAULT 0,         -- 32
            unknown_33              INTEGER NOT NULL DEFAULT 0,         -- 33
            id                      INTEGER PRIMARY KEY,                -- 34 (wire item id)
            weight                  REAL    NOT NULL DEFAULT 0,         -- 35
            unknown_36              INTEGER NOT NULL DEFAULT 0,         -- 36
            unknown_37              INTEGER NOT NULL DEFAULT 0,         -- 37
            unknown_38              INTEGER NOT NULL DEFAULT 0,         -- 38
            size                    INTEGER NOT NULL DEFAULT 0,         -- 40
            usable_slot_mask        INTEGER NOT NULL DEFAULT 0,         -- 41
            cost                    INTEGER NOT NULL DEFAULT 0,         -- 42
            icon_id                 INTEGER NOT NULL DEFAULT 0,         -- 43
            unknown_44              INTEGER NOT NULL DEFAULT 0,         -- 44
            is_tradeskill           INTEGER NOT NULL DEFAULT 0,         -- 45
            save_cold               INTEGER NOT NULL DEFAULT 0,         -- 46
            save_disease            INTEGER NOT NULL DEFAULT 0,         -- 47
            save_poison             INTEGER NOT NULL DEFAULT 0,         -- 48
            save_magic              INTEGER NOT NULL DEFAULT 0,         -- 49
            save_fire               INTEGER NOT NULL DEFAULT 0,         -- 50
            save_corruption         INTEGER NOT NULL DEFAULT 0,         -- 51
            plus_strength           INTEGER NOT NULL DEFAULT 0,         -- 52
            plus_stamina            INTEGER NOT NULL DEFAULT 0,         -- 53
            plus_agility            INTEGER NOT NULL DEFAULT 0,         -- 54
            plus_dexterity          INTEGER NOT NULL DEFAULT 0,         -- 55
            plus_charisma           INTEGER NOT NULL DEFAULT 0,         -- 56
            plus_intelligence       INTEGER NOT NULL DEFAULT 0,         -- 57
            plus_wisdom             INTEGER NOT NULL DEFAULT 0,         -- 58
            plus_hp                 INTEGER NOT NULL DEFAULT 0,         -- 59
            plus_mana               INTEGER NOT NULL DEFAULT 0,         -- 61
            plus_endurance          INTEGER NOT NULL DEFAULT 0,         -- 62
            plus_ac                 INTEGER NOT NULL DEFAULT 0,         -- 63
            hp_regen                INTEGER NOT NULL DEFAULT 0,         -- 64
            mana_regen              INTEGER NOT NULL DEFAULT 0,         -- 65
            unknown_66              INTEGER NOT NULL DEFAULT 0,         -- 66
            class_mask              INTEGER NOT NULL DEFAULT 0,         -- 67
            race_mask               INTEGER NOT NULL DEFAULT 0,         -- 68
            unknown_69              INTEGER NOT NULL DEFAULT 0,         -- 69
            skill_percent_chance    INTEGER NOT NULL DEFAULT 0,         -- 70
            skill_max_change        INTEGER NOT NULL DEFAULT 0,         -- 71
            skill_id                INTEGER NOT NULL DEFAULT 0,         -- 72
            unknown_73              INTEGER NOT NULL DEFAULT 0,         -- 73
            unknown_74              INTEGER NOT NULL DEFAULT 0,         -- 74
            unknown_75              INTEGER NOT NULL DEFAULT 0,         -- 75
            unknown_76              INTEGER NOT NULL DEFAULT 0,         -- 76
            unknown_77              INTEGER NOT NULL DEFAULT 0,         -- 77
            unknown_78              INTEGER NOT NULL DEFAULT 0,         -- 78
            food_drink_value        INTEGER NOT NULL DEFAULT 0,         -- 79
            required_level          INTEGER NOT NULL DEFAULT 0,         -- 80
            recommended_level       INTEGER NOT NULL DEFAULT 0,         -- 81
            bard_value              INTEGER NOT NULL DEFAULT 0,         -- 82
            unknown_83              INTEGER NOT NULL DEFAULT 0,         -- 83
            unknown_84              INTEGER NOT NULL DEFAULT 0,         -- 84
            weapon_delay            INTEGER NOT NULL DEFAULT 0,         -- 85
            unknown_86              INTEGER NOT NULL DEFAULT 0,         -- 86
            unknown_87              INTEGER NOT NULL DEFAULT 0,         -- 87
            weapon_range            INTEGER NOT NULL DEFAULT 0,         -- 88
            weapon_base_damage      INTEGER NOT NULL DEFAULT 0,         -- 89
            color                   INTEGER NOT NULL DEFAULT 0,         -- 90
            unknown_91              INTEGER NOT NULL DEFAULT 0,         -- 91
            item_type1              INTEGER NOT NULL DEFAULT 0,         -- 92
            material                INTEGER NOT NULL DEFAULT 0,         -- 93
            unknown_94              INTEGER NOT NULL DEFAULT 0,         -- 94
            unknown_95              INTEGER NOT NULL DEFAULT 0,         -- 95
            unknown_96              INTEGER NOT NULL DEFAULT 0,         -- 96
            unknown_97              INTEGER NOT NULL DEFAULT 0,         -- 97
            unknown_98              INTEGER NOT NULL DEFAULT 0,         -- 98
            unknown_99              INTEGER NOT NULL DEFAULT 0,         -- 99
            unknown_100             INTEGER NOT NULL DEFAULT 0,         -- 100
            unknown_101             INTEGER NOT NULL DEFAULT 0,         -- 101
            unknown_102             TEXT    NOT NULL DEFAULT '',        -- 102
            unknown_103             INTEGER NOT NULL DEFAULT 0,         -- 103
            unknown_104             INTEGER NOT NULL DEFAULT 0,         -- 104
            unknown_105             INTEGER NOT NULL DEFAULT 0,         -- 105
            unknown_107             INTEGER NOT NULL DEFAULT 0,         -- 107
            unknown_108             INTEGER NOT NULL DEFAULT 0,         -- 108
            unknown_109             INTEGER NOT NULL DEFAULT 0,         -- 109
            unknown_110             INTEGER NOT NULL DEFAULT 0,         -- 110
            unknown_111             INTEGER NOT NULL DEFAULT 0,         -- 111
            bag_type                INTEGER NOT NULL DEFAULT 0,         -- 112
            bag_slot_count          INTEGER NOT NULL DEFAULT 0,         -- 113
            bag_size                INTEGER NOT NULL DEFAULT 0,         -- 114
            bag_weight_reduction    INTEGER NOT NULL DEFAULT 0,         -- 115
            unknown_116             INTEGER NOT NULL DEFAULT 0,         -- 116
            unknown_117             INTEGER NOT NULL DEFAULT 0,         -- 117
            unknown_118             TEXT    NOT NULL DEFAULT '',        -- 118
            lore_group              INTEGER NOT NULL DEFAULT 0,         -- 119
            unknown_120             INTEGER NOT NULL DEFAULT 0,         -- 120
            tribute                 INTEGER NOT NULL DEFAULT 0,         -- 121
            unknown_122             INTEGER NOT NULL DEFAULT 0,         -- 122
            plus_attack             INTEGER NOT NULL DEFAULT 0,         -- 123
            haste                   INTEGER NOT NULL DEFAULT 0,         -- 124
            unknown_125             INTEGER NOT NULL DEFAULT 0,         -- 125
            aug_distiller_needed    INTEGER NOT NULL DEFAULT 0,         -- 126
            unknown_127             INTEGER NOT NULL DEFAULT 0,         -- 127
            unknown_128             INTEGER NOT NULL DEFAULT 0,         -- 128
            unknown_129             INTEGER NOT NULL DEFAULT 0,         -- 129
            unknown_130             INTEGER NOT NULL DEFAULT 0,         -- 130
            max_stack_size          INTEGER NOT NULL DEFAULT 0,         -- 131
            unknown_132             INTEGER NOT NULL DEFAULT 0,         -- 132
            unknown_133             INTEGER NOT NULL DEFAULT 0,         -- 133
            unknown_134             BLOB    NOT NULL DEFAULT X'',       -- 134 (78 bytes)
            unknown_136             INTEGER NOT NULL DEFAULT 0,         -- 136
            unknown_137             INTEGER NOT NULL DEFAULT 0,         -- 137
            unknown_138             INTEGER NOT NULL DEFAULT 0,         -- 138
            unknown_139             INTEGER NOT NULL DEFAULT 0,         -- 139
            backstab_damage         INTEGER NOT NULL DEFAULT 0,         -- 140
            heroic_strength         INTEGER NOT NULL DEFAULT 0,         -- 141
            heroic_intelligence     INTEGER NOT NULL DEFAULT 0,         -- 142
            heroic_wisdom           INTEGER NOT NULL DEFAULT 0,         -- 143
            heroic_agility          INTEGER NOT NULL DEFAULT 0,         -- 144
            heroic_dexterity        INTEGER NOT NULL DEFAULT 0,         -- 145
            heroic_stamina          INTEGER NOT NULL DEFAULT 0,         -- 146
            heroic_charisma         INTEGER NOT NULL DEFAULT 0,         -- 147
            unknown_148             INTEGER NOT NULL DEFAULT 0,         -- 148
            unknown_149             INTEGER NOT NULL DEFAULT 0,         -- 149
            unknown_150             INTEGER NOT NULL DEFAULT 0,         -- 150
            unknown_151             INTEGER NOT NULL DEFAULT 0,         -- 151
            unknown_152             INTEGER NOT NULL DEFAULT 0,         -- 152
            unknown_153             INTEGER NOT NULL DEFAULT 0,         -- 153
            unknown_154             INTEGER NOT NULL DEFAULT 0,         -- 154
            unknown_155             INTEGER NOT NULL DEFAULT 0,         -- 155
            unknown_156             INTEGER NOT NULL DEFAULT 0,         -- 156
            unknown_157             INTEGER NOT NULL DEFAULT 0,         -- 157
            unknown_158             INTEGER NOT NULL DEFAULT 0,         -- 158
            unknown_159             INTEGER NOT NULL DEFAULT 0,         -- 159
            unknown_160             INTEGER NOT NULL DEFAULT 0,         -- 160
            unknown_161             INTEGER NOT NULL DEFAULT 0,         -- 161
            unknown_162             INTEGER NOT NULL DEFAULT 0,         -- 162
            unknown_163             TEXT    NOT NULL DEFAULT '',        -- 163
            unknown_164             INTEGER NOT NULL DEFAULT 0,         -- 164
            unknown_165             INTEGER NOT NULL DEFAULT 0,         -- 165
            unknown_166             INTEGER NOT NULL DEFAULT 0,         -- 166
            unknown_167             INTEGER NOT NULL DEFAULT 0,         -- 167
            unknown_168             INTEGER NOT NULL DEFAULT 0,         -- 168
            unknown_169             INTEGER NOT NULL DEFAULT 0,         -- 169
            unknown_170             INTEGER NOT NULL DEFAULT 0,         -- 170
            unknown_171             INTEGER NOT NULL DEFAULT 0,         -- 171
            unknown_172             INTEGER NOT NULL DEFAULT 0,         -- 172
            unknown_173             INTEGER NOT NULL DEFAULT 0,         -- 173
            unknown_175             INTEGER NOT NULL DEFAULT 0,         -- 175
            unknown_176             INTEGER NOT NULL DEFAULT 0,         -- 176
            unknown_177             INTEGER NOT NULL DEFAULT 0,         -- 177
            unknown_178             INTEGER NOT NULL DEFAULT 0,         -- 178
            unknown_179             INTEGER NOT NULL DEFAULT 0,         -- 179
            unknown_180             INTEGER NOT NULL DEFAULT 0,         -- 180
            unknown_181             INTEGER NOT NULL DEFAULT 0,         -- 181
            unknown_182             INTEGER NOT NULL DEFAULT 0,         -- 182
            unknown_183             INTEGER NOT NULL DEFAULT 0,         -- 183
            unknown_184             INTEGER NOT NULL DEFAULT 0,         -- 184
            unknown_185             INTEGER NOT NULL DEFAULT 0,         -- 185
            unknown_186             INTEGER NOT NULL DEFAULT 0,         -- 186
            unknown_187             INTEGER NOT NULL DEFAULT 0,         -- 187
            unknown_188             INTEGER NOT NULL DEFAULT 0,         -- 188
            unknown_189             INTEGER NOT NULL DEFAULT 0,         -- 189
            unknown_190             INTEGER NOT NULL DEFAULT 0,         -- 190
            unknown_191             TEXT    NOT NULL DEFAULT '',        -- 191
            unknown_196             INTEGER NOT NULL DEFAULT 0,         -- 196
            unknown_198             INTEGER NOT NULL DEFAULT 0,         -- 198
            unknown_199             INTEGER NOT NULL DEFAULT 0          -- 199
        );
    ";

    private const string Migration_065 = @"
        DROP TABLE IF EXISTS ItemInstances;

        CREATE TABLE ItemInstances (
            id                  INTEGER PRIMARY KEY,
            character_id        INTEGER NOT NULL REFERENCES Characters(id),
            parent_id           INTEGER REFERENCES ItemInstances(id),   -- NULL for top level
            stack_size          INTEGER NOT NULL DEFAULT 0,             -- 2
            storage             INTEGER NOT NULL,                       -- 3
            main_position       INTEGER NOT NULL,                       -- 4
            sub_position        INTEGER NOT NULL,                       -- 5
            aug_position        INTEGER NOT NULL,                       -- 6
            remaining_charges   INTEGER NOT NULL DEFAULT 0,             -- 12
            is_attuned          INTEGER NOT NULL DEFAULT 0,             -- 13
            is_copied           INTEGER NOT NULL DEFAULT 0,             -- 26
            item_id             INTEGER NOT NULL REFERENCES ItemRecords(id)  -- 34
        );

        CREATE INDEX IF NOT EXISTS idx_iteminstances_character ON ItemInstances(character_id);

        CREATE UNIQUE INDEX IF NOT EXISTS idx_iteminstances_position
            ON ItemInstances(character_id, storage, main_position, sub_position, aug_position);
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