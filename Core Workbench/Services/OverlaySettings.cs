using System.IO;
using System.Text.Json;

namespace Core_Workbench.Services
{
    public enum OverlayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>Persisted configuration for the performance overlay (OSD).</summary>
    public sealed class OverlaySettings
    {
        public bool Enabled { get; set; }
        public bool ShowCpu { get; set; } = true;
        public bool ShowCpuTemp { get; set; } = true;
        public bool ShowCpuClock { get; set; } = true;
        public bool ShowCpuPower { get; set; } = true;
        public bool ShowCpuCores { get; set; }
        public bool ShowGpu { get; set; } = true;
        public bool ShowGpuTemp { get; set; } = true;
        public bool ShowGpuClock { get; set; } = true;
        public bool ShowGpuMemClock { get; set; }
        public bool ShowGpuVram { get; set; } = true;
        public bool ShowGpuPower { get; set; } = true;
        public bool ShowGpuFan { get; set; } = true;
        public bool ShowRam { get; set; } = true;
        public bool ShowNet { get; set; }
        public bool ShowClock { get; set; } = true;
        public bool ShowFps { get; set; }
        public bool ShowFrametime { get; set; }
        public bool ShowLows { get; set; }

        public OverlayCorner Corner { get; set; } = OverlayCorner.TopRight;
        public double Opacity { get; set; } = 0.9;
        public double FontScale { get; set; } = 1.0;
        public bool ClickThrough { get; set; } = true;

        public int Columns { get; set; } = 1;        // 1–3 side-by-side columns
        public double LineSpacing { get; set; } = 1;  // extra px between rows

        // Global hotkey to toggle the overlay. Default Ctrl+Alt+O.
        public bool HotkeyEnabled { get; set; } = true;
        public uint HotkeyMods { get; set; } = 3;     // MOD_ALT(1) | MOD_CONTROL(2)
        public uint HotkeyVk { get; set; } = 0x4F;    // 'O'

        // Customizable colors (hex). CPU/GPU/RAM/NET = label colors; Value = the numbers.
        public string CpuColor { get; set; } = "#8B6FE0";
        public string GpuColor { get; set; } = "#5BE3A0";
        public string RamColor { get; set; } = "#6FB1FF";
        public string NetColor { get; set; } = "#FFB454";
        public string ValueColor { get; set; } = "#FFFFFF";
        public string BackColor { get; set; } = "#000000";
        public double BackAlpha { get; set; } = 0.7;

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Core Workbench", "overlay.json");

        private static OverlaySettings? _current;
        public static OverlaySettings Current => _current ??= Load();

        private static OverlaySettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { }
            return new OverlaySettings();
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
