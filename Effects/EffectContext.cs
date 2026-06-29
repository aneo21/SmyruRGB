namespace SmyruRGB.Effects;

internal sealed class EffectContext(
    LedColor baseColor,
    bool backgroundColorEnabled,
    LedColor backgroundColor,
    int speed,
    IReadOnlyList<int> ledCountsPerChannel,
    IReadOnlyList<IReadOnlyList<EffectLedLocation>> ledLocationsPerChannel,
    IReadOnlyList<string> channelNames,
    int channelCount,
    long tick,
    IReadOnlyDictionary<string, int> parameterValues)
{
    public LedColor BaseColor { get; } = baseColor;

    public bool BackgroundColorEnabled { get; } = backgroundColorEnabled;

    public LedColor BackgroundColor { get; } = backgroundColor;

    public LedColor EffectiveBackgroundColor => BackgroundColorEnabled ? BackgroundColor : LedColor.Black;

    public int Speed { get; } = speed;

    public IReadOnlyList<int> LedCountsPerChannel { get; } = ledCountsPerChannel;

    public IReadOnlyList<IReadOnlyList<EffectLedLocation>> LedLocationsPerChannel { get; } = ledLocationsPerChannel;

    public IReadOnlyList<string> ChannelNames { get; } = channelNames;

    public int ChannelCount { get; } = channelCount;

    public long Tick { get; } = tick;

    public IReadOnlyDictionary<string, int> ParameterValues { get; } = parameterValues;

    public EffectLedLocation GetLedLocation(int channelIndex, int ledIndex)
    {
        if (channelIndex < LedLocationsPerChannel.Count)
        {
            IReadOnlyList<EffectLedLocation> channel = LedLocationsPerChannel[channelIndex];
            if (ledIndex < channel.Count)
            {
                return channel[ledIndex];
            }
        }

        return new EffectLedLocation(ledIndex, 0.5, 0.5, ledIndex, 0, 0);
    }

    public int GetParameterValue(string key, int defaultValue)
    {
        return ParameterValues.TryGetValue(key, out int value) ? value : defaultValue;
    }
}