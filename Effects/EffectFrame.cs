namespace SmyruRGB.Effects;

internal sealed record EffectFrame(IReadOnlyList<IReadOnlyList<LedColor>> ChannelLeds, int DelayMilliseconds);