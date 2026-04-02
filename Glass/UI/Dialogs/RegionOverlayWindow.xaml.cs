using Glass.Core;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Glass.UI.Dialogs;

//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// RegionOverlayWindow
//
// A borderless, translucent overlay window with visible resize handles at each corner.
// The user can drag the window via its content area and resize via corner handles.
// Border and handle colors are configurable via dependency properties.
// Fires RegionChanged event whenever the position or size changes.
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
public partial class RegionOverlayWindow : Window
{
    private Point _dragStartPoint;
    private Point _resizeStartPoint;
    private Size _resizeStartSize;
    private double _resizeStartWindowLeft;
    private double _resizeStartWindowTop;
    private bool _isDragging = false;
    private ResizeCorner _currentResizeCorner = ResizeCorner.None;
    private readonly int _minWidth = 30;
    private readonly int _minHeight = 30;

    private enum ResizeCorner
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RegionChangedEventArgs
    //
    // Carries the current screen coordinates and dimensions of the overlay region.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public class RegionChangedEventArgs : EventArgs
    {
        public double ScreenX { get; init; }
        public double ScreenY { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
    }

    public event EventHandler<RegionChangedEventArgs>? RegionChanged;

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // BorderColor dependency property
    //
    // The color of the window border. Defaults to white.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty BorderColorProperty =
        DependencyProperty.Register(
            nameof(BorderColor),
            typeof(Brush),
            typeof(RegionOverlayWindow),
            new PropertyMetadata(Brushes.White, OnBorderColorChanged));

    public Brush BorderColor
    {
        get
        {
            return (Brush)GetValue(BorderColorProperty);
        }
        set
        {
            SetValue(BorderColorProperty, value);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnBorderColorChanged
    //
    // Updates the main border brush when the BorderColor property changes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static void OnBorderColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        RegionOverlayWindow window = (RegionOverlayWindow)d;
        if (window.MainBorder != null)
        {
            window.MainBorder.BorderBrush = (Brush)e.NewValue;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // HandleColor dependency property
    //
    // The color of the corner resize handles. Defaults to white.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty HandleColorProperty =
        DependencyProperty.Register(
            nameof(HandleColor),
            typeof(Brush),
            typeof(RegionOverlayWindow),
            new PropertyMetadata(Brushes.White, OnHandleColorChanged));

    public Brush HandleColor
    {
        get
        {
            return (Brush)GetValue(HandleColorProperty);
        }
        set
        {
            SetValue(HandleColorProperty, value);
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // AspectRatio dependency property
    //
    // Optional aspect ratio constraint for resizing. Set to width/height ratio to lock,
    // or 0.0 to allow free resize (default).
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public static readonly DependencyProperty AspectRatioProperty =
        DependencyProperty.Register(
            nameof(AspectRatio),
            typeof(double),
            typeof(RegionOverlayWindow),
            new PropertyMetadata(0.0));

    public double AspectRatio
    {
        get { return (double)GetValue(AspectRatioProperty); }
        set { SetValue(AspectRatioProperty, value); }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // OnHandleColorChanged
    //
    // Updates all corner handle brushes when the HandleColor property changes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private static void OnHandleColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        RegionOverlayWindow window = (RegionOverlayWindow)d;
        Brush brush = (Brush)e.NewValue;

        if (window.TopLeftHandle != null)
        {
            window.TopLeftHandle.Stroke = brush;
        }
        if (window.TopRightHandle != null)
        {
            window.TopRightHandle.Stroke = brush;
        }
        if (window.BottomLeftHandle != null)
        {
            window.BottomLeftHandle.Stroke = brush;
        }
        if (window.BottomRightHandle != null)
        {
            window.BottomRightHandle.Stroke = brush;
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // RegionOverlayWindow constructor
    //
    // Initializes the window and wires up mouse event handlers.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public RegionOverlayWindow()
    {
        InitializeComponent();

        this.MouseMove += Window_MouseMove;
        this.MouseLeftButtonUp += Window_MouseLeftButtonUp;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Window_MouseLeftButtonDown
    //
    // Begins dragging the window when the user clicks on the content area.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentResizeCorner == ResizeCorner.None)
        {
            _isDragging = true;
            GetCursorPos(out Win32Point cursorPos);
            _dragStartPoint = new Point(cursorPos.X, cursorPos.Y);
            CaptureMouse();
        }
    }
    private void TopLeftHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _currentResizeCorner = ResizeCorner.TopLeft;
        GetCursorPos(out Win32Point cursorPos);
        _resizeStartPoint = new Point(cursorPos.X, cursorPos.Y);
        _resizeStartSize = new Size(this.Width, this.Height);
        _resizeStartWindowLeft = this.Left;
        _resizeStartWindowTop = this.Top;
        CaptureMouse();
        e.Handled = true;
    }

    private void TopRightHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _currentResizeCorner = ResizeCorner.TopRight;
        GetCursorPos(out Win32Point cursorPos);
        _resizeStartPoint = new Point(cursorPos.X, cursorPos.Y);
        _resizeStartSize = new Size(this.Width, this.Height);
        _resizeStartWindowLeft = this.Left;
        _resizeStartWindowTop = this.Top;
        CaptureMouse();
        e.Handled = true;
    }

    private void BottomLeftHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _currentResizeCorner = ResizeCorner.BottomLeft;
        GetCursorPos(out Win32Point cursorPos);
        _resizeStartPoint = new Point(cursorPos.X, cursorPos.Y);
        _resizeStartSize = new Size(this.Width, this.Height);
        _resizeStartWindowLeft = this.Left;
        _resizeStartWindowTop = this.Top;
        CaptureMouse();
        e.Handled = true;
    }

    private void BottomRightHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _currentResizeCorner = ResizeCorner.BottomRight;
        GetCursorPos(out Win32Point cursorPos);
        _resizeStartPoint = new Point(cursorPos.X, cursorPos.Y);
        _resizeStartSize = new Size(this.Width, this.Height);
        _resizeStartWindowLeft = this.Left;
        _resizeStartWindowTop = this.Top;
        CaptureMouse();
        e.Handled = true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Window_MouseMove
    //
    // Handles dragging or resizing based on the current mode.
    // Fires RegionChanged notification whenever position or size changes.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        bool regionChanged = false;

        if (_isDragging)
        {
            GetCursorPos(out Win32Point cursorPos);
            Point currentPoint = new Point(cursorPos.X, cursorPos.Y);
            double deltaX = currentPoint.X - _dragStartPoint.X;
            double deltaY = currentPoint.Y - _dragStartPoint.Y;

            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                Matrix transform = source.CompositionTarget.TransformFromDevice;
                Point logicalDelta = transform.Transform(new Point(deltaX, deltaY));
                this.Left += logicalDelta.X;
                this.Top += logicalDelta.Y;
            }

            _dragStartPoint = currentPoint;
            regionChanged = true;
        }
        else if (_currentResizeCorner != ResizeCorner.None)
        {
            GetCursorPos(out Win32Point cursorPos);
            Point currentPoint = new Point(cursorPos.X, cursorPos.Y);
            double deltaX = currentPoint.X - _resizeStartPoint.X;
            double deltaY = currentPoint.Y - _resizeStartPoint.Y;

            PresentationSource source = PresentationSource.FromVisual(this);
            if (source == null)
            {
                return;
            }

            Matrix transform = source.CompositionTarget.TransformFromDevice;
            Point logicalDelta = transform.Transform(new Point(deltaX, deltaY));

            double newWidth = _resizeStartSize.Width;
            double newHeight = _resizeStartSize.Height;
            double newLeft = this.Left;
            double newTop = this.Top;

            double ldX = logicalDelta.X;
            double ldY = logicalDelta.Y;

            // If aspect ratio is locked, compute the constrained delta.
            if (AspectRatio > 0.0)
            {
                // Determine which axis has the larger absolute delta and drive from that.
                double absDeltaX = Math.Abs(ldX);
                double absDeltaY = Math.Abs(ldY);

                if (absDeltaX >= absDeltaY)
                {
                    // Width drives — compute height from width delta.
                    ldY = ldX / AspectRatio;
                }
                else
                {
                    // Height drives — compute width from height delta.
                    ldX = ldY * AspectRatio;
                }
            }

            if (_currentResizeCorner == ResizeCorner.TopLeft)
            {
                newWidth = _resizeStartSize.Width - ldX;
                newHeight = _resizeStartSize.Height - ldY;
                newLeft = _resizeStartWindowLeft + ldX;
                newTop = _resizeStartWindowTop + ldY;
            }
            else if (_currentResizeCorner == ResizeCorner.TopRight)
            {
                newWidth = _resizeStartSize.Width + ldX;
                newHeight = _resizeStartSize.Height - ldY;
                newTop = _resizeStartWindowTop + ldY;
            }
            else if (_currentResizeCorner == ResizeCorner.BottomLeft)
            {
                newWidth = _resizeStartSize.Width - ldX;
                newHeight = _resizeStartSize.Height + ldY;
                newLeft = _resizeStartWindowLeft + ldX;
            }
            else if (_currentResizeCorner == ResizeCorner.BottomRight)
            {
                newWidth = _resizeStartSize.Width + ldX;
                newHeight = _resizeStartSize.Height + ldY;
            }

            if (newWidth >= _minWidth && newHeight >= _minHeight)
            {
                this.Width = newWidth;
                this.Height = newHeight;
                this.Left = newLeft;
                this.Top = newTop;
                regionChanged = true;
            }
        }

        if (regionChanged)
        {
            FireRegionChanged();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Window_MouseLeftButtonUp
    //
    // Ends dragging or resizing.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
        else if (_currentResizeCorner != ResizeCorner.None)
        {
            _currentResizeCorner = ResizeCorner.None;
            ReleaseMouseCapture();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // FireRegionChanged
    //
    // Raises the RegionChanged event with current position and size.
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    private void FireRegionChanged()
    {
        PresentationSource source = PresentationSource.FromVisual(this);
        if (source == null)
        {
            return;
        }

        Matrix transform = source.CompositionTarget.TransformToDevice;

        Point topLeft = new Point(this.Left, this.Top);
        Point physicalTopLeft = transform.Transform(topLeft);

        Point size = new Point(this.Width, this.Height);
        Point physicalSize = transform.Transform(size);

        RegionChanged?.Invoke(this, new RegionChangedEventArgs
        {
            ScreenX = physicalTopLeft.X,
            ScreenY = physicalTopLeft.Y,
            Width = physicalSize.X,
            Height = physicalSize.Y
        });
    }
}