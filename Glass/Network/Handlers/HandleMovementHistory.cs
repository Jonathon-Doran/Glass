using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Runtime.InteropServices;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleMovementHistory
//
// Handles Unknown_d9d9 packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleMovementHistory : IHandleOpcodes
{
    private ushort _opcode = 0xdc5d;
    private readonly string _opcodeName = "OP_MovementHistory";

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Opcode
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public ushort Opcode
    {
        get { return _opcode; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeName
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public string OpcodeName
    {
        get { return _opcodeName; }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        switch (metadata.Channel)
        {
            case SoeConstants.StreamId.StreamClientToZone:
                HandleClientToZone(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Extract
    //
    // Fills the supplied bag with field values decoded from data, using this handler's cached
    // field definitions.  Called by OpcodeDispatch.Extract on the cold path (e.g. the
    // Inference opcode log tab during refresh).  Handlers not yet refactored to use the
    // FieldExtractor may leave this empty; callers will see an empty bag.
    //
    // The caller owns the bag's lifetime — must Rent it before this call and Release it after.
    //
    // data:  The application payload
    // bag:   A bag rented by the caller; will be filled by this method
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Extract(ReadOnlySpan<byte> data, FieldBag bag)
    {

    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MovementHistoryEntry
    //
    // Represents a single 17-byte entry in an OP_MovementHistory (0xd9d9) packet.
    // The packet payload is an array of these entries followed by a single trailing byte.
    //
    // Bytes 0-3:    X position (float, little-endian)
    // Bytes 4-7:    Y position (float, little-endian)
    // Bytes 8-11:   Z position (float, little-endian)
    // Bytes 12-16:  Unknown (5 bytes — possibly heading, timestamp, flags)
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MovementHistoryEntry
    {
        public float X;
        public float Y;
        public float Z;
        public byte MoveState;      // may be an animation tag,  02 = standing, 01 when moving
        public ushort Timestamp;    // fine timestamp
        public ushort Sequence;     // rolling sequence number, with wrap
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToServer
    //
    // Processes OP_MovementHistory (0xd9d9) client-to-zone packets.
    // The payload is an array of 17-byte MovementHistoryEntry structures followed by
    // a single trailing byte of unknown purpose.
    //
    // Each entry contains three little-endian floats (X, Y, Z) and 5 unknown bytes.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        if (data.Length < 18)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " packet too short: length=" + data.Length + ", minimum is 18.");
            return;
        }

        if ((data.Length - 1) % 17 != 0)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " WARNING: payload length " + data.Length + " minus trailing byte is not a multiple of 17.");
        }

        int entryCount = (data.Length - 1) / 17;
        byte trailingByte = data[data.Length - 1];

        for (int i = 0; i < entryCount; i++)
        {
            MovementHistoryEntry entry = MemoryMarshal.Read<MovementHistoryEntry>(data.Slice(i * 17));

            // unknown 2 seems 2 when standing still, 1 when moving.   And 2 appears mid-movement during duplicate position
            DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] " + _opcodeName + " entry[" + i + "]"
                + " X=" + entry.X.ToString("F2")
                + " Y=" + entry.Y.ToString("F2")
                + " Z=" + entry.Z.ToString("F2")
                + " MoveState= " + entry.MoveState.ToString("x2")
                + " Sequence= " + entry.Sequence.ToString("x4")
                + " Timestamp= " + entry.Timestamp.ToString("x4")
            );
        }
    }


    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CheckSpawnIds
    //
    // Scan packet for common spawn ids
    //
    //
    // data:    A slice of the payload
    ///////////////////////////////////////////////////////////////////////////////////////////////
    ///
    private void CheckSpawnIds(ReadOnlySpan<byte> data, int index)
    {
        // ushort spawnId = BinaryPrimitives.ReadUInt16BigEndian(data);
        ushort spawnId = 0;

        if (_knownSpawns.TryGetValue(spawnId, out string? name))
        {
            DebugLog.Write(LogChannel.Opcodes, name + " seen at offset " + index
                + " id=(0x" + spawnId.ToString("x4") + ")");
        }
    }

    private static readonly Dictionary<ushort, string> _knownSpawns = new Dictionary<ushort, string>
{
    { 0x725e, "Mumarh" },
    { 0x5856, "Sentinel_Flavius00" },
    { 0x5a56, "Sentinel_Drom00" },
    { 0x5054, "Emil_Parsini00" },
    { 0x205e, "Stylla_Parsini00" },
    { 0x645c, "a_kobold_watcher05" },
    { 0x5a50, "a_kobold_runt00" },
    { 0x0e57, "Ricandrow_Starbourne00" },
    { 0x6c5c, "a_fire_beetle51" },
    { 0x0a5c, "a_fire_beetle50" },
    { 0x9259, "a_thistle_snake00" },
    { 0xfb50, "a_kobold_runt25" },
    { 0x8351, "a_fire_beetle14" },
    { 0x974f, "a_fire_beetle22" },
    { 0x405e, "a_fire_beetle56" },
    { 0x9150, "a_kobold_runt08" },
    { 0x9f50, "a_widow_hatchling01" },
    { 0x6550, "a_fire_beetle01" },
    { 0x9757, "a_kobold_runt57" },
    { 0x5152, "a_thistle_snake09" },
    { 0xcb6c, "Poacher_Hill00" },
    { 0xe457, "a_fire_beetle44" },
    { 0x0751, "an_infected_rat00" },
    { 0x644e, "a_thistle_snake07" },
    { 0x6250, "a_kobold_runt02" },
    { 0x8725, "a_moss_snake05" },
    { 0xa46c, "a_fire_beetle27" },
    { 0xe34f, "a_decaying_skeleton07" },
    { 0x9f54, "Xylania_Rainsparkle00" },
    { 0xa450, "a_fire_beetle02" },
    { 0xa454, "Quana_Rainsparkle00" },
    { 0x5b52, "a_fire_beetle20" },
    { 0x2651, "a_decaying_skeleton02" },
    { 0xdb52, "Eadsol00" },
    { 0x8b4e, "a_fire_beetle37" },
    { 0xb254, "Cyria_Lorewhisper00" },
    { 0xaa54, "Tran_Lilspin00" },
    { 0xd550, "a_kobold_runt22" },
    { 0x145e, "a_widow_hatchling05" },
    { 0x6f52, "a_widow_hatchling08" },
    { 0xa750, "a_fire_beetle03" },
    { 0xef59, "a_decaying_skeleton03" },
    { 0x8e5a, "a_fire_beetle30" },
    { 0x7a4e, "a_skunk05" },
    { 0xce5a, "a_kobold_runt44" },
    { 0x6652, "a_kobold_runt36" },
    { 0x5c51, "a_moss_snake08" },
    { 0x5f50, "a_fire_beetle00" },
    { 0x9a50, "a_kobold_runt11" },
    { 0x1a51, "a_fire_beetle10" },
    { 0xd257, "a_kobold_runt34" },
    { 0xe157, "a_fire_beetle42" },
    { 0x4b52, "a_thistle_snake08" },
    { 0x8651, "a_fire_beetle15" },
    { 0x7a4f, "a_spiderling01" },
    { 0x785c, "a_kobold_shaman01" },
    { 0x0a57, "a_faded_banner00" },
    { 0x9650, "a_skeleton06" },
    { 0xe158, "a_widow_hatchling02" },
    { 0x125e, "a_kobold_watcher26" },
    { 0x2a5e, "a_fire_beetle54" },
    { 0x055d, "a_fire_beetle38" },
    { 0x704f, "a_kobold_runt46" },
    { 0x745c, "a_kobold_runt14" },
    { 0x9233, "a_kobold_runt16" },
    { 0xa356, "a_kobold_runt24" },
    { 0x5e52, "a_fire_beetle21" },
    { 0x8950, "a_thistle_snake01" },
    { 0xa16c, "a_fire_beetle23" },
    { 0x4a55, "a_kobold_runt18" },
    { 0x5d50, "a_kobold_runt01" },
    { 0x8b25, "a_kobold_runt06" },
    { 0x884e, "a_fire_beetle07" },
    { 0x1d51, "a_fire_beetle11" },
    { 0xec57, "a_thistle_snake19" },
    { 0xe750, "a_thistle_snake04" },
    { 0x9858, "a_thistle_snake18" },
    { 0xd45a, "a_decaying_skeleton01" },
    { 0xc26b, "a_fire_beetle55" },
    { 0x1c51, "a_willowisp01" },
    { 0x0e5e, "a_kobold_watcher21" },
    { 0x0e51, "a_kobold_runt26" },
    { 0x7e51, "a_skunk00" },
    { 0x1554, "abandoned_heretic_pet00" },
    { 0x1d54, "abandoned_heretic_pet01" },
    { 0xe859, "an_infected_rat15" },
    { 0x1c5e, "a_thistle_snake06" },
    { 0x2a54, "abandoned_heretic_pet03" },
    { 0x3e51, "a_fire_beetle13" },
    { 0xd250, "a_kobold_runt21" },
    { 0x065c, "a_fire_beetle49" },
    { 0x4e5d, "a_kobold_sentry01" },
    { 0x6850, "a_widow_hatchling00" },
    { 0x3054, "abandoned_heretic_pet04" },
    { 0x6a52, "a_kobold_runt37" },
    { 0x685c, "a_decaying_skeleton05" },
    { 0x2a53, "Martyn_Firechaser00" },
    { 0x5850, "Ilisiv_Gantrau00" },
    { 0x485d, "a_thistle_snake14" },
    { 0x6051, "a_kobold_runt30" },
    { 0xb16b, "a_fire_beetle52" },
    { 0x6e5d, "a_fire_beetle34" },
    { 0x6353, "Terago_Omath00" },
    { 0x085d, "a_kobold_scout25" },
    { 0xd85d, "a_fire_beetle39" },
    { 0x9354, "Islan_Hetston00" },
    { 0x9854, "Win_Karnam00" },
    { 0xe062, "Sentinel_Creot00" },
    { 0xc059, "a_decaying_skeleton10" },
    { 0x1057, "Telanku_Trailrunner00" },
    { 0xa44f, "a_fire_beetle33" },
    { 0x0c5b, "a_fire_beetle48" },
    { 0x0d56, "a_kobold_watcher28" },
    { 0x3251, "a_kobold_runt28" },
    { 0xb36b, "a_fire_beetle53" },
    { 0x615d, "a_kobold_runt42" },
    { 0x3854, "Verogone_Wayfinder00" },
    { 0xd955, "a_fire_beetle43" },
    { 0x2156, "an_infected_rat11" },
    { 0x5f52, "a_thistle_snake11" },
    { 0x1056, "a_kobold_watcher29" },
    { 0x6a5c, "a_fire_beetle18" },
    { 0x8d6b, "an_infected_rat03" },
    { 0x365e, "a_kobold_runt10" },
    { 0x4659, "a_decaying_skeleton04" },
    { 0x315e, "a_kobold_scout35" },
    { 0x1a56, "a_kobold_runt51" },
    { 0x435d, "a_fire_beetle45" },
    { 0x445e, "a_fire_beetle57" },
    { 0x5051, "a_kobold_runt29" },
    { 0xbe5d, "a_kobold_runt33" },
    { 0x0255, "a_kobold_runt32" },
    { 0x4851, "a_thistle_snake05" },
    { 0x0f5d, "a_kobold_runt45" },
    { 0x6451, "a_kobold_runt31" },
    { 0xb15d, "a_skunk04" },
    { 0x3b51, "a_fire_beetle12" },
    { 0x9a40, "Reeking_Skunk00" },
    { 0xcc5a, "a_kobold_runt15" },
    { 0xd45d, "a_fire_beetle35" },
    { 0x935b, "a_decaying_skeleton08" },
    { 0x265e, "a_fire_beetle09" },
    { 0xdf74, "a_kobold_runt59" },
    { 0x9357, "a_skunk01" },
    { 0x675e, "a_kobold_runt55" },
    { 0xed5a, "a_moss_snake04" },
    { 0xdf5d, "a_kobold_runt61" },
    { 0x5e53, "a_spiderling07" },
    { 0x0c5d, "a_kobold_scout27" },
    { 0x3d54, "Assistant_T`os00" },
    { 0xaf5d, "a_fire_beetle36" },
    { 0x8d57, "a_decaying_skeleton06" },
    { 0x0f55, "a_piranha04" },
    { 0xae59, "a_fire_beetle05" },
    { 0x9c58, "an_infected_rat07" },
    { 0xcf5b, "a_kobold_runt56" },
    { 0x0755, "a_piranha02" },
    { 0x0f51, "a_thistle_snake17" },
    { 0xaa52, "a_kobold_runt04" },
    { 0x015d, "a_fire_beetle19" },
    { 0xbb51, "a_fire_beetle29" },
    { 0x1457, "Friedalla_Dawncrest00" },
    { 0xd654, "a_fish07" },
    { 0x3a53, "#Aglthin_Dasmore00" },
    { 0x3713, "Shintar_Vinlail00" },
    { 0x1855, "a_fish12" },
    { 0x1555, "a_fish11" },
    { 0x7b50, "a_fire_beetle08" },
    { 0xbe52, "an_Erudin_Emissary01" },
    { 0x625c, "a_kobold_watcher01" },
    { 0x4e51, "a_decaying_skeleton00" },
    { 0xfe54, "a_fish10" },
    { 0xb352, "an_Erudin_Emissary00" },
    { 0xfa54, "a_fish09" },
    { 0x3c5b, "a_decaying_skeleton09" },
    { 0xc654, "a_fish03" },
    { 0xef5b, "a_kobold_scout23" },
    { 0xf754, "a_fish08" },
    { 0x3c5a, "a_kobold_runt35" },
    { 0xcb54, "a_fish05" },
    { 0x165e, "a_thistle_snake16" },
    { 0xbc5c, "an_infected_rat14" },
    { 0xa651, "a_kobold_watcher03" },
    { 0xd43e, "a_kobold_sentry15" },
    { 0x0253, "a_briar_snake04" },
    { 0x3851, "a_kobold_sentry04" },
    { 0xff3e, "a_kobold_scout24" },
    { 0x0d55, "a_piranha03" },
    { 0x0b51, "a_giant_briar_snake00" },
    { 0xc854, "a_fish04" },
    { 0x0355, "a_piranha01" },
    { 0xe35c, "a_skunk06" },
    { 0x1c53, "an_infected_rat04" },
    { 0x4e52, "Jalen_Goldsinger00" },
    { 0x053f, "a_kobold_shaman00" },
    { 0xf545, "a_kobold_scout30" },
    { 0x015e, "a_kobold_watcher16" },
    { 0xf452, "a_kobold_sentry14" },
    { 0x185e, "a_widow_hatchling06" },
    { 0xd254, "a_fish06" },
    { 0xa251, "a_kobold_watcher02" },
    { 0xed50, "a_kobold_scout01" },
    { 0xf85d, "a_kobold_scout34" },
    { 0xc85d, "a_briar_snake12" },
    { 0xbb54, "a_fish01" },
    { 0x3451, "a_kobold_sentry03" },
    { 0xf55d, "a_kobold_scout33" },
    { 0x5c3f, "a_kobold_sentry21" },
    { 0x635e, "a_kobold_runt48" },
    { 0xba5d, "a_kobold_scout32" },
    { 0xf453, "a_kobold_sentry25" },
    { 0xe35d, "a_kobold_sentry12" },
    { 0xfc52, "a_kobold_watcher13" },
    { 0xb43e, "a_briar_snake07" },
    { 0x9142, "a_kobold_sentry10" },
    { 0x465d, "a_thistle_snake12" },
    { 0xa353, "a_widow_hatchling13" },
    { 0xc951, "a_large_thistle_snake02" },
    { 0xc65d, "a_briar_snake11" },
    { 0xfe45, "a_fire_beetle47" },
    { 0x045e, "a_kobold_watcher17" },
    { 0xd951, "a_kobold_sentry05" },
    { 0x925d, "a_kobold_runt65" },
    { 0x5450, "Spikefish01" },
    { 0x5d5e, "#Veisha_Fathomwalker00" },
    { 0xe751, "a_kobold_sentry08" },
    { 0xdc5d, "a_giant_fire_beetle02" },
    { 0xac53, "a_kobold_watcher20" },
    { 0xb854, "a_fish00" },
    { 0x1f6c, "an_infected_rat08" },
    { 0x4e50, "Spikefish00" },
    { 0xd051, "a_kobold_scout06" },
    { 0xf25d, "a_kobold_watcher08" },
    { 0xd15d, "a_kobold_runt54" },
    { 0x4d53, "a_kobold_scout16" },
    { 0xe351, "a_kobold_sentry07" },
    { 0x1d6c, "an_infected_rat06" },
    { 0xee5d, "a_kobold_watcher04" },
    { 0xf052, "a_kobold_sentry13" },
    { 0xfe53, "a_kobold_sentry26" },
    { 0xdd54, "a_piranha00" },
    { 0xe75d, "a_kobold_sentry19" },
    { 0x565e, "an_infected_rat13" },
    { 0xf53e, "a_giant_thistle_snake09" },
    { 0x0852, "a_fire_beetle16" },
    { 0xd351, "a_kobold_scout07" },
    { 0x0653, "a_briar_snake05" },
    { 0x0c52, "a_fire_beetle17" },
    { 0x8f42, "a_large_briar_snake02" },
    { 0x5453, "a_fire_beetle31" },
    { 0xc054, "a_fish02" },
    { 0xe53e, "a_large_briar_snake03" },
    { 0xe353, "a_kobold_sentry23" },
    { 0xd653, "a_large_briar_snake07" },
    { 0xdf5c, "a_skunk02" },
    { 0xe752, "a_kobold_scout14" },
    { 0xb65d, "a_kobold_scout31" },
    { 0xce5d, "a_kobold_runt47" },
    { 0xd14d, "E`lial_B`rook00" },
    { 0x3453, "a_kobold_sentry17" },
    { 0x8053, "a_kobold_watcher19" },
    { 0x5e5d, "a_thistle_snake15" },
    { 0xce3e, "a_kobold_watcher09" },
    { 0x523f, "a_skeleton00" },
    { 0xea45, "a_fire_beetle40" },
    { 0x9b53, "a_giant_thistle_snake06" },
    { 0x5e3f, "a_kobold_sentry24" },
    { 0xa853, "a_giant_fire_beetle00" },
    { 0xf33e, "a_decaying_skeleton16" },
    { 0x8f51, "an_infected_rat01" },
    { 0x9553, "a_giant_thistle_snake05" },
    { 0x9351, "an_infected_rat02" },
    { 0x1451, "a_large_briar_snake00" },
    { 0x1452, "a_boat00" },
    { 0xef51, "a_kobold_scout09" },
    { 0xe745, "a_fire_beetle26" },
    { 0xfb5d, "a_widow_hatchling04" },
    { 0x7f51, "a_briar_snake03" },
    { 0xf550, "a_kobold_scout02" },
    { 0x6f5e, "a_kobold_runt60" },
    { 0xfa45, "a_fire_beetle46" },
    { 0xdb3e, "a_briar_snake09" },
    { 0xc651, "a_large_thistle_snake01" },
    { 0xfe5d, "a_large_widow00" },
    { 0xd23e, "a_kobold_watcher10" },
    { 0x033f, "a_kobold_scout26" },
    { 0x9d53, "a_kobold_sentry20" },
    { 0x615e, "a_kobold_runt43" },
    { 0x5f6a, "Knarthenne_Skurl00" },
    { 0x0454, "a_large_thistle_snake07" },
    { 0xdf52, "a_large_wood_spider04" },
    { 0xeb52, "a_kobold_scout15" },
    { 0x2051, "a_kobold_scout04" },
    { 0xb13e, "a_briar_snake06" },
    { 0xec51, "a_kobold_scout08" },
    { 0xf952, "a_kobold_watcher12" },
    { 0xe95d, "a_widow_hatchling03" },
    { 0x7c53, "a_kobold_watcher18" },
    { 0x3853, "a_kobold_sentry18" },
    { 0xdc51, "a_kobold_sentry06" },
    { 0xf850, "a_kobold_scout03" },
    { 0x2667, "Fittorin_Bladespur00" },
    { 0x2251, "a_kobold_scout05" },
    { 0x0d53, "a_large_wood_spider07" },
    { 0x0a5e, "a_large_briar_snake01" },
    { 0xd83e, "a_briar_snake08" },
    { 0xae51, "a_giant_thistle_snake01" },
    { 0xb353, "a_kobold_sentry22" },
    { 0x5050, "a_venomous_spikefish00" },
    { 0x6c53, "a_giant_briar_snake02" },
    { 0xb551, "a_large_briar_snake05" },
    { 0x1857, "Sanidella_Syllvic00" },
    { 0x463f, "a_kobold_watcher00" },
    { 0x2053, "an_infected_rat05" },
    { 0x4a3f, "a_kobold_watcher06" },
    { 0x5653, "a_fire_beetle32" },
    { 0x083f, "a_large_thistle_snake00" },
    { 0xe03e, "a_kobold_watcher25" },
    { 0xc053, "a_kobold_scout19" },
    { 0xf145, "a_kobold_scout18" },
    { 0x0b27, "a_kerran_ademzada00" },
    { 0x0026, "Melixis00" },
    { 0x6b54, "a_kobold_watcher24" },
    { 0x5053, "a_kobold_scout17" },
    { 0xd723, "a_kerran_ghazi_shaman08" },
    { 0xe726, "a_Kerran_ademzada_shaman01" },
    { 0x2f53, "a_kobold_sentry16" },
    { 0xd325, "Roary_Fishpouncer00" },
    { 0x1326, "a_kerran_mamluk00" },
    { 0x0427, "a_kerran_pasdar15" },
    { 0xe026, "a_Kerran_ademzada_shaman00" },
    { 0xf13e, "a_decaying_skeleton15" },
    { 0x1b26, "a_kerran_mamluk02" },
    { 0x1526, "a_kerran_awrat01" },
    { 0x1826, "a_kerran_mamluk01" },
    { 0x1f26, "a_kerran_mamluk03" },
    { 0x5d73, "an_infected_rat09" },
    { 0xed26, "a_kerran_pazdar04" },
    { 0x0827, "a_kerran_ghazi`amir00" },
    { 0xe426, "a_kerran_pazdar_shaman00" },
    { 0xf726, "a_kerran_pazdar06" },
    { 0x5c54, "a_kobold_watcher22" },
    { 0xef26, "a_kerran_pazdar05" },
    { 0xd123, "a_kerran_pazdar_shaman01" },
    { 0x0f3c, "a_kerran_sha`rr_apprentice00" },
    { 0x4b22, "a_kerran_pazhal01" },
    { 0xf326, "a_Kerran_pazdar_shaman02" },
    { 0x6454, "a_kobold_watcher23" },
    { 0x0d27, "a_kerran_pazhal00" },
    { 0xb624, "a_kerran_mamluk09" },
    { 0xe950, "a_kobold_scout00" },
    { 0xb424, "a_kerran_awrat03" },
    { 0x0d3c, "a_tiger00" },
    { 0xf926, "a_kerran_pazdar_shaman03" },
    { 0x6452, "a_kobold_scout11" },
    { 0xf631, "a_kerran_puma00" },
    { 0x0b3c, "a_kerran_ahmad_shaman00" },
    { 0xfd26, "a_kerran_pazdar_shaman04" },
    { 0xd531, "a_kerran_ispusar00" },
    { 0x0232, "a_kerran_puma01" },
    { 0x6c55, "a_giant_fire_beetle01" },
    { 0xe926, "a_kerran_mujahed06" },
    { 0xd026, "a_kerran_pasdar14" },
    { 0x0453, "a_kobold_sentry29" },
    { 0x1822, "a_kerran_gorilla_shaman00" },
    { 0x1f22, "a_kerran_gorilla01" },
    { 0x4153, "a_kobold_watcher15" },
    { 0xc126, "a_kerran_pasdar13" },
    { 0xec52, "a_large_wood_spider02" },
    { 0xcf50, "a_kobold_runt20" },
    { 0x3223, "a_kobold_prisoner00" },
    { 0x3d22, "a_kerran_gorilla02" },
    { 0xcd26, "a_kobold_prisoner01" },
    { 0xa331, "a_kerran_awrat04" },
    { 0xc526, "a_kerran_ghazi_shaman03" },
    { 0x1d52, "a_boat01" },
    { 0xc750, "a_thistle_snake02" },
    { 0xbe26, "a_kerran_pasdar12" },
    { 0xd926, "a_kerran_ghazi_shaman04" },
    { 0x9056, "a_kobold_prisoner04" },
    { 0xcf31, "a_kerran_ghulam06" },
    { 0x9c50, "a_kobold_runt12" },
    { 0x9723, "a_kobold_prisoner03" },
    { 0x5873, "a_kobold_runt39" },
    { 0xcc31, "a_kerran_ghulam05" },
    { 0x9f31, "a_kerran_awrat02" },
    { 0x8623, "a_kobold_prisoner02" },
    { 0xc926, "a_kerran_pazdar03" },
    { 0xc131, "a_kerran_ghulam04" },
    { 0xc86c, "a_kobold_runt23" },
    { 0xbd31, "a_kerran_mamluk11" },
    { 0xd352, "a_kobold_runt41" },
    { 0xcc50, "a_kobold_runt19" },
    { 0x7623, "a_kerran_pazdar02" },
    { 0xb931, "a_kerran_awrat05" },
    { 0x5a26, "a_kerran_ispusar03" },
    { 0xb126, "a_kerran_gorilla00" },
    { 0x9654, "a_kobold_runt03" },
    { 0x5517, "a_fire_beetle41" },
    { 0xae26, "a_kerran_pazdar01" },
    { 0x9350, "a_kobold_runt09" },
    { 0x9b31, "a_kerran_mamluk05" },
    { 0x5452, "a_large_wood_spider00" },
    { 0x9d26, "a_kerran_ghazi01" },
    { 0xc250, "a_kobold_runt17" },
    { 0x9631, "a_kerran_mamluk04" },
    { 0x4232, "a_kerran_ispusar02" },
    { 0xee52, "a_large_wood_spider05" },
    { 0x7352, "a_kobold_scout12" },
    { 0xc745, "a_large_widow01" },
    { 0xdd26, "a_patrolling_tiger00" },
    { 0x9726, "a_kerran_ademzada`amir01" },
    { 0x7e52, "a_giant_briar_snake01" },
    { 0xb350, "a_fire_beetle06" },
    { 0x9331, "a_kerran_awrat00" },
    { 0x4732, "a_kerran_ghulam02" },
    { 0x1a32, "a_kerra_lion00" },
    { 0x2632, "Shazda_Asad00" },
    { 0xd345, "an_infected_rat10" },
    { 0xb031, "a_kerran_mamluk07" },
    { 0x4432, "a_kerra_lion01" },
    { 0xd152, "a_kobold_runt40" },
    { 0xee31, "a_kerran_ghulam09" },
    { 0x6d52, "a_moss_snake02" },
    { 0xad31, "a_kerran_mamluk06" },
    { 0x3b32, "a_kerran_`amir04" },
    { 0x0432, "a_kerran_`amir03" },
    { 0xf158, "a_kerran_mujahed00" },
    { 0x8526, "a_kerran_pasdar03" },
    { 0x0d59, "a_kerran_mujahed02" },
    { 0x7a0a, "Marl_Kastane00" },
    { 0xdb45, "a_willowisp00" },
    { 0x8b26, "a_kerran_ademzada`amir00" },
    { 0x3532, "a_kerran_pasdar06" },
    { 0xf831, "a_kerran_pasdar02" },
    { 0x9926, "a_kerran_pasdar08" },
    { 0x3132, "a_kerran_pasdar05" },
    { 0x1f59, "a_kerran_ghazi_shaman02" },
    { 0x0459, "a_kerran_pasdar00" },
    { 0x8d50, "a_moss_snake01" },
    { 0xb331, "a_kerran_mamluk08" },
    { 0xc86b, "a_kobold_runt50" },
    { 0xbb3f, "a_kobold_scout29" },
    { 0x8326, "a_kerran_pasdar01" },
    { 0xf652, "a_kobold_runt27" },
    { 0x9326, "a_kerran_pasdar07" },
    { 0xf656, "a_kerran_ghulam08" },
    { 0xb526, "a_kerran_pasdar10" },
    { 0x7c26, "a_kerran_ghazi_shaman00" },
    { 0x8826, "a_kerran_pasdar04" },
    { 0xf821, "a_kerran_ghazi_shaman06" },
    { 0x9126, "a_kerran_ghazi00" },
    { 0x1532, "a_kerran_ispusar01" },
    { 0xb926, "a_kerran_pasdar11" },
    { 0x9f26, "a_kerran_ghazi_shaman01" },
    { 0x9b26, "a_kerran_pasdar09" },
    { 0xb631, "a_kerran_mamluk10" },
    { 0xc052, "a_fire_beetle28" },
    { 0x3c3f, "a_large_fire_beetle01" },
    { 0x3d32, "kerran_tseq00" },
    { 0x4032, "kerran_tseq01" },
    { 0x2253, "Lanivon_Baxer00" },
    { 0x5332, "Urkath_Greyface00" },
    { 0x2559, "a_gorilla_protector01" },
    { 0x7d50, "a_kobold_runt05" },
    { 0x2359, "a_gorilla_protector00" },
    { 0x0a59, "a_kerran_mujahed01" },
    { 0x6651, "a_giant_thistle_snake00" },
    { 0x0333, "a_kerran_awrat06" },
    { 0x5a32, "a_kerran_ghulam07" },
    { 0xe825, "a_catfisher00" },
    { 0xa626, "a_kerran_pazdar00" },
    { 0x1159, "a_kerran_mujahed03" },
    { 0x4459, "a_kerran_amira_guardian00" },
    { 0x4059, "Khonza_Mitty_of_Kerra00" },
    { 0x7826, "a_kerran_amira_protector00" },
    { 0x5b73, "a_kobold_runt53" },
    { 0xdf50, "a_thistle_snake03" },
    { 0x0426, "Feren00" },
    { 0x7752, "a_kobold_scout13" },
    { 0xbc6c, "a_large_thistle_snake09" },
    { 0xc662, "a_kerran_ghulam03" },
    { 0xff52, "Nexus_Scion00" },
    { 0xed52, "The_Norrath_Spires00" },
    { 0xe452, "A_Mystic_Voice00" },
    { 0x521c, "Norrath_Scion00" },
    { 0x2057, "#Jharin_Kevva00" },
    { 0x1b57, "Kellkoan_Rishta00" },
    { 0xd356, "a_gorilla_guard00" },
    { 0x8652, "a_kobold_watcher07" },
    { 0x8b23, "dozing_ghulam00" },
    { 0xe36b, "a_thistle_snake13" },
    { 0xad52, "a_fire_beetle24" },
    { 0xdc56, "Maugarim00" },
    { 0xb73f, "a_kobold_scout28" },
    { 0xe462, "wharf_rat01" },
    { 0xb20e, "a_kobold_scout20" },
    { 0x6a59, "Thalith_Mamluk00" },
    { 0xb60e, "a_kobold_scout21" },
    { 0x2c3f, "a_kobold_sentry02" },
    { 0xed62, "a_kerran_ghulam10" },
    { 0x9b52, "a_kobold_watcher11" },
    { 0xb90e, "a_kobold_scout22" },
    { 0x2f3f, "a_kobold_sentry09" },
    { 0xd745, "an_infected_rat12" },
    { 0x4a59, "Falthrik_Lothoro00" },
    { 0x3b59, "wharf_rat00" },
    { 0x6859, "Iffrir_Soulcaller00" },
    { 0xa422, "Feskr_Drinkmaker00" },
    { 0xb052, "a_fire_beetle25" },
    { 0x3e59, "wharf_rat02" },
    { 0xdf56, "a_banished_kerran00" },
    { 0xa624, "drunken_ghulam00" },
    { 0xf745, "a_skunk03" },
    { 0x4e59, "Erfer_Longclaw00" },
    { 0xca52, "a_kobold_runt38" },
    { 0x4124, "Raarrk00" },
    { 0x6052, "a_kobold_scout10" },
    { 0x0b26, "Wislen_Mamluk00" },
    { 0x2457, "#Geia_Korrel00" },
    { 0xe450, "a_kobold_sentry00" },
    { 0xba62, "a_kerran_ghulam00" },
    { 0x946c, "a_moss_snake07" },
    { 0x8472, "a_fading_spectre00" },
    { 0xa250, "a_kobold_runt13" },
    { 0xdf6b, "a_large_briar_snake08" },
    { 0xaa50, "a_fire_beetle04" },
    { 0x1333, "a_kerran_awrat07" },
    { 0xcf45, "a_kobold_runt52" },
    { 0xbe62, "a_kerran_ghulam01" },
    { 0x6459, "Errrak_Thickshank00" },
    { 0x3559, "Allix00" },
    { 0x5459, "Graalf_Sharpclaw00" },
    { 0xcd45, "a_kobold_runt49" },
    { 0x6259, "a_kerran_`amir02" },
    { 0x4554, "Spikefish02" },
    { 0x6059, "a_kerran_`amir01" },
    { 0xfe56, "a_kerran_puma02" },
    { 0xc46c, "a_kobold_runt07" },
    { 0x0057, "a_kerran_puma03" },
    { 0x5059, "a_wild_tiger00" },
    { 0xcf4d, "abandoned_heretic_pet02" },
    { 0xe35f, "a_skeleton02" },
    { 0x5859, "a_wild_tiger01" },
    { 0x1253, "a_skeleton01" },
    { 0x0757, "a_kerran_puma06" },
    { 0x1859, "kerran_tiger_spahi00" },
    { 0xb35d, "Rungupp00" },
    { 0x0457, "a_kerran_puma05" },
    { 0x5a59, "a_wild_tiger02" },
    { 0x3259, "a_kerran_mujahed04" },
    { 0x4859, "a_kerran_`amir00" },
    { 0x5c59, "a_kerran_mujahed05" },
    { 0x0257, "a_kerran_puma04" },
    { 0x745e, "Mumarh" },
    { 0x785e, "Larry00" },
    { 0x7a5e, "a_skunk07" },
    { 0x7c5e, "a_moss_snake00" },
    { 0x805e, "a_decaying_skeleton11" },
    { 0x845e, "a_fire_beetle34" },
    { 0x865e, "Poacher_Hill00" },
};
}



