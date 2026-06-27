namespace SmyruRGB.Controllers.Hid.Asus;

internal static class AsusAuraFamilies
{
    public const int VendorId = 0x0B05;

    public static IReadOnlyList<int> AuraCoreProductIds { get; } = [0x1854, 0x1866, 0x1869];

    public static IReadOnlyList<int> AuraUsbMainboardProductIds { get; } = [0x18F3, 0x1939, 0x19AF, 0x1AA6, 0x1BED];

    public const string TufMainboardControllerFamily = "Aura USB motherboard/addressable";
}