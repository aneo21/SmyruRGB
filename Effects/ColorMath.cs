namespace SmyruRGB.Effects;

internal static class ColorMath
{
    public static LedColor FromHsv(double hue, double saturation, double value)
    {
        double normalizedHue = hue % 360.0;
        if (normalizedHue < 0)
        {
            normalizedHue += 360.0;
        }

        double chroma = value * saturation;
        double hueSection = normalizedHue / 60.0;
        double secondary = chroma * (1 - Math.Abs((hueSection % 2) - 1));
        double match = value - chroma;

        double red;
        double green;
        double blue;

        if (hueSection >= 0 && hueSection < 1)
        {
            red = chroma;
            green = secondary;
            blue = 0;
        }
        else if (hueSection >= 1 && hueSection < 2)
        {
            red = secondary;
            green = chroma;
            blue = 0;
        }
        else if (hueSection >= 2 && hueSection < 3)
        {
            red = 0;
            green = chroma;
            blue = secondary;
        }
        else if (hueSection >= 3 && hueSection < 4)
        {
            red = 0;
            green = secondary;
            blue = chroma;
        }
        else if (hueSection >= 4 && hueSection < 5)
        {
            red = secondary;
            green = 0;
            blue = chroma;
        }
        else
        {
            red = chroma;
            green = 0;
            blue = secondary;
        }

        return new LedColor(
            (byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255),
            (byte)Math.Round((blue + match) * 255));
    }
}