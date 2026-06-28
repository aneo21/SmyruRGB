using SmyruRGB.Controllers.Hid.Asus;
using SmyruRGB.Controllers.Hid.Nollie;
using SmyruRGB.Controllers.Hid.Roccat;

namespace SmyruRGB.Controllers.Hid;

internal static class HidRgbControllerRegistry
{
    public static IReadOnlyList<IHidRgbControllerDriver> All { get; } =
    [
        new AsusAuraUsbMainboardDriver(),
        new NollieControllerDriver(),
        new RoccatVulcanAimoKeyboardDriver()
    ];
}