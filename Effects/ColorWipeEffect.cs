namespace SmyruRGB.Effects;

internal sealed class ColorWipeEffect : ILedEffect
{
    private static readonly IReadOnlyList<EffectParameterDefinition> ParameterDefinitions =
    [
        new("tailLength", "Tail length", "How many LEDs stay lit in the moving wipe.", 1, 60, 12, "Motion")
    ];

    public string Name => "Color wipe";

    public string Description => "Moves a lit wipe across all LEDs channel by channel.";

    public bool IsAnimated => true;

    public bool UsesBaseColor => true;

    public bool SupportsBackgroundColor => true;

    public bool UsesSpeed => true;

    public IReadOnlyList<EffectParameterDefinition> Parameters => ParameterDefinitions;

    public EffectFrame CreateFrame(EffectContext context)
    {
        int totalLeds = context.LedCountsPerChannel.Sum();
        int tailLength = Math.Min(Math.Max(1, context.GetParameterValue("tailLength", 12)), Math.Max(1, totalLeds));
        int cursor = totalLeds == 0 ? 0 : (int)(context.Tick % (totalLeds + tailLength));

        return EffectFrameFactory.CreateMappedFrame(
            context.LedCountsPerChannel,
            (_, _, flatIndex) => flatIndex <= cursor && flatIndex > cursor - tailLength ? context.BaseColor : context.EffectiveBackgroundColor,
            Math.Max(25, 125 - context.Speed));
    }
}