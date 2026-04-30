using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using System.IO;

namespace Glass.ClientUI;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// HotbuttonFileGenerator
//
// Generates the <Name>_<server>_<CLASS>.ini file for a character.
// Reads the existing file and preserves all sections except [Socials],
// [HotButtons2], [HotButtons3], [HotButtons4], and [KeyMaps], which are
// generated fresh based on the character's class.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class HotbuttonFileGenerator
{
    private const string Channel = "hkspam_00";
    private const string ColorGreen = "14";
    private const string ColorRed = "13";

    public static string GetClassAbbreviation(EQClass eqClass)
    {
        return ClassAbbreviations.TryGetValue(eqClass, out var abbrev) ? abbrev : "UNK";
    }

    private static readonly Dictionary<EQClass, string> ClassAbbreviations = new()
    {
        { EQClass.Bard,         "BRD" },
        { EQClass.Beastlord,    "BST" },
        { EQClass.Berserker,    "BER" },
        { EQClass.Cleric,       "CLR" },
        { EQClass.Druid,        "DRU" },
        { EQClass.Enchanter,    "ENC" },
        { EQClass.Magician,         "MAG" },
        { EQClass.Monk,         "MNK" },
        { EQClass.Necromancer,  "NEC" },
        { EQClass.Paladin,      "PAL" },
        { EQClass.Ranger,       "RNG" },
        { EQClass.Rogue,        "ROG" },
        { EQClass.Shadowknight, "SHD" },
        { EQClass.Shaman,       "SHM" },
        { EQClass.Warrior,      "WAR" },
        { EQClass.Wizard,       "WIZ" }
    };

    private readonly string _outputDirectory;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HotbuttonFileGenerator
    //
    // outputDirectory:  Directory to write generated files to
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public HotbuttonFileGenerator(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
        DebugLog.Write(LogChannel.Database, $"HotbuttonFileGenerator: outputDirectory='{outputDirectory}'.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // GetFileName
    //
    // Returns the hotbutton file name for the given character.
    //
    // character:  The character to get the file name for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static string GetFileName(Character character)
    {
        string classAbbrev = ClassAbbreviations.TryGetValue(character.Class, out var abbrev) ? abbrev : "UNK";
        return $"{character.Name}_{character.Server}_{classAbbrev}.ini";
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Generate
    //
    // Generates the hotbutton file for the given character.
    //
    // character:  The character to generate for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Generate(Character character)
    {
        DebugLog.Write(LogChannel.Database, $"HotbuttonFileGenerator.Generate: character='{character.Name}' class={character.Class}.");

        string fileName = GetFileName(character);
        string outputPath = Path.Combine(_outputDirectory, fileName);

        using var writer = new StreamWriter(outputPath);

        WriteSocials(writer, character);
        WriteHotButtons2(writer);
        WriteHotButtons3(writer);
        WriteHotButtons4(writer);
        WriteKeyMaps(writer);

        DebugLog.Write(LogChannel.Database, $"HotbuttonFileGenerator.Generate: written to '{outputPath}'.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteSocials
    //
    // Writes the [Socials] section with class-appropriate content.
    //
    // writer:     The stream writer to write to
    // character:  The character being generated for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteSocials(StreamWriter writer, Character character)
    {
        DebugLog.Write(LogChannel.Database, $"HotbuttonFileGenerator.WriteSocials: character='{character.Name}' class={character.Class}.");

        writer.WriteLine("[Socials]");

        // 6:1 Assist — always active
        writer.WriteLine("Page6Button1Name=Assist");
        writer.WriteLine($"Page6Button1Color={ColorGreen}");
        writer.WriteLine("Page6Button1Line1=/pause 4");
        writer.WriteLine($"Page6Button1Line2=/chat #{Channel} Assisting with %T");

        // 6:2 Nuke
        writer.WriteLine("Page6Button2Name=Nuke");
        if ((character.Class == EQClass.Magician) || (character.Class == EQClass.Enchanter))
        {
            writer.WriteLine($"Page6Button2Color={ColorGreen}");
            writer.WriteLine($"Page6Button2Line1=/chat #{Channel} Nuking %T");
            writer.WriteLine("Page6Button2Line2=/pause 45, /cast 2");
        }
        else
        {
            writer.WriteLine($"Page6Button2Color={ColorRed}");
        }

        // 6:3 DoT
        writer.WriteLine("Page6Button3Name=DoT");
        if ((character.Class == EQClass.Shaman) || (character.Class == EQClass.Druid) || (character.Class == EQClass.Enchanter))
        {
            writer.WriteLine($"Page6Button3Color={ColorGreen}");
            writer.WriteLine($"Page6Button3Line1=/chat #{Channel} Dotting %T");
        }
        else
        {
            writer.WriteLine($"Page6Button3Color={ColorRed}");
        }

        // 6:4 Slow
        writer.WriteLine("Page6Button4Name=Slow");
        if (character.Class == EQClass.Shaman)
        {
            writer.WriteLine($"Page6Button4Color={ColorGreen}");
            writer.WriteLine($"Page6Button4Line1=/chat #{Channel} Slowing %T");
        }
        else
        {
            writer.WriteLine($"Page6Button4Color={ColorRed}");
        }

        // 6:5 Debuff
        writer.WriteLine("Page6Button5Name=Debuff");
        if ((character.Class == EQClass.Magician) || (character.Class == EQClass.Enchanter))
        {
            writer.WriteLine($"Page6Button5Color={ColorGreen}");
            writer.WriteLine($"Page6Button5Line1=/chat #{Channel} Debuffing %T");
        }
        else
        {
            writer.WriteLine($"Page6Button5Color={ColorRed}");
        }

        // 6:6 Pets
        writer.WriteLine("Page6Button6Name=Pets");
        if ((character.Class == EQClass.Magician) || (character.Class == EQClass.Shaman) ||
            (character.Class == EQClass.Shadowknight) || (character.Class == EQClass.Beastlord))
        {
            writer.WriteLine($"Page6Button6Color={ColorGreen}");
            writer.WriteLine($"Page6Button6Line1=/chat #{Channel} Pets in on %T");
            writer.WriteLine("Page6Button6Line2=/pet attack");
        }
        else
        {
            writer.WriteLine($"Page6Button6Color={ColorRed}");
        }

        // 6:7 FHeal
        writer.WriteLine("Page6Button7Name=FHeal");
        if (character.Class == EQClass.Cleric)
        {
            writer.WriteLine($"Page6Button7Color={ColorGreen}");
            writer.WriteLine($"Page6Button7Line1=/chat #{Channel} Fast Heal on %T");
            writer.WriteLine("Page6Button7Line2=/pause 35, /cast 1");
        }
        else
        {
            writer.WriteLine($"Page6Button7Color={ColorRed}");
        }

        // 6:8 CHeal
        writer.WriteLine("Page6Button8Name=CHeal");
        if (character.Class == EQClass.Cleric)
        {
            writer.WriteLine($"Page6Button8Color={ColorGreen}");
            writer.WriteLine($"Page6Button8Line1=/chat #{Channel} Complete Heal on %T");
            writer.WriteLine("Page6Button8Line2=/cast 7");
        }
        else
        {
            writer.WriteLine($"Page6Button8Color={ColorRed}");
        }

        // 6:9 HOT
        writer.WriteLine("Page6Button9Name=HOT");
        if (character.Class == EQClass.Cleric)
        {
            writer.WriteLine($"Page6Button9Color={ColorGreen}");
            writer.WriteLine($"Page6Button9Line1=/chat #{Channel} HoT on %T");
            writer.WriteLine("Page6Button9Line2=/cast 2");
        }
        else
        {
            writer.WriteLine($"Page6Button9Color={ColorRed}");
        }

        // 6:10 GHoT
        writer.WriteLine("Page6Button10Name=GHoT");
        if (character.Class == EQClass.Cleric)
        {
            writer.WriteLine($"Page6Button10Color={ColorGreen}");
            writer.WriteLine($"Page6Button10Line1=/chat #{Channel} Group HoT on %T");
            writer.WriteLine("Page6Button10Line2=/cast 2");
        }
        else
        {
            writer.WriteLine($"Page6Button10Color={ColorRed}");
        }

        // 6:11 GrpHeal
        writer.WriteLine("Page6Button11Name=GrpHeal");
        if (character.Class == EQClass.Cleric)
        {
            writer.WriteLine($"Page6Button11Color={ColorGreen}");
            writer.WriteLine($"Page6Button11Line1=/chat #{Channel} Group Heal on %T");
            writer.WriteLine("Page6Button11Line2=/cast 6");
        }
        else
        {
            writer.WriteLine($"Page6Button11Color={ColorRed}");
        }

        // 7:1 ID — always active, character-specific
        writer.WriteLine("Page7Button1Name=ID");
        writer.WriteLine($"Page7Button1Color={ColorGreen}");
        writer.WriteLine($"Page7Button1Line1=/chat #{Channel} {character.Name} {character.Class}");

        // 7:2 Follow — always active
        writer.WriteLine("Page7Button2Name=Follow");
        writer.WriteLine($"Page7Button2Color={ColorGreen}");
        writer.WriteLine("Page7Button2Line1=/pause 2");
        writer.WriteLine($"Page7Button2Line2=/chat #{Channel} Following %T");
        writer.WriteLine("Page7Button2Line3=/follow");

        // 7:3 HCA — always active
        writer.WriteLine("Page7Button3Name=HCA");
        writer.WriteLine($"Page7Button3Color={ColorGreen}");
        writer.WriteLine("Page7Button3Line1=/hidecorpse allbutgroup");

        // 7:4 Stun
        writer.WriteLine("Page7Button4Name=Stun");
        if (character.Class == EQClass.Cleric)
        {
            writer.WriteLine($"Page7Button4Color={ColorGreen}");
            writer.WriteLine($"Page7Button4Line1=/chat #{Channel} Stunning %T");
            writer.WriteLine("Page7Button4Line2=/cast 3");
        }
        else
        {
            writer.WriteLine($"Page7Button4Color={ColorRed}");
        }

        // 7:5 Snare
        writer.WriteLine("Page7Button5Name=Snare");
        if ((character.Class == EQClass.Druid) || (character.Class == EQClass.Ranger))
        {
            writer.WriteLine($"Page7Button5Color={ColorGreen}");
            writer.WriteLine($"Page7Button5Line1=/chat #{Channel} Snaring %T");
            writer.WriteLine("Page7Button5Line2=/cast 7");
        }
        else
        {
            writer.WriteLine($"Page7Button5Color={ColorRed}");
        }

        // 7:6 Root
        writer.WriteLine("Page7Button6Name=Root");
        if ((character.Class == EQClass.Druid) || (character.Class == EQClass.Wizard) ||
            (character.Class == EQClass.Enchanter))
        {
            writer.WriteLine($"Page7Button6Color={ColorGreen}");
            writer.WriteLine($"Page7Button6Line1=/chat #{Channel} Rooting %T");
            writer.WriteLine("Page7Button6Line2=/cast 7");
        }
        else
        {
            writer.WriteLine($"Page7Button6Color={ColorRed}");
        }

        // 7:7 Mez
        writer.WriteLine("Page7Button7Name=Mez");
        if (character.Class == EQClass.Enchanter)
        {
            writer.WriteLine($"Page7Button7Color={ColorGreen}");
            writer.WriteLine($"Page7Button7Line1=/chat #{Channel} Mezzing %T");
            writer.WriteLine("Page7Button7Line2=/cast 1");
        }
        else
        {
            writer.WriteLine($"Page7Button7Color={ColorRed}");
        }

        // 7:8 D Shield
        writer.WriteLine("Page7Button8Name=D Shield");
        if ((character.Class == EQClass.Magician) || (character.Class == EQClass.Druid))
        {
            writer.WriteLine($"Page7Button8Color={ColorGreen}");
            writer.WriteLine($"Page7Button8Line1=/chat #{Channel} Damage Shield on %T");
            writer.WriteLine("Page7Button8Line2=/cast 7");
        }
        else
        {
            writer.WriteLine($"Page7Button8Color={ColorRed}");
        }

        // 7:9 HP Buff
        writer.WriteLine("Page7Button9Name=HP Buff");
        if ((character.Class == EQClass.Cleric) || (character.Class == EQClass.Shaman))
        {
            writer.WriteLine($"Page7Button9Color={ColorGreen}");
            writer.WriteLine($"Page7Button9Line1=/chat #{Channel} HP Buff on %T");
            writer.WriteLine("Page7Button9Line2=/cast 5");
        }
        else
        {
            writer.WriteLine($"Page7Button9Color={ColorRed}");
        }

        // 7:10 Haste
        writer.WriteLine("Page7Button10Name=Haste");
        if ((character.Class == EQClass.Enchanter) || (character.Class == EQClass.Shaman))
        {
            writer.WriteLine($"Page7Button10Color={ColorGreen}");
            writer.WriteLine($"Page7Button10Line1=/chat #{Channel} Haste on %T");
            writer.WriteLine("Page7Button10Line2=/cast 3");
        }
        else
        {
            writer.WriteLine($"Page7Button10Color={ColorRed}");
        }

        // 7:11 Burnout
        writer.WriteLine("Page7Button11Name=Burnout");
        if (character.Class == EQClass.Magician)
        {
            writer.WriteLine($"Page7Button11Color={ColorGreen}");
            writer.WriteLine($"Page7Button11Line1=/chat #{Channel} Burnout on Pet");
            writer.WriteLine("Page7Button11Line2=/cast 6");
        }
        else
        {
            writer.WriteLine($"Page7Button11Color={ColorRed}");
        }

        // 7:12 Dex
        writer.WriteLine("Page7Button12Name=Dex");
        if (character.Class == EQClass.Shaman)
        {
            writer.WriteLine($"Page7Button12Color={ColorGreen}");
            writer.WriteLine($"Page7Button12Line1=/chat #{Channel} Dexterity on %T");
            writer.WriteLine("Page7Button12Line2=/cast 7");
        }
        else
        {
            writer.WriteLine($"Page7Button12Color={ColorRed}");
        }

        // 8:1 Clarity
        writer.WriteLine("Page8Button1Name=Clarity");
        if (character.Class == EQClass.Enchanter)
        {
            writer.WriteLine($"Page8Button1Color={ColorGreen}");
            writer.WriteLine($"Page8Button1Line1=/chat #{Channel} Clarity on %T");
            writer.WriteLine("Page8Button1Line2=/cast 2");
        }
        else
        {
            writer.WriteLine($"Page8Button1Color={ColorRed}");
        }

        // 8:2 SoW
        writer.WriteLine("Page8Button2Name=SoW");
        if (character.Class == EQClass.Shaman)
        {
            writer.WriteLine($"Page8Button2Color={ColorGreen}");
            writer.WriteLine($"Page8Button2Line1=/chat #{Channel} Spirit of Wolf on %T");
            writer.WriteLine("Page8Button2Line2=/cast 7");
        }
        else
        {
            writer.WriteLine($"Page8Button2Color={ColorRed}");
        }

        // 8:3 SecTgt — always active, note XXX needs manual edit per character
        writer.WriteLine("Page8Button3Name=SecTgt");
        writer.WriteLine($"Page8Button3Color={ColorGreen}");
        writer.WriteLine($"Page8Button3Line1=/chat #{Channel} Secondary target XXX");
        writer.WriteLine("Page8Button3Line2=/tar XXX");

        // 8:4 Melody
        writer.WriteLine("Page8Button4Name=Melody");
        if (character.Class == EQClass.Bard)
        {
            writer.WriteLine($"Page8Button4Color={ColorGreen}");
            writer.WriteLine($"Page8Button4Line1=/chat #{Channel} Starting melody");
            writer.WriteLine("Page8Button4Line2=/melody 1 2 3 4");
        }
        else
        {
            writer.WriteLine($"Page8Button4Color={ColorRed}");
        }

        // 8:5 FFeed — always active
        writer.WriteLine("Page8Button5Name=FFeed");
        writer.WriteLine($"Page8Button5Color={ColorGreen}");
        writer.WriteLine("Page8Button5Line1=/useitem 25 0");
        writer.WriteLine("Page8Button5Line2=/useitem 25 4");

        // 8:6 ManaD
        writer.WriteLine("Page8Button6Name=ManaD");
        if ((character.Class == EQClass.Bard) || (character.Class == EQClass.Enchanter))
        {
            writer.WriteLine($"Page8Button6Color={ColorGreen}");
            writer.WriteLine($"Page8Button6Line1=/chat #{Channel} Unimplemented Draining mana");
            writer.WriteLine("Page8Button6Line2=");
        }
        else
        {
            writer.WriteLine($"Page8Button6Color={ColorRed}");
        }

        // 8:7 HarmT — note ISCreator wrote this twice (bug), we write SecMel only
        writer.WriteLine("Page8Button7Name=HarmT");
        if (character.Class == EQClass.Shadowknight)
        {
            writer.WriteLine($"Page8Button7Color={ColorGreen}");
            writer.WriteLine($"Page8Button7Line1=/chat #{Channel} Unimplemented Harmtouch");
            writer.WriteLine("Page8Button7Line2=");
        }
        else
        {
            writer.WriteLine($"Page8Button7Color={ColorRed}");
        }

        writer.WriteLine("Page8Button7Name=SecMel");
        if (character.Class == EQClass.Bard)
        {
            writer.WriteLine($"Page8Button7Color={ColorGreen}");
            writer.WriteLine($"Page8Button7Line1=/chat #{Channel} Secondary melody");
            writer.WriteLine("Page8Button7Line2=/melody 1 2 5 6");
        }
        else
        {
            writer.WriteLine($"Page8Button7Color={ColorRed}");
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteHotButtons2
    //
    // Writes the fixed [HotButtons2] section — social macro references for hotbar 2.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteHotButtons2(StreamWriter writer)
    {
        writer.WriteLine("[HotButtons2]");
        writer.WriteLine("Page1Button1=E60,@-1,0000000000000000,0,Assist,");
        writer.WriteLine("Page1Button2=E61,@-1,0000000000000000,0,Nuke,");
        writer.WriteLine("Page1Button3=E62,@-1,0000000000000000,0,Dot,");
        writer.WriteLine("Page1Button4=E63,@-1,0000000000000000,0,Slow,");
        writer.WriteLine("Page1Button5=E64,@-1,0000000000000000,0,GrpHeal,");
        writer.WriteLine("Page1Button6=E65,@-1,0000000000000000,0,Pets,");
        writer.WriteLine("Page1Button7=E66,@-1,0000000000000000,0,FastHeal,");
        writer.WriteLine("Page1Button8=E67,@-1,0000000000000000,0,C Heal,");
        writer.WriteLine("Page1Button9=E68,@-1,0000000000000000,0,S HoT,");
        writer.WriteLine("Page1Button10=E69,@-1,0000000000000000,0,G HoT,");
        writer.WriteLine("Page1Button11=E70,@-1,0000000000000000,0,GrpHeal,");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteHotButtons3
    //
    // Writes the fixed [HotButtons3] section — social macro references for hotbar 3.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteHotButtons3(StreamWriter writer)
    {
        writer.WriteLine("[HotButtons3]");
        writer.WriteLine("Page1Button1=E72,@-1,0000000000000000,0,ID,");
        writer.WriteLine("Page1Button2=E73,@-1,0000000000000000,0,Follow,");
        writer.WriteLine("Page1Button3=E74,@-1,0000000000000000,0,HCA,");
        writer.WriteLine("Page1Button4=E75,@-1,0000000000000000,0,Stun,");
        writer.WriteLine("Page1Button5=E76,@-1,0000000000000000,0,Snare,");
        writer.WriteLine("Page1Button6=E77,@-1,0000000000000000,0,Root,");
        writer.WriteLine("Page1Button7=E78,@-1,0000000000000000,0,Mez,");
        writer.WriteLine("Page1Button8=E79,@-1,0000000000000000,0,D Shield,");
        writer.WriteLine("Page1Button9=E80,@-1,0000000000000000,0,HPBuf,");
        writer.WriteLine("Page1Button10=E81,@-1,0000000000000000,0,Haste,");
        writer.WriteLine("Page1Button11=E82,@-1,0000000000000000,0,Burnout,");
        writer.WriteLine("Page1Button12=E83,@-1,0000000000000000,0,Dex,");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteHotButtons4
    //
    // Writes the fixed [HotButtons4] section — social macro references for hotbar 4.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteHotButtons4(StreamWriter writer)
    {
        writer.WriteLine("[HotButtons4]");
        writer.WriteLine("Page1Button1=E84,@-1,0000000000000000,0,Clarity,");
        writer.WriteLine("Page1Button2=E85,@-1,0000000000000000,0,SOW,");
        writer.WriteLine("Page1Button3=E86,@-1,0000000000000000,0,SecTgt");
        writer.WriteLine("Page1Button4=E87,@-1,0000000000000000,0,Melody,");
        writer.WriteLine("Page1Button5=E88,@-1,0000000000000000,0,FFeed,");
        writer.WriteLine("Page1Button6=E89,@-1,0000000000000000,0,ManaD,");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteKeyMaps
    //
    // Writes the fixed [KeyMaps] section with the standard hotbar key assignments.
    // Encoding: 0x60000000 | scan_code for Ctrl+Alt modifier set.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteKeyMaps(StreamWriter writer)
    {
        writer.WriteLine("[KeyMaps]");

        // Hotbar 2: Ctrl+Shift+A through Ctrl+Shift+L
        writer.WriteLine("KEYMAPPING_HOT2_1_1=1610612766");
        writer.WriteLine("KEYMAPPING_HOT2_2_1=1610612784");
        writer.WriteLine("KEYMAPPING_HOT2_3_1=1610612782");
        writer.WriteLine("KEYMAPPING_HOT2_4_1=1610612768");
        writer.WriteLine("KEYMAPPING_HOT2_5_1=1610612754");
        writer.WriteLine("KEYMAPPING_HOT2_6_1=1610612769");
        writer.WriteLine("KEYMAPPING_HOT2_7_1=1610612770");
        writer.WriteLine("KEYMAPPING_HOT2_8_1=1610612771");
        writer.WriteLine("KEYMAPPING_HOT2_9_1=1610612759");
        writer.WriteLine("KEYMAPPING_HOT2_10_1=1610612772");
        writer.WriteLine("KEYMAPPING_HOT2_11_1=1610612773");
        writer.WriteLine("KEYMAPPING_HOT2_12_1=1610612774");

        // Hotbar 3: Ctrl+Shift+M through Ctrl+Shift+X
        writer.WriteLine("KEYMAPPING_HOT3_1_1=1610612786");
        writer.WriteLine("KEYMAPPING_HOT3_2_1=1610612785");
        writer.WriteLine("KEYMAPPING_HOT3_3_1=1610612760");
        writer.WriteLine("KEYMAPPING_HOT3_4_1=1610612761");
        writer.WriteLine("KEYMAPPING_HOT3_5_1=1610612752");
        writer.WriteLine("KEYMAPPING_HOT3_6_1=1610612755");
        writer.WriteLine("KEYMAPPING_HOT3_7_1=1610612767");
        writer.WriteLine("KEYMAPPING_HOT3_8_1=1610612756");
        writer.WriteLine("KEYMAPPING_HOT3_9_1=1610612758");
        writer.WriteLine("KEYMAPPING_HOT3_10_1=1610612783");
        writer.WriteLine("KEYMAPPING_HOT3_11_1=1610612753");
        writer.WriteLine("KEYMAPPING_HOT3_12_1=1610612781");

        // Hotbar 4: Ctrl+Shift+Y, Z, F1-F10
        writer.WriteLine("KEYMAPPING_HOT4_1_1=1610612757");
        writer.WriteLine("KEYMAPPING_HOT4_2_1=1610612780");
        writer.WriteLine("KEYMAPPING_HOT4_3_1=1610612795");
        writer.WriteLine("KEYMAPPING_HOT4_4_1=1610612796");
        writer.WriteLine("KEYMAPPING_HOT4_5_1=1610612797");
        writer.WriteLine("KEYMAPPING_HOT4_6_1=1610612798");
        writer.WriteLine("KEYMAPPING_HOT4_7_1=1610612799");
        writer.WriteLine("KEYMAPPING_HOT4_8_1=1610612800");
        writer.WriteLine("KEYMAPPING_HOT4_9_1=1610612801");
        writer.WriteLine("KEYMAPPING_HOT4_10_1=1610612802");
        writer.WriteLine("KEYMAPPING_HOT4_11_1=1610612803");
        writer.WriteLine("KEYMAPPING_HOT4_12_1=1610612804");

        // Hotbar 5: Ctrl+Shift+F11, F12, numpad keys, arrows
        writer.WriteLine("KEYMAPPING_HOT5_1_1=1610612823");
        writer.WriteLine("KEYMAPPING_HOT5_2_1=1610612824");
        writer.WriteLine("KEYMAPPING_HOT5_3_1=1610612805");
        writer.WriteLine("KEYMAPPING_HOT5_4_1=1610612917");
        writer.WriteLine("KEYMAPPING_HOT5_5_1=1610612791");
        writer.WriteLine("KEYMAPPING_HOT5_6_1=1610612810");
        writer.WriteLine("KEYMAPPING_HOT5_7_1=1610612814");
        writer.WriteLine("KEYMAPPING_HOT5_8_1=1610612892");
        writer.WriteLine("KEYMAPPING_HOT5_9_1=1610612936");
        writer.WriteLine("KEYMAPPING_HOT5_10_1=1610612944");
        writer.WriteLine("KEYMAPPING_HOT5_11_1=1610612939");
        writer.WriteLine("KEYMAPPING_HOT5_12_1=1610612941");

        // Hotbar 6: Shift+Alt modifier set
        writer.WriteLine("KEYMAPPING_HOT6_1_1=1610612946");
        writer.WriteLine("KEYMAPPING_HOT6_2_1=1610612935");
        writer.WriteLine("KEYMAPPING_HOT6_3_1=1610612937");
        writer.WriteLine("KEYMAPPING_HOT6_4_1=1610612947");
        writer.WriteLine("KEYMAPPING_HOT6_5_1=1610612943");
        writer.WriteLine("KEYMAPPING_HOT6_6_1=1610612945");
        writer.WriteLine("KEYMAPPING_HOT6_7_1=1342177310");
        writer.WriteLine("KEYMAPPING_HOT6_8_1=1342177328");
        writer.WriteLine("KEYMAPPING_HOT6_9_1=1342177326");
        writer.WriteLine("KEYMAPPING_HOT6_10_1=1342177312");
        writer.WriteLine("KEYMAPPING_HOT6_11_1=1342177298");
        writer.WriteLine("KEYMAPPING_HOT6_12_1=1342177313");

        // Hotbar 7: Shift+Alt continued
        writer.WriteLine("KEYMAPPING_HOT7_1_1=1342177314");
        writer.WriteLine("KEYMAPPING_HOT7_2_1=1342177315");
        writer.WriteLine("KEYMAPPING_HOT7_3_1=1342177303");
        writer.WriteLine("KEYMAPPING_HOT7_4_1=1342177316");
        writer.WriteLine("KEYMAPPING_HOT7_5_1=1342177317");
        writer.WriteLine("KEYMAPPING_HOT7_6_1=1342177318");
        writer.WriteLine("KEYMAPPING_HOT7_7_1=1342177329");
        writer.WriteLine("KEYMAPPING_HOT7_8_1=1342177304");
        writer.WriteLine("KEYMAPPING_HOT7_9_1=1342177305");
        writer.WriteLine("KEYMAPPING_HOT7_10_1=1342177296");
        writer.WriteLine("KEYMAPPING_HOT7_11_1=1342177311");
        writer.WriteLine("KEYMAPPING_HOT7_12_1=1342177300");
    }
}
