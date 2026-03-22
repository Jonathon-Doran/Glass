using Glass.Core;
using Glass.Data.Models;
using Glass.Data.Repositories;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Glass;

public partial class ManageCommandsDialog : Window
{
    private Command? _selectedCommand;
    private CommandStep? _selectedStep;
    private bool _suppressNameLostFocus = false;
    private string _nameBeforeEdit = string.Empty;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManageCommandsDialog
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ManageCommandsDialog()
    {
        InitializeComponent();
        LoadCommandList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadCommandList
    //
    // Loads all commands from the database into the command list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadCommandList()
    {
        DebugLog.Write("ManageCommandsDialog.LoadCommandList: loading.");

        var repo = new CommandRepository();
        var commands = repo.GetAllCommands();

        CommandListView.ItemsSource = commands;

        DebugLog.Write($"ManageCommandsDialog.LoadCommandList: loaded {commands.Count} commands.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadStepList
    //
    // Loads all steps for the selected command into the step list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadStepList()
    {
        if (_selectedCommand == null)
        {
            DebugLog.Write("ManageCommandsDialog.LoadStepList: no command selected.");
            StepListView.ItemsSource = null;
            return;
        }

        DebugLog.Write($"ManageCommandsDialog.LoadStepList: commandId={_selectedCommand.Id}.");

        var repo = new CommandRepository();
        var command = repo.GetCommand(_selectedCommand.Id);

        if (command == null)
        {
            DebugLog.Write($"ManageCommandsDialog.LoadStepList: command {_selectedCommand.Id} not found.");
            StepListView.ItemsSource = null;
            return;
        }

        _selectedCommand = command;

        StepListView.ItemsSource = command.Steps
            .OrderBy(s => s.Sequence)
            .Select((s, i) => new StepViewModel
            {
                Step = s,
                DisplayText = $"{i + 1}. {s.Type}: {s.Value}" + (s.DelayMs > 0 ? $" (followed by {s.DelayMs}ms delay)" : "")
            }).ToList();

        DebugLog.Write($"ManageCommandsDialog.LoadStepList: loaded {command.Steps.Count} steps.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CommandListView_SelectionChanged
    //
    // Fires when the user selects a command. Loads the command name and steps.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CommandListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandListView.SelectedItem is not Command command)
        {
            DebugLog.Write("ManageCommandsDialog.CommandListView_SelectionChanged: no command selected.");
            _selectedCommand = null;
            CommandNameTextBox.Text = string.Empty;
            StepListView.ItemsSource = null;
            return;
        }

        DebugLog.Write($"ManageCommandsDialog.CommandListView_SelectionChanged: command='{command.Name}'.");

        _selectedCommand = command;
        _nameBeforeEdit = command.Name;
        DeleteCommandButton.IsEnabled = true;
        NewRenameButton.Content = "Rename";
        NewRenameButton.IsEnabled = true;
        CommandNameTextBox.Text = command.Name;
        ClearStepSelection();
        LoadStepList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CommandNameTextBox_TextChanged
    //
    // Enables the New button when text is present.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CommandNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool hasText = !string.IsNullOrWhiteSpace(CommandNameTextBox.Text);
        NewRenameButton.IsEnabled = hasText;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CommandNameTextBox_KeyDown
    //
    // Enter commits the rename. Escape cancels and restores the original name.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CommandNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            DebugLog.Write("ManageCommandsDialog.CommandNameTextBox_KeyDown: Enter pressed, committing rename.");
            _suppressNameLostFocus = true;
            CommitRename();
            _suppressNameLostFocus = false;
            CommandListView.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DebugLog.Write("ManageCommandsDialog.CommandNameTextBox_KeyDown: Escape pressed, cancelling rename.");
            _suppressNameLostFocus = true;
            CommandNameTextBox.Text = _nameBeforeEdit;
            _suppressNameLostFocus = false;
            CommandListView.Focus();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CommandNameTextBox_LostFocus
    //
    // Commits the rename when the name field loses focus, unless suppressed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CommandNameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressNameLostFocus)
        {
            DebugLog.Write("ManageCommandsDialog.CommandNameTextBox_LostFocus: suppressed.");
            return;
        }

        DebugLog.Write("ManageCommandsDialog.CommandNameTextBox_LostFocus: committing.");
        CommitRename();
    }


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CommitRename
    //
    // Validates and saves the current name field text as the selected command's name.
    // Does nothing if no command is selected, the name is empty, or the name is unchanged.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CommitRename()
    {
        if (_selectedCommand == null)
        {
            DebugLog.Write("ManageCommandsDialog.CommitRename: no command selected, nothing to commit.");
            return;
        }

        string newName = CommandNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(newName))
        {
            DebugLog.Write("ManageCommandsDialog.CommitRename: name is empty, restoring original.");
            CommandNameTextBox.Text = _nameBeforeEdit;
            return;
        }

        if (newName == _selectedCommand.Name)
        {
            DebugLog.Write("ManageCommandsDialog.CommitRename: name unchanged, skipping save.");
            return;
        }

        var repo = new CommandRepository();
        var existing = repo.GetAllCommands();

        if (existing.Any(c => c.Name == newName && c.Id != _selectedCommand.Id))
        {
            DebugLog.Write($"ManageCommandsDialog.CommitRename: name '{newName}' already exists, restoring original.");
            MessageBox.Show($"A command named '{newName}' already exists.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
            CommandNameTextBox.Text = _nameBeforeEdit;
            return;
        }

        _selectedCommand.Name = newName;
        repo.SaveCommand(_selectedCommand);

        DebugLog.Write($"ManageCommandsDialog.CommitRename: saved name='{newName}'.");
        int savedId = _selectedCommand.Id;

        _suppressNameLostFocus = true;
        LoadCommandList();
        CommandListView.SelectedItem = (CommandListView.ItemsSource as List<Command>)
            ?.FirstOrDefault(c => c.Id == savedId);
        _suppressNameLostFocus = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearSelection
    //
    // Deselects the current command, clears the name field, and resets the button label to "New".
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearSelection()
    {
        DebugLog.Write("ManageCommandsDialog.ClearSelection: clearing selection.");

        _selectedCommand = null;
        _nameBeforeEdit = string.Empty;

        CommandListView.SelectedItem = null;

        _suppressNameLostFocus = true;
        CommandNameTextBox.Text = string.Empty;
        _suppressNameLostFocus = false;

        StepListView.ItemsSource = null;
        DeleteCommandButton.IsEnabled = false;
        NewRenameButton.Content = "New";
        NewRenameButton.IsEnabled = false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CommandListView_MouseDown
    //
    // Clicking on an already-selected item deselects it.
    // Clicking on empty space in the list also deselects.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CommandListView_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as DependencyObject)
            ?.FindAncestorOrSelf<ListViewItem>();

        if (item == null)
        {
            DebugLog.Write("ManageCommandsDialog.CommandListView_MouseDown: click on empty space, clearing selection.");
            ClearSelection();
            return;
        }

        if ((item.DataContext as Command)?.Id == _selectedCommand?.Id)
        {
            DebugLog.Write("ManageCommandsDialog.CommandListView_MouseDown: click on already-selected item, clearing selection.");
            ClearSelection();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // CommandListView_KeyDown
    //
    // Escape clears the selection.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void CommandListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DebugLog.Write("ManageCommandsDialog.CommandListView_KeyDown: Escape pressed, clearing selection.");
            ClearSelection();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NewRename_Click
    //
    // Creates a new command when nothing is selected, or renames the selected command.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NewRename_Click(object sender, RoutedEventArgs e)
    {
        string name = CommandNameTextBox.Text.Trim();

        if (_selectedCommand == null)
        {
            DebugLog.Write($"ManageCommandsDialog.NewRename_Click: creating command name='{name}'.");

            var repo = new CommandRepository();
            var existing = repo.GetAllCommands();

            if (existing.Any(c => c.Name == name))
            {
                DebugLog.Write($"ManageCommandsDialog.NewRename_Click: name '{name}' already exists.");
                MessageBox.Show($"A command named '{name}' already exists.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var command = new Command { Name = name };
            repo.SaveCommand(command);

            DebugLog.Write($"ManageCommandsDialog.NewRename_Click: created. id={command.Id}.");

            LoadCommandList();
            ClearSelection();
        }
        else
        {
            DebugLog.Write($"ManageCommandsDialog.NewRename_Click: renaming '{_selectedCommand.Name}' to '{name}'.");
            CommitRename();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteCommand_Click
    //
    // Deletes the selected command and all its steps.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteCommand_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCommand == null)
        {
            DebugLog.Write("ManageCommandsDialog.DeleteCommand_Click: no command selected.");
            return;
        }

        DebugLog.Write($"ManageCommandsDialog.DeleteCommand_Click: deleting command id={_selectedCommand.Id}.");

        var result = MessageBox.Show($"Delete command '{_selectedCommand.Name}'?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var repo = new CommandRepository();
        repo.DeleteCommand(_selectedCommand.Id);

        DebugLog.Write($"ManageCommandsDialog.DeleteCommand_Click: deleted.");

        _selectedCommand = null;
        DeleteCommandButton.IsEnabled = false;

        _suppressNameLostFocus = true;
        CommandNameTextBox.Text = string.Empty;
        _suppressNameLostFocus = false;

        StepListView.ItemsSource = null;
        LoadCommandList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NewStep_Click
    //
    // Adds a new step to the selected command.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NewStepUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCommand == null)
        {
            DebugLog.Write("ManageCommandsDialog.NewStep_Click: no command selected.");
            return;
        }

        string type = (StepTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "text";
        int delayMs = int.TryParse(StepDelayTextBox.Text, out int d) ? d : 0;
        string value = StepValueTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            DebugLog.Write("ManageCommandsDialog.NewStep_Click: value is empty, ignoring.");
            MessageBox.Show("Please enter a value for the step.", "No Value", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int nextSequence = ((_selectedCommand.Steps?.Count ?? 0) > 0)
            ? _selectedCommand.Steps!.Max(s => s.Sequence) + 1
            : 1;

        var step = new CommandStep
        {
            CommandId = _selectedCommand.Id,
            Sequence = nextSequence,
            Type = type,
            Value = value,
            DelayMs = delayMs
        };

        DebugLog.Write($"ManageCommandsDialog.NewStep_Click: commandId={_selectedCommand.Id} sequence={nextSequence} type='{type}' value='{value}' delayMs={delayMs}.");

        var repo = new CommandRepository();
        repo.SaveStep(step);

        DebugLog.Write($"ManageCommandsDialog.NewStep_Click: saved. id={step.Id}.");

        LoadStepList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteStep_Click
    //
    // Deletes the selected step.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteStep_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStep == null)
        {
            DebugLog.Write("ManageCommandsDialog.DeleteStep_Click: no step selected.");
            return;
        }

        DebugLog.Write($"ManageCommandsDialog.DeleteStep_Click: deleting step id={_selectedStep.Id}.");

        var repo = new CommandRepository();
        repo.DeleteStep(_selectedStep.Id);

        DebugLog.Write($"ManageCommandsDialog.DeleteStep_Click: deleted.");

        _selectedStep = null;
        ClearStepSelection();
        StepTypeComboBox.SelectedIndex = 0;

        LoadStepList();
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ClearStepSelection
    //
    // Deselects the current step, clears the step editor fields, and resets the button label to "New".
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void ClearStepSelection()
    {
        DebugLog.Write("ManageCommandsDialog.ClearStepSelection: clearing step selection.");

        _selectedStep = null;
        StepListView.SelectedItem = null;
        StepValueTextBox.Text = string.Empty;
        StepDelayTextBox.Text = string.Empty;
        StepTypeComboBox.SelectedIndex = 0;
        NewUpdateStepButton.Content = "New";
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StepListView_MouseDown
    //
    // Clicking an already-selected step deselects it.
    // Clicking empty space in the list also deselects.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void StepListView_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as DependencyObject)
            ?.FindAncestorOrSelf<ListViewItem>();

        if (item == null)
        {
            DebugLog.Write("ManageCommandsDialog.StepListView_MouseDown: click on empty space, clearing selection.");
            ClearStepSelection();
            return;
        }

        if ((item.DataContext as StepViewModel)?.Step.Id == _selectedStep?.Id)
        {
            DebugLog.Write("ManageCommandsDialog.StepListView_MouseDown: click on already-selected step, clearing selection.");
            ClearStepSelection();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StepListView_KeyDown
    //
    // Escape clears the step selection.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void StepListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DebugLog.Write("ManageCommandsDialog.StepListView_KeyDown: Escape pressed, clearing selection.");
            ClearStepSelection();
            e.Handled = true;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NewUpdateStep_Click
    //
    // Creates a new step when nothing is selected, or updates the selected step.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NewUpdateStep_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCommand == null)
        {
            DebugLog.Write("ManageCommandsDialog.NewUpdateStep_Click: no command selected.");
            return;
        }

        string type = (StepTypeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "text";
        string value = StepValueTextBox.Text.Trim();
        int delayMs = int.TryParse(StepDelayTextBox.Text, out int d) ? d : 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            DebugLog.Write("ManageCommandsDialog.NewUpdateStep_Click: value is empty, ignoring.");
            MessageBox.Show("Please enter a value for the step.", "No Value", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var repo = new CommandRepository();

        if (_selectedStep == null)
        {
            int nextSequence = ((_selectedCommand.Steps?.Count ?? 0) > 0)
                ? _selectedCommand.Steps!.Max(s => s.Sequence) + 1
                : 1;

            var step = new CommandStep
            {
                CommandId = _selectedCommand.Id,
                Sequence = nextSequence,
                Type = type,
                Value = value,
                DelayMs = delayMs
            };

            DebugLog.Write($"ManageCommandsDialog.NewUpdateStep_Click: creating step commandId={_selectedCommand.Id} sequence={nextSequence} type='{type}' value='{value}' delayMs={delayMs}.");

            repo.SaveStep(step);

            DebugLog.Write($"ManageCommandsDialog.NewUpdateStep_Click: created. id={step.Id}.");
        }
        else
        {
            _selectedStep.Type = type;
            _selectedStep.Value = value;
            _selectedStep.DelayMs = delayMs;

            DebugLog.Write($"ManageCommandsDialog.NewUpdateStep_Click: updating step id={_selectedStep.Id} type='{type}' value='{value}' delayMs={delayMs}.");

            repo.SaveStep(_selectedStep);

            DebugLog.Write($"ManageCommandsDialog.NewUpdateStep_Click: updated.");
        }

        int? selectedStepId = _selectedStep?.Id;
        LoadStepList();

        if (selectedStepId != null)
        {
            StepListView.SelectedItem = (StepListView.ItemsSource as List<StepViewModel>)
                ?.FirstOrDefault(s => s.Step.Id == selectedStepId);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MoveStepUp_Click
    //
    // Moves the selected step up one position in the sequence.
    // Uses a temporary sequence value to avoid UNIQUE constraint violations during the swap.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MoveStepUp_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStep == null || _selectedCommand == null)
        {
            DebugLog.Write("ManageCommandsDialog.MoveStepUp_Click: no step or command selected.");
            return;
        }

        var steps = _selectedCommand.Steps.OrderBy(s => s.Sequence).ToList();
        int index = steps.FindIndex(s => s.Id == _selectedStep.Id);

        if (index <= 0)
        {
            DebugLog.Write("ManageCommandsDialog.MoveStepUp_Click: already at top.");
            return;
        }

        DebugLog.Write($"ManageCommandsDialog.MoveStepUp_Click: moving step id={_selectedStep.Id} up.");

        var repo = new CommandRepository();
        int seqA = steps[index].Sequence;
        int seqB = steps[index - 1].Sequence;
        int tempSeq = steps.Max(s => s.Sequence) + 1;

        steps[index].Sequence = tempSeq;
        repo.SaveStep(steps[index]);
        DebugLog.Write($"ManageCommandsDialog.MoveStepUp_Click: moved step id={steps[index].Id} to temp sequence {tempSeq}.");

        steps[index - 1].Sequence = seqA;
        repo.SaveStep(steps[index - 1]);
        DebugLog.Write($"ManageCommandsDialog.MoveStepUp_Click: moved step id={steps[index - 1].Id} to sequence {seqA}.");

        steps[index].Sequence = seqB;
        repo.SaveStep(steps[index]);
        DebugLog.Write($"ManageCommandsDialog.MoveStepUp_Click: moved step id={steps[index].Id} to sequence {seqB}.");

        int selectedStepId = _selectedStep.Id;

        LoadStepList();

        StepListView.SelectedItem = (StepListView.ItemsSource as List<StepViewModel>)
       ?.FirstOrDefault(s => s.Step.Id == selectedStepId);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MoveStepDown_Click
    //
    // Moves the selected step down one position in the sequence.
    // Uses a temporary sequence value to avoid UNIQUE constraint violations during the swap.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MoveStepDown_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedStep == null || _selectedCommand == null)
        {
            DebugLog.Write("ManageCommandsDialog.MoveStepDown_Click: no step or command selected.");
            return;
        }

        var steps = _selectedCommand.Steps.OrderBy(s => s.Sequence).ToList();
        int index = steps.FindIndex(s => s.Id == _selectedStep.Id);

        if (index >= steps.Count - 1)
        {
            DebugLog.Write("ManageCommandsDialog.MoveStepDown_Click: already at bottom.");
            return;
        }

        DebugLog.Write($"ManageCommandsDialog.MoveStepDown_Click: moving step id={_selectedStep.Id} down.");

        var repo = new CommandRepository();
        int seqA = steps[index].Sequence;
        int seqB = steps[index + 1].Sequence;
        int tempSeq = steps.Max(s => s.Sequence) + 1;

        steps[index].Sequence = tempSeq;
        repo.SaveStep(steps[index]);
        DebugLog.Write($"ManageCommandsDialog.MoveStepDown_Click: moved step id={steps[index].Id} to temp sequence {tempSeq}.");

        steps[index + 1].Sequence = seqA;
        repo.SaveStep(steps[index + 1]);
        DebugLog.Write($"ManageCommandsDialog.MoveStepDown_Click: moved step id={steps[index + 1].Id} to sequence {seqA}.");

        steps[index].Sequence = seqB;
        repo.SaveStep(steps[index]);
        DebugLog.Write($"ManageCommandsDialog.MoveStepDown_Click: moved step id={steps[index].Id} to sequence {seqB}.");

        int selectedStepId = _selectedStep.Id;

        LoadStepList();

        StepListView.SelectedItem = (StepListView.ItemsSource as List<StepViewModel>)
            ?.FirstOrDefault(s => s.Step.Id == selectedStepId);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StepListView_SelectionChanged
    //
    // Fires when the user selects a step. Loads the step into the editor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void StepListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StepListView.SelectedItem is not StepViewModel item)
        {
            DebugLog.Write("ManageCommandsDialog.StepListView_SelectionChanged: no step selected.");
            _selectedStep = null;
            return;
        }

        DebugLog.Write($"ManageCommandsDialog.StepListView_SelectionChanged: step={item.Step.Sequence}.");

        _selectedStep = item.Step;
        NewUpdateStepButton.Content = "Update";

        StepTypeComboBox.SelectedItem = StepTypeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content.ToString() == item.Step.Type);
        StepValueTextBox.Text = item.Step.Value;
        StepDelayTextBox.Text = item.Step.DelayMs.ToString();

        if (_selectedStep.Type == "pageload" && int.TryParse(_selectedStep.Value, out int pageId))
        {
            StepValueTextBox.Text = _selectedStep.Value;
            DebugLog.Write($"ManageCommandsDialog.StepListView_SelectionChanged: resolved pageload id={pageId} to '{StepValueTextBox.Text}'.");
        }
        else
        {
            StepValueTextBox.Text = _selectedStep.Value;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Close_Click
    //
    // Closes the dialog.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ManageCommandsDialog.Close_Click: closing.");
        Close();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // StepViewModel
    //
    // View model for a command step displayed in the step list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public class StepViewModel
    {
        public CommandStep Step { get; set; } = null!;
        public string DisplayText { get; set; } = string.Empty;
    }
}