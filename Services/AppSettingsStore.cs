using System.Text.Json;

namespace SmyruRGB;

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public AppSettingsStore()
    {
        string appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmyruRGB");

        _settingsFilePath = Path.Combine(appDirectory, "settings.json");
    }

    public PersistedSettings Load()
    {
        if (!File.Exists(_settingsFilePath))
        {
            return new PersistedSettings();
        }

        try
        {
            string json = File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<PersistedSettings>(json, JsonOptions) ?? new PersistedSettings();
        }
        catch
        {
            return new PersistedSettings();
        }
    }

    public void Save(PersistedSettings settings)
    {
        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    internal sealed class PersistedSettings
    {
        public UiSettings Ui { get; set; } = new();

        public List<DeviceSettings> Devices { get; set; } = [];
    }

    internal sealed class UiSettings
    {
        public bool MinimizeToTrayEnabled { get; set; }

        public bool CloseToTrayEnabled { get; set; }

        public bool StartMinimizedEnabled { get; set; }

        public bool StartWithWindowsEnabled { get; set; }
    }

    internal sealed class DeviceSettings
    {
        public string DeviceIdentifier { get; set; } = string.Empty;

        public string? CustomName { get; set; }

        public string? LastProfileName { get; set; }

        public List<ProfileSettings> Profiles { get; set; } = [];
    }

    internal sealed class ProfileSettings
    {
        public string Name { get; set; } = "Default";

        public string EffectName { get; set; } = "Static color";

        public int EffectSpeed { get; set; } = 50;

        public List<EffectParameterSetting> EffectParameters { get; set; } = [];

        public int Red { get; set; } = 255;

        public int Green { get; set; }

        public int Blue { get; set; }

        public bool BackgroundColorEnabled { get; set; }

        public int BackgroundRed { get; set; }

        public int BackgroundGreen { get; set; }

        public int BackgroundBlue { get; set; }

        public List<ChannelSettings> Channels { get; set; } = [];
    }

    internal sealed class ChannelSettings
    {
        public string DevicePresetId { get; set; } = ChannelDevicePresetCatalog.CustomPresetId;

        public string DeviceShapeId { get; set; } = ChannelDeviceShapeCatalog.BarShapeId;

        public string Name { get; set; } = string.Empty;

        public int LedCount { get; set; }

        public List<int> LedAddresses { get; set; } = [];
    }

    internal sealed class EffectParameterSetting
    {
        public string Key { get; set; } = string.Empty;

        public int Value { get; set; }
    }
}