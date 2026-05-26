using System.ComponentModel;

namespace Inference.Models;

///////////////////////////////////////////////////////////////////////////////////////////////
// OpcodeManageRow
//
// Per-cell view model for the Opcode Manage dialog's wrap-panel.  One
// instance per known wire opcode value at the moment the dialog opens.
// IsHidden is two-way bound to the cell's checkbox; the dialog
// subscribes to PropertyChanged to forward state flips to the presenter.
///////////////////////////////////////////////////////////////////////////////////////////////
public class OpcodeManageRow : INotifyPropertyChanged
{
    private bool _isHidden;

    public event PropertyChangedEventHandler? PropertyChanged;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // OpcodeManageRow (constructor)
    //
    // Captures the cell's display values and initial hide state.
    //
    // opcodeValue:  Wire opcode value.
    // opcodeHex:    Pre-formatted hex string (e.g. "0xdb56").
    // opcodeName:   Resolved opcode name from the patch registry, or
    //               "<Unknown>" when the opcode is not in the patch.
    // isHidden:     Initial value of the cell's checkbox.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public OpcodeManageRow(ushort opcodeValue, string opcodeHex,
        string opcodeName, bool isHidden)
    {
        OpcodeValue = opcodeValue;
        OpcodeHex = opcodeHex;
        OpcodeName = opcodeName;
        _isHidden = isHidden;
    }

    public ushort OpcodeValue { get; }
    public string OpcodeHex { get; }
    public string OpcodeName { get; }

    public bool IsHidden
    {
        get { return _isHidden; }
        set
        {
            if (_isHidden == value)
            {
                return;
            }
            _isHidden = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHidden)));
        }
    }
}