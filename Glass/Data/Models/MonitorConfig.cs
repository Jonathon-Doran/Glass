using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using Glass.Core;

namespace Glass.Data.Models;

// Describes a monitor used in a window layout, including its resolution,
// orientation, and the slot rectangles calculated for it.
public class MonitorConfig : INotifyPropertyChanged
{
    private int _monitorNumber;
    private int _monitorWidth;
    private int _monitorHeight;
    private int _overlayCanvasWidth;
    private int _overlayCanvasHeight;
    private string _orientation = "Landscape";
    private string _selectedResolution = "1920x1080";
    private int _preferredWidth = 0;
    private int _numSlots = 0;
    private bool _isSelectedForConfiguration;
    private string _deviceName = string.Empty;
    private int _deviceIndex;
    private float _dpiScale;
    private List<Rect> _slotRectangles = new();

    public int MonitorNumber
    {
        get => _monitorNumber;
        set { _monitorNumber = value; OnPropertyChanged(nameof(MonitorNumber)); }
    }

    public int MonitorWidth
    {
        get => _monitorWidth;
        set
        {
            if (_monitorWidth != value)
            {
                _monitorWidth = value;
                OnPropertyChanged(nameof(MonitorWidth));
                AdjustMonitorDimensions();
            }
        }
    }

    public int MonitorHeight
    {
        get => _monitorHeight;
        set
        {
            if (_monitorHeight != value)
            {
                _monitorHeight = value;
                OnPropertyChanged(nameof(MonitorHeight));
                AdjustMonitorDimensions();
            }
        }
    }

    public int OverlayCanvasWidth
    {
        get => _overlayCanvasWidth;
        set { _overlayCanvasWidth = value; OnPropertyChanged(nameof(OverlayCanvasWidth)); }
    }

    public int OverlayCanvasHeight
    {
        get => _overlayCanvasHeight;
        set { _overlayCanvasHeight = value; OnPropertyChanged(nameof(OverlayCanvasHeight)); }
    }

    public string Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                OnPropertyChanged(nameof(Orientation));
                AdjustMonitorDimensions();
            }
        }
    }

    public string SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (_selectedResolution != value)
            {
                _selectedResolution = value;
                OnPropertyChanged(nameof(SelectedResolution));
                AdjustMonitorDimensions();
            }
        }
    }

    public int DeviceIndex
    {
        get => _deviceIndex;
        set { _deviceIndex = value; OnPropertyChanged(nameof(DeviceIndex)); }
    }

    public float DpiScale
    {
        get => _dpiScale;
        set { _dpiScale = value; OnPropertyChanged(nameof(DpiScale)); }
    }

    public int PreferredWidth
    {
        get => _preferredWidth;
        set { _preferredWidth = value; OnPropertyChanged(nameof(PreferredWidth)); }
    }

    public int NumSlots
    {
        get => _numSlots;
        set { _numSlots = value; OnPropertyChanged(nameof(NumSlots)); }
    }

    public List<Rect> SlotRectangles
    {
        get => _slotRectangles;
        set { _slotRectangles = value; OnPropertyChanged(nameof(SlotRectangles)); }
    }

    public string DeviceName
    {
        get => _deviceName;
        set { _deviceName = value; OnPropertyChanged(nameof(DeviceName)); }
    }

    public bool IsSelectedForConfiguration
    {
        get => _isSelectedForConfiguration;
        set { _isSelectedForConfiguration = value; OnPropertyChanged(nameof(IsSelectedForConfiguration)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Recalculates monitor pixel dimensions and overlay canvas size based on
    // the current resolution and orientation selections.
    public void AdjustMonitorDimensions()
    {
        string[] dimensions = _selectedResolution.Split('x');
        if ((dimensions.Length != 2) ||
            !int.TryParse(dimensions[0], out int width) ||
            !int.TryParse(dimensions[1], out int height))
        {
            throw new ArgumentException("Invalid resolution format. Use 'WidthxHeight'.");
        }

        if (_orientation == "Portrait")
        {
            _monitorWidth = Math.Min(width, height);
            _monitorHeight = Math.Max(width, height);
        }
        else if (_orientation == "Landscape")
        {
            _monitorWidth = Math.Max(width, height);
            _monitorHeight = Math.Min(width, height);
        }
        else
        {
            throw new ArgumentException("Invalid orientation. Use 'Portrait' or 'Landscape'.");
        }

        OverlayCanvasWidth = (int)Math.Round(_monitorWidth / LayoutConstants.ScalingFactor);
        OverlayCanvasHeight = (int)Math.Round(_monitorHeight / LayoutConstants.ScalingFactor);

        OnPropertyChanged(nameof(MonitorWidth));
        OnPropertyChanged(nameof(MonitorHeight));
    }
}
