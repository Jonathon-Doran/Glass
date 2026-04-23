using System;
using System.Windows;
using Glass.Core;
using Glass.Data.Models;

namespace Glass.ClientUI;

///////////////////////////////////////////////////////////////////////////////////////////////
// NewCharacterDialog
//
// Modal dialog for creating a new character. The server is passed in from the
// profile dialog and cannot be changed. The operator provides the character
// name, class, and account ID.
///////////////////////////////////////////////////////////////////////////////////////////////
public partial class NewCharacterDialog : Window
{
    ///////////////////////////////////////////////////////////////////////////////////////////
    // CreatedCharacter
    //
    // The character created by this dialog. Valid after the dialog closes with OK.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public Character CreatedCharacter { get; private set; } = new Character();

    ///////////////////////////////////////////////////////////////////////////////////////////
    // NewCharacterDialog
    //
    // Constructs the dialog and initializes controls. The server field is set
    // from the caller and is read-only.
    //
    // server:  The server name for the new character.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public NewCharacterDialog(string server)
    {
        InitializeComponent();
        CharacterServer.Text = server;
        PopulateClassComboBox();

        CharacterName.TextChanged += (s, e) => ValidateOK();
        AccountIdBox.TextChanged += (s, e) => ValidateOK();
        ClassComboBox.SelectionChanged += (s, e) => ValidateOK();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // PopulateClassComboBox
    //
    // Fills the class combo box with all EQClass enum values.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void PopulateClassComboBox()
    {
        foreach (EQClass eqClass in Enum.GetValues(typeof(EQClass)))
        {
            ClassComboBox.Items.Add(eqClass);
        }
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ValidateOK
    //
    // Enables the OK button only when all fields are filled in and the account ID
    // is a valid integer.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void ValidateOK()
    {
        bool nameValid = !string.IsNullOrWhiteSpace(CharacterName.Text);
        bool classValid = ClassComboBox.SelectedItem != null;
        bool accountValid = int.TryParse(AccountIdBox.Text, out int _);

        ButtonOK.IsEnabled = nameValid && classValid && accountValid;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_OK_Click
    //
    // Handles the OK button click. Creates the character object from the form
    // values and closes the dialog with a positive result.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_OK_Click(object sender, RoutedEventArgs e)
    {
        CreatedCharacter = new Character
        {
            Name = CharacterName.Text.Trim(),
            Server = CharacterServer.Text,
            Class = (EQClass)ClassComboBox.SelectedItem,
            AccountId = int.Parse(AccountIdBox.Text.Trim())
        };

        DebugLog.Write("NewCharacterDialog: created character="
            + CreatedCharacter.Name
            + " server=" + CreatedCharacter.Server
            + " class=" + CreatedCharacter.Class
            + " accountId=" + CreatedCharacter.AccountId);

        DialogResult = true;
        Close();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // Button_Cancel_Click
    //
    // Handles the Cancel button click. Closes the dialog with a negative result.
    //
    // sender:  The button that raised the event.
    // e:       Event arguments.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private void Button_Cancel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("NewCharacterDialog: cancelled");
        DialogResult = false;
        Close();
    }
}
