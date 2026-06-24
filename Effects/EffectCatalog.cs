namespace SmyruRGB.Effects;

internal static class EffectCatalog
{
    private static readonly IReadOnlyList<ILedEffect> Effects =
    [
        new StaticColorEffect(),
        new BreathingEffect(),
        new FireEffect(),
        new MeteorRainEffect(),
        new RainbowEffect(),
        new StrobeEffect(),
        new TwinkleRainbowEffect(),
        new WaveEffect(),
        new ColorWipeEffect(),
        new SparkleEffect(),
        new ScannerEffect()
    ];

    public static IReadOnlyList<ILedEffect> All => Effects;

    public static string DefaultEffectName => Effects[0].Name;

    public static ILedEffect FindByName(string? effectName)
    {
        return Effects.FirstOrDefault(effect => string.Equals(effect.Name, effectName, StringComparison.OrdinalIgnoreCase))
            ?? Effects[0];
    }

    public static string NormalizeEffectName(string? effectName)
    {
        return FindByName(effectName).Name;
    }
}