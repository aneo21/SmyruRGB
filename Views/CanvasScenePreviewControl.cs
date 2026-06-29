using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SmyruRGB.Views;

public sealed class CanvasScenePreviewControl : Control
{
    public static readonly StyledProperty<CanvasPreviewSceneSnapshot?> SceneProperty =
        AvaloniaProperty.Register<CanvasScenePreviewControl, CanvasPreviewSceneSnapshot?>(nameof(Scene));

    public static readonly StyledProperty<ICommand?> MoveDeviceCommandProperty =
        AvaloniaProperty.Register<CanvasScenePreviewControl, ICommand?>(nameof(MoveDeviceCommand));

    private static readonly IBrush SurfaceBrush = new SolidColorBrush(Color.Parse("#0B1220"));
    private static readonly IBrush DeviceBrush = new SolidColorBrush(Color.FromArgb(220, 15, 23, 42));
    private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.FromArgb(235, 30, 41, 59));
    private static readonly IBrush DeviceOutlineBrush = new SolidColorBrush(Color.Parse("#334155"));
    private static readonly IBrush SelectedOutlineBrush = new SolidColorBrush(Color.Parse("#60A5FA"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#E5E7EB"));
    private static readonly IBrush SecondaryLabelBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly Pen DeviceOutlinePen = new(DeviceOutlineBrush, 1.2);
    private static readonly Pen SelectedOutlinePen = new(SelectedOutlineBrush, 2);

    private string? _selectedDeviceIdentifier;
    private string? _dragDeviceIdentifier;
    private Point _dragPointerOrigin;
    private Point _dragDeviceOrigin;

    public CanvasPreviewSceneSnapshot? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public ICommand? MoveDeviceCommand
    {
        get => GetValue(MoveDeviceCommandProperty);
        set => SetValue(MoveDeviceCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SceneProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = Bounds.Deflate(6);
        context.DrawRectangle(SurfaceBrush, null, bounds, 12);

        CanvasPreviewSceneSnapshot? scene = Scene;
        if (scene is null)
        {
            return;
        }

        foreach (CanvasPreviewBackgroundCellSnapshot cell in scene.BackgroundCells)
        {
            context.DrawRectangle(new SolidColorBrush(cell.Color), null, Project(bounds, cell.Bounds));
        }

        foreach (CanvasPreviewDeviceSnapshot device in scene.Devices)
        {
            DrawDevice(context, bounds, device, string.Equals(device.DeviceIdentifier, _selectedDeviceIdentifier, StringComparison.Ordinal));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Point pointerPosition = e.GetPosition(this);
        CanvasPreviewDeviceSnapshot? device = HitTestDevice(pointerPosition);
        if (device is null)
        {
            _selectedDeviceIdentifier = null;
            InvalidateVisual();
            return;
        }

        _selectedDeviceIdentifier = device.DeviceIdentifier;
        _dragDeviceIdentifier = device.DeviceIdentifier;
        _dragPointerOrigin = pointerPosition;
        _dragDeviceOrigin = new Point(device.Bounds.X, device.Bounds.Y);
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (string.IsNullOrWhiteSpace(_dragDeviceIdentifier)
            || e.Pointer.Captured != this
            || Bounds.Width <= 0
            || Bounds.Height <= 0)
        {
            return;
        }

        CanvasPreviewDeviceSnapshot? device = Scene?.Devices.FirstOrDefault(candidate =>
            string.Equals(candidate.DeviceIdentifier, _dragDeviceIdentifier, StringComparison.Ordinal));
        if (device is null)
        {
            return;
        }

        Point currentPosition = e.GetPosition(this);
        double deltaX = (currentPosition.X - _dragPointerOrigin.X) / Bounds.Width;
        double deltaY = (currentPosition.Y - _dragPointerOrigin.Y) / Bounds.Height;
        double maxLeft = Math.Max(0, 1 - device.Bounds.Width);
        double maxTop = Math.Max(0, 1 - device.Bounds.Height);
        double nextLeft = Math.Clamp(_dragDeviceOrigin.X + deltaX, 0, maxLeft);
        double nextTop = Math.Clamp(_dragDeviceOrigin.Y + deltaY, 0, maxTop);

        ICommand? command = MoveDeviceCommand;
        CanvasDeviceMoveRequest request = new(device.DeviceIdentifier, nextLeft, nextTop);
        if (command?.CanExecute(request) == true)
        {
            command.Execute(request);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragDeviceIdentifier = null;
        e.Pointer.Capture(null);
    }

    private void DrawDevice(DrawingContext context, Rect canvasBounds, CanvasPreviewDeviceSnapshot device, bool isSelected)
    {
        Rect deviceBounds = Project(canvasBounds, device.Bounds);
        context.DrawRectangle(DeviceBrush, isSelected ? SelectedOutlinePen : DeviceOutlinePen, deviceBounds, 14);

        double headerHeight = Math.Min(deviceBounds.Height * 0.17, 28);
        Rect headerBounds = new(deviceBounds.X, deviceBounds.Y, deviceBounds.Width, headerHeight);
        context.DrawRectangle(HeaderBrush, null, headerBounds, 14);
        DrawLabel(context, device.DeviceName, 14, LabelBrush, new Point(headerBounds.X + 10, headerBounds.Y + 6));

        foreach (CanvasPreviewChannelSnapshot channel in device.Channels)
        {
            DrawChannel(context, canvasBounds, channel);
        }
    }

    private void DrawChannel(DrawingContext context, Rect canvasBounds, CanvasPreviewChannelSnapshot channel)
    {
        Rect channelBounds = Project(canvasBounds, channel.Bounds);
        context.DrawRectangle(null, DeviceOutlinePen, channelBounds, 10);
        DrawLabel(context, channel.ChannelLabel, 11, SecondaryLabelBrush, new Point(channelBounds.X + 8, channelBounds.Y + 6));

        Rect shapeBounds = new(
            channelBounds.X + 10,
            channelBounds.Y + 24,
            Math.Max(24, channelBounds.Width - 20),
            Math.Max(24, channelBounds.Height - 32));

        switch (channel.Layout.ShapeKind)
        {
            case DeviceShapeKind.Pizza:
                DrawPizza(context, shapeBounds, channel);
                break;
            case DeviceShapeKind.Keyboard:
                DrawKeyboard(context, shapeBounds, channel);
                break;
            default:
                DrawBar(context, shapeBounds, channel);
                break;
        }
    }

    private static void DrawBar(DrawingContext context, Rect bounds, CanvasPreviewChannelSnapshot preview)
    {
        Point start = new(bounds.X + (bounds.Width * 0.08), bounds.Center.Y);
        Point end = new(bounds.Right - (bounds.Width * 0.08), bounds.Center.Y);
        context.DrawLine(new Pen(HeaderBrush, Math.Max(4, bounds.Height * 0.08), lineCap: PenLineCap.Round), start, end);

        double radius = Math.Min(bounds.Width / Math.Max(6, preview.LedCount * 2.4), bounds.Height * 0.16);
        foreach ((ChannelDeviceLedLayout led, Color ledColor) in preview.Layout.Leds.Zip(preview.LedColors))
        {
            Point center = Project(bounds, led);
            context.DrawEllipse(new SolidColorBrush(ledColor), DeviceOutlinePen, center, radius, radius);
        }
    }

    private static void DrawKeyboard(DrawingContext context, Rect bounds, CanvasPreviewChannelSnapshot preview)
    {
        int columns = Math.Max(1, preview.Layout.Columns);
        int rows = Math.Max(1, preview.Layout.Rows);
        double keyWidth = Math.Min(bounds.Width / (columns + 1.2), bounds.Width * 0.18);
        double keyHeight = Math.Min(bounds.Height / (rows + 1.3), bounds.Height * 0.2);
        double cornerRadius = Math.Min(keyWidth, keyHeight) * 0.2;

        foreach ((ChannelDeviceLedLayout led, Color ledColor) in preview.Layout.Leds.Zip(preview.LedColors))
        {
            Point center = Project(bounds, led);
            Rect keyRect = new(center.X - (keyWidth / 2.0), center.Y - (keyHeight / 2.0), keyWidth, keyHeight);
            context.DrawRectangle(new SolidColorBrush(ledColor), DeviceOutlinePen, keyRect, cornerRadius);
        }
    }

    private static void DrawPizza(DrawingContext context, Rect bounds, CanvasPreviewChannelSnapshot preview)
    {
        Point center = bounds.Center;
        double referenceSize = Math.Min(bounds.Width, bounds.Height);
        double outerRadius = referenceSize * preview.Layout.OuterRadius;
        double innerRadius = referenceSize * preview.Layout.InnerRadius;

        context.DrawEllipse(null, new Pen(HeaderBrush, Math.Max(2, bounds.Width * 0.02)), center, outerRadius + 4, outerRadius + 4);
        context.DrawEllipse(HeaderBrush, null, center, innerRadius * 0.55, innerRadius * 0.55);

        foreach ((ChannelDeviceLedLayout led, Color ledColor) in preview.Layout.Leds.Zip(preview.LedColors))
        {
            StreamGeometry geometry = CreatePizzaSliceGeometry(center, innerRadius, outerRadius, led.StartAngleDegrees, led.SweepAngleDegrees);
            context.DrawGeometry(new SolidColorBrush(ledColor), DeviceOutlinePen, geometry);
        }
    }

    private CanvasPreviewDeviceSnapshot? HitTestDevice(Point pointerPosition)
    {
        CanvasPreviewSceneSnapshot? scene = Scene;
        if (scene is null)
        {
            return null;
        }

        Rect bounds = Bounds.Deflate(6);
        return scene.Devices.LastOrDefault(device => Project(bounds, device.Bounds).Contains(pointerPosition));
    }

    private static Rect Project(Rect outerBounds, Rect normalizedRect)
    {
        return new Rect(
            outerBounds.X + (outerBounds.Width * normalizedRect.X),
            outerBounds.Y + (outerBounds.Height * normalizedRect.Y),
            outerBounds.Width * normalizedRect.Width,
            outerBounds.Height * normalizedRect.Height);
    }

    private static Point Project(Rect bounds, ChannelDeviceLedLayout led)
    {
        return new Point(
            bounds.X + (bounds.Width * led.X),
            bounds.Y + (bounds.Height * led.Y));
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        double radians = (angleDegrees * Math.PI) / 180.0;
        return new Point(
            center.X + (Math.Cos(radians) * radius),
            center.Y + (Math.Sin(radians) * radius));
    }

    private static StreamGeometry CreatePizzaSliceGeometry(Point center, double innerRadius, double outerRadius, double startAngleDegrees, double sweepAngleDegrees)
    {
        Point innerStart = PointOnCircle(center, innerRadius, startAngleDegrees);
        Point outerStart = PointOnCircle(center, outerRadius, startAngleDegrees);
        Point outerEnd = PointOnCircle(center, outerRadius, startAngleDegrees + sweepAngleDegrees);
        Point innerEnd = PointOnCircle(center, innerRadius, startAngleDegrees + sweepAngleDegrees);
        bool isLargeArc = sweepAngleDegrees > 180;

        StreamGeometry geometry = new();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(innerStart, true);
            path.LineTo(outerStart);
            path.ArcTo(outerEnd, new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise);
            path.LineTo(innerEnd);
            path.ArcTo(innerStart, new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.CounterClockwise);
            path.EndFigure(true);
        }

        return geometry;
    }

    private static void DrawLabel(DrawingContext context, string text, double fontSize, IBrush brush, Point origin)
    {
        FormattedText formattedText = new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            brush);
        context.DrawText(formattedText, origin);
    }
}