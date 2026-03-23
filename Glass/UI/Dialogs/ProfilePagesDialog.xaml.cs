using Glass.Core;
using Glass.Data.Models;
using Glass.Data.Repositories;
using Glass.UI.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Glass;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ProfilePagesDialog
//
// Allows the user to associate pages with a profile and designate a start page per device.
// Color coding: teal = in profile, gold = start page, neutral = not in profile.
// Set Start Page mode: toggle button arms mode, next click promotes page to start page.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public partial class ProfilePagesDialog : Window
{
    private readonly int _characterSetId;
    private bool _setStartPageMode = false;
    private ObservableCollection<ProfilePageViewModel> _items = new();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // ProfilePagesDialog
    //
    // characterSetId:  The profile to manage pages for
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public ProfilePagesDialog(int characterSetId)
    {
        InitializeComponent();
        _characterSetId = characterSetId;
        LoadPages();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LoadPages
    //
    // Loads all pages from the database and marks which are associated with the profile.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void LoadPages()
    {
        DebugLog.Write($"ProfilePagesDialog.LoadPages: characterSetId={_characterSetId}.");

        var pageRepo = new KeyPageRepository();
        var profilePageRepo = new ProfilePageRepository();

        var allPages = pageRepo.GetAllPages();
        var profilePages = profilePageRepo.GetPagesForProfile(_characterSetId)
            .ToDictionary(p => p.KeyPageId, p => p);

        _items.Clear();

        foreach (var page in allPages)
        {
            bool inProfile = profilePages.ContainsKey(page.Id);
            bool isStartPage = inProfile && profilePages[page.Id].IsStartPage;

            _items.Add(new ProfilePageViewModel
            {
                KeyPageId = page.Id,
                PageName = page.Name,
                Device = page.Device,
                InProfile = inProfile,
                IsStartPage = isStartPage
            });
        }

        PageListView.ItemsSource = _items;

        DebugLog.Write($"ProfilePagesDialog.LoadPages: loaded {_items.Count} pages.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // PageListView_MouseLeftButtonUp
    //
    // Handles a row click. In normal mode toggles in/out of profile.
    // In Set Start Page mode promotes the clicked page to start page for its device.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void PageListView_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PageListView.SelectedItem is not ProfilePageViewModel item)
        {
            return;
        }

        DebugLog.Write($"ProfilePagesDialog.PageListView_MouseLeftButtonUp: page='{item.PageName}' device='{item.Device}' setStartPageMode={_setStartPageMode}.");

        if (_setStartPageMode)
        {
            if (!item.InProfile)
            {
                DebugLog.Write($"ProfilePagesDialog.PageListView_MouseLeftButtonUp: page not in profile, cannot set as start page.");
                return;
            }

            // Demote current start page for this device
            foreach (var other in _items)
            {
                if ((other.Device == item.Device) && other.IsStartPage)
                {
                    DebugLog.Write($"ProfilePagesDialog.PageListView_MouseLeftButtonUp: demoting '{other.PageName}'.");
                    other.IsStartPage = false;
                }
            }

            item.IsStartPage = true;
            DebugLog.Write($"ProfilePagesDialog.PageListView_MouseLeftButtonUp: promoted '{item.PageName}' to start page.");

            // Exit Set Start Page mode
            _setStartPageMode = false;
            SetStartPageButton.IsChecked = false;
        }
        else
        {
            if (item.IsStartPage)
            {
                DebugLog.Write($"ProfilePagesDialog.PageListView_MouseLeftButtonUp: '{item.PageName}' is start page, cannot remove.");
                return;
            }

            if (item.InProfile)
            {
                item.InProfile = false;
                DebugLog.Write($"ProfilePagesDialog.PageListView_MouseLeftButtonUp: removed '{item.PageName}' from profile.");
            }
            else
            {
                item.InProfile = true;
                DebugLog.Write($"ProfilePagesDialog.PageListView_MouseLeftButtonUp: added '{item.PageName}' to profile.");

                // If this is the first page for this device, make it the start page
                bool hasStartPage = _items.Any(p => (p.Device == item.Device) && p.IsStartPage);
                if (!hasStartPage)
                {
                    item.IsStartPage = true;
                    DebugLog.Write($"ProfilePagesDialog.PageListView_MouseLeftButtonUp: first page for device '{item.Device}', auto-set as start page.");
                }
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SetStartPageButton_Click
    //
    // Toggles Set Start Page mode.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void SetStartPageButton_Click(object sender, RoutedEventArgs e)
    {
        _setStartPageMode = SetStartPageButton.IsChecked == true;
        DebugLog.Write($"ProfilePagesDialog.SetStartPageButton_Click: setStartPageMode={_setStartPageMode}.");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Save_Click
    //
    // Saves the current profile page associations to the database.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write($"ProfilePagesDialog.Save_Click: characterSetId={_characterSetId}.");

        var pages = _items
            .Where(p => p.InProfile)
            .Select(p => new ProfilePage
            {
                CharacterSetId = _characterSetId,
                KeyPageId = p.KeyPageId,
                IsStartPage = p.IsStartPage
            })
            .ToList();

        var repo = new ProfilePageRepository();
        repo.SetPagesForProfile(_characterSetId, pages);

        DebugLog.Write($"ProfilePagesDialog.Save_Click: saved {pages.Count} pages.");
        DialogResult = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Cancel_Click
    //
    // Closes the dialog without saving.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DebugLog.Write("ProfilePagesDialog.Cancel_Click: cancelled.");
        DialogResult = false;
    }
}