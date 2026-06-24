namespace SmyruRGB.Effects;

internal sealed class StaticColorEffect : ILedEffect
{
    public string Name => "Static color";

    public string Description => "Applies one steady color to every channel.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => true;

    public bool SupportsBackgroundColor => false;

    public bool UsesSpeed => false;

    public IReadOnlyList<EffectParameterDefinition> Parameters => [];

    public EffectFrame CreateFrame(EffectContext context)
    {
        return EffectFrameFactory.CreateSolidFrame(context.LedCountsPerChannel, context.BaseColor, 100);
    }
}