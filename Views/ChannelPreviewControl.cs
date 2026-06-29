using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace SmyruRGB.Views;

public sealed class ChannelPreviewControl : Control
{
    public static readonly StyledProperty<CanvasPreviewChannelSnapshot?> PreviewChannelProperty =
        AvaloniaProperty.Register<ChannelPreviewControl, CanvasPreviewChannelSnapshot?>(nameof(PreviewChannel));

    private static readonly IBrush SurfaceBrush = new SolidColorBrush(Color.Parse("#111827"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#1F2937"));
    private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.Parse("#374151"));
    private static readonly Pen OutlinePen = new(OutlineBrush, 1);

    public CanvasPreviewChannelSnapshot? PreviewChannel
    {
        get => GetValue(PreviewChannelProperty);
        set => SetValue(PreviewChannelProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = Bounds.Deflate(6);
        context.DrawRectangle(SurfaceBrush, null, bounds, 12);

        CanvasPreviewChannelSnapshot? preview = PreviewChannel;
        if (preview is null || preview.LedColors.Count == 0)
        {
            return;
        }

        Rect shapeBounds = bounds.Deflate(14);
        switch (preview.Layout.ShapeKind)
        {
            case DeviceShapeKind.Pizza:
                DrawPizza(context, shapeBounds, preview);
                break;
            case DeviceShapeKind.Keyboard:
                DrawKeyboard(context, shapeBounds, preview);
                break;
            default:
                DrawBar(context, shapeBounds, preview);
                break;
        }
    }

    private static void DrawBar(DrawingContext context, Rect bounds, CanvasPreviewChannelSnapshot preview)
    {
        Point start = new(bounds.X + (bounds.Width * 0.08), bounds.Center.Y);
        Point end = new(bounds.Right - (bounds.Width * 0.08), bounds.Center.Y);
        context.DrawLine(new Pen(AccentBrush, Math.Max(6, bounds.Height * 0.08), lineCap: PenLineCap.Round), start, end);

        double radius = Math.Min(bounds.Width / Math.Max(6, preview.LedCount * 2.4), bounds.Height * 0.16);
        foreach ((ChannelDeviceLedLayout led, Color ledColor) in preview.Layout.Leds.Zip(preview.LedColors))
        {
            Point center = Project(bounds, led);
            context.DrawEllipse(ToBrush(ledColor), OutlinePen, center, radius, radius);
        }
    }

    private static void DrawKeyboard(DrawingContext context, Rect bounds, CanvasPreviewChannelSnapshot preview)
    {
        int columns = Math.Max(1, preview.Layout.Columns);
        int rows = Math.Max(1, preview.Layout.Rows);
        double keyWidth = Math.Min(bounds.Width / (columns + 1.4), bounds.Width * 0.18);
        double keyHeight = Math.Min(bounds.Height / (rows + 1.6), bounds.Height * 0.2);
        double cornerRadius = Math.Min(keyWidth, keyHeight) * 0.2;

        foreach ((ChannelDeviceLedLayout led, Color ledColor) in preview.Layout.Leds.Zip(preview.LedColors))
        {
            Point center = Project(bounds, led);
            Rect keyRect = new(center.X - (keyWidth / 2.0), center.Y - (keyHeight / 2.0), keyWidth, keyHeight);
            context.DrawRectangle(ToBrush(ledColor), OutlinePen, keyRect, cornerRadius);
        }
    }

    private static void DrawPizza(DrawingContext context, Rect bounds, CanvasPreviewChannelSnapshot preview)
    {
        Point center = bounds.Center;
        double referenceSize = Math.Min(bounds.Width, bounds.Height);
        double outerRadius = referenceSize * preview.Layout.OuterRadius;
        double innerRadius = referenceSize * preview.Layout.InnerRadius;

        context.DrawEllipse(null, new Pen(AccentBrush, Math.Max(2, bounds.Width * 0.03)), center, outerRadius + 6, outerRadius + 6);
        context.DrawEllipse(AccentBrush, null, center, innerRadius * 0.55, innerRadius * 0.55);

        foreach ((ChannelDeviceLedLayout led, Color ledColor) in preview.Layout.Leds.Zip(preview.LedColors))
        {
            StreamGeometry geometry = CreatePizzaSliceGeometry(center, innerRadius, outerRadius, led.StartAngleDegrees, led.SweepAngleDegrees);
            context.DrawGeometry(ToBrush(ledColor), OutlinePen, geometry);
        }
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

    private static IBrush ToBrush(Color color)
    {
        return new SolidColorBrush(color);
    }
}