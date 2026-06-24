namespace SmyruRGB.Effects;

internal interface ILedEffect
{
    string Name { get; }

    string Description { get; }

    bool IsAnimated { get; }

    bool UsesBaseColor { get; }

    bool SupportsBackgroundColor { get; }

    bool UsesSpeed { get; }

    IReadOnlyList<EffectParameterDefinition> Parameters { get; }

    EffectFrame CreateFrame(EffectContext context);
}