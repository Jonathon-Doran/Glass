using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;
using System.Text;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleZoneEntry_C2Z
//
// Handles OP_ZoneEntry packets.  Client-to-zone
// packets contain the player's own zone entry with a different layout.
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleZoneEntry_C2Z: OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _nameSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneEntry_Z2C (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleZoneEntry_C2Z(PatchLevel patchLevel)
        : base(patchLevel, "OP_ZoneEntry_C2Z")
    {
        PatchOpcode baseOpcode = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _opcodeHandled = baseOpcode with { Version = 2 };
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_ZoneEntryV2");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _nameSlot = _registry.IndexOfField(_collectionHandle, "name");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientToZone
    //
    // Processes client-to-zone OP_ZoneEntry. 
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        DebugLog.Write(LogChannel.Opcodes, "HandleZoneEntry_C2Z.HandleClientToZone: "
            + _opcodeName + " length=" + data.Length);
    }

    // V2 handlers do not need the Resolver code
}