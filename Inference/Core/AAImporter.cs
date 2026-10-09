using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Protocol;
using Inference.Models;
using System;
using System.Collections.Generic;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// AAImporter
//
// Imports alternate advancement ability records from a capture into the database.
///////////////////////////////////////////////////////////////////////////////////////////////
public class AAImporter
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // ImportAARecords
    //
    // Builds an AARecord from every zone-to-client Send_AA packet in the catalog and stores
    // the records in the database.  AA name and description text is read from the stored
    // database strings.  When the same ability appears in more than one packet, the values
    // of the last packet are kept.  A packet that cannot be extracted is skipped.  An
    // exception raised while storing is not caught here.
    //
    // catalog:  The cataloged packets of the capture.
    //
    // Returns:  True when records were stored.  False when no AA name strings are stored,
    //           the current patch level has no OP_Send_AA opcode, or the capture holds no
    //           OP_Send_AA packets.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public bool ImportAARecords(PacketCatalog catalog)
    {
        Dictionary<uint, string> names = DbStringGateway.Instance.LoadStrings(DbStringType.AAName);
        Dictionary<uint, string> descriptions = DbStringGateway.Instance.LoadStrings(DbStringType.AADescription);
        if (names.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "AAImporter.ImportAARecords: no AA name strings are " +
                "stored, nothing imported.", LogLevel.Warn);
            return false;
        }

        HandleSend_AA handler = new HandleSend_AA(GlassContext.CurrentPatchLevel);
        PatchOpcode opcode = handler.OpcodeHandled;
        if (opcode.Exists == false)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "AAImporter.ImportAARecords: the current patch level " +
                "has no OP_Send_AA opcode, nothing imported.", LogLevel.Warn);
            return false;
        }

        List<CatalogedPacket> packets = catalog.PacketsFor(opcode.Value,
            SoeConstants.StreamId.StreamZoneToClient, null, int.MaxValue);
        if (packets.Count == 0)
        {
            DebugLog.Write(LogChannel.InferenceDebug, "AAImporter.ImportAARecords: the capture holds no " +
                "OP_Send_AA packets, nothing imported.", LogLevel.Warn);
            return false;
        }

        Dictionary<AAId, AARecord> recordsById = new Dictionary<AAId, AARecord>();
        uint failed = 0;

        foreach (CatalogedPacket packet in packets)
        {
            AARecord? record = handler.BuildAARecord(packet.Payload.AsReadOnlySpan(), names, descriptions);
            if (record == null)
            {
                failed++;
                DebugLog.Write(LogChannel.InferenceDebug, "AAImporter.ImportAARecords: packet " +
                    packet.PacketIndex + " could not be extracted, skipping.", LogLevel.Warn);
                continue;
            }

            recordsById[record.Id] = record;
        }

        DebugLog.Write(LogChannel.InferenceDebug, "AAImporter.ImportAARecords: " + packets.Count +
            " packets yielded " + recordsById.Count + " distinct abilities, " + failed + " failed.",
            LogLevel.Trace);

        List<AARecord> records = new List<AARecord>(recordsById.Values);
        AAGateway.Instance.StoreRecords(records);

        DebugLog.Write(LogChannel.InferenceDebug, "AAImporter.ImportAARecords: stored " + records.Count +
            " abilities.", LogLevel.Trace);
        return true;
    }
}