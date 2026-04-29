using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using System.Buffers.Binary;


namespace Glass.Network.Handlers;

///////////////////////////////////////////////////////////////////////////////////////////////
// HandleClientUpdate
//
// Handles OP_ClientUpdate packets.  
///////////////////////////////////////////////////////////////////////////////////////////////
public class HandleClientUpdate : IHandleOpcodes
{
    private readonly string _opcodeName = "OP_ClientUpdate";

    private ushort _opcode;
    private readonly FieldDefinition[]? _fields;

    private readonly int _sequenceId;
    private readonly int _playerId;
    private readonly int _xPosId;
    private readonly int _yPosId;
    private readonly int _zPosId;
    private readonly int _headingId;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleClientUpdate(constructor)
    //
    // Resolves the wire opcode and loads the field definitions for OP_ClientUpdate from the
    // active patch via GlassContext.FieldExtractor.  Caches the index of each field the
    // handler reads so the hot path can access the bag by integer index without name lookup.
    //
    // If the active patch does not define OP_ClientUpdate, GetOpcodeValue returns 0 and the
    // handler is effectively disabled — OpcodeDispatch refuses to register handlers with a
    // zero opcode, so this handler simply will not receive packets.  All field index lookups
    // resolve to -1 in that case but are never consulted.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleClientUpdate()
    {
        FieldExtractor extractor = GlassContext.FieldExtractor;
        _opcode = extractor.GetOpcodeValue(_opcodeName);
        _fields = extractor.GetFieldDefinitions(_opcodeName);

        _sequenceId = _fields.IndexOfField("sequence");
        _playerId = _fields.IndexOfField("player_id");
        _xPosId = _fields.IndexOfField("x_pos");
        _yPosId = _fields.IndexOfField("y_pos");
        _zPosId = _fields.IndexOfField("z_pos");
        _headingId = _fields.IndexOfField("heading");
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
    // data:       The application payload
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
    // HandleClientToZone
    //
    // Processes client-to-zone
    //
    // data:    The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleClientToZone(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        if (_fields == null)
        {
            return;
        }

        FieldExtractor extractor = GlassContext.FieldExtractor;
        FieldBag bag = extractor.Rent();

        try
        {
            extractor.Extract(_fields, data, bag);

            uint sequence = bag.GetUIntAt(_sequenceId);
            uint playerId = bag.GetUIntAt(_playerId);
            float xPos = bag.GetFloatAt(_xPosId);
            float yPos = bag.GetFloatAt(_yPosId);
            float zPos = bag.GetFloatAt(_zPosId);

            // Note on heading:  measured as 160-degrees per second to within 0.2%.  One degree is 6.25ms of keypress.  

            float headingDeg = bag.GetUIntAt(_headingId) / 8192.0f * 360.0f;
            string name = GlassContext.SessionRegistry.CharacterNameFromMetadata(metadata);

            DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + "] " + _opcodeName);
            DebugLog.Write(LogChannel.Opcodes, "Player " + playerId + " (" + name + ", 0x" + playerId.ToString("x4") + ") sequence " + sequence);
            DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + " ID: " + playerId.ToString("x4") + " Position:  (" + xPos.ToString("F2") + "," + yPos.ToString("F2") + "," + zPos.ToString("F2") + ")");
            DebugLog.Write(LogChannel.Opcodes, "[" + metadata.Timestamp.ToString("HH:mm:ss.fff") + " Heading is " + headingDeg.ToString() + " degrees");

        }
        finally
        {
            bag.Release();
        }
    }
}
