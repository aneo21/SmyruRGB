namespace SmyruRGB.Effects;

internal sealed class EffectLedLocation(
    int ledIndex,
    double x,
    double y,
    double axisPosition,
    double radius,
    double angleDegrees)
{
    public int LedIndex { get; } = ledIndex;

    public double X { get; } = x;

    public double Y { get; } = y;

    public double AxisPosition { get; } = axisPosition;

    public double Radius { get; } = radius;

    public double AngleDegrees { get; } = angleDegrees;
}