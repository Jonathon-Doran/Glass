using Glass.Data.Models;
using System.ComponentModel;

namespace Glass.UI.ViewModels;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ProfilePageViewModel
//
// View model for a page row in ProfilePagesDialog.
// Tracks in-profile and start-page state for display and editing.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class ProfilePageViewModel : INotifyPropertyChanged
{
    public int KeyPageId { get; set; }
    public string PageName { get; set; } = string.Empty;
    public KeyboardType Device { get; set; }

    private bool _inProfile;
    public bool InProfile
    {
        get => _inProfile;
        set { _inProfile = value; OnPropertyChanged(nameof(InProfile)); }
    }

    private bool _isStartPage;
    public bool IsStartPage
    {
        get => _isStartPage;
        set { _isStartPage = value; OnPropertyChanged(nameof(IsStartPage)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
