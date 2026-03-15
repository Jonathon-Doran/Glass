using System.ComponentModel;
using Glass.Core;

namespace Glass.Core;

// Tracks the current layout settings for a character set's window configuration.
public class LayoutManager : INotifyPropertyChanged
{
    private bool _stacked = true;
    private LayoutStrategies _selectedLayoutStrategy = LayoutStrategies.FixedWindowSize;
    private string _currentUIConfiguration = string.Empty;

    public bool Stacked
    {
        get => _stacked;
        set { _stacked = value; OnPropertyChanged(nameof(Stacked)); }
    }

    public LayoutStrategies SelectedLayoutStrategy
    {
        get => _selectedLayoutStrategy;
        set { _selectedLayoutStrategy = value; OnPropertyChanged(nameof(SelectedLayoutStrategy)); }
    }

    public string CurrentUIConfiguration
    {
        get => _currentUIConfiguration;
        set { _currentUIConfiguration = value; OnPropertyChanged(nameof(CurrentUIConfiguration)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}