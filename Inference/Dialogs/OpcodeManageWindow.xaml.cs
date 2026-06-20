using Glass.Core;
using Glass.Core.Logging;
using Inference.Core;
using Inference.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Glass.Network.Protocol;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeManageWindow
//
// Modeless management dialog for the per-opcode hide set.  Lists every
// opcode known to the catalog at the moment of opening, sorted by
// hex value ascending, in a wrap panel of fixed-width cells.  Each
// cell carries a checkbox bound two-way to the row's IsHidden flag.
// Flips push through to the presenter so rows for that opcode disappear
// or reappear in the trace list immediately.
//
// The cell width is computed once in the constructor by measuring the
// rendered width of the widest "0xHHHH  Name" string using the dialog's
// font, plus a fixed allowance for the checkbox and surrounding padding.
// The result is exposed through the CellWidth property bound by the
// cell template.
//
// The dialog is a snapshot at open time.  Opcodes captured after the
// dialog opens are not reflected; close and reopen to refresh.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class OpcodeManageWindow : Window
{
    private readonly OpcodeTracePresenter _presenter;
    private readonly PacketCatalog _catalog;
    private readonly List<OpcodeManageRow> _rows;

    public double CellWidth { get; private set; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeManageWindow (constructor)
    //
    // Snapshots the catalog's known opcodes and the presenter's hidden
    // set, builds one OpcodeManageRow per opcode, sorts by opcode value
    // ascending, measures the widest cell label to size the cell width,
    // subscribes to each row's PropertyChanged to forward IsHidden flips
    // to the presenter, and binds the row list to the items control.
    //
    // presenter:  The presenter that owns the per-opcode hide set.
    // catalog:    The packet catalog supplying KnownOpcodes.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeManageWindow(OpcodeTracePresenter presenter, PacketCatalog catalog)
    {
        InitializeComponent();
        DataContext = this;

        _presenter = presenter;
        _catalog = catalog;
        _rows = new List<OpcodeManageRow>();
        
        OpcodeValue[] knownOpcodes = _catalog.KnownOpcodes();
        Array.Sort(knownOpcodes);
        HashSet<OpcodeValue> hidden = _presenter.GetHiddenOpcodes();

        for (int i = 0; i < knownOpcodes.Length; i++)
        {
            // note on version:  This usage is ok because we do not need to know the exact version
            // when obtaining the opcode name.  V1's output is identical to all other versions.

            PatchOpcode patchOpcode = new PatchOpcode(GlassContext.CurrentPatchLevel, knownOpcodes[i]);

            OpcodeValue opcode = knownOpcodes[i];
            string opcodeHex = "0x" + opcode;
            string opcodeName = GlassContext.PatchRegistry.GetOpcodeName(patchOpcode);
            bool isHidden = hidden.Contains(opcode);

            OpcodeManageRow row = new OpcodeManageRow(opcode, opcodeHex, opcodeName, isHidden);
            row.PropertyChanged += OnRowPropertyChanged;
            _rows.Add(row);
        }

        CellWidth = MeasureCellWidth(_rows);

        HeaderText.Text = knownOpcodes.Length + " opcode(s), "
            + hidden.Count + " hidden";

        OpcodeItems.ItemsSource = _rows;

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeManageWindow: opened with " + knownOpcodes.Length
            + " opcode(s), " + hidden.Count + " initially hidden, cellWidth="
            + CellWidth.ToString("F0", CultureInfo.InvariantCulture), LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OnRowPropertyChanged
    //
    // Forwards a single row's IsHidden flip to the presenter.  Only
    // IsHidden is mutable on the row; other property changes are
    // ignored.  The presenter call updates both the per-opcode hide set
    // and every matching row's IsHidden flag in the trace list.
    //
    // sender:  The OpcodeManageRow whose property changed.
    // e:       The property changed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OpcodeManageRow.IsHidden))
        {
            return;
        }

        OpcodeManageRow? row = sender as OpcodeManageRow;
        if (row == null)
        {
            return;
        }

        _presenter.SetOpcodesHidden(new[] { row.OpcodeValue }, row.IsHidden);

        DebugLog.Write(LogChannel.InferenceDebug,
            "OpcodeManageWindow.OnRowPropertyChanged: opcode=" + row.OpcodeHex
            + " hidden=" + row.IsHidden, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // MeasureCellWidth
    //
    // Measures the rendered width of each row's "OpcodeHex  OpcodeName"
    // string in the dialog's font and returns the maximum, plus a fixed
    // allowance for the checkbox and surrounding padding.  Used to size
    // each cell so no opcode name is truncated regardless of length.
    //
    // The measurement uses FormattedText against the dialog's resolved
    // typeface and font size, which matches what the cell's TextBlock
    // will render.  Pixels-per-DIP is taken from the window's visual
    // root to honor DPI scaling.
    //
    // rows:  The rows to measure.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private double MeasureCellWidth(List<OpcodeManageRow> rows)
    {
        const double checkboxAndPaddingAllowance = 32.0;
        const double minimumCellWidth = 120.0;

        Typeface typeface = new Typeface(
            FontFamily,
            FontStyle,
            FontWeight,
            FontStretch);

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double widest = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            OpcodeManageRow row = rows[i];
            string text = row.OpcodeHex + "  " + row.OpcodeName;
            FormattedText formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection,
                typeface,
                FontSize,
                Brushes.Black,
                pixelsPerDip);
            if (formatted.Width > widest)
            {
                widest = formatted.Width;
            }
        }

        double cellWidth = widest + checkboxAndPaddingAllowance;
        if (cellWidth < minimumCellWidth)
        {
            cellWidth = minimumCellWidth;
        }
        return cellWidth;
    }
}