using Glass.Core.Logging;

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

    // Daybreak server address ranges, each a /16.  Every capture filter selects only these,
    // so the address inside one of them is the server end of any datagram.
    public static readonly string[] ServerNetworks = { "69.174", "64.37", "209.0" };

    // ---------------------------------------------------------------------------
    // Stream identifiers
    // ---------------------------------------------------------------------------

    public enum StreamId
    {
        StreamClientToWorld = 0,
        StreamWorldToClient = 1,
        StreamClientToZone = 2,
        StreamZoneToClient = 3,
        StreamClientToLogin = 4,
        StreamLoginToClient = 5,
        StreamClientToWorldChat = 6,
        StreamWorldChatToClient = 7
    }

    public static readonly Dictionary<StreamId, string> StreamNames = new Dictionary<StreamId, string>
    {
        { StreamId.StreamClientToWorld, "client->world" },
        { StreamId.StreamWorldToClient, "world->client" },
        { StreamId.StreamClientToZone, "client->zone" },
        { StreamId.StreamZoneToClient, "zone->client" },
        { StreamId.StreamClientToLogin, "client->login" },
        { StreamId.StreamLoginToClient, "login->client" },
        { StreamId.StreamClientToWorldChat, "client->world-chat" },
        { StreamId.StreamWorldChatToClient, "world-chat->client" }
    };

    public static readonly Dictionary<StreamId, string> StreamAbbrev = new Dictionary<StreamId, string>
    {
        { StreamId.StreamClientToWorld, "C2W" },
        { StreamId.StreamWorldToClient, "W2C" },
        { StreamId.StreamClientToZone, "C2Z" },
        { StreamId.StreamZoneToClient, "Z2C" },
        { StreamId.StreamClientToLogin, "C2L" },
        { StreamId.StreamLoginToClient, "L2C" },
        { StreamId.StreamClientToWorldChat, "C2WC" },
        { StreamId.StreamWorldChatToClient, "WC2C" }
    };

    // ---------------------------------------------------------------------------
    // Direction flags
    // ---------------------------------------------------------------------------
    public const byte DirectionClientToServer = 0x01;
    public const byte DirectionServerToClient = 0x02;

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

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // IsServerAddress
    //
    // Tests whether a dotted-decimal IPv4 address lies inside one of the Daybreak /16 ranges.
    //
    // ip:  The address to test
    //
    // Returns true when the first two octets match one of the server networks.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static bool IsServerAddress(string ip)
    {
        int secondDot = ip.IndexOf('.', ip.IndexOf('.') + 1);

        if (secondDot < 0)
        {
            DebugLog.Write(LogChannel.LowNetwork, "SoeConstants.IsServerAddress: malformed address '" + ip + "'",
                LogLevel.Warn);
            return false;
        }

        string prefix = ip.Substring(0, secondDot);

        foreach (string network in ServerNetworks)
        {
            if (prefix == network)
            {
                return true;
            }
        }

        return false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // CaptureFilter
    //
    // Builds the BPF filter that selects UDP traffic to or from the Daybreak server networks.
    //
    // Returns the filter string, for example
    // "udp and (net 69.174.0.0/16 or net 64.37.0.0/16 or net 209.0.0.0/16)".
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public static string CaptureFilter()
    {
        string terms = string.Empty;

        foreach (string network in ServerNetworks)
        {
            if (terms.Length > 0)
            {
                terms += " or ";
            }

            terms += "net " + network + ".0.0/16";
        }

        string filter = "udp and (" + terms + ")";

        DebugLog.Write(LogChannel.LowNetwork, "SoeConstants.CaptureFilter: '" + filter + "'", LogLevel.Trace);

        return filter;
    }
}