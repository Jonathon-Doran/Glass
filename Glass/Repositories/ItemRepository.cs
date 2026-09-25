using Glass.Core.Logging;
using Glass.Data.Models;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Glass.Data.Repositories;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ItemRepository
//
// In-memory repository of ItemRecord definitions, keyed by item id.  One record exists per item id and
// is shared by every instance of that item on every character.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class ItemRepository
{
    private static ItemRepository? _instance = null;

    private readonly Dictionary<ItemId, ItemRecord> _recordsById;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Instance
    //
    // Lazy singleton accessor.  The instance is created on first access with an empty cache.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static ItemRepository Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new ItemRepository();
            }
            return _instance;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ItemRepository
    //
    // Private constructor.  Initializes an empty record cache.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private ItemRepository()
    {
        _recordsById = new Dictionary<ItemId, ItemRecord>();
        DebugLog.Write(LogChannel.Inventory, "ItemRepository: singleton instance created with empty cache.", LogLevel.Trace);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Add
    //
    // Stores an item definition keyed by its Id.  A record whose Id is None is rejected.  When a record
    // with the same Id is already cached, the cached record is kept and the given record is discarded.
    // Otherwise the record is written to the ItemRecords table, where an existing row with the same id
    // is kept, and the given record is cached either way.
    //
    // record:  The definition to store.  Its Id must exist.
    //
    // Returns true if the record was written to the table, false if it was rejected or a record with
    // its Id already existed in the cache or the table.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool Add(ItemRecord record)
    {
        if (record.Id.Exists == false)
        {
            DebugLog.Write(LogChannel.Inventory, "ItemRepository.Add: record '" + record.Name +
                "' has no Id; not stored.", LogLevel.Warn);
            return false;
        }

        if (_recordsById.ContainsKey(record.Id))
        {
            DebugLog.Write(LogChannel.Inventory, "ItemRepository.Add: '" + record.Name + "' (" + record.Id +
                ") already cached; existing record kept.", LogLevel.Trace);
            return false;
        }

        bool written = Insert(record);

        _recordsById[record.Id] = record;
        DebugLog.Write(LogChannel.Inventory, "ItemRepository.Add: cached '" + record.Name + "' (" + record.Id +
            "), written " + written + ", " + _recordsById.Count + " records cached.", LogLevel.Trace);
        return written;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TryGet
    //
    // Looks up the item definition stored under the given Id.  The cache is checked first; on a miss
    // the ItemRecords table is queried by id and a found row is cached before it is returned.
    //
    // itemId:  Id of the definition to look up.
    // record:  Receives the definition if found, null otherwise.
    //
    // Returns true if the definition was found, false otherwise.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool TryGet(ItemId itemId, [NotNullWhen(true)] out ItemRecord? record)
    {
        if (itemId.Exists == false)
        {
            record = null;
            DebugLog.Write(LogChannel.Inventory, "ItemRepository.TryGet: itemId is None.", LogLevel.Warn);
            return false;
        }

        if (_recordsById.TryGetValue(itemId, out record))
        {
            DebugLog.Write(LogChannel.Inventory, "ItemRepository.TryGet: found '" + record.Name + "' (" + itemId +
                ") in cache.", LogLevel.Trace);
            return true;
        }

        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ItemRecords WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", (uint)itemId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (reader.Read() == false)
        {
            record = null;
            DebugLog.Write(LogChannel.Inventory, "ItemRepository.TryGet: " + itemId + " not in cache or table.",
                LogLevel.Trace);
            return false;
        }

        record = ReadRecord(reader);
        _recordsById[itemId] = record;
        DebugLog.Write(LogChannel.Inventory, "ItemRepository.TryGet: loaded '" + record.Name + "' (" + itemId +
            ") from table, " + _recordsById.Count + " records cached.", LogLevel.Trace);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Insert
    //
    // Writes one item definition to the ItemRecords table.  An existing row with the same id is left
    // untouched and the given record is not written.  Columns are listed in wire order.
    //
    // record:  The definition to write.  Its Id must exist.
    //
    // Returns true if a row was written, false if a row with the record's id already existed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private bool Insert(ItemRecord record)
    {
        using SqliteConnection conn = Database.Instance.Connect();
        conn.Open();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO ItemRecords (
                id_string, container_type, unknown_7, unknown_8, unknown_9, unknown_10, unknown_11,
                unknown_14, unknown_15, unknown_16, unknown_17, is_evolving_item,
                unknown_20, unknown_21, unknown_22, unknown_23, unknown_24, unknown_25,
                unknown_27, unknown_28, item_type2, name, lore, it_file, unknown_33, id, weight,
                unknown_36, unknown_37, unknown_38, size, usable_slot_mask, cost, icon_id, unknown_44,
                is_tradeskill, save_cold, save_disease, save_poison, save_magic, save_fire, save_corruption,
                plus_strength, plus_stamina, plus_agility, plus_dexterity, plus_charisma, plus_intelligence,
                plus_wisdom, plus_hp, plus_mana, plus_endurance, plus_ac, hp_regen, mana_regen, unknown_66,
                class_mask, race_mask, unknown_69, skill_percent_chance, skill_max_change, skill_id,
                unknown_73, unknown_74, unknown_75, unknown_76, unknown_77, unknown_78,
                food_drink_value, required_level, recommended_level, bard_value, unknown_83, unknown_84,
                weapon_delay, unknown_86, unknown_87, weapon_range, weapon_base_damage, color, unknown_91,
                item_type1, material, unknown_94, unknown_95, unknown_96, unknown_97, unknown_98,
                unknown_99, unknown_100, unknown_101, unknown_102, unknown_103, unknown_104, unknown_105,
                unknown_107, unknown_108, unknown_109, unknown_110, unknown_111,
                bag_type, bag_slot_count, bag_size, bag_weight_reduction, unknown_116, unknown_117, unknown_118,
                lore_group, unknown_120, tribute, unknown_122, plus_attack, haste, unknown_125,
                aug_distiller_needed, unknown_127, unknown_128, unknown_129, unknown_130, max_stack_size,
                unknown_132, unknown_133, unknown_134, unknown_136, unknown_137, unknown_138, unknown_139,
                backstab_damage, heroic_strength, heroic_intelligence, heroic_wisdom, heroic_agility,
                heroic_dexterity, heroic_stamina, heroic_charisma, unknown_148, unknown_149,
                unknown_150, unknown_151, unknown_152, unknown_153, unknown_154, unknown_155, unknown_156,
                unknown_157, unknown_158, unknown_159, unknown_160, unknown_161, unknown_162, unknown_163,
                unknown_164, unknown_165, unknown_166, unknown_167, unknown_168, unknown_169,
                unknown_170, unknown_171, unknown_172, unknown_173,
                unknown_175, unknown_176, unknown_177, unknown_178, unknown_179,
                unknown_180, unknown_181, unknown_182, unknown_183, unknown_184, unknown_185,
                unknown_186, unknown_187, unknown_188, unknown_189, unknown_190, unknown_191,
                unknown_196, unknown_198, unknown_199
            ) VALUES (
                @id_string, @container_type, @unknown_7, @unknown_8, @unknown_9, @unknown_10, @unknown_11,
                @unknown_14, @unknown_15, @unknown_16, @unknown_17, @is_evolving_item,
                @unknown_20, @unknown_21, @unknown_22, @unknown_23, @unknown_24, @unknown_25,
                @unknown_27, @unknown_28, @item_type2, @name, @lore, @it_file, @unknown_33, @id, @weight,
                @unknown_36, @unknown_37, @unknown_38, @size, @usable_slot_mask, @cost, @icon_id, @unknown_44,
                @is_tradeskill, @save_cold, @save_disease, @save_poison, @save_magic, @save_fire, @save_corruption,
                @plus_strength, @plus_stamina, @plus_agility, @plus_dexterity, @plus_charisma, @plus_intelligence,
                @plus_wisdom, @plus_hp, @plus_mana, @plus_endurance, @plus_ac, @hp_regen, @mana_regen, @unknown_66,
                @class_mask, @race_mask, @unknown_69, @skill_percent_chance, @skill_max_change, @skill_id,
                @unknown_73, @unknown_74, @unknown_75, @unknown_76, @unknown_77, @unknown_78,
                @food_drink_value, @required_level, @recommended_level, @bard_value, @unknown_83, @unknown_84,
                @weapon_delay, @unknown_86, @unknown_87, @weapon_range, @weapon_base_damage, @color, @unknown_91,
                @item_type1, @material, @unknown_94, @unknown_95, @unknown_96, @unknown_97, @unknown_98,
                @unknown_99, @unknown_100, @unknown_101, @unknown_102, @unknown_103, @unknown_104, @unknown_105,
                @unknown_107, @unknown_108, @unknown_109, @unknown_110, @unknown_111,
                @bag_type, @bag_slot_count, @bag_size, @bag_weight_reduction, @unknown_116, @unknown_117, @unknown_118,
                @lore_group, @unknown_120, @tribute, @unknown_122, @plus_attack, @haste, @unknown_125,
                @aug_distiller_needed, @unknown_127, @unknown_128, @unknown_129, @unknown_130, @max_stack_size,
                @unknown_132, @unknown_133, @unknown_134, @unknown_136, @unknown_137, @unknown_138, @unknown_139,
                @backstab_damage, @heroic_strength, @heroic_intelligence, @heroic_wisdom, @heroic_agility,
                @heroic_dexterity, @heroic_stamina, @heroic_charisma, @unknown_148, @unknown_149,
                @unknown_150, @unknown_151, @unknown_152, @unknown_153, @unknown_154, @unknown_155, @unknown_156,
                @unknown_157, @unknown_158, @unknown_159, @unknown_160, @unknown_161, @unknown_162, @unknown_163,
                @unknown_164, @unknown_165, @unknown_166, @unknown_167, @unknown_168, @unknown_169,
                @unknown_170, @unknown_171, @unknown_172, @unknown_173,
                @unknown_175, @unknown_176, @unknown_177, @unknown_178, @unknown_179,
                @unknown_180, @unknown_181, @unknown_182, @unknown_183, @unknown_184, @unknown_185,
                @unknown_186, @unknown_187, @unknown_188, @unknown_189, @unknown_190, @unknown_191,
                @unknown_196, @unknown_198, @unknown_199
            )";

        cmd.Parameters.AddWithValue("@id_string", record.IdString);
        cmd.Parameters.AddWithValue("@container_type", (byte)record.ContainerType);
        cmd.Parameters.AddWithValue("@unknown_7", record.Unknown_7);
        cmd.Parameters.AddWithValue("@unknown_8", record.Unknown_8);
        cmd.Parameters.AddWithValue("@unknown_9", record.Unknown_9);
        cmd.Parameters.AddWithValue("@unknown_10", record.Unknown_10);
        cmd.Parameters.AddWithValue("@unknown_11", record.Unknown_11);
        cmd.Parameters.AddWithValue("@unknown_14", record.Unknown_14);
        cmd.Parameters.AddWithValue("@unknown_15", record.Unknown_15);
        cmd.Parameters.AddWithValue("@unknown_16", record.Unknown_16);
        cmd.Parameters.AddWithValue("@unknown_17", record.Unknown_17);
        cmd.Parameters.AddWithValue("@is_evolving_item", record.Is_Evolving_Item);
        cmd.Parameters.AddWithValue("@unknown_20", record.Unknown_20);
        cmd.Parameters.AddWithValue("@unknown_21", record.Unknown_21);
        cmd.Parameters.AddWithValue("@unknown_22", record.Unknown_22);
        cmd.Parameters.AddWithValue("@unknown_23", record.Unknown_23);
        cmd.Parameters.AddWithValue("@unknown_24", record.Unknown_24);
        cmd.Parameters.AddWithValue("@unknown_25", record.Unknown_25);
        cmd.Parameters.AddWithValue("@unknown_27", record.Unknown_27);
        cmd.Parameters.AddWithValue("@unknown_28", record.Unknown_28);
        cmd.Parameters.AddWithValue("@item_type2", record.ItemType2);
        cmd.Parameters.AddWithValue("@name", record.Name);
        cmd.Parameters.AddWithValue("@lore", record.Lore);
        cmd.Parameters.AddWithValue("@it_file", record.IT_File);
        cmd.Parameters.AddWithValue("@unknown_33", record.Unknown_33);
        cmd.Parameters.AddWithValue("@id", (uint)record.Id);
        cmd.Parameters.AddWithValue("@weight", record.Weight);
        cmd.Parameters.AddWithValue("@unknown_36", record.Unknown_36);
        cmd.Parameters.AddWithValue("@unknown_37", record.Tradeable);
        cmd.Parameters.AddWithValue("@unknown_38", record.Attuneable);
        cmd.Parameters.AddWithValue("@size", record.Size);
        cmd.Parameters.AddWithValue("@usable_slot_mask", record.UsableSlotMask);
        cmd.Parameters.AddWithValue("@cost", record.Cost);
        cmd.Parameters.AddWithValue("@icon_id", record.Icon_ID);
        cmd.Parameters.AddWithValue("@unknown_44", record.Unknown_44);
        cmd.Parameters.AddWithValue("@is_tradeskill", record.IsTradeskill);
        cmd.Parameters.AddWithValue("@save_cold", record.SaveCold);
        cmd.Parameters.AddWithValue("@save_disease", record.SaveDisease);
        cmd.Parameters.AddWithValue("@save_poison", record.SavePoison);
        cmd.Parameters.AddWithValue("@save_magic", record.SaveMagic);
        cmd.Parameters.AddWithValue("@save_fire", record.SaveFire);
        cmd.Parameters.AddWithValue("@save_corruption", record.SaveCorruption);
        cmd.Parameters.AddWithValue("@plus_strength", record.PlusStrength);
        cmd.Parameters.AddWithValue("@plus_stamina", record.PlusStamina);
        cmd.Parameters.AddWithValue("@plus_agility", record.PlusAgility);
        cmd.Parameters.AddWithValue("@plus_dexterity", record.PlusDexterity);
        cmd.Parameters.AddWithValue("@plus_charisma", record.PlusCharisma);
        cmd.Parameters.AddWithValue("@plus_intelligence", record.PlusIntelligence);
        cmd.Parameters.AddWithValue("@plus_wisdom", record.PlusWisdom);
        cmd.Parameters.AddWithValue("@plus_hp", record.PlusHP);
        cmd.Parameters.AddWithValue("@plus_mana", record.PlusMana);
        cmd.Parameters.AddWithValue("@plus_endurance", record.PlusEndurance);
        cmd.Parameters.AddWithValue("@plus_ac", record.PlusAC);
        cmd.Parameters.AddWithValue("@hp_regen", record.HpRegen);
        cmd.Parameters.AddWithValue("@mana_regen", record.ManaRegen);
        cmd.Parameters.AddWithValue("@unknown_66", record.Unknown_66);
        cmd.Parameters.AddWithValue("@class_mask", record.ClassMask);
        cmd.Parameters.AddWithValue("@race_mask", record.RaceMask);
        cmd.Parameters.AddWithValue("@unknown_69", record.Deity);
        cmd.Parameters.AddWithValue("@skill_percent_chance", record.Skill_Percent_Chance);
        cmd.Parameters.AddWithValue("@skill_max_change", record.Skill_Max_Change);
        cmd.Parameters.AddWithValue("@skill_id", record.Skill_ID);
        cmd.Parameters.AddWithValue("@unknown_73", record.Unknown_73);
        cmd.Parameters.AddWithValue("@unknown_74", record.Unknown_74);
        cmd.Parameters.AddWithValue("@unknown_75", record.Unknown_75);
        cmd.Parameters.AddWithValue("@unknown_76", record.Unknown_76);
        cmd.Parameters.AddWithValue("@unknown_77", record.Unknown_77);
        cmd.Parameters.AddWithValue("@unknown_78", record.Is_Magic);
        cmd.Parameters.AddWithValue("@food_drink_value", record.FoodDrinkValue);
        cmd.Parameters.AddWithValue("@required_level", record.RequiredLevel);
        cmd.Parameters.AddWithValue("@recommended_level", record.RecommendedLevel);
        cmd.Parameters.AddWithValue("@bard_value", record.Bard_Value);
        cmd.Parameters.AddWithValue("@unknown_83", record.Unknown_83);
        cmd.Parameters.AddWithValue("@unknown_84", record.Light);
        cmd.Parameters.AddWithValue("@weapon_delay", record.Weapon_Delay);
        cmd.Parameters.AddWithValue("@unknown_86", record.Elemental_Damage_Type);
        cmd.Parameters.AddWithValue("@unknown_87", record.Elemental_Damage_Amount);
        cmd.Parameters.AddWithValue("@weapon_range", record.Weapon_Range);
        cmd.Parameters.AddWithValue("@weapon_base_damage", record.Weapon_Base_Damage);
        cmd.Parameters.AddWithValue("@color", record.Color);
        cmd.Parameters.AddWithValue("@unknown_91", record.Prestige);
        cmd.Parameters.AddWithValue("@item_type1", record.ItemType1);
        cmd.Parameters.AddWithValue("@material", record.Material);
        cmd.Parameters.AddWithValue("@unknown_94", record.Unknown_94);
        cmd.Parameters.AddWithValue("@unknown_95", record.Unknown_95);
        cmd.Parameters.AddWithValue("@unknown_96", record.Unknown_96);
        cmd.Parameters.AddWithValue("@unknown_97", record.Material2);
        cmd.Parameters.AddWithValue("@unknown_98", record.Unknown_98);
        cmd.Parameters.AddWithValue("@unknown_99", record.Unknown_99);
        cmd.Parameters.AddWithValue("@unknown_100", record.Unknown_100);
        cmd.Parameters.AddWithValue("@unknown_101", record.CharmFileID);
        cmd.Parameters.AddWithValue("@unknown_102", record.CharmFile);
        cmd.Parameters.AddWithValue("@unknown_103", record.AugValue);
        cmd.Parameters.AddWithValue("@unknown_104", record.Unknown_104);
        cmd.Parameters.AddWithValue("@unknown_105", record.AugRestriction);
        cmd.Parameters.AddWithValue("@unknown_107", record.LDON_Sold);
        cmd.Parameters.AddWithValue("@unknown_108", record.LDON_Theme);
        cmd.Parameters.AddWithValue("@unknown_109", record.LDON_Price);
        cmd.Parameters.AddWithValue("@unknown_110", record.Unknown_110);
        cmd.Parameters.AddWithValue("@unknown_111", record.Unknown_111);
        cmd.Parameters.AddWithValue("@bag_type", record.Bag_Type);
        cmd.Parameters.AddWithValue("@bag_slot_count", record.Bag_Slot_Count);
        cmd.Parameters.AddWithValue("@bag_size", record.Bag_Size);
        cmd.Parameters.AddWithValue("@bag_weight_reduction", record.Bag_Weight_Reduction);
        cmd.Parameters.AddWithValue("@unknown_116", record.Unknown_116);
        cmd.Parameters.AddWithValue("@unknown_117", record.Unknown_117);
        cmd.Parameters.AddWithValue("@unknown_118", record.Unknown_118);
        cmd.Parameters.AddWithValue("@lore_group", record.LoreGroup);
        cmd.Parameters.AddWithValue("@unknown_120", record.Unknown_120);
        cmd.Parameters.AddWithValue("@tribute", record.Tribute);
        cmd.Parameters.AddWithValue("@unknown_122", record.FV_Nodrop);
        cmd.Parameters.AddWithValue("@plus_attack", record.PlusAttack);
        cmd.Parameters.AddWithValue("@haste", record.Haste);
        cmd.Parameters.AddWithValue("@unknown_125", record.Unknown_125);
        cmd.Parameters.AddWithValue("@aug_distiller_needed", record.AugDistillerNeeded);
        cmd.Parameters.AddWithValue("@unknown_127", record.Unknown_127);
        cmd.Parameters.AddWithValue("@unknown_128", record.Unknown_128);
        cmd.Parameters.AddWithValue("@unknown_129", record.Unknown_129);
        cmd.Parameters.AddWithValue("@unknown_130", record.Unknown_130);
        cmd.Parameters.AddWithValue("@max_stack_size", record.Max_Stack_Size);
        cmd.Parameters.AddWithValue("@unknown_132", record.Unknown_132);
        cmd.Parameters.AddWithValue("@unknown_133", record.Unknown_133);
        cmd.Parameters.AddWithValue("@unknown_134", record.Unknown_134);
        cmd.Parameters.AddWithValue("@unknown_136", record.Unknown_136);
        cmd.Parameters.AddWithValue("@unknown_137", record.Unknown_137);
        cmd.Parameters.AddWithValue("@unknown_138", record.Unknown_138);
        cmd.Parameters.AddWithValue("@unknown_139", record.Purity);
        cmd.Parameters.AddWithValue("@backstab_damage", record.Backstab_Damage);
        cmd.Parameters.AddWithValue("@heroic_strength", record.Heroic_Strength);
        cmd.Parameters.AddWithValue("@heroic_intelligence", record.Heroic_Intelligence);
        cmd.Parameters.AddWithValue("@heroic_wisdom", record.Heroic_Wisdom);
        cmd.Parameters.AddWithValue("@heroic_agility", record.Heroic_Agility);
        cmd.Parameters.AddWithValue("@heroic_dexterity", record.Heroic_Dexterity);
        cmd.Parameters.AddWithValue("@heroic_stamina", record.Heroic_Stamina);
        cmd.Parameters.AddWithValue("@heroic_charisma", record.Heroic_Charisma);
        cmd.Parameters.AddWithValue("@unknown_148", record.Heal_Amount);
        cmd.Parameters.AddWithValue("@unknown_149", record.Spell_Damage);
        cmd.Parameters.AddWithValue("@unknown_150", record.Clairvoyance);
        cmd.Parameters.AddWithValue("@unknown_151", record.Unknown_151);
        cmd.Parameters.AddWithValue("@unknown_152", record.Unknown_152);
        cmd.Parameters.AddWithValue("@unknown_153", record.Unknown_153);
        cmd.Parameters.AddWithValue("@unknown_154", record.Unknown_154);
        cmd.Parameters.AddWithValue("@unknown_155", record.Placeable2);
        cmd.Parameters.AddWithValue("@unknown_156", record.Unknown_156);
        cmd.Parameters.AddWithValue("@unknown_157", record.Unknown_157);
        cmd.Parameters.AddWithValue("@unknown_158", record.Unknown_158);
        cmd.Parameters.AddWithValue("@unknown_159", record.Unknown_159);
        cmd.Parameters.AddWithValue("@unknown_160", record.Unknown_160);
        cmd.Parameters.AddWithValue("@unknown_161", record.Unknown_161);
        cmd.Parameters.AddWithValue("@unknown_162", record.Unknown_162);
        cmd.Parameters.AddWithValue("@unknown_163", record.Unknown_163);
        cmd.Parameters.AddWithValue("@unknown_164", record.Unknown_164);
        cmd.Parameters.AddWithValue("@unknown_165", record.Unknown_165);
        cmd.Parameters.AddWithValue("@unknown_166", record.Unknown_166);
        cmd.Parameters.AddWithValue("@unknown_167", record.Unknown_167);
        cmd.Parameters.AddWithValue("@unknown_168", record.Unknown_168);
        cmd.Parameters.AddWithValue("@unknown_169", record.Unknown_169);
        cmd.Parameters.AddWithValue("@unknown_170", record.Unknown_170);
        cmd.Parameters.AddWithValue("@unknown_171", record.Unknown_171);
        cmd.Parameters.AddWithValue("@unknown_172", record.Unknown_172);
        cmd.Parameters.AddWithValue("@unknown_173", record.Unknown_173);
        cmd.Parameters.AddWithValue("@unknown_175", record.Unknown_175);
        cmd.Parameters.AddWithValue("@unknown_176", record.Unknown_176);
        cmd.Parameters.AddWithValue("@unknown_177", record.Unknown_177);
        cmd.Parameters.AddWithValue("@unknown_178", record.Unknown_178);
        cmd.Parameters.AddWithValue("@unknown_179", record.Unknown_179);
        cmd.Parameters.AddWithValue("@unknown_180", record.Unknown_180);
        cmd.Parameters.AddWithValue("@unknown_181", record.Unknown_181);
        cmd.Parameters.AddWithValue("@unknown_182", record.Unknown_182);
        cmd.Parameters.AddWithValue("@unknown_183", record.Unknown_183);
        cmd.Parameters.AddWithValue("@unknown_184", record.Unknown_184);
        cmd.Parameters.AddWithValue("@unknown_185", record.Unknown_185);
        cmd.Parameters.AddWithValue("@unknown_186", record.Unknown_186);
        cmd.Parameters.AddWithValue("@unknown_187", record.Unknown_187);
        cmd.Parameters.AddWithValue("@unknown_188", record.Unknown_188);
        cmd.Parameters.AddWithValue("@unknown_189", record.Unknown_189);
        cmd.Parameters.AddWithValue("@unknown_190", record.Unknown_190);
        cmd.Parameters.AddWithValue("@unknown_191", record.Unknown_191);
        cmd.Parameters.AddWithValue("@unknown_196", record.Unknown_196);
        cmd.Parameters.AddWithValue("@unknown_198", record.Unknown_198);
        cmd.Parameters.AddWithValue("@unknown_199", record.Unknown_199);

        int rowsWritten = cmd.ExecuteNonQuery();
        if (rowsWritten == 0)
        {
            DebugLog.Write(LogChannel.Database, "ItemRepository.Insert: row for '" + record.Name + "' (" +
                record.Id + ") already exists; not written.", LogLevel.Trace);
            return false;
        }

        DebugLog.Write(LogChannel.Database, "ItemRepository.Insert: wrote '" + record.Name + "' (" +
            record.Id + ").", LogLevel.Trace);
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ReadRecord
    //
    // Builds an item definition from the current row of a reader positioned on an ItemRecords row that
    // selected every column.  Columns are read by name.  The effects list is left at its default.
    //
    // reader:  A reader positioned on the row to read.
    //
    // Returns the filled definition.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static ItemRecord ReadRecord(SqliteDataReader reader)
    {
        ItemRecord record = new ItemRecord();

        record.IdString = reader.GetFieldValue<string>(reader.GetOrdinal("id_string"));
        record.ContainerType = (ContainerType)reader.GetFieldValue<byte>(reader.GetOrdinal("container_type"));
        record.Unknown_7 = reader.GetFieldValue<ulong>(reader.GetOrdinal("unknown_7"));
        record.Unknown_8 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_8"));
        record.Unknown_9 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_9"));
        record.Unknown_10 = reader.GetFieldValue<ulong>(reader.GetOrdinal("unknown_10"));
        record.Unknown_11 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_11"));
        record.Unknown_14 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_14"));
        record.Unknown_15 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_15"));
        record.Unknown_16 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_16"));
        record.Unknown_17 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_17"));
        record.Is_Evolving_Item = reader.GetFieldValue<bool>(reader.GetOrdinal("is_evolving_item"));
        record.Unknown_20 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_20"));
        record.Unknown_21 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_21"));
        record.Unknown_22 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_22"));
        record.Unknown_23 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_23"));
        record.Unknown_24 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_24"));
        record.Unknown_25 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_25"));
        record.Unknown_27 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_27"));
        record.Unknown_28 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_28"));
        record.ItemType2 = reader.GetFieldValue<byte>(reader.GetOrdinal("item_type2"));
        record.Name = reader.GetFieldValue<string>(reader.GetOrdinal("name"));
        record.Lore = reader.GetFieldValue<string>(reader.GetOrdinal("lore"));
        record.IT_File = reader.GetFieldValue<uint>(reader.GetOrdinal("it_file"));
        record.Unknown_33 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_33"));
        record.Id = (ItemId)reader.GetFieldValue<uint>(reader.GetOrdinal("id"));
        record.Weight = reader.GetFieldValue<float>(reader.GetOrdinal("weight"));
        record.Unknown_36 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_36"));
        record.Tradeable = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_37"));
        record.Attuneable = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_38"));
        record.Size = reader.GetFieldValue<byte>(reader.GetOrdinal("size"));
        record.UsableSlotMask = reader.GetFieldValue<uint>(reader.GetOrdinal("usable_slot_mask"));
        record.Cost = reader.GetFieldValue<uint>(reader.GetOrdinal("cost"));
        record.Icon_ID = reader.GetFieldValue<uint>(reader.GetOrdinal("icon_id"));
        record.Unknown_44 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_44"));
        record.IsTradeskill = reader.GetFieldValue<bool>(reader.GetOrdinal("is_tradeskill"));
        record.SaveCold = reader.GetFieldValue<byte>(reader.GetOrdinal("save_cold"));
        record.SaveDisease = reader.GetFieldValue<byte>(reader.GetOrdinal("save_disease"));
        record.SavePoison = reader.GetFieldValue<byte>(reader.GetOrdinal("save_poison"));
        record.SaveMagic = reader.GetFieldValue<byte>(reader.GetOrdinal("save_magic"));
        record.SaveFire = reader.GetFieldValue<byte>(reader.GetOrdinal("save_fire"));
        record.SaveCorruption = reader.GetFieldValue<byte>(reader.GetOrdinal("save_corruption"));
        record.PlusStrength = reader.GetFieldValue<sbyte>(reader.GetOrdinal("plus_strength"));
        record.PlusStamina = reader.GetFieldValue<sbyte>(reader.GetOrdinal("plus_stamina"));
        record.PlusAgility = reader.GetFieldValue<sbyte>(reader.GetOrdinal("plus_agility"));
        record.PlusDexterity = reader.GetFieldValue<sbyte>(reader.GetOrdinal("plus_dexterity"));
        record.PlusCharisma = reader.GetFieldValue<sbyte>(reader.GetOrdinal("plus_charisma"));
        record.PlusIntelligence = reader.GetFieldValue<sbyte>(reader.GetOrdinal("plus_intelligence"));
        record.PlusWisdom = reader.GetFieldValue<sbyte>(reader.GetOrdinal("plus_wisdom"));
        record.PlusHP = reader.GetFieldValue<int>(reader.GetOrdinal("plus_hp"));
        record.PlusMana = reader.GetFieldValue<int>(reader.GetOrdinal("plus_mana"));
        record.PlusEndurance = reader.GetFieldValue<int>(reader.GetOrdinal("plus_endurance"));
        record.PlusAC = reader.GetFieldValue<int>(reader.GetOrdinal("plus_ac"));
        record.HpRegen = reader.GetFieldValue<int>(reader.GetOrdinal("hp_regen"));
        record.ManaRegen = reader.GetFieldValue<int>(reader.GetOrdinal("mana_regen"));
        record.Unknown_66 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_66"));
        record.ClassMask = reader.GetFieldValue<uint>(reader.GetOrdinal("class_mask"));
        record.RaceMask = reader.GetFieldValue<uint>(reader.GetOrdinal("race_mask"));
        record.Deity = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_69"));
        record.Skill_Percent_Chance = reader.GetFieldValue<uint>(reader.GetOrdinal("skill_percent_chance"));
        record.Skill_Max_Change = reader.GetFieldValue<uint>(reader.GetOrdinal("skill_max_change"));
        record.Skill_ID = reader.GetFieldValue<uint>(reader.GetOrdinal("skill_id"));
        record.Unknown_73 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_73"));
        record.Unknown_74 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_74"));
        record.Unknown_75 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_75"));
        record.Unknown_76 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_76"));
        record.Unknown_77 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_77"));
        record.Is_Magic = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_78"));
        record.FoodDrinkValue = reader.GetFieldValue<uint>(reader.GetOrdinal("food_drink_value"));
        record.RequiredLevel = reader.GetFieldValue<uint>(reader.GetOrdinal("required_level"));
        record.RecommendedLevel = reader.GetFieldValue<uint>(reader.GetOrdinal("recommended_level"));
        record.Bard_Value = reader.GetFieldValue<uint>(reader.GetOrdinal("bard_value"));
        record.Unknown_83 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_83"));
        record.Light = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_84"));
        record.Weapon_Delay = reader.GetFieldValue<byte>(reader.GetOrdinal("weapon_delay"));
        record.Elemental_Damage_Type = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_86"));
        record.Elemental_Damage_Amount = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_87"));
        record.Weapon_Range = reader.GetFieldValue<byte>(reader.GetOrdinal("weapon_range"));
        record.Weapon_Base_Damage = reader.GetFieldValue<uint>(reader.GetOrdinal("weapon_base_damage"));
        record.Color = reader.GetFieldValue<uint>(reader.GetOrdinal("color"));
        record.Prestige = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_91"));
        record.ItemType1 = reader.GetFieldValue<byte>(reader.GetOrdinal("item_type1"));
        record.Material = reader.GetFieldValue<uint>(reader.GetOrdinal("material"));
        record.Unknown_94 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_94"));
        record.Unknown_95 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_95"));
        record.Unknown_96 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_96"));
        record.Material2 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_97"));
        record.Unknown_98 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_98"));
        record.Unknown_99 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_99"));
        record.Unknown_100 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_100"));
        record.CharmFileID = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_101"));
        record.CharmFile = reader.GetFieldValue<string>(reader.GetOrdinal("unknown_102"));
        record.AugValue = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_103"));
        record.Unknown_104 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_104"));
        record.AugRestriction = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_105"));
        record.LDON_Sold = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_107"));
        record.LDON_Theme = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_108"));
        record.LDON_Price = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_109"));
        record.Unknown_110 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_110"));
        record.Unknown_111 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_111"));
        record.Bag_Type = reader.GetFieldValue<byte>(reader.GetOrdinal("bag_type"));
        record.Bag_Slot_Count = reader.GetFieldValue<byte>(reader.GetOrdinal("bag_slot_count"));
        record.Bag_Size = reader.GetFieldValue<byte>(reader.GetOrdinal("bag_size"));
        record.Bag_Weight_Reduction = reader.GetFieldValue<byte>(reader.GetOrdinal("bag_weight_reduction"));
        record.Unknown_116 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_116"));
        record.Unknown_117 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_117"));
        record.Unknown_118 = reader.GetFieldValue<string>(reader.GetOrdinal("unknown_118"));
        record.LoreGroup = reader.GetFieldValue<uint>(reader.GetOrdinal("lore_group"));
        record.Unknown_120 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_120"));
        record.Tribute = reader.GetFieldValue<uint>(reader.GetOrdinal("tribute"));
        record.FV_Nodrop = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_122"));
        record.PlusAttack = reader.GetFieldValue<int>(reader.GetOrdinal("plus_attack"));
        record.Haste = reader.GetFieldValue<uint>(reader.GetOrdinal("haste"));
        record.Unknown_125 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_125"));
        record.AugDistillerNeeded = reader.GetFieldValue<uint>(reader.GetOrdinal("aug_distiller_needed"));
        record.Unknown_127 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_127"));
        record.Unknown_128 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_128"));
        record.Unknown_129 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_129"));
        record.Unknown_130 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_130"));
        record.Max_Stack_Size = reader.GetFieldValue<uint>(reader.GetOrdinal("max_stack_size"));
        record.Unknown_132 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_132"));
        record.Unknown_133 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_133"));
        record.Unknown_134 = reader.GetFieldValue<byte[]>(reader.GetOrdinal("unknown_134"));
        record.Unknown_136 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_136"));
        record.Unknown_137 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_137"));
        record.Unknown_138 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_138"));
        record.Purity = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_139"));
        record.Backstab_Damage = reader.GetFieldValue<uint>(reader.GetOrdinal("backstab_damage"));
        record.Heroic_Strength = reader.GetFieldValue<uint>(reader.GetOrdinal("heroic_strength"));
        record.Heroic_Intelligence = reader.GetFieldValue<uint>(reader.GetOrdinal("heroic_intelligence"));
        record.Heroic_Wisdom = reader.GetFieldValue<uint>(reader.GetOrdinal("heroic_wisdom"));
        record.Heroic_Agility = reader.GetFieldValue<uint>(reader.GetOrdinal("heroic_agility"));
        record.Heroic_Dexterity = reader.GetFieldValue<uint>(reader.GetOrdinal("heroic_dexterity"));
        record.Heroic_Stamina = reader.GetFieldValue<uint>(reader.GetOrdinal("heroic_stamina"));
        record.Heroic_Charisma = reader.GetFieldValue<uint>(reader.GetOrdinal("heroic_charisma"));
        record.Heal_Amount = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_148"));
        record.Spell_Damage = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_149"));
        record.Clairvoyance = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_150"));
        record.Unknown_151 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_151"));
        record.Unknown_152 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_152"));
        record.Unknown_153 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_153"));
        record.Unknown_154 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_154"));
        record.Placeable2 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_155"));
        record.Unknown_156 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_156"));
        record.Unknown_157 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_157"));
        record.Unknown_158 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_158"));
        record.Unknown_159 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_159"));
        record.Unknown_160 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_160"));
        record.Unknown_161 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_161"));
        record.Unknown_162 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_162"));
        record.Unknown_163 = reader.GetFieldValue<string>(reader.GetOrdinal("unknown_163"));
        record.Unknown_164 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_164"));
        record.Unknown_165 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_165"));
        record.Unknown_166 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_166"));
        record.Unknown_167 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_167"));
        record.Unknown_168 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_168"));
        record.Unknown_169 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_169"));
        record.Unknown_170 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_170"));
        record.Unknown_171 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_171"));
        record.Unknown_172 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_172"));
        record.Unknown_173 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_173"));
        record.Unknown_175 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_175"));
        record.Unknown_176 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_176"));
        record.Unknown_177 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_177"));
        record.Unknown_178 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_178"));
        record.Unknown_179 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_179"));
        record.Unknown_180 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_180"));
        record.Unknown_181 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_181"));
        record.Unknown_182 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_182"));
        record.Unknown_183 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_183"));
        record.Unknown_184 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_184"));
        record.Unknown_185 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_185"));
        record.Unknown_186 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_186"));
        record.Unknown_187 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_187"));
        record.Unknown_188 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_188"));
        record.Unknown_189 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_189"));
        record.Unknown_190 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_190"));
        record.Unknown_191 = reader.GetFieldValue<string>(reader.GetOrdinal("unknown_191"));
        record.Unknown_196 = reader.GetFieldValue<byte>(reader.GetOrdinal("unknown_196"));
        record.Unknown_198 = reader.GetFieldValue<ulong>(reader.GetOrdinal("unknown_198"));
        record.Unknown_199 = reader.GetFieldValue<uint>(reader.GetOrdinal("unknown_199"));

        DebugLog.Write(LogChannel.Database, "ItemRepository.ReadRecord: read '" + record.Name + "' (" +
            record.Id + ").", LogLevel.Trace);

        return record;
    }
}