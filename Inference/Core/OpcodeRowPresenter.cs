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
    private readonly Dictionary<PatchOpcode, OpcodeEntry> _rowByOpcode;
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
        _rowByOpcode = new Dictionary<PatchOpcode, OpcodeEntry>();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Update
    //
    // Updates the row for the opcode carried in the metadata, creating it on first sight.
    // Rows are keyed on the full PatchOpcode, so each version of a wire value has its own row.
    // Count and size statistics are accrued per PatchOpcode.  Must be called on the UI thread.
    //
    // metadata:  Packet metadata carrying the resolved PatchOpcode and the channel.
    // length:    Payload length in bytes for this packet.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public void Update(PacketMetadata metadata, uint length)
    {
        PatchOpcode patchOpcode = metadata.Opcode;

        OpcodeEntry? row;
        if (_rowByOpcode.TryGetValue(patchOpcode, out row))
        {
            row.Count = row.Count + 1;
            if (length < row.MinSize)
            {
                row.MinSize = length;
            }
            if (length > row.MaxSize)
            {
                row.MaxSize = length;
            }
            return;
        }

        string opcodeHex = "0x" + patchOpcode.Value;
        PatchLevel currentPatchLevel = GlassContext.CurrentPatchLevel;
        string name = GlassContext.PatchRegistry.GetOpcodeName(currentPatchLevel, patchOpcode);
        row = new OpcodeEntry(opcodeHex, metadata.Channel, length)
        {
            RawOpcode = patchOpcode.Value,
        };
        row.Name = name;
        _rowByOpcode[patchOpcode] = row;
        _rows.Add(row);
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
    }
}
