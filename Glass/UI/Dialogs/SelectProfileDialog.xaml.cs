using Glass.Data.Models;
using Glass.Data.Repositories;
using System.Windows;

namespace Glass;

public partial class SelectProfileDialog : Window
{
    public string? SelectedProfileName { get; private set; }

    public SelectProfileDialog()
    {
        InitializeComponent();
        LoadProfiles();
    }

    private void LoadProfiles()
    {
        ProfileList.ItemsSource = CharacterSetRepository.GetAllNames();
    }

    private void ProfileList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SelectedProfileName = ProfileList.SelectedItem as string;
        OKButton.IsEnabled = (SelectedProfileName != null);
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}