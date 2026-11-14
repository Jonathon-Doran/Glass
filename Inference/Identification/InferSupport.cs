using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Network.Client;
using Glass.Network.Protocol;
using Inference.Core;
using Inference.Models;
using System;
using System.Collections.Generic;

namespace Inference.Identification;

///////////////////////////////////////////////////////////////////////////////////////////////
// InferSupport
//
// Catalog walks and payload searches shared by identification handlers: the characters on a
// capture's zone sessions, the unaccepted zone-to-client packets, and searches of a payload
// for a character name or a 32-bit value.  Stateless.
///////////////////////////////////////////////////////////////////////////////////////////////
internal static class InferSupport
{
    private const LogLevel defaultLoglevel = LogLevel.Trace;
    private const int UIntLength = 4;                    // bytes in a 32-bit value

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CollectCharacters
    //
    // Walks the catalog once and gathers the Character of every zone session that has one,
    // each character once, keyed by name.  A Character without a name or without a
    // character id is left out.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The distinct characters found, in the order first seen.
    ///////////////////////////////////////////////////////////////////////////////////////////
    internal static List<Character> CollectCharacters(PacketCatalog catalog)
    {
        List<Character> characters = new List<Character>();
        HashSet<Session> examined = new HashSet<Session>();
        HashSet<string> names = new HashSet<string>();
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint index = 1; index <= totalCount; index++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(index);
            if (packetOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferSupport.CollectCharacters: no packet at index " + index, LogLevel.Error);
                continue;
            }

            Session session = packetOrNull.Value.Metadata.Session;
            if (session == null || session.ServerType != ServerType.Zone)
            {
                continue;
            }
            if (!examined.Add(session))
            {
                continue;
            }

            Character? character = session.Character;
            if (character == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferSupport.CollectCharacters: zone session " + session.SessionId
                    + " has no character", LogLevel.Warn);
                continue;
            }
            if (character.Name.Length == 0 || !character.CharacterId.Exists)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferSupport.CollectCharacters: zone session " + session.SessionId
                    + " character '" + character.Name + "' lacks a name or character id, left out", LogLevel.Warn);
                continue;
            }
            if (!names.Add(character.Name))
            {
                continue;
            }

            characters.Add(character);
            DebugLog.Write(LogChannel.InferenceDebug,
                "InferSupport.CollectCharacters: '" + character.Name + "' characterId=0x"
                + character.CharacterId.Value.ToString("X") + " from zone session " + session.SessionId,
                defaultLoglevel);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferSupport.CollectCharacters: " + characters.Count + " characters from "
            + examined.Count + " zone sessions", defaultLoglevel);
        return characters;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // BuildZoneToClientList
    //
    // Walks the catalog once and gathers every zone-to-client packet on a zone session whose
    // wire value is not accepted, in arrival order.
    //
    // catalog:  The session's cataloged packets.
    //
    // Returns:  The unaccepted zone-to-client packets.
    ///////////////////////////////////////////////////////////////////////////////////////////
    internal static List<CatalogedPacket> BuildZoneToClientList(PacketCatalog catalog)
    {
        List<CatalogedPacket> packets = new List<CatalogedPacket>();
        uint totalCount = (uint)catalog.TotalPacketCount();

        for (uint index = 1; index <= totalCount; index++)
        {
            CatalogedPacket? packetOrNull = catalog.PacketAt(index);
            if (packetOrNull == null)
            {
                DebugLog.Write(LogChannel.InferenceDebug,
                    "InferSupport.BuildZoneToClientList: no packet at index " + index, LogLevel.Error);
                continue;
            }
            CatalogedPacket packet = packetOrNull.Value;

            Session session = packet.Metadata.Session;
            if (session == null || session.ServerType != ServerType.Zone)
            {
                continue;
            }
            if (packet.Metadata.ToServer)
            {
                continue;
            }
            if (AcceptedOpcodes.Instance.IsAccepted(packet.Metadata.WireValue))
            {
                continue;
            }

            packets.Add(packet);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "InferSupport.BuildZoneToClientList: " + packets.Count + " unaccepted zone-to-client packets of "
            + totalCount, defaultLoglevel);
        return packets;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountName
    //
    // Counts the occurrences of a name in a payload.  An occurrence is the name's bytes
    // followed by a null byte, starting at offset 0 or after a byte outside printable ASCII
    // (0x20 to 0x7E).
    //
    // payload:    The bytes to search.
    // nameBytes:  The name as ASCII bytes, without a terminator.
    //
    // Returns:  The number of occurrences, 0 when the name is empty.
    ///////////////////////////////////////////////////////////////////////////////////////////
    internal static int CountName(ReadOnlySpan<byte> payload, byte[] nameBytes)
    {
        if (nameBytes.Length == 0)
        {
            return 0;
        }

        int count = 0;
        int lastStart = payload.Length - nameBytes.Length - 1;

        for (int start = 0; start <= lastStart; start++)
        {
            if (start > 0 && payload[start - 1] >= 0x20 && payload[start - 1] <= 0x7E)
            {
                continue;
            }
            if (payload[start + nameBytes.Length] != 0x00)
            {
                continue;
            }
            if (payload.Slice(start, nameBytes.Length).SequenceEqual(nameBytes))
            {
                count++;
                start += nameBytes.Length;
            }
        }

        return count;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // CountUInt
    //
    // Counts the occurrences of a 32-bit little-endian value at any byte offset in a
    // payload.  Occurrences do not overlap.
    //
    // payload:  The bytes to search.
    // value:    The value to find.
    //
    // Returns:  The number of occurrences, 0 when the value is 0.
    ///////////////////////////////////////////////////////////////////////////////////////////
    internal static int CountUInt(ReadOnlySpan<byte> payload, uint value)
    {
        if (value == 0)
        {
            return 0;
        }

        Span<byte> pattern = stackalloc byte[UIntLength];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(pattern, value);

        int count = 0;
        int lastStart = payload.Length - UIntLength;

        for (int start = 0; start <= lastStart; start++)
        {
            if (payload.Slice(start, UIntLength).SequenceEqual(pattern))
            {
                count++;
                start += UIntLength - 1;
            }
        }

        return count;
    }
}