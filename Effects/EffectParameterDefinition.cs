namespace SmyruRGB.Effects;

internal sealed record EffectParameterDefinition(
    string Key,
    string Label,
    string Description,
    int Minimum,
    int Maximum,
    int DefaultValue,
    string Group = "Effect");