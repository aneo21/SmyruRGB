namespace SmyruRGB.Effects;

internal readonly record struct LedColor(byte Red, byte Green, byte Blue)
{
    public static readonly LedColor Black = new(0, 0, 0);

    public LedColor Scale(double factor)
    {
        return new LedColor(
            (byte)Math.Clamp((int)Math.Round(Red * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(Green * factor), 0, 255),
            (byte)Math.Clamp((int)Math.Round(Blue * factor), 0, 255));
    }
}