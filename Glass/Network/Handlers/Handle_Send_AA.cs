///////////////////////////////////////////////////////////////////////////////////////////////
// HandleSend_AA
//
// Handles OP_Send_AA messages.  
///////////////////////////////////////////////////////////////////////////////////////////////
using Glass.Core;
using Glass.Core.Logging;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.Network.Handlers;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Glass.World;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

public class HandleSend_AA : OpcodeHandler
{
    private readonly CollectionHandle _collectionHandle;
    private readonly GateDefinitionHandle _top_level_gate;

    private readonly SlotId _AA_ID_Slot;
    private readonly SlotId _Upper_Hotkey_Slot;
    private readonly SlotId _Lower_Hotkey_Slot;
    private readonly SlotId _Name_Slot;
    private readonly SlotId _Desc_Slot;
    private readonly SlotId _Required_Level_Slot;
    private readonly SlotId _Cost_Slot;
    private readonly SlotId _Seq_Slot;
    private readonly SlotId _Current_Rank_Slot;
    private readonly SlotId _Spell_ID_Slot;
    private readonly SlotId _Unknown_5_Slot;  // possible spell type
    private readonly SlotId _Unknown_6_Slot;  // possible spell refresh
    private readonly SlotId _SPA_ID_Slot;
    private readonly SlotId _Base_Slot;
    private readonly SlotId _Unknown_1_Slot;
    private readonly SlotId _Unknown_2_Slot;
    private readonly SlotId _Unknown_3_Slot;
    private readonly SlotId _Unknown_4_Slot;

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandleSend_AA (constructor)
    //
    // Resolves the opcode and caches the field slots this handler reads.
    //
    // patchLevel:  The patch level this handler decodes against.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public HandleSend_AA(PatchLevel patchLevel)
        : base(patchLevel, "OP_Send_AA")
    {
        _opcodeHandled = _registry.GetBaseOpcode(_patchLevel, _opcodeName);
        _collectionHandle = _registry.GetCollectionHandle(_patchLevel, "Send_AA");
        _top_level_gate = _registry.GetOpcodeGateDefinition(_opcodeHandled);

        _AA_ID_Slot = _registry.IndexOfField(_collectionHandle, "ID");
        _Upper_Hotkey_Slot = _registry.IndexOfField(_collectionHandle, "Upper_Hotkey_SID");
        _Lower_Hotkey_Slot = _registry.IndexOfField(_collectionHandle, "Lower_Hotkey_SID");
        _Name_Slot = _registry.IndexOfField(_collectionHandle, "Name_SID");
        _Desc_Slot = _registry.IndexOfField(_collectionHandle, "Desc_SID");
        _Required_Level_Slot = _registry.IndexOfField(_collectionHandle, "Required_Level");
        _Cost_Slot = _registry.IndexOfField(_collectionHandle, "Cost");
        _Seq_Slot = _registry.IndexOfField(_collectionHandle, "Seq");
        _Current_Rank_Slot = _registry.IndexOfField(_collectionHandle, "Current_Rank");
        _Spell_ID_Slot = _registry.IndexOfField(_collectionHandle, "Spell_ID");
        _Unknown_5_Slot = _registry.IndexOfField(_collectionHandle, "Spell_Type");
        _Unknown_6_Slot = _registry.IndexOfField(_collectionHandle, "Spell_Refresh");

        CollectionHandle effectsCollection = _registry.GetCollectionHandle(_patchLevel, "AA_Effects");
        _SPA_ID_Slot = _registry.IndexOfField(effectsCollection, "SPA_ID");
        _Base_Slot = _registry.IndexOfField(effectsCollection, "Base");
        _Unknown_1_Slot = _registry.IndexOfField(effectsCollection, "Unknown_1");
        _Unknown_2_Slot = _registry.IndexOfField(effectsCollection, "Unknown_2");
        _Unknown_3_Slot = _registry.IndexOfField(effectsCollection, "Unknown_3");
        _Unknown_4_Slot = _registry.IndexOfField(effectsCollection, "Unknown_4");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // HandlePacket
    //
    // Dispatches to direction-specific handlers.
    //
    // data:      The application payload
    // metadata:  Packet metadata (timestamp, source/dest)
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override void HandlePacket(ReadOnlySpan<byte> data, PacketMetadata metadata)
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
    // Processes a zone-to-client Send_AA packet.  The top-level Once gate yields the single
    // Send_AA bag, whose Effects_Gate slot holds the Times gate over the per-effect
    // entries.
    //
    // data:      The application payload.
    // metadata:  Packet metadata (timestamp, source/dest).
    ///////////////////////////////////////////////////////////////////////////////////////////////
    private void HandleZoneToClient(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleSend_AA:  No RootGate", LogLevel.Error);
                return;
            }

            uint ID = _extractor.GetUIntAt(_AA_ID_Slot);

            int Name_SID = _extractor.GetIntAt(_Name_Slot);
            string Name = DbStringGateway.Instance.LookupString(DbStringType.AAName, (uint)Name_SID);
            int Desc_SID = _extractor.GetIntAt(_Desc_Slot);
            string Desc = DbStringGateway.Instance.LookupString(DbStringType.AADescription, (uint)Desc_SID);
            uint Required_Level = _extractor.GetUIntAt(_Required_Level_Slot);
            uint Cost = _extractor.GetUIntAt(_Cost_Slot);
            uint Seq = _extractor.GetUIntAt(_Seq_Slot);
            uint Current_Rank = _extractor.GetUIntAt(_Current_Rank_Slot);
            int Spell_ID = _extractor.GetIntAt(_Spell_ID_Slot);
            int Upper_Hotkey_SID = _extractor.GetIntAt(_Upper_Hotkey_Slot);
            string Upper_Hotkey = DbStringGateway.Instance.LookupString(DbStringType.AAName, (uint)Upper_Hotkey_SID);
            int Lower_Hotkey_SID = _extractor.GetIntAt(_Upper_Hotkey_Slot);
            string Lower_Hotkey = DbStringGateway.Instance.LookupString(DbStringType.AAName, (uint)Lower_Hotkey_SID);
            uint Unknown_5 = _extractor.GetUIntAt(_Unknown_5_Slot);
            uint Unknown_6 = _extractor.GetUIntAt(_Unknown_6_Slot);

            DebugLog.Write(LogChannel.Fields, "\n\n-----------", LogLevel.Info);
            DebugLog.Write(LogChannel.Fields, "ID = " + ID, LogLevel.Info);
            DebugLog.Write(LogChannel.Fields, "Name = " + Name +
                " (" + Name_SID + ")", LogLevel.Info);
            DebugLog.Write(LogChannel.Fields, "Desc = " + Desc +
                " (" + Desc_SID + ")", LogLevel.Info);

            DebugLog.Write(LogChannel.Fields, "Upper Hotkey = " + Upper_Hotkey + 
                " (" + Upper_Hotkey_SID + ")", LogLevel.Info);
            DebugLog.Write(LogChannel.Fields, "Lower Hotkey = " + Lower_Hotkey +
                " (" + Lower_Hotkey_SID + ")", LogLevel.Info);

            DebugLog.Write(LogChannel.Fields, "Required Level = " + Required_Level, LogLevel.Info);
            DebugLog.Write(LogChannel.Fields, "Cost = " + Cost, LogLevel.Info);
            DebugLog.Write(LogChannel.Fields, "Seq = " + Seq, LogLevel.Info);
            DebugLog.Write(LogChannel.Fields, "Current Rank = " + Current_Rank, LogLevel.Info);
            DebugLog.Write(LogChannel.Fields, "Spell ID = " + Spell_ID + " (0x" + Spell_ID.ToString("X4") + ")",
                LogLevel.Info);
            DebugLog.Write(LogChannel.Fields, "Unknown 5 = " + Unknown_5 + " (0x" + Unknown_5.ToString("X4") + ")",
                LogLevel.Info);
            DebugLog.Write(LogChannel.Fields, "Unknown 6 = " + Unknown_6 + " (0x" + Unknown_6.ToString("X4") + ")",
                LogLevel.Info);

            SlotId effectsSlot = _registry.IndexOfField(_collectionHandle, "Effects_Gate");
            GateHandle effectsGate = _extractor.GetGateAt(effectsSlot);
            if (effectsGate.Exists == false)
            {
                return;
            }

            uint bagCount = _extractor.BagCount(effectsGate);
            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(effectsGate, bagIndex);

                AAEffect effect = new AAEffect();
                effect.Spa = (SPAId)_extractor.GetUIntAt(_SPA_ID_Slot);
                effect.Base = unchecked((int)_extractor.GetUIntAt(_Base_Slot));
                effect.Unknown_1 = _extractor.GetUIntAt(_Unknown_1_Slot);
                effect.Unknown_2 = _extractor.GetUIntAt(_Unknown_2_Slot);
                effect.Unknown_3 = _extractor.GetUIntAt(_Unknown_3_Slot);
                effect.Slot = _extractor.GetUIntAt(_Unknown_4_Slot);

                DebugLog.Write(LogChannel.Fields, "Effect " + bagIndex + ":", LogLevel.Info);
                DebugLog.Write(LogChannel.Fields, "   SPA: " + effect.Spa.ToString() + 
                    " (" + (int) effect.Spa + ")", LogLevel.Info);
                DebugLog.Write(LogChannel.Fields, "   Base: " + effect.Base, LogLevel.Info);
                DebugLog.Write(LogChannel.Fields, "   Unknown_1: " + effect.Unknown_1, LogLevel.Info);
                DebugLog.Write(LogChannel.Fields, "   Unknown_2: " + effect.Unknown_2, LogLevel.Info);
                DebugLog.Write(LogChannel.Fields, "   Unknown_31: " + effect.Unknown_3, LogLevel.Info);
                DebugLog.Write(LogChannel.Fields, "   Effect Number: " + effect.Slot, LogLevel.Info);
            }
        }
        finally
        {
            _extractor.Release();
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // Describe
    //
    // Extracts OP_Tracking V2 against the active patch and builds a display tree.  The top-level
    // Once gate yields the single Tracking_V2 bag, whose tracking_entries slot holds the Times gate
    // over the per-target entries.  A root node is built with one child node per entry, each
    // carrying that entry's name, spawn id, and level with their payload byte ranges.
    //
    // data:      The application payload.
    // metadata:  Packet metadata (timestamp, source/dest).
    //
    // Returns:   The root FieldDisplayNode.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public override FieldDisplayNode Describe(ReadOnlySpan<byte> data, PacketMetadata metadata)
    {
        ZoneId zoneId = GlassContext.SessionRegistry.ZoneFromMetadata(metadata);
        FieldDisplayNode root = new FieldDisplayNode();

        string Name;

        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleSend_AA:  No RootGate", LogLevel.Error);
                return root;
            }
            int Name_SID = _extractor.GetIntAt(_Name_Slot);
            Name = DbStringGateway.Instance.LookupString(DbStringType.AAName, (uint) Name_SID);

            FieldNodes.AddUIntNode(_extractor, _AA_ID_Slot, "ID", root, "?");
            FieldNodes.AddDbStringNode(_extractor, _Name_Slot, DbStringType.AAName, "Name", root);
            FieldNodes.AddDbStringNode(_extractor, _Desc_Slot, DbStringType.AADescription, "Description", root);
            FieldNodes.AddDbStringNode(_extractor, _Upper_Hotkey_Slot, DbStringType.AAName, "Upper Hotkey", root);
            FieldNodes.AddDbStringNode(_extractor, _Lower_Hotkey_Slot, DbStringType.AAName, "Lower Hotkey", root);
            FieldNodes.AddUIntNode(_extractor, _Required_Level_Slot, "Required Level", root, "D");
            FieldNodes.AddUIntNode(_extractor, _Cost_Slot, "Cost", root, "D");
            FieldNodes.AddUIntNode(_extractor, _Seq_Slot, "Seq", root, "D");
            FieldNodes.AddUIntNode(_extractor, _Current_Rank_Slot, "Current Rank", root, "D");


            SlotId effectsSlot = _registry.IndexOfField(_collectionHandle, "Effects_Gate");

            if (_extractor.IsPresent(effectsSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "Handle_Send_AA.Describe: no effects gate present", LogLevel.Warn);
                root.Text = "SendAA";
                return root;
            }

            GateHandle effectsGate = _extractor.GetGateAt(effectsSlot);
            if (effectsGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes,
                    "HandleSendAA.Describe: effects present but no gate", LogLevel.Warn);
                root.Text = "SendAA";
                return root;
            }

            uint bagCount = _extractor.BagCount(effectsGate);

            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(effectsGate, bagIndex);

                FieldDisplayNode entryNode = new FieldDisplayNode("Effect " + (bagIndex + 1));
                root.AddChild(entryNode);

                FieldNodes.AddUIntNode(_extractor, _SPA_ID_Slot, "SPA ID", entryNode, "?");
                FieldNodes.AddUIntNode(_extractor, _Base_Slot, "Base", entryNode, "?");
                FieldNodes.AddUIntNode(_extractor, _Unknown_1_Slot, "Unknown 1", entryNode, "?");
                FieldNodes.AddUIntNode(_extractor, _Unknown_2_Slot, "Unknown 2", entryNode, "?");
                FieldNodes.AddUIntNode(_extractor, _Unknown_3_Slot, "Unknown 3", entryNode, "?");
                FieldNodes.AddUIntNode(_extractor, _Unknown_4_Slot, "Unknown 4", entryNode, "?");
            }
        }
        finally
        {
            _extractor.Release();
        }
        root.Text = "AA: " + Name;
        return root;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////
    // BuildAARecord
    //
    // Extracts one Send_AA payload into an AARecord.  The name and description text are looked
    // up in the supplied string tables by the payload's name and description string ids.  A
    // negative string id means the payload carries no string, and a string id with no entry
    // in its table has no known text; both leave that text empty.  One AAEffect is built for
    // each entry of the effects gate.
    //
    // data:          The application payload.
    // names:         AA name text keyed by string id.
    // descriptions:  AA description text keyed by string id.
    //
    // Returns:       The record, or null when the payload has no root gate.
    ///////////////////////////////////////////////////////////////////////////////////////////////
    public AARecord? BuildAARecord(ReadOnlySpan<byte> data, IReadOnlyDictionary<uint, string> names,
        IReadOnlyDictionary<uint, string> descriptions)
    {
        try
        {
            GateHandle rootGate = _extractor.Extract(_top_level_gate, data);
            if (!rootGate.Exists)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleSend_AA.BuildAARecord: no root gate.", LogLevel.Error);
                return null;
            }

            AARecord record = new AARecord();
            record.Id = (AAId)_extractor.GetUIntAt(_AA_ID_Slot);
            record.RequiredLevel = _extractor.GetUIntAt(_Required_Level_Slot);
            record.Cost = _extractor.GetUIntAt(_Cost_Slot);
            record.Seq = _extractor.GetUIntAt(_Seq_Slot);

            int nameSid = _extractor.GetIntAt(_Name_Slot);
            string? name;
            if (nameSid < 0)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleSend_AA.BuildAARecord: AA " + record.Id +
                    " carries no name string id.", LogLevel.Warn);
            }
            else if (names.TryGetValue((uint)nameSid, out name) == true)
            {
                record.Name = name;
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleSend_AA.BuildAARecord: AA " + record.Id +
                    " has no name text for string id " + nameSid + ".", LogLevel.Warn);
            }

            int descSid = _extractor.GetIntAt(_Desc_Slot);
            string? description;
            if (descSid < 0)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleSend_AA.BuildAARecord: AA " + record.Id +
                    " carries no description string id.", LogLevel.Warn);
            }
            else if (descriptions.TryGetValue((uint)descSid, out description) == true)
            {
                record.Description = description;
            }
            else
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleSend_AA.BuildAARecord: AA " + record.Id +
                    " has no description text for string id " + descSid + ".", LogLevel.Warn);
            }

            SlotId effectsSlot = _registry.IndexOfField(_collectionHandle, "Effects_Gate");
            if (_extractor.IsPresent(effectsSlot) == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleSend_AA.BuildAARecord: AA " + record.Id +
                    " has no effects gate.", LogLevel.Trace);
                return record;
            }

            GateHandle effectsGate = _extractor.GetGateAt(effectsSlot);
            if (effectsGate.Exists == false)
            {
                DebugLog.Write(LogChannel.Opcodes, "HandleSend_AA.BuildAARecord: AA " + record.Id +
                    " effects slot is present but holds no gate.", LogLevel.Warn);
                return record;
            }

            uint bagCount = _extractor.BagCount(effectsGate);
            for (uint bagIndex = 0; bagIndex < bagCount; bagIndex++)
            {
                _extractor.EnterGate(effectsGate, bagIndex);

                AAEffect effect = new AAEffect();
                effect.Spa = (SPAId)_extractor.GetUIntAt(_SPA_ID_Slot);
                effect.Base = unchecked((int)_extractor.GetUIntAt(_Base_Slot));
                effect.Unknown_1 = _extractor.GetUIntAt(_Unknown_1_Slot);
                effect.Unknown_2 = _extractor.GetUIntAt(_Unknown_2_Slot);
                effect.Unknown_3 = _extractor.GetUIntAt(_Unknown_3_Slot);
                effect.Slot = _extractor.GetUIntAt(_Unknown_4_Slot);
                record.Effects.Add(effect);
            }

            DebugLog.Write(LogChannel.Opcodes, "HandleSend_AA.BuildAARecord: built AA " + record.Id + " '" +
                record.Name + "' with " + record.Effects.Count + " effects.", LogLevel.Trace);
            return record;
        }
        finally
        {
            _extractor.Release();
        }
    }
}