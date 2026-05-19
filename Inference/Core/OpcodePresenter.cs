using Glass.Core;
using Glass.Core.Logging;
using Glass.Network.Protocol;
using Inference.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeRowPresenter
//
// UI-side owner of the Opcodes grid's row collection.  Holds the
// ObservableCollection bound to the grid and a per-opcode lookup so updates
// find their row in O(1).  Reads aggregate values from PacketCatalog and
// resolves opcode names from the active PatchData captured at construction.
//
// All mutation runs on the UI thread.  MainWindow's bus handler dispatches
// to the UI thread and calls Update from inside the dispatched delegate.
// The presenter does not subscribe to the bus and does not take locks.
///////////////////////////////////////////////////////////////////////////////////////////////
public class OpcodeRowPresenter
{
    private readonly PacketCatalog _catalog;
    private readonly PatchLevel _patchLevel;
    private readonly ObservableCollection<OpcodeEntry> _rows;
    private readonly Dictionary<ushort, OpcodeEntry> _rowByOpcode;
    public ObservableCollection<OpcodeEntry> Rows => _rows;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeRowPresenter (constructor)
    //
    // Captures the catalog reference for aggregate reads and snapshots the
    // active patch level for opcode name lookups via PatchRegistry.  A patch
    // level change requires constructing a new presenter.
    //
    // catalog:  The PacketCatalog whose stats the presenter reads when
    //           updating rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeRowPresenter(PacketCatalog catalog)
    {
        _catalog = catalog;
        _patchLevel = GlassContext.CurrentPatchLevel;
        _rows = new ObservableCollection<OpcodeEntry>();
        _rowByOpcode = new Dictionary<ushort, OpcodeEntry>();
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeRowPresenter.ctor: created for patchLevel=" + _patchLevel);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Update
    //
    // Updates the row for the given opcode, creating it on first sight.
    // Must be called on the UI thread.  Reads a snapshot of the catalog's
    // stats for the opcode and applies them to the row's properties.
    //
    // On first sight: resolves the opcode name via PatchRegistry, creates
    // an OpcodeEntry from the snapshot, adds it to the bound collection
    // and the lookup dictionary.
    //
    // On subsequent calls: looks up the existing row, increments Count,
    // and writes MinSize and MaxSize from the snapshot.  Channel and Name
    // are immutable after first sight and not touched.
    //
    // opcode:  Wire opcode value just delivered by the bus.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Update(ushort opcode)
    {
        OpcodeStats? snapshot = _catalog.StatsFor(opcode);
        if (snapshot == null)
        {
            DebugLog.Write(LogChannel.InferenceDebug,
                "OpcodeRowPresenter.Update: no stats for opcode=0x"
                + opcode.ToString("x4") + ", catalog out of sync");
            return;
        }
        OpcodeStats stats = snapshot.Value;

        OpcodeEntry? row;
        if (_rowByOpcode.TryGetValue(opcode, out row))
        {
            row.Count = row.Count + 1;
            row.MinSize = stats.MinSize;
            row.MaxSize = stats.MaxSize;
            return;
        }

        string opcodeHex = "0x" + opcode.ToString("x4");
        string name = GlassContext.PatchRegistry.GetOpcodeName(_patchLevel, opcode);
        row = new OpcodeEntry(opcodeHex, stats.Channel, stats.MinSize)
        {
            RawOpcode = opcode,
        };
        row.Name = name;
        row.Count = _catalog.CountFor(opcode);
        _rows.Add(row);
        _rowByOpcode[opcode] = row;
        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeRowPresenter.Update: added row for opcode="
            + opcodeHex + " name=" + name + " channel=" + stats.Channel);
    }
}
