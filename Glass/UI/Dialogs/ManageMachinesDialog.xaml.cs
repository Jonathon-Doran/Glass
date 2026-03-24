using Glass.Core;
using Glass.Data.Models;
using Glass.Data.Repositories;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Glass;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ManageMachinesDialog
//
// Allows the user to create, edit and delete machine definitions.
// Each machine has a name and a list of connected HID keyboard devices.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public partial class ManageMachinesDialog : Window
{
    private Machine? _selectedMachine;
    private ObservableCollection<MachineDevice> _devices = new();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ManageMachinesDialog
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ManageMachinesDialog()
    {
        InitializeComponent();
        DeviceListView.ItemsSource = _devices;
        LoadMachineList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadMachineList
    //
    // Loads all machines from the database into the machine list.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadMachineList()
    {
        DebugLog.Write("ManageMachinesDialog.LoadMachineList: loading.");

        var repo = new MachineRepository();
        var machines = repo.GetAll();

        MachineListView.ItemsSource = machines;

        DebugLog.Write($"ManageMachinesDialog.LoadMachineList: loaded {machines.Count} machines.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadDeviceList
    //
    // Loads the device list for the selected machine.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadDeviceList()
    {
        _devices.Clear();

        if (_selectedMachine == null)
        {
            return;
        }

        DebugLog.Write($"ManageMachinesDialog.LoadDeviceList: machineId={_selectedMachine.Id}.");

        foreach (var device in _selectedMachine.Devices)
        {
            _devices.Add(device);
        }

        DebugLog.Write($"ManageMachinesDialog.LoadDeviceList: loaded {_devices.Count} devices.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MachineListView_SelectionChanged
    //
    // Fires when the user selects a machine. Loads the machine editor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MachineListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MachineListView.SelectedItem is not Machine machine)
        {
            DebugLog.Write("ManageMachinesDialog.MachineListView_SelectionChanged: no machine selected.");
            _selectedMachine = null;
            MachineNameTextBox.Text = string.Empty;
            _devices.Clear();
            DeleteMachineButton.IsEnabled = false;
            AddDeviceButton.IsEnabled = false;
            NewMachineButton.Content = "New";
            return;
        }

        DebugLog.Write($"ManageMachinesDialog.MachineListView_SelectionChanged: machine='{machine.Name}'.");

        _selectedMachine = machine;
        DeleteMachineButton.IsEnabled = true;
        AddDeviceButton.IsEnabled = true;
        NewMachineButton.Content = "Update";

        MachineNameTextBox.Text = machine.Name;

        LoadDeviceList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // MachineNameTextBox_TextChanged
    //
    // Enables the New button when text is present.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void MachineNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NewMachineButton.IsEnabled = !string.IsNullOrWhiteSpace(MachineNameTextBox.Text);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveMachineName
    //
    // Saves the current name to the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveMachineName()
    {
        if (_selectedMachine == null)
        {
            return;
        }

        string name = MachineNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            DebugLog.Write("ManageMachinesDialog.SaveMachineName: name is empty, restoring.");
            MachineNameTextBox.Text = _selectedMachine.Name;
            return;
        }

        if (name == _selectedMachine.Name)
        {
            return;
        }

        DebugLog.Write($"ManageMachinesDialog.SaveMachineName: saving name='{name}'.");

        _selectedMachine.Name = name;
        var repo = new MachineRepository();
        repo.Save(_selectedMachine);

        int savedId = _selectedMachine.Id;
        LoadMachineList();
        MachineListView.SelectedItem = (MachineListView.ItemsSource as List<Machine>)
            ?.FirstOrDefault(m => m.Id == savedId);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NewMachine_Click
    //
    // Creates a new machine with the current hostname as default name.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void NewMachine_Click(object sender, RoutedEventArgs e)
    {
        string name = MachineNameTextBox.Text.Trim();

        if (_selectedMachine != null)
        {
            DebugLog.Write($"ManageMachinesDialog.NewMachine_Click: updating machine id={_selectedMachine.Id} name='{name}'.");
            _selectedMachine.Name = name;
            var repo = new MachineRepository();
            repo.Save(_selectedMachine);
            int savedId = _selectedMachine.Id;
            LoadMachineList();
            MachineListView.SelectedItem = (MachineListView.ItemsSource as List<Machine>)
                ?.FirstOrDefault(m => m.Id == savedId);
            return;
        }

        DebugLog.Write($"ManageMachinesDialog.NewMachine_Click: creating machine name='{name}'.");
        var machine = new Machine { Name = name };
        var updateRepo = new MachineRepository();
        updateRepo.Save(machine);
        DebugLog.Write($"ManageMachinesDialog.NewMachine_Click: created id={machine.Id}.");
        LoadMachineList();
        MachineListView.SelectedItem = (MachineListView.ItemsSource as List<Machine>)
            ?.FirstOrDefault(m => m.Id == machine.Id);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // DeleteMachine_Click
    //
    // Deletes the selected machine.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void DeleteMachine_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMachine == null)
        {
            return;
        }

        DebugLog.Write($"ManageMachinesDialog.DeleteMachine_Click: deleting machine id={_selectedMachine.Id}.");

        var result = MessageBox.Show($"Delete machine '{_selectedMachine.Name}'?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var repo = new MachineRepository();
        repo.Delete(_selectedMachine.Id);

        DebugLog.Write($"ManageMachinesDialog.DeleteMachine_Click: deleted.");

        _selectedMachine = null;
        LoadMachineList();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AddDevice_Click
    //
    // Adds a new device entry for the selected machine.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMachine == null)
        {
            return;
        }

        DebugLog.Write($"ManageMachinesDialog.AddDevice_Click: machineId={_selectedMachine.Id}.");

        var existing = _devices.Select(d => d.KeyboardType).ToHashSet();
        var available = Enum.GetValues<KeyboardType>()
            .FirstOrDefault(kt => !existing.Contains(kt));

        if (existing.Count >= Enum.GetValues<KeyboardType>().Length)
        {
            DebugLog.Write("ManageMachinesDialog.AddDevice_Click: all device types already added.");
            MessageBox.Show("All device types are already added.", "No More Devices", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var device = new MachineDevice
        {
            MachineId = _selectedMachine.Id,
            KeyboardType = available,
            InstanceCount = 1
        };

        _devices.Add(device);

        DebugLog.Write($"ManageMachinesDialog.AddDevice_Click: added {device.KeyboardType}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RemoveDevice_Click
    //
    // Removes the selected device entry.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void RemoveDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceListView.SelectedItem is not MachineDevice device)
        {
            return;
        }

        DebugLog.Write($"ManageMachinesDialog.RemoveDevice_Click: removing {device.KeyboardType}.");

        _devices.Remove(device);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SaveDevices
    //
    // Saves the current device list to the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SaveDevices()
    {
        if (_selectedMachine == null)
        {
            return;
        }

        DebugLog.Write($"ManageMachinesDialog.SaveDevices: machineId={_selectedMachine.Id} count={_devices.Count}.");

        var repo = new MachineRepository();
        repo.SaveDevices(_selectedMachine.Id, _devices.ToList());
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save_Click
    //
    // Saves all changes to the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ManageMachinesDialog.Save_Click: saving.");

        if (_selectedMachine != null)
        {
            var repo = new MachineRepository();
            repo.SaveDevices(_selectedMachine.Id, _devices.ToList());
        }

        DialogResult = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Cancel_Click
    //
    // Closes without saving.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ManageMachinesDialog.Cancel_Click: cancelled.");
        DialogResult = false;
    }
}
