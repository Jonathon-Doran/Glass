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
// HandleHpUpdate
//
// Handles OP_PlayerProfile packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleHpUpdate : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_HpUpdate";

    private ushort _opcode;
    private OpcodeHandle _handle;
    private readonly IReadOnlyList<FieldDefinition>? _fields;
    private bool _nullFieldsObserved = false;
    private bool _characterNotFound = false;

    private readonly int _playerId;
    private readonly int _currentHPId;
    private readonly int _maxHPId;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleHpUpdate (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_HpUpdate from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_HpUpdate, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleHpUpdate()
    {
        PatchRegistry registry = GlassContext.PatchRegistry;
        FieldExtractor extractor = GlassContext.FieldExtractor;
        PatchLevel patchLevel = GlassContext.CurrentPatchLevel;

        _opcode = extractor.GetOpcodeValue(patchLevel, _opcodeName);
        PatchOpcode opcodeId = new PatchOpcode(patchLevel, _opcode);
        _handle = registry.GetOpcodeHandle(patchLevel, _opcodeName);

        _fields = extractor.GetFields(patchLevel, opcodeId);

        _playerId = registry.IndexOfField(patchLevel, _handle, "player_id");
        _currentHPId = registry.IndexOfField(patchLevel, _handle, "current_hp");
        _maxHPId = registry.IndexOfField(patchLevel, _handle, "max_hp");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////

    public void Dispose()
    {
        if (_nullFieldsObserved)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " had null field descriptions");
        }

        if (_characterNotFound)
        {
            DebugLog.Write(LogChannel.Opcodes, _opcodeName + " could not find character");
        }
        GC.SuppressFinalize(this);
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
            case SoeConstants.StreamId.StreamZoneToClient:
                HandleZoneToClient(data, metadata);
                break;
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleZoneToClient
    //
    // Processes zone-to-client traffic
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        // Ensure that _fields exist if we process this packet
        if (_fields == null)
        {
            _nullFieldsObserved = true;         // log this on exit
            return;
        }

        Character? character = null;

        FieldBag bag = GlassContext.FieldExtractor.Rent(_opcodeName);
        try
        {
            GlassContext.FieldExtractor.Extract(_fields!, data, bag);

            uint playerId = bag.GetUIntAt(_playerId);

            character = CharacterRepository.Instance.GetById((int) playerId);

            if (character == null)
            {
                DebugLog.Write(LogChannel.Opcodes, _opcodeName + ": no Character with ID '" + playerId + "' in repository; fields not stored.");
                return;
            }

            character.MaxHP = bag.GetUIntAt(_maxHPId);
            character.CurrentHP = bag.GetUIntAt(_currentHPId);
        }
        finally
        {
            bag.Release();
        }

        DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
            + _opcodeName + " length=" + data.Length);
        DebugLog.Write(LogChannel.Opcodes, "HP at " + character.CurrentHP + " / " + character.MaxHP);
    }
}


