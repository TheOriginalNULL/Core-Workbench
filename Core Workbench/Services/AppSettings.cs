using System.IO;
using System.Text.Json;

namespace Core_Workbench.Services
{
    /// <summary>App-wide preferences, persisted to %AppData%\Core Workbench\appsettings.json.</summary>
    public sealed class AppSettings
    {
        public bool MinimizeToTray { get; set; } = false;
        public bool CloseToTray { get; set; } = false;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Core Workbench", "appsettings.json");

        private static AppSettings? _current;
        public static AppSettings Current => _current ??= Load();

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            }
            catch { }
        }
    }
}
