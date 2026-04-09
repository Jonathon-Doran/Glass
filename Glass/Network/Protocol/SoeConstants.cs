namespace Glass.Network.Protocol;

///////////////////////////////////////////////////////////////////////////////////////////////
// SoeConstants
//
// Port ranges, net opcodes, protocol flags, stream identifiers, and direction
// constants for the SOE (Sony Online Entertainment) UDP protocol used by EverQuest.
//
// Net opcodes are stored on the wire in network byte order.  On a little-endian host
// they read as these values (low byte is always 0x00 for net opcodes).
///////////////////////////////////////////////////////////////////////////////////////////////
public static class SoeConstants
{
    // ---------------------------------------------------------------------------
    // Port ranges
    // ---------------------------------------------------------------------------
    public const int WorldServerGeneralMinPort = 9000;
    public const int WorldServerGeneralMaxPort = 9015;
    public const int WorldServerChatPort = 9876;
    public const int WorldServerChat2Port = 9875;
    public const int LoginServerMinPort = 15900;
    public const int LoginServerMaxPort = 15910;
    public const int ChatServerPort = 5998;

    // ---------------------------------------------------------------------------
    // Net opcodes
    // ---------------------------------------------------------------------------
    public const ushort OP_SessionRequest = 0x0100;
    public const ushort OP_SessionResponse = 0x0200;
    public const ushort OP_Combined = 0x0300;
    public const ushort OP_SessionDisconnect = 0x0500;
    public const ushort OP_KeepAlive = 0x0600;
    public const ushort OP_SessionStatRequest = 0x0700;
    public const ushort OP_SessionStatResponse = 0x0800;
    public const ushort OP_Packet = 0x0900;
    public const ushort OP_Oversized = 0x0D00;
    public const ushort OP_AckFuture = 0x1100;
    public const ushort OP_Ack = 0x1500;
    public const ushort OP_AppCombined = 0x1900;
    public const ushort OP_AckAfterDisconnect = 0x1D00;

    // ---------------------------------------------------------------------------
    // Protocol flags
    // ---------------------------------------------------------------------------
    public const byte FLAG_COMPRESSED = 0x5A;
    public const byte FLAG_UNCOMPRESSED = 0xA5;

    // ---------------------------------------------------------------------------
    // Stream identifiers
    // ---------------------------------------------------------------------------
    public const int StreamClient2World = 0;
    public const int StreamWorld2Client = 1;
    public const int StreamClient2Zone = 2;
    public const int StreamZone2Client = 3;
    public const int MaxStreams = 4;

    public static readonly string[] StreamNames =
    {
        "client-world",
        "world-client",
        "client-zone",
        "zone-client"
    };

    // ---------------------------------------------------------------------------
    // Direction flags
    // ---------------------------------------------------------------------------
    public const byte DirectionClient = 0x01;
    public const byte DirectionServer = 0x02;

    // ---------------------------------------------------------------------------
    // Protocol limits
    // ---------------------------------------------------------------------------
    public const int ArqSeqWrapCutoff = 1024;

    // ---------------------------------------------------------------------------
    // Session struct sizes
    // ---------------------------------------------------------------------------
    public const int SizeOfSessionRequest = 22;
    public const int SizeOfSessionResponse = 19;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsAppOpcode
    //
    // Returns true if the opcode is an application-level opcode.
    // App opcodes have a non-zero low byte.
    //
    // opcode:  The 16-bit opcode to test
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static bool IsAppOpcode(ushort opcode)
    {
        return (opcode & 0x00FF) != 0x0000;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsNetOpcode
    //
    // Returns true if the opcode is a network-level (SOE protocol) opcode.
    // Net opcodes have a zero low byte.
    //
    // opcode:  The 16-bit net opcode to test
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static bool IsNetOpcode(ushort opcode)
    {
        return (opcode & 0x00FF) == 0x0000;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HasFlags
    //
    // Returns true if the given net opcode carries a flags byte at position 2.
    // Subpackets never have flags.
    //
    // opcode:       The 16-bit net opcode to test
    // isSubpacket:  True if this packet was extracted from an OP_Combined container
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static bool HasFlags(ushort opcode, bool isSubpacket)
    {
        if (isSubpacket)
        {
            return false;
        }

        if (opcode == OP_SessionStatRequest ||
            opcode == OP_SessionStatResponse ||
            opcode == OP_Combined ||
            opcode == OP_Packet ||
            opcode == OP_Oversized ||
            opcode == OP_AppCombined)
        {
            return true;
        }

        return IsAppOpcode(opcode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HasCrc
    //
    // Returns true if the given net opcode carries a 2-byte CRC trailer.
    // Subpackets never have a CRC.
    //
    // opcode:       The 16-bit net opcode to test
    // isSubpacket:  True if this packet was extracted from an OP_Combined container
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static bool HasCrc(ushort opcode, bool isSubpacket)
    {
        if (isSubpacket)
        {
            return false;
        }

        if (opcode == OP_SessionStatRequest ||
            opcode == OP_SessionStatResponse ||
            opcode == OP_Combined ||
            opcode == OP_Packet ||
            opcode == OP_Oversized ||
            opcode == OP_AppCombined)
        {
            return true;
        }

        return IsAppOpcode(opcode);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HasArqSeq
    //
    // Returns true if the given net opcode carries an ARQ sequence number.
    // Only OP_Packet and OP_Oversized carry sequence numbers.
    //
    // opcode:  The 16-bit net opcode to test
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static bool HasArqSeq(ushort opcode)
    {
        return opcode == OP_Packet || opcode == OP_Oversized;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // GetNetOpcodeName
    //
    // Returns a human-readable name for a net opcode, or a hex string
    // for unrecognized opcodes.
    //
    // opcode:  The 16-bit net opcode to look up
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string GetNetOpcodeName(ushort opcode)
    {
        switch (opcode)
        {
            case OP_SessionRequest: return "OP_SessionRequest";
            case OP_SessionResponse: return "OP_SessionResponse";
            case OP_Combined: return "OP_Combined";
            case OP_SessionDisconnect: return "OP_SessionDisconnect";
            case OP_KeepAlive: return "OP_KeepAlive";
            case OP_SessionStatRequest: return "OP_SessionStatRequest";
            case OP_SessionStatResponse: return "OP_SessionStatResponse";
            case OP_Packet: return "OP_Packet";
            case OP_Oversized: return "OP_Oversized";
            case OP_AckFuture: return "OP_AckFuture";
            case OP_Ack: return "OP_Ack";
            case OP_AppCombined: return "OP_AppCombined";
            case OP_AckAfterDisconnect: return "OP_AckAfterDisconnect";
            default:
                {
                    if (IsAppOpcode(opcode))
                    {
                        return $"APP_{opcode:X4}";
                    }
                    return $"UNK_{opcode:X4}";
                }
        }
    }
}