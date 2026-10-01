using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using System.IO;

namespace Glass.ClientUI;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// EqClientFileGenerator
//
// Generates the eqclient-<Name>-<Server>.ini file for a character.
// Writes [VideoMode] with dimensions from the first monitor in the database.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class EqClientFileGenerator
{
    private const int TaskbarHeight = 30;

    private readonly string _outputDirectory;
    private readonly int _monitorWidth;
    private readonly int _monitorHeight;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // EqClientFileGenerator
    //
    // outputDirectory:  Directory to write generated files to
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public EqClientFileGenerator(string outputDirectory)
    {
        _outputDirectory = outputDirectory;

        DebugLog.Write(LogChannel.Database, "EqClientFileGenerator: loading monitor dimensions.");

        var monitorRepo = new MonitorGateway();
        var monitor = monitorRepo.GetFirstMonitor();

        if (monitor.HasValue)
        {
            _monitorWidth = monitor.Value.Width;
            _monitorHeight = monitor.Value.Height;
            DebugLog.Write(LogChannel.Database, $"EqClientFileGenerator: monitor {_monitorWidth}x{_monitorHeight}.");
        }
        else
        {
            _monitorWidth = 1920;
            _monitorHeight = 1080;
            DebugLog.Write(LogChannel.Database, $"EqClientFileGenerator: no monitor found, defaulting to {_monitorWidth}x{_monitorHeight}.");
        }
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Generate
    //
    // Generates the eqclient file for the given character.
    // All content is hardcoded from a fresh character baseline.
    // Only [VideoMode] is calculated — everything else is standard.
    //
    // character:  The character to generate for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void Generate(Character character)
    {
        DebugLog.Write(LogChannel.Database, $"EqClientFileGenerator.Generate: character='{character.Name}' server='{character.Server}'.");

        string fileName = $"eqclient-{character.Name}-{character.Server}.ini";
        string outputPath = Path.Combine(_outputDirectory, fileName);

        using var writer = new StreamWriter(outputPath);

        WriteDefaults(writer);
        WriteVideoMode(writer);
        WriteOptions(writer);
        WriteTextColors(writer);
        WriteBristlebane(writer);
        WriteNews(writer);

        DebugLog.Write(LogChannel.Database, $"EqClientFileGenerator.Generate: written to '{outputPath}'.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteDefaults
    //
    // Writes the [Defaults] section from the fresh character baseline.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteDefaults(StreamWriter writer)
    {
        writer.WriteLine("[Defaults]");
        writer.WriteLine("UseWASDDefault=1");
        writer.WriteLine("GraphicsMemoryModeSwitch=1");
        writer.WriteLine("APVOptimizations=TRUE");
        writer.WriteLine("Sound=1");
        writer.WriteLine("TextureQuality=1");
        writer.WriteLine("VertexShaders=TRUE");
        writer.WriteLine("20PixelShaders=TRUE");
        writer.WriteLine("MultiPassLighting=FALSE");
        writer.WriteLine("PostEffects=FALSE");
        writer.WriteLine("UseLitBatches=FALSE");
        writer.WriteLine("ItemPlacementShowOverlay=TRUE");
        writer.WriteLine("WindowedModeXOffset=-8");
        writer.WriteLine("WindowedModeYOffset=-45");
        writer.WriteLine("AllowResize=1");
        writer.WriteLine("Maximized=0");
        writer.WriteLine("AlwaysOnTop=0");
        writer.WriteLine("ChatFontSize=3");
        writer.WriteLine("ShowNamesLevel=4");
        writer.WriteLine("MousePointerSpeedMod=0");
        writer.WriteLine("ShowSpellEffects=1");
        writer.WriteLine("MixAhead=8");
        writer.WriteLine("TrackPlayers=1");
        writer.WriteLine("TrackSortType=NORMAL");
        writer.WriteLine("TrackFilterType=0");
        writer.WriteLine("Sound44k=0");
        writer.WriteLine("HidePlayers=0");
        writer.WriteLine("HidePets=0");
        writer.WriteLine("HideFamiliars=0");
        writer.WriteLine("HideMercs=0");
        writer.WriteLine("AllLuclinPcModelsOff=0");
        writer.WriteLine("UseLuclinHumanMale=1");
        writer.WriteLine("UseLuclinHumanFemale=1");
        writer.WriteLine("UseLuclinBarbarianMale=1");
        writer.WriteLine("UseLuclinBarbarianFemale=1");
        writer.WriteLine("UseLuclinEruditeMale=1");
        writer.WriteLine("UseLuclinEruditeFemale=1");
        writer.WriteLine("UseLuclinWoodElfMale=1");
        writer.WriteLine("UseLuclinWoodElfFemale=1");
        writer.WriteLine("UseLuclinHighElfMale=1");
        writer.WriteLine("UseLuclinHighElfFemale=1");
        writer.WriteLine("UseLuclinDarkElfMale=1");
        writer.WriteLine("UseLuclinDarkElfFemale=1");
        writer.WriteLine("UseLuclinHalfElfMale=1");
        writer.WriteLine("UseLuclinHalfElfFemale=1");
        writer.WriteLine("UseLuclinDwarfMale=1");
        writer.WriteLine("UseLuclinDwarfFemale=1");
        writer.WriteLine("UseLuclinTrollMale=1");
        writer.WriteLine("UseLuclinTrollFemale=1");
        writer.WriteLine("UseLuclinOgreMale=1");
        writer.WriteLine("UseLuclinOgreFemale=1");
        writer.WriteLine("UseLuclinHalflingMale=1");
        writer.WriteLine("UseLuclinHalflingFemale=1");
        writer.WriteLine("UseLuclinGnomeMale=1");
        writer.WriteLine("UseLuclinGnomeFemale=1");
        writer.WriteLine("UseLuclinIksarMale=1");
        writer.WriteLine("UseLuclinIksarFemale=1");
        writer.WriteLine("UseLuclinVahShirMale=1");
        writer.WriteLine("UseLuclinVahShirFemale=1");
        writer.WriteLine("DefaultChannel=8");
        writer.WriteLine("Music=0");
        writer.WriteLine("SoundVolume=0");
        writer.WriteLine("BrightnessBias=0.000000");
        writer.WriteLine("SpellParticleOpacity=1.000000");
        writer.WriteLine("EnvironmentParticleOpacity=1.000000");
        writer.WriteLine("ActorParticleOpacity=1.000000");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteVideoMode
    //
    // Writes the [VideoMode] section with dimensions from the first monitor in the database.
    // Windowed height leaves room for the taskbar.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteVideoMode(StreamWriter writer)
    {
        int windowedHeight = _monitorHeight - TaskbarHeight;

        writer.WriteLine("[VideoMode]");
        writer.WriteLine($"Width={_monitorWidth}");
        writer.WriteLine($"Height={_monitorHeight}");
        writer.WriteLine($"WindowedWidth={_monitorWidth}");
        writer.WriteLine($"WindowedHeight={windowedHeight}");
        writer.WriteLine("FullscreenBitsPerPixel=32");
        writer.WriteLine("FullscreenRefreshRate=59");
        writer.WriteLine("Borderless=1");
        writer.WriteLine("Fullscreen=0");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteOptions
    //
    // Writes the [Options] section from the fresh character baseline.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteOptions(StreamWriter writer)
    {
        writer.WriteLine("[Options]");
        writer.WriteLine("ClickThroughMask=0");
        writer.WriteLine("Camera1-Distance=30.000000");
        writer.WriteLine("Camera1-DirHeading=192.000000");
        writer.WriteLine("Camera1-Heading=0.000000");
        writer.WriteLine("Camera1-Pitch=0.000000");
        writer.WriteLine("Camera1-Height=5.000000");
        writer.WriteLine("Camera1-Zoom=90.000000");
        writer.WriteLine("Camera1-Change=1");
        writer.WriteLine("Camera2-Distance=82.000000");
        writer.WriteLine("Camera2-DirHeading=277.000000");
        writer.WriteLine("Camera2-Heading=0.000000");
        writer.WriteLine("Camera2-Pitch=0.000000");
        writer.WriteLine("Camera2-Height=18.000000");
        writer.WriteLine("Camera2-Zoom=90.000000");
        writer.WriteLine("Camera2-Change=1");
        writer.WriteLine("MaxFPS=150");
        writer.WriteLine("MaxBGFPS=9");
        writer.WriteLine("NameFlashSpeed=5");
        writer.WriteLine("Realism=1");
        writer.WriteLine("ClipPlane=15");
        writer.WriteLine("LODBias=10");
        writer.WriteLine("XMouseSensitivity=4");
        writer.WriteLine("YMouseSensitivity=4");
        writer.WriteLine("SavePersonaChat=0");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteTextColors
    //
    // Writes the [TextColors] section from the fresh character baseline.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteTextColors(StreamWriter writer)
    {
        writer.WriteLine("[TextColors]");
        writer.WriteLine("User_1_Red=255");
        writer.WriteLine("User_1_Green=255");
        writer.WriteLine("User_1_Blue=255");
        writer.WriteLine("User_2_Red=190");
        writer.WriteLine("User_2_Green=40");
        writer.WriteLine("User_2_Blue=190");
        writer.WriteLine("User_3_Red=0");
        writer.WriteLine("User_3_Green=255");
        writer.WriteLine("User_3_Blue=255");
        writer.WriteLine("User_4_Red=40");
        writer.WriteLine("User_4_Green=240");
        writer.WriteLine("User_4_Blue=40");
        writer.WriteLine("User_5_Red=0");
        writer.WriteLine("User_5_Green=128");
        writer.WriteLine("User_5_Blue=0");
        writer.WriteLine("User_6_Red=0");
        writer.WriteLine("User_6_Green=128");
        writer.WriteLine("User_6_Blue=0");
        writer.WriteLine("User_7_Red=255");
        writer.WriteLine("User_7_Green=0");
        writer.WriteLine("User_7_Blue=0");
        writer.WriteLine("User_8_Red=90");
        writer.WriteLine("User_8_Green=90");
        writer.WriteLine("User_8_Blue=255");
        writer.WriteLine("User_9_Red=90");
        writer.WriteLine("User_9_Green=90");
        writer.WriteLine("User_9_Blue=255");
        writer.WriteLine("User_10_Red=255");
        writer.WriteLine("User_10_Green=255");
        writer.WriteLine("User_10_Blue=255");
        writer.WriteLine("User_11_Red=255");
        writer.WriteLine("User_11_Green=0");
        writer.WriteLine("User_11_Blue=0");
        writer.WriteLine("User_12_Red=255");
        writer.WriteLine("User_12_Green=255");
        writer.WriteLine("User_12_Blue=255");
        writer.WriteLine("User_13_Red=255");
        writer.WriteLine("User_13_Green=255");
        writer.WriteLine("User_13_Blue=255");
        writer.WriteLine("User_14_Red=255");
        writer.WriteLine("User_14_Green=255");
        writer.WriteLine("User_14_Blue=0");
        writer.WriteLine("User_15_Red=90");
        writer.WriteLine("User_15_Green=90");
        writer.WriteLine("User_15_Blue=255");
        writer.WriteLine("User_16_Red=255");
        writer.WriteLine("User_16_Green=255");
        writer.WriteLine("User_16_Blue=255");
        writer.WriteLine("User_17_Red=255");
        writer.WriteLine("User_17_Green=0");
        writer.WriteLine("User_17_Blue=0");
        writer.WriteLine("User_18_Red=255");
        writer.WriteLine("User_18_Green=255");
        writer.WriteLine("User_18_Blue=255");
        writer.WriteLine("User_19_Red=255");
        writer.WriteLine("User_19_Green=255");
        writer.WriteLine("User_19_Blue=255");
        writer.WriteLine("User_20_Red=240");
        writer.WriteLine("User_20_Green=240");
        writer.WriteLine("User_20_Blue=0");
        writer.WriteLine("User_21_Red=240");
        writer.WriteLine("User_21_Green=240");
        writer.WriteLine("User_21_Blue=0");
        writer.WriteLine("User_22_Red=255");
        writer.WriteLine("User_22_Green=255");
        writer.WriteLine("User_22_Blue=255");
        writer.WriteLine("User_23_Red=255");
        writer.WriteLine("User_23_Green=255");
        writer.WriteLine("User_23_Blue=255");
        writer.WriteLine("User_24_Red=255");
        writer.WriteLine("User_24_Green=255");
        writer.WriteLine("User_24_Blue=255");
        writer.WriteLine("User_25_Red=255");
        writer.WriteLine("User_25_Green=255");
        writer.WriteLine("User_25_Blue=255");
        writer.WriteLine("User_26_Red=128");
        writer.WriteLine("User_26_Green=0");
        writer.WriteLine("User_26_Blue=128");
        writer.WriteLine("User_27_Red=255");
        writer.WriteLine("User_27_Green=255");
        writer.WriteLine("User_27_Blue=255");
        writer.WriteLine("User_28_Red=0");
        writer.WriteLine("User_28_Green=0");
        writer.WriteLine("User_28_Blue=225");
        writer.WriteLine("User_29_Red=240");
        writer.WriteLine("User_29_Green=240");
        writer.WriteLine("User_29_Blue=0");
        writer.WriteLine("User_30_Red=0");
        writer.WriteLine("User_30_Green=140");
        writer.WriteLine("User_30_Blue=0");
        writer.WriteLine("User_31_Red=90");
        writer.WriteLine("User_31_Green=90");
        writer.WriteLine("User_31_Blue=255");
        writer.WriteLine("User_32_Red=255");
        writer.WriteLine("User_32_Green=0");
        writer.WriteLine("User_32_Blue=0");
        writer.WriteLine("User_33_Red=90");
        writer.WriteLine("User_33_Green=90");
        writer.WriteLine("User_33_Blue=255");
        writer.WriteLine("User_34_Red=255");
        writer.WriteLine("User_34_Green=0");
        writer.WriteLine("User_34_Blue=0");
        writer.WriteLine("User_35_Red=215");
        writer.WriteLine("User_35_Green=154");
        writer.WriteLine("User_35_Blue=66");
        writer.WriteLine("User_36_Red=110");
        writer.WriteLine("User_36_Green=143");
        writer.WriteLine("User_36_Blue=176");
        writer.WriteLine("User_37_Red=110");
        writer.WriteLine("User_37_Green=143");
        writer.WriteLine("User_37_Blue=176");
        writer.WriteLine("User_38_Red=110");
        writer.WriteLine("User_38_Green=143");
        writer.WriteLine("User_38_Blue=176");
        writer.WriteLine("User_39_Red=110");
        writer.WriteLine("User_39_Green=143");
        writer.WriteLine("User_39_Blue=176");
        writer.WriteLine("User_40_Red=110");
        writer.WriteLine("User_40_Green=143");
        writer.WriteLine("User_40_Blue=176");
        writer.WriteLine("User_41_Red=110");
        writer.WriteLine("User_41_Green=143");
        writer.WriteLine("User_41_Blue=176");
        writer.WriteLine("User_42_Red=110");
        writer.WriteLine("User_42_Green=143");
        writer.WriteLine("User_42_Blue=176");
        writer.WriteLine("User_43_Red=110");
        writer.WriteLine("User_43_Green=143");
        writer.WriteLine("User_43_Blue=176");
        writer.WriteLine("User_44_Red=110");
        writer.WriteLine("User_44_Green=143");
        writer.WriteLine("User_44_Blue=176");
        writer.WriteLine("User_45_Red=110");
        writer.WriteLine("User_45_Green=143");
        writer.WriteLine("User_45_Blue=176");
        writer.WriteLine("User_46_Red=255");
        writer.WriteLine("User_46_Green=255");
        writer.WriteLine("User_46_Blue=255");
        writer.WriteLine("User_47_Red=240");
        writer.WriteLine("User_47_Green=240");
        writer.WriteLine("User_47_Blue=120");
        writer.WriteLine("User_48_Red=255");
        writer.WriteLine("User_48_Green=0");
        writer.WriteLine("User_48_Blue=0");
        writer.WriteLine("User_49_Red=255");
        writer.WriteLine("User_49_Green=0");
        writer.WriteLine("User_49_Blue=0");
        writer.WriteLine("User_50_Red=255");
        writer.WriteLine("User_50_Green=0");
        writer.WriteLine("User_50_Blue=0");
        writer.WriteLine("User_51_Red=255");
        writer.WriteLine("User_51_Green=0");
        writer.WriteLine("User_51_Blue=0");
        writer.WriteLine("User_52_Red=255");
        writer.WriteLine("User_52_Green=255");
        writer.WriteLine("User_52_Blue=255");
        writer.WriteLine("User_53_Red=255");
        writer.WriteLine("User_53_Green=255");
        writer.WriteLine("User_53_Blue=255");
        writer.WriteLine("User_54_Red=255");
        writer.WriteLine("User_54_Green=255");
        writer.WriteLine("User_54_Blue=255");
        writer.WriteLine("User_55_Red=255");
        writer.WriteLine("User_55_Green=255");
        writer.WriteLine("User_55_Blue=255");
        writer.WriteLine("User_56_Red=255");
        writer.WriteLine("User_56_Green=255");
        writer.WriteLine("User_56_Blue=255");
        writer.WriteLine("User_57_Red=255");
        writer.WriteLine("User_57_Green=255");
        writer.WriteLine("User_57_Blue=255");
        writer.WriteLine("User_58_Red=255");
        writer.WriteLine("User_58_Green=255");
        writer.WriteLine("User_58_Blue=255");
        writer.WriteLine("User_59_Red=255");
        writer.WriteLine("User_59_Green=255");
        writer.WriteLine("User_59_Blue=255");
        writer.WriteLine("User_60_Red=215");
        writer.WriteLine("User_60_Green=154");
        writer.WriteLine("User_60_Blue=66");
        writer.WriteLine("User_61_Red=215");
        writer.WriteLine("User_61_Green=154");
        writer.WriteLine("User_61_Blue=66");
        writer.WriteLine("User_62_Red=215");
        writer.WriteLine("User_62_Green=154");
        writer.WriteLine("User_62_Blue=66");
        writer.WriteLine("User_63_Red=215");
        writer.WriteLine("User_63_Green=154");
        writer.WriteLine("User_63_Blue=66");
        writer.WriteLine("User_64_Red=215");
        writer.WriteLine("User_64_Green=154");
        writer.WriteLine("User_64_Blue=66");
        writer.WriteLine("User_65_Red=215");
        writer.WriteLine("User_65_Green=154");
        writer.WriteLine("User_65_Blue=66");
        writer.WriteLine("User_66_Red=215");
        writer.WriteLine("User_66_Green=154");
        writer.WriteLine("User_66_Blue=66");
        writer.WriteLine("User_67_Red=215");
        writer.WriteLine("User_67_Green=154");
        writer.WriteLine("User_67_Blue=66");
        writer.WriteLine("User_68_Red=215");
        writer.WriteLine("User_68_Green=154");
        writer.WriteLine("User_68_Blue=66");
        writer.WriteLine("User_69_Red=215");
        writer.WriteLine("User_69_Green=154");
        writer.WriteLine("User_69_Blue=66");
        writer.WriteLine("User_70_Red=255");
        writer.WriteLine("User_70_Green=255");
        writer.WriteLine("User_70_Blue=0");
        writer.WriteLine("User_71_Red=255");
        writer.WriteLine("User_71_Green=0");
        writer.WriteLine("User_71_Blue=255");
        writer.WriteLine("User_72_Red=0");
        writer.WriteLine("User_72_Green=200");
        writer.WriteLine("User_72_Blue=200");
        writer.WriteLine("User_73_Red=255");
        writer.WriteLine("User_73_Green=255");
        writer.WriteLine("User_73_Blue=255");
        writer.WriteLine("User_74_Red=255");
        writer.WriteLine("User_74_Green=255");
        writer.WriteLine("User_74_Blue=255");
        writer.WriteLine("User_75_Red=0");
        writer.WriteLine("User_75_Green=255");
        writer.WriteLine("User_75_Blue=255");
        writer.WriteLine("User_76_Red=255");
        writer.WriteLine("User_76_Green=0");
        writer.WriteLine("User_76_Blue=0");
        writer.WriteLine("User_77_Red=255");
        writer.WriteLine("User_77_Green=255");
        writer.WriteLine("User_77_Blue=255");
        writer.WriteLine("User_78_Red=90");
        writer.WriteLine("User_78_Green=90");
        writer.WriteLine("User_78_Blue=255");
        writer.WriteLine("User_79_Red=255");
        writer.WriteLine("User_79_Green=255");
        writer.WriteLine("User_79_Blue=0");
        writer.WriteLine("User_80_Red=255");
        writer.WriteLine("User_80_Green=255");
        writer.WriteLine("User_80_Blue=0");
        writer.WriteLine("User_81_Red=255");
        writer.WriteLine("User_81_Green=255");
        writer.WriteLine("User_81_Blue=255");
        writer.WriteLine("User_82_Red=255");
        writer.WriteLine("User_82_Green=255");
        writer.WriteLine("User_82_Blue=255");
        writer.WriteLine("User_83_Red=255");
        writer.WriteLine("User_83_Green=255");
        writer.WriteLine("User_83_Blue=255");
        writer.WriteLine("User_84_Red=255");
        writer.WriteLine("User_84_Green=255");
        writer.WriteLine("User_84_Blue=255");
        writer.WriteLine("User_85_Red=255");
        writer.WriteLine("User_85_Green=255");
        writer.WriteLine("User_85_Blue=255");
        writer.WriteLine("User_86_Red=255");
        writer.WriteLine("User_86_Green=155");
        writer.WriteLine("User_86_Blue=155");
        writer.WriteLine("User_87_Red=90");
        writer.WriteLine("User_87_Green=90");
        writer.WriteLine("User_87_Blue=255");
        writer.WriteLine("User_88_Red=255");
        writer.WriteLine("User_88_Green=255");
        writer.WriteLine("User_88_Blue=255");
        writer.WriteLine("User_89_Red=255");
        writer.WriteLine("User_89_Green=255");
        writer.WriteLine("User_89_Blue=255");
        writer.WriteLine("User_90_Red=255");
        writer.WriteLine("User_90_Green=255");
        writer.WriteLine("User_90_Blue=255");
        writer.WriteLine("User_91_Red=255");
        writer.WriteLine("User_91_Green=255");
        writer.WriteLine("User_91_Blue=255");
        writer.WriteLine("User_92_Red=255");
        writer.WriteLine("User_92_Green=127");
        writer.WriteLine("User_92_Blue=0");
        writer.WriteLine("User_93_Red=255");
        writer.WriteLine("User_93_Green=255");
        writer.WriteLine("User_93_Blue=255");
        writer.WriteLine("User_94_Red=255");
        writer.WriteLine("User_94_Green=255");
        writer.WriteLine("User_94_Blue=255");
        writer.WriteLine("User_95_Red=255");
        writer.WriteLine("User_95_Green=255");
        writer.WriteLine("User_95_Blue=255");
        writer.WriteLine("User_96_Red=192");
        writer.WriteLine("User_96_Green=0");
        writer.WriteLine("User_96_Blue=0");
        writer.WriteLine("User_97_Red=0");
        writer.WriteLine("User_97_Green=255");
        writer.WriteLine("User_97_Blue=0");
        writer.WriteLine("User_98_Red=255");
        writer.WriteLine("User_98_Green=255");
        writer.WriteLine("User_98_Blue=0");
        writer.WriteLine("User_99_Red=255");
        writer.WriteLine("User_99_Green=0");
        writer.WriteLine("User_99_Blue=0");
        writer.WriteLine("User_100_Red=24");
        writer.WriteLine("User_100_Green=224");
        writer.WriteLine("User_100_Blue=255");
        writer.WriteLine("User_101_Red=255");
        writer.WriteLine("User_101_Green=255");
        writer.WriteLine("User_101_Blue=255");
        writer.WriteLine("User_102_Red=255");
        writer.WriteLine("User_102_Green=255");
        writer.WriteLine("User_102_Blue=255");
        writer.WriteLine("User_103_Red=255");
        writer.WriteLine("User_103_Green=255");
        writer.WriteLine("User_103_Blue=255");
        writer.WriteLine("User_104_Red=255");
        writer.WriteLine("User_104_Green=0");
        writer.WriteLine("User_104_Blue=0");
        writer.WriteLine("User_105_Red=255");
        writer.WriteLine("User_105_Green=0");
        writer.WriteLine("User_105_Blue=0");
        writer.WriteLine("User_106_Red=255");
        writer.WriteLine("User_106_Green=0");
        writer.WriteLine("User_106_Blue=0");
        writer.WriteLine("User_107_Red=255");
        writer.WriteLine("User_107_Green=255");
        writer.WriteLine("User_107_Blue=255");
        writer.WriteLine("User_108_Red=255");
        writer.WriteLine("User_108_Green=255");
        writer.WriteLine("User_108_Blue=255");
        writer.WriteLine("User_109_Red=255");
        writer.WriteLine("User_109_Green=255");
        writer.WriteLine("User_109_Blue=255");
        writer.WriteLine("User_110_Red=0");
        writer.WriteLine("User_110_Green=255");
        writer.WriteLine("User_110_Blue=0");
        writer.WriteLine("User_111_Red=240");
        writer.WriteLine("User_111_Green=240");
        writer.WriteLine("User_111_Blue=0");
        writer.WriteLine("User_112_Red=240");
        writer.WriteLine("User_112_Green=240");
        writer.WriteLine("User_112_Blue=0");
        writer.WriteLine("User_113_Red=255");
        writer.WriteLine("User_113_Green=255");
        writer.WriteLine("User_113_Blue=255");
        writer.WriteLine("User_114_Red=255");
        writer.WriteLine("User_114_Green=255");
        writer.WriteLine("User_114_Blue=255");
        writer.WriteLine("User_115_Red=255");
        writer.WriteLine("User_115_Green=255");
        writer.WriteLine("User_115_Blue=255");
        writer.WriteLine("User_116_Red=255");
        writer.WriteLine("User_116_Green=100");
        writer.WriteLine("User_116_Blue=25");
        writer.WriteLine("User_117_Red=66");
        writer.WriteLine("User_117_Green=78");
        writer.WriteLine("User_117_Blue=244");
        writer.WriteLine("User_118_Red=66");
        writer.WriteLine("User_118_Green=78");
        writer.WriteLine("User_118_Blue=244");
        writer.WriteLine("User_119_Red=0");
        writer.WriteLine("User_119_Green=255");
        writer.WriteLine("User_119_Blue=100");
        writer.WriteLine("User_120_Red=70");
        writer.WriteLine("User_120_Green=150");
        writer.WriteLine("User_120_Blue=70");
        writer.WriteLine("User_121_Red=100");
        writer.WriteLine("User_121_Green=50");
        writer.WriteLine("User_121_Blue=255");
        writer.WriteLine("User_122_Red=0");
        writer.WriteLine("User_122_Green=67");
        writer.WriteLine("User_122_Blue=255");
        writer.WriteLine("User_123_Red=70");
        writer.WriteLine("User_123_Green=70");
        writer.WriteLine("User_123_Blue=255");
        writer.WriteLine("User_124_Red=180");
        writer.WriteLine("User_124_Green=150");
        writer.WriteLine("User_124_Blue=125");
        writer.WriteLine("User_125_Red=90");
        writer.WriteLine("User_125_Green=90");
        writer.WriteLine("User_125_Blue=255");
        writer.WriteLine("User_126_Red=255");
        writer.WriteLine("User_126_Green=127");
        writer.WriteLine("User_126_Blue=0");
        writer.WriteLine("User_127_Red=90");
        writer.WriteLine("User_127_Green=90");
        writer.WriteLine("User_127_Blue=255");
        writer.WriteLine("User_128_Red=255");
        writer.WriteLine("User_128_Green=255");
        writer.WriteLine("User_128_Blue=255");
        writer.WriteLine("User_129_Red=0");
        writer.WriteLine("User_129_Green=255");
        writer.WriteLine("User_129_Blue=0");
        writer.WriteLine("User_130_Red=255");
        writer.WriteLine("User_130_Green=0");
        writer.WriteLine("User_130_Blue=0");
        writer.WriteLine("User_131_Red=100");
        writer.WriteLine("User_131_Green=255");
        writer.WriteLine("User_131_Blue=37");
        writer.WriteLine("User_132_Red=128");
        writer.WriteLine("User_132_Green=128");
        writer.WriteLine("User_132_Blue=128");
        writer.WriteLine("User_133_Red=255");
        writer.WriteLine("User_133_Green=255");
        writer.WriteLine("User_133_Blue=255");
        writer.WriteLine("User_134_Red=255");
        writer.WriteLine("User_134_Green=255");
        writer.WriteLine("User_134_Blue=0");
        writer.WriteLine("User_135_Red=255");
        writer.WriteLine("User_135_Green=0");
        writer.WriteLine("User_135_Blue=0");
        writer.WriteLine("User_136_Red=255");
        writer.WriteLine("User_136_Green=255");
        writer.WriteLine("User_136_Blue=0");
        writer.WriteLine("User_137_Red=255");
        writer.WriteLine("User_137_Green=255");
        writer.WriteLine("User_137_Blue=255");
        writer.WriteLine("User_138_Red=0");
        writer.WriteLine("User_138_Green=64");
        writer.WriteLine("User_138_Blue=255");
        writer.WriteLine("User_139_Red=0");
        writer.WriteLine("User_139_Green=255");
        writer.WriteLine("User_139_Blue=255");
        writer.WriteLine("User_140_Red=0");
        writer.WriteLine("User_140_Green=128");
        writer.WriteLine("User_140_Blue=0");
        writer.WriteLine("User_141_Red=128");
        writer.WriteLine("User_141_Green=128");
        writer.WriteLine("User_141_Blue=128");
        writer.WriteLine("User_142_Red=255");
        writer.WriteLine("User_142_Green=150");
        writer.WriteLine("User_142_Blue=50");
        writer.WriteLine("User_143_Red=255");
        writer.WriteLine("User_143_Green=200");
        writer.WriteLine("User_143_Blue=200");
        writer.WriteLine("User_144_Red=150");
        writer.WriteLine("User_144_Green=115");
        writer.WriteLine("User_144_Blue=255");
        writer.WriteLine("User_145_Red=0");
        writer.WriteLine("User_145_Green=255");
        writer.WriteLine("User_145_Blue=160");
        writer.WriteLine("User_146_Red=170");
        writer.WriteLine("User_146_Green=50");
        writer.WriteLine("User_146_Blue=255");
        writer.WriteLine("User_147_Red=0");
        writer.WriteLine("User_147_Green=255");
        writer.WriteLine("User_147_Blue=200");
        writer.WriteLine("User_148_Red=200");
        writer.WriteLine("User_148_Green=255");
        writer.WriteLine("User_148_Blue=100");
        writer.WriteLine("User_149_Red=100");
        writer.WriteLine("User_149_Green=220");
        writer.WriteLine("User_149_Blue=100");
        writer.WriteLine("User_150_Red=255");
        writer.WriteLine("User_150_Green=255");
        writer.WriteLine("User_150_Blue=255");
        writer.WriteLine("User_151_Red=175");
        writer.WriteLine("User_151_Green=0");
        writer.WriteLine("User_151_Blue=0");
        writer.WriteLine("User_152_Red=255");
        writer.WriteLine("User_152_Green=0");
        writer.WriteLine("User_152_Blue=0");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteBristlebane
    //
    // Writes the [BristlebaneWasHere] section.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteBristlebane(StreamWriter writer)
    {
        writer.WriteLine("[BristlebaneWasHere]");
        writer.WriteLine("IHateFun=0");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // WriteNews
    //
    // Writes the [News] section.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void WriteNews(StreamWriter writer)
    {
        writer.WriteLine("[News]");
        writer.WriteLine("LastRead=00000000");
        writer.WriteLine("Automatic=0");
    }
}