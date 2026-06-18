using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System;
using System.Buffers.Binary;

namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleSessionResponse
//
// Handles OP_PlayerProfile packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleSessionResponse : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_SessionResponse";
    private readonly PatchOpcode _opcodeHandled;
    private readonly CollectionHandle _collectionHandle;
    private readonly PatchRegistry _registry;
    private readonly PatchLevel _patchLevel;

    private readonly SlotId _sessionIdSlot;
    private readonly SlotId _sessionKeySlot;
    private readonly SlotId _maxLengthSlot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleSessionResponse (constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_ManaUpdate from
    // the current patch via GlassContext.FieldExtractor and GlassContext.CurrentPatchLevel.
    // Caches the index of each field the handler reads so the hot path can access the bag
    // by integer index without name lookup.
    //
    // If the current patch does not define OP_ManaUpdate, GetOpcodeValue returns 0 and
    // the handler is effectively disabled — OpcodeDispatch refuses to register handlers
    // with a zero opcode, so this handler simply will not receive packets.  All field
    // index lookups resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleSessionResponse()
    {
        _registry = GlassContext.PatchRegistry;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "OP_SessionResponse");

        _sessionIdSlot = _registry.IndexOfField(_collectionHandle, "session_id");
        _sessionKeySlot = _registry.IndexOfField(_collectionHandle, "session_key");
        _maxLengthSlot = _registry.IndexOfField(_collectionHandle, "max_length");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Dispose
    //
    // Log any errors in the cold-path, dispose of any local storage. 
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public void Dispose()
    {
        GC.SuppressFinalize(this);
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
/*        FieldBag bag = _registry.Rent(_collectionHandle);

        try
        {
            GlassContext.FieldExtractor.ExtractCollection(_patchLevel, _collectionHandle, data, bag);

            uint sessionId = bag.GetUIntAt(_sessionIdSlot);
            uint sessionKey = bag.GetUIntAt(_sessionKeySlot);
            uint maxLength = bag.GetUIntAt(_maxLengthSlot);

            DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] "
                + _opcodeName + " length=" + data.Length);
            DebugLog.Write(LogChannel.Opcodes, "sessionId = " + sessionId + ", sessionKey = " +
                sessionKey + ", maxLength=" + maxLength);
        }
        finally
        {
            bag.Release();
        }*/
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeHandled
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public PatchOpcode OpcodeHandled
    {
        get { return _opcodeHandled; }
    }
}


