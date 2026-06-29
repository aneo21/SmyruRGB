namespace SmyruRGB;

public enum DeviceShapeKind
{
    Bar,
    Pizza,
    Keyboard
}

public sealed class ChannelDeviceShapeOption
{
    public ChannelDeviceShapeOption(string id, string displayName, DeviceShapeKind shapeKind)
    {
        Id = id;
        DisplayName = displayName;
        ShapeKind = shapeKind;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public DeviceShapeKind ShapeKind { get; }
}

internal static class ChannelDeviceShapeCatalog
{
    public const string BarShapeId = "bar";
    public const string PizzaShapeId = "pizza";
    public const string KeyboardShapeId = "keyboard";

    private static readonly ChannelDeviceShapeOption Bar = new(BarShapeId, "Pasek", DeviceShapeKind.Bar);
    private static readonly ChannelDeviceShapeOption Pizza = new(PizzaShapeId, "Pizza", DeviceShapeKind.Pizza);
    private static readonly ChannelDeviceShapeOption Keyboard = new(KeyboardShapeId, "Klawiatura", DeviceShapeKind.Keyboard);

    private static readonly IReadOnlyList<ChannelDeviceShapeOption> Shapes =
    [
        Bar,
        Pizza,
        Keyboard
    ];

    public static IReadOnlyList<ChannelDeviceShapeOption> All => Shapes;

    public static ChannelDeviceShapeOption FindById(string? shapeId)
    {
        if (!string.IsNullOrWhiteSpace(shapeId))
        {
            ChannelDeviceShapeOption? shape = Shapes.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, shapeId, StringComparison.OrdinalIgnoreCase));

            if (shape is not null)
            {
                return shape;
            }
        }

        return Bar;
    }
}