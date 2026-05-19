using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Glass.Network.Protocol.Fields;
using Inference.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeTracePresenter
//
// UI-side owner of the Opcode Trace list's row collection.  Cold path:
// rows are rebuilt on demand when Refresh is called from the toolbar
// button, not subscribed to the bus.
//
// Holds the ObservableCollection bound to OpcodeTraceList plus three
// ephemeral session-scoped maps:
//   - hidden opcodes (per-opcode hide set)
//   - per-opcode color overrides
//   - per-arrival-index color overrides
// Per-arrival-index wins over per-opcode at render time.
//
// All mutation runs on the UI thread.  The catalog is read under its own
// lock via PacketsFor; the presenter does not hold the catalog lock past
// those calls.
///////////////////////////////////////////////////////////////////////////////////////////////
public class OpcodeTracePresenter
{
    private readonly PacketCatalog _catalog;
    private readonly PatchLevel _patchLevel;
    private readonly ObservableCollection<OpcodeTraceRow> _rows;
    private readonly HashSet<ushort> _hiddenOpcodes;
    private readonly Dictionary<ushort, uint> _colorByOpcode;
    private readonly Dictionary<uint, uint> _colorByPacketIndex;

    public ObservableCollection<OpcodeTraceRow> Rows => _rows;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeTracePresenter (constructor)
    //
    // Captures the catalog reference for snapshotting on Refresh and
    // snapshots the active patch level for opcode name lookups via
    // PatchRegistry.  A patch level change requires constructing a new
    // presenter.
    //
    // catalog:  The PacketCatalog the presenter reads on Refresh.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeTracePresenter(PacketCatalog catalog)
    {
        _catalog = catalog;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _rows = new ObservableCollection<OpcodeTraceRow>();
        _hiddenOpcodes = new HashSet<ushort>();
        _colorByOpcode = new Dictionary<ushort, uint>();
        _colorByPacketIndex = new Dictionary<uint, uint>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Refresh
    //
    // Rebuilds the row list from the catalog.  Walks every known opcode
    // except those in the hide set, accumulates the packets, sorts by
    // arrival index for absolute arrival order, resolves names and applies
    // colors, and replaces the bound collection.
    //
    // Must be called on the UI thread.  Catalog reads are short-locked
    // inside the per-opcode PacketsFor calls; the presenter does not hold
    // any lock across the build.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Refresh()
    {
        ushort[] opcodes = _catalog.KnownOpcodes();
        List<CatalogedPacket> accumulated = new List<CatalogedPacket>();
        for (int i = 0; i < opcodes.Length; i++)
        {
            ushort opcode = opcodes[i];
            if (_hiddenOpcodes.Contains(opcode))
            {
                continue;
            }
            List<CatalogedPacket> bucket = _catalog.PacketsFor(opcode, null, null, int.MaxValue);
            accumulated.AddRange(bucket);
        }

        accumulated.Sort((a, b) => a.PacketIndex.CompareTo(b.PacketIndex));

        _rows.Clear();
        for (int i = 0; i < accumulated.Count; i++)
        {
            CatalogedPacket packet = accumulated[i];
            string timestampLocal = packet.Metadata.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            string opcodeHex = "0x" + packet.Opcode.ToString("x4");
            string opcodeName = GlassContext.PatchRegistry.GetOpcodeName(_patchLevel, packet.Opcode);
            string characterName = ResolveCharacterName(packet.Metadata.SessionId);
            int length = packet.Payload.Length;

            OpcodeTraceRow row = new OpcodeTraceRow(packet.PacketIndex,
                timestampLocal, packet.Opcode, opcodeName, packet.Metadata.Channel,
                characterName, length, packet.Payload);
            row.Color = ResolveColor(packet.PacketIndex, packet.Opcode);
            _rows.Add(row);
        }

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeTracePresenter.Refresh: rebuilt " + _rows.Count + " rows from "
            + opcodes.Length + " known opcodes (" + _hiddenOpcodes.Count + " hidden)");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolveCharacterName
    //
    // Returns the character name associated with the given session id at the
    // moment of the call, or empty string if the session is unknown or has
    // not yet been identified.  Reads SessionRegistry directly; result is
    // stable for the lifetime of the row but does not update if the
    // registry later identifies the session.  The next Refresh re-resolves.
    //
    // sessionId:  Session id from the packet's metadata.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private string ResolveCharacterName(int sessionId)
    {
        if (sessionId < 0)
        {
            return string.Empty;
        }
        if (GlassContext.SessionRegistry == null)
        {
            return string.Empty;
        }
        return GlassContext.SessionRegistry.CharacterNameFromSession(sessionId);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ResolveColor
    //
    // Returns the ARGB color to apply to a row, using the two-layer
    // override model: a per-arrival-index override wins, the per-opcode
    // override is the fallback, and 0 (no highlight) is the default.
    //
    // Both maps are session-scoped and mutated only on the UI thread; no
    // locking needed.
    //
    // packetIndex:  Arrival index from the originating CatalogedPacket.
    // opcode:       Wire opcode value, used for the per-opcode fallback.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private uint ResolveColor(uint packetIndex, ushort opcode)
    {
        uint color;
        if (_colorByPacketIndex.TryGetValue(packetIndex, out color))
        {
            return color;
        }
        if (_colorByOpcode.TryGetValue(opcode, out color))
        {
            return color;
        }
        return 0;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetPacketColor
    //
    // Sets or clears the per-packet color override for the given arrival
    // index.  Writing 0 removes the entry (zero is the no-override
    // sentinel).  After the map update, the row with the matching
    // PacketIndex has its Color recomputed via ResolveColor so the visible
    // background reflects the new override layer plus any per-opcode
    // fallback.
    //
    // packetIndex:  Arrival index of the target row.
    // argb:         ARGB color value, or 0 to clear the override.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetPacketColor(uint packetIndex, uint argb)
    {
        if (argb == 0)
        {
            _colorByPacketIndex.Remove(packetIndex);
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.SetPacketColor: cleared override for index="
                + packetIndex);
        }
        else
        {
            _colorByPacketIndex[packetIndex] = argb;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.SetPacketColor: set index="
                + packetIndex + " color=0x" + argb.ToString("x8"));
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            OpcodeTraceRow row = _rows[i];
            if (row.PacketIndex == packetIndex)
            {
                row.Color = ResolveColor(row.PacketIndex, row.OpcodeValue);
                return;
            }
        }
    }
    ///////////////////////////////////////////////////////////////////////////////////////////
    // SetOpcodeColor
    //
    // Sets or clears the per-opcode color override for the given wire
    // opcode value.  Writing 0 removes the entry (zero is the no-override
    // sentinel).  After the map update, every visible row with the matching
    // OpcodeValue has its Color recomputed via ResolveColor so per-packet
    // overrides still win where set.
    //
    // opcode:  Wire opcode value to apply the override to.
    // argb:    ARGB color value, or 0 to clear the override.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void SetOpcodeColor(ushort opcode, uint argb)
    {
        if (argb == 0)
        {
            _colorByOpcode.Remove(opcode);
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.SetOpcodeColor: cleared override for opcode=0x"
                + opcode.ToString("x4"));
        }
        else
        {
            _colorByOpcode[opcode] = argb;
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.SetOpcodeColor: set opcode=0x"
                + opcode.ToString("x4") + " color=0x" + argb.ToString("x8"));
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            OpcodeTraceRow row = _rows[i];
            if (row.OpcodeValue == opcode)
            {
                row.Color = ResolveColor(row.PacketIndex, row.OpcodeValue);
            }
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PopulateRowDetail
    //
    // Runs the field extractor against the row's payload and stores the formatted
    // field text on the row.  Rows whose opcode is not in the active patch produce
    // an empty FieldText.
    //
    // row:  The row to populate.
    ///////////////////////////////////////////////////////////////////////////////////////
    public void PopulateRowDetail(OpcodeTraceRow row)
    {
        OpcodeHandle handle = GlassContext.PatchRegistry.GetOpcodeHandle(_patchLevel, row.OpcodeValue);
        if ((int)handle == -1)
        {
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.PopulateRowDetail: opcode=" + row.OpcodeHex
                + " not in patch, no fields to extract");
            row.FieldText = string.Empty;
            return;
        }

        FieldBag bag = GlassContext.PatchRegistry.Rent(_patchLevel, handle);
        try
        {
            ReadOnlySpan<byte> payload = row.Payload.AsReadOnlySpan();
            GlassContext.FieldExtractor.Extract(_patchLevel, handle, payload, bag);

            StringBuilder sb = new StringBuilder();
            BagWalker walker = bag.Walk();
            FieldBinding? binding = walker.Next();
            while (binding != null)
            {
                FieldBinding b = binding.Value;
                sb.Append(b.Name);
                sb.Append(" = ");
                sb.Append(b.Value);
                sb.Append('\n');
                binding = walker.Next();
            }
            row.FieldText = sb.ToString();
            DebugLog.Write(LogChannel.Opcodes,
                "OpcodeTracePresenter.PopulateRowDetail: opcode=" + row.OpcodeHex
                + " extracted text length=" + row.FieldText.Length);
        }
        finally
        {
            bag.Release();
        }
    }
}
