using Glass.Core;
using Glass.Data.Models;
using System.ComponentModel;
using System.Windows;

namespace Glass.UI.ViewModels;

using Monitor = Glass.Data.Models.Monitor;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// LayoutMonitorViewModel
//
// View model for a monitor participating in a window layout.
// Used exclusively in the Window Layout tab of the profile editor.
// Combines a hardware Monitor record with layout-specific settings
// and UI state for the slot preview canvas.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public class LayoutMonitorViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private int _slotWidth;
    private List<Rect> _slotRectangles = new();
    private int _numSlots;
    private string _selectedResolution = "1920x1080";

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Monitor
    //
    // The physical monitor record. Provides hardware facts — adapter name,
    // resolution, orientation — for display in the editor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public Monitor Monitor { get; set; } = new();

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // LayoutPosition
    //
    // The position of this monitor within the layout.
    // Position 1 is the primary monitor reserved for full-size client windows.
    // Positions 2+ are slot monitors.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int LayoutPosition { get; set; }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SlotWidth
    //
    // The width in pixels of a single game window slot on this monitor.
    // The only user-configurable layout property per monitor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int SlotWidth
    {
        get => _slotWidth;
        set
        {
            if (_slotWidth != value)
            {
                _slotWidth = value;
                OnPropertyChanged(nameof(SlotWidth));
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // IsSelected
    //
    // True when this monitor is currently selected in the profile editor UI.
    // Pure UI state — not persisted.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SlotRectangles
    //
    // Full-size slot rectangles computed for this monitor's grid.
    // Used for the slot preview canvas and when saving the layout.
    // Pure UI computation state — not persisted.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public List<Rect> SlotRectangles
    {
        get => _slotRectangles;
        set
        {
            _slotRectangles = value;
            OnPropertyChanged(nameof(SlotRectangles));
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // NumSlots
    //
    // The total number of slots that fit on this monitor given its resolution and SlotWidth.
    // Computed at runtime — not persisted.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int NumSlots
    {
        get => _numSlots;
        set
        {
            _numSlots = value;
            OnPropertyChanged(nameof(NumSlots));
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // SelectedResolution
    //
    // Resolution string for the resolution combo box e.g. "3840x2160".
    // UI-only — used when manually configuring a monitor not yet in the database.
    // When loading from MonitorRepository, Width and Height are used directly.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public string SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (_selectedResolution != value)
            {
                _selectedResolution = value;
                OnPropertyChanged(nameof(SelectedResolution));
            }
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OverlayCanvasWidth
    //
    // Scaled width of the slot preview canvas.
    // Computed from Monitor.Width / LayoutConstants.ScalingFactor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int OverlayCanvasWidth => (int)Math.Round(Monitor.Width / LayoutConstants.ScalingFactor);

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OverlayCanvasHeight
    //
    // Scaled height of the slot preview canvas.
    // Computed from Monitor.Height / LayoutConstants.ScalingFactor.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public int OverlayCanvasHeight => (int)Math.Round(Monitor.Height / LayoutConstants.ScalingFactor);

    public event PropertyChangedEventHandler? PropertyChanged;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnPropertyChanged
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AdjustMonitorDimensions
    //
    // Parses the SelectedResolution string and updates Monitor.Width and Monitor.Height
    // to reflect the current resolution and orientation selections.
    // Also notifies the UI that OverlayCanvasWidth and OverlayCanvasHeight have changed.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public void AdjustMonitorDimensions()
    {
        string[] dimensions = SelectedResolution.Split('x');
        if ((dimensions.Length != 2) ||
            !int.TryParse(dimensions[0], out int width) ||
            !int.TryParse(dimensions[1], out int height))
        {
            DebugLog.Write($"LayoutMonitorViewModel.AdjustMonitorDimensions: invalid resolution '{SelectedResolution}'.");
            return;
        }

        if (Monitor.Orientation == MonitorOrientation.Portrait)
        {
            Monitor.Width = Math.Min(width, height);
            Monitor.Height = Math.Max(width, height);
        }
        else
        {
            Monitor.Width = Math.Max(width, height);
            Monitor.Height = Math.Min(width, height);
        }

        OnPropertyChanged(nameof(OverlayCanvasWidth));
        OnPropertyChanged(nameof(OverlayCanvasHeight));

        DebugLog.Write($"LayoutMonitorViewModel.AdjustMonitorDimensions: resolution='{SelectedResolution}' orientation={Monitor.Orientation} width={Monitor.Width} height={Monitor.Height}.");
    }
}