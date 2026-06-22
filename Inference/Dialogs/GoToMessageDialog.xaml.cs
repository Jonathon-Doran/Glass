using Glass.Core.Logging;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Inference.Dialogs;

///////////////////////////////////////////////////////////////////////////////////////////////
// GoToMessageDialog
//
// Modal dialog that collects a target message index from the user.  Displays the current
// message index, the highest reachable index, and an input box, modeled on the Notepad++
// "Go To" dialog.  Confirming parses and clamps the entry into the supplied bounds and exposes
// it through Target; the caller performs the actual navigation.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class GoToMessageDialog : Window
{
    private readonly uint _lowest;
    private readonly uint _highest;

    public uint Target { get; private set; }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // GoToMessageDialog (constructor)
    //
    // Fills the "you are here" and "you can't go further than" lines from the supplied values,
    // stores the bounds for clamping on confirm, seeds the input box with the current index,
    // and places the caret ready for entry.
    //
    // current:  The message index the cursor is currently on, shown as "you are here".
    // lowest:   The lowest message index present, used as the clamp floor.
    // highest:  The highest message index present, shown as the ceiling and used as the clamp
    //           ceiling.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public GoToMessageDialog(uint current, uint lowest, uint highest)
    {
        InitializeComponent();

        _lowest = lowest;
        _highest = highest;
        Target = current;

        CurrentMessageText.Text = current.ToString();
        MaxMessageText.Text = highest.ToString();
        TargetMessageBox.Text = current.ToString();
        TargetMessageBox.SelectAll();

        DebugLog.Write(LogChannel.Opcodes,
            "GoToMessageDialog: opened current=" + current
            + " lowest=" + lowest + " highest=" + highest, LogLevel.Trace);
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TryCommit
    //
    // Parses the input box as an unsigned message index, clamps it into the dialog's bounds,
    // stores it in Target, and reports success.  A blank or non-numeric entry stores nothing
    // and reports failure so the caller can keep the dialog open.
    //
    // returns: True when the entry parsed and Target was set, false otherwise.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private bool TryCommit()
    {
        string entry = TargetMessageBox.Text;

        uint parsed;
        if (!uint.TryParse(entry, out parsed))
        {
            DebugLog.Write(LogChannel.Opcodes,
                "GoToMessageDialog.TryCommit: '" + entry + "' is not a valid index, ignoring",
                LogLevel.Warn);
            return false;
        }

        if (parsed < _lowest)
        {
            parsed = _lowest;
        }
        if (parsed > _highest)
        {
            parsed = _highest;
        }

        Target = parsed;

        DebugLog.Write(LogChannel.Opcodes,
            "GoToMessageDialog.TryCommit: target set to " + parsed, LogLevel.Trace);
        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Go_Click
    //
    // Commits the entry.  On success sets DialogResult true to close the dialog; on a failed
    // parse leaves the dialog open for correction.
    //
    // sender:  The Go button.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Go_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCommit())
        {
            return;
        }

        DialogResult = true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Cancel_Click
    //
    // Closes the dialog without committing.  Target retains the current index passed at
    // construction, but the false DialogResult tells the caller not to navigate.
    //
    // sender:  The Cancel button.
    // e:       Routed event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // TargetMessageBox_KeyDown
    //
    // Enter commits the entry, mirroring the Go button; on a successful commit the dialog
    // closes with DialogResult true.  Escape closes the dialog without committing.  Other keys
    // pass through for normal text entry.
    //
    // sender:  The target text box.
    // e:       Key event args.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void TargetMessageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (TryCommit())
            {
                DialogResult = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
            return;
        }
    }
}
