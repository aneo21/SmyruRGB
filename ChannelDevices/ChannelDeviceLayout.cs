namespace SmyruRGB;

public sealed class ChannelDeviceLedLayout
{
    public ChannelDeviceLedLayout(int ledIndex, double x, double y, double startAngleDegrees = 0, double sweepAngleDegrees = 0)
    {
        LedIndex = ledIndex;
        X = x;
        Y = y;
        StartAngleDegrees = startAngleDegrees;
        SweepAngleDegrees = sweepAngleDegrees;
    }

    public int LedIndex { get; }

    public double X { get; }

    public double Y { get; }

    public double StartAngleDegrees { get; }

    public double SweepAngleDegrees { get; }
}

public sealed class ChannelDeviceLayout
{
    public ChannelDeviceLayout(
        DeviceShapeKind shapeKind,
        IReadOnlyList<ChannelDeviceLedLayout> leds,
        int columns = 0,
        int rows = 0,
        double innerRadius = 0,
        double outerRadius = 0.45)
    {
        ShapeKind = shapeKind;
        Leds = leds;
        Columns = columns;
        Rows = rows;
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
    }

    public DeviceShapeKind ShapeKind { get; }

    public IReadOnlyList<ChannelDeviceLedLayout> Leds { get; }

    public int Columns { get; }

    public int Rows { get; }

    public double InnerRadius { get; }

    public double OuterRadius { get; }
}

internal static class ChannelDeviceLayoutFactory
{
    public static ChannelDeviceLayout Create(DeviceShapeKind shapeKind, int ledCount)
    {
        int normalizedLedCount = Math.Max(1, ledCount);

        return shapeKind switch
        {
            DeviceShapeKind.Pizza => CreatePizzaLayout(normalizedLedCount),
            DeviceShapeKind.Keyboard => CreateKeyboardLayout(normalizedLedCount),
            _ => CreateBarLayout(normalizedLedCount)
        };
    }

    private static ChannelDeviceLayout CreateBarLayout(int ledCount)
    {
        List<ChannelDeviceLedLayout> leds = new(ledCount);
        double spacing = ledCount == 1 ? 0 : 0.8 / (ledCount - 1);

        for (int index = 0; index < ledCount; index++)
        {
            double x = ledCount == 1 ? 0.5 : 0.1 + (spacing * index);
            leds.Add(new ChannelDeviceLedLayout(index, x, 0.5));
        }

        return new ChannelDeviceLayout(DeviceShapeKind.Bar, leds);
    }

    private static ChannelDeviceLayout CreatePizzaLayout(int ledCount)
    {
        List<ChannelDeviceLedLayout> leds = new(ledCount);
        double sweepAngle = 360.0 / ledCount;
        double angleOffset = -90.0;
        double centerRadius = 0.34;

        for (int index = 0; index < ledCount; index++)
        {
            double startAngle = angleOffset + (index * sweepAngle);
            double centerAngleRadians = ((startAngle + (sweepAngle / 2.0)) * Math.PI) / 180.0;
            double x = 0.5 + (Math.Cos(centerAngleRadians) * centerRadius);
            double y = 0.5 + (Math.Sin(centerAngleRadians) * centerRadius);
            leds.Add(new ChannelDeviceLedLayout(index, x, y, startAngle, sweepAngle));
        }

        return new ChannelDeviceLayout(DeviceShapeKind.Pizza, leds, innerRadius: 0.16, outerRadius: 0.48);
    }

    private static ChannelDeviceLayout CreateKeyboardLayout(int ledCount)
    {
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(ledCount)));
        int rows = Math.Max(1, (int)Math.Ceiling((double)ledCount / columns));
        List<ChannelDeviceLedLayout> leds = new(ledCount);

        for (int index = 0; index < ledCount; index++)
        {
            int column = index % columns;
            int row = index / columns;
            double x = columns == 1 ? 0.5 : 0.1 + ((0.8 / (columns - 1)) * column);
            double y = rows == 1 ? 0.5 : 0.15 + ((0.7 / (rows - 1)) * row);
            leds.Add(new ChannelDeviceLedLayout(index, x, y));
        }

        return new ChannelDeviceLayout(DeviceShapeKind.Keyboard, leds, columns, rows);
    }
}