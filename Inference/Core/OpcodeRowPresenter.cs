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
// resolves opcode names from the PatchData for the current working patch
// level at the time of each row insertion.
//
// All mutation runs on the UI thread.  MainWindow's bus handler dispatches
// to the UI thread and calls Update from inside the dispatched delegate.
// The presenter does not subscribe to the bus and does not take locks.
///////////////////////////////////////////////////////////////////////////////////////////////
public class OpcodeRowPresenter
{
    private readonly PacketCatalog _catalog;
    private readonly ObservableCollection<OpcodeEntry> _rows;
    private readonly Dictionary<OpcodeValue, OpcodeEntry> _rowByOpcode;
    public ObservableCollection<OpcodeEntry> Rows => _rows;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeRowPresenter (constructor)
    //
    // Captures the catalog reference for aggregate reads.  The working patch level is
    // not captured here; Update reads GlassContext.CurrentPatchLevel each time it
    // resolves an opcode name, so the presenter follows the user's current patch level
    // selection without needing to be reconstructed.
    //
    // catalog:  The PacketCatalog whose stats the presenter reads when
    //           updating rows.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeRowPresenter(PacketCatalog catalog)
    {
        _catalog = catalog;
        _rows = new ObservableCollection<OpcodeEntry>();
        _rowByOpcode = new Dictionary<OpcodeValue, OpcodeEntry>();
        DebugLog.Write(LogChannel.InferenceDebug, "OpcodeRowPresenter.ctor: created");
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Update
    //
    // Updates the row for the given opcode, creating it on first sight.
    // Must be called on the UI thread.  Reads a snapshot of the catalog's
    // stats for the opcode and applies them to the row's properties.
    //
    // On first sight: reads the current working patch level from GlassContext,
    // resolves the opcode name via PatchRegistry against that patch level,
    // creates an OpcodeEntry from the snapshot, and adds it to the bound
    // collection and the lookup dictionary.
    //
    // On subsequent calls: looks up the existing row, increments Count,
    // and writes MinSize and MaxSize from the snapshot.  Channel and Name
    // are immutable after first sight and not touched.
    //
    // opcode:  Wire opcode value just delivered by the bus.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Update(PacketMetadata metadata)
    {
        OpcodeValue wireValue;

        // use the PatchOpcode as the primary ID, but fallback to the wire value for unknown opcodes
        if (metadata.Opcode.Exists)
        {
            wireValue = metadata.Opcode.Value;

            if (wireValue != metadata.WireValue)
            {
                DebugLog.Write(LogChannel.Opcodes, "Wirevalue " + metadata.WireValue + 
                    " != opcode value " + metadata.Opcode.Value);
            }
        }
        else
        {
            wireValue = metadata.WireValue;
        }
        
        OpcodeStats? snapshot = _catalog.StatsFor(wireValue);
        if (snapshot == null)
        {
            return;
        }
        OpcodeStats stats = snapshot.Value;

        OpcodeEntry? row;
        if (_rowByOpcode.TryGetValue(wireValue, out row))
        {
            row.Count = row.Count + 1;
            row.MinSize = stats.MinSize;
            row.MaxSize = stats.MaxSize;
            return;
        }

        string opcodeHex = "0x" + wireValue;

        PatchLevel currentPatchLevel = GlassContext.CurrentPatchLevel;

        string name = GlassContext.PatchRegistry.GetOpcodeName(currentPatchLevel, metadata.Opcode);

        row = new OpcodeEntry(opcodeHex, stats.Channel, stats.MinSize)
        {
            RawOpcode = wireValue,
        };
        row.Name = name;
        row.Count = _catalog.CountFor(wireValue);
        _rows.Add(row);
        _rowByOpcode[wireValue] = row;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Clear
    //
    // Removes every row from the bound collection and the per-opcode lookup.
    // Must be called on the UI thread.
    ///////////////////////////////////////////////////////////////////////////////////////
    public void Clear()
    {
        _rows.Clear();
        _rowByOpcode.Clear();
        DebugLog.Write(LogChannel.InferenceDebug, "OpcodeRowPresenter.Clear: cleared");
    }
}
