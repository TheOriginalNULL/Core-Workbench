using System.ComponentModel;
using System.Windows.Media;

namespace Core_Workbench.Models
{
    // Ordered worst → best (after Unknown). Bands by health %:
    // 0–10 Failing · 10–30 Bad · 30–60 Fair · 60–90 Good · 90–100 Great.
    public enum DriveStatus { Unknown, Failing, Bad, Fair, Good, Great }

    /// <summary>
    /// Immutable snapshot of one drive's S.M.A.R.T. / NVMe health, as gathered by
    /// the DriveHealthService. Any field may be null when the drive doesn't expose it.
    /// </summary>
    public sealed class DriveHealthData
    {
        public int Index { get; init; } = -1;
        public string Model { get; init; } = "Unknown drive";
        public string Serial { get; init; } = "";
        public string Firmware { get; init; } = "";
        public string Interface { get; init; } = "—";   // NVMe / SATA / USB …
        public string MediaType { get; init; } = "—";    // SSD / HDD
        public long SizeBytes { get; init; }

        public double? TempC { get; init; }
        public double? HealthPercent { get; init; }
        public double? RemainingLifePercent { get; init; }
        public long? PowerOnHours { get; init; }
        public long? PowerCycles { get; init; }
        public double? HostReadsGb { get; init; }
        public double? HostWritesGb { get; init; }
        public long? ReallocatedSectors { get; init; }
        public long? MediaErrors { get; init; }
        public string? CriticalWarning { get; init; }
        public string HealthStatusText { get; init; } = "Unknown";

        public DriveStatus Status { get; init; } = DriveStatus.Unknown;
    }

    /// <summary>
    /// Bindable view-model for a drive card. Reused across refreshes (updated in
    /// place) so the cards don't rebuild and flicker.
    /// </summary>
    public sealed class DriveHealthVM : INotifyPropertyChanged
    {
        public int Index { get; private set; } = -1;

        private DriveHealthData _d = new();

        public void Update(DriveHealthData d)
        {
            _d = d;
            Index = d.Index;
            foreach (var p in _props) Raise(p);
        }

        // ---- identity ----
        public string Model => _d.Model;
        public string Serial => Show(_d.Serial);
        public string Firmware => Show(_d.Firmware);
        public string Interface => _d.Interface;
        public string MediaType => _d.MediaType;
        public string Capacity => _d.SizeBytes > 0 ? FormatBytesDecimal(_d.SizeBytes) : "—";

        // ---- health ----
        public double HealthValue => _d.HealthPercent ?? 0;
        public string HealthText => _d.HealthPercent.HasValue ? $"{_d.HealthPercent:0}%" : "—";
        public string StatusText => _d.Status switch
        {
            DriveStatus.Failing => "Critical",
            DriveStatus.Bad => "Bad",
            DriveStatus.Fair => "Fair",
            DriveStatus.Good => "Good",
            DriveStatus.Great => "Great",
            _ => "Unknown",
        };

        public Brush StatusBrush => new SolidColorBrush(_d.Status switch
        {
            DriveStatus.Failing => Color.FromRgb(0xFF, 0x6B, 0x6B),  // red
            DriveStatus.Bad => Color.FromRgb(0xFF, 0x8A, 0x4D),      // orange
            DriveStatus.Fair => Color.FromRgb(0xFF, 0xB4, 0x54),     // amber
            DriveStatus.Good => Color.FromRgb(0x8B, 0xD4, 0x6B),     // yellow-green
            DriveStatus.Great => Color.FromRgb(0x5B, 0xE3, 0xA0),    // green
            _ => Color.FromRgb(0x9A, 0x8F, 0xB8),                    // muted
        });

        // ---- temperature ----
        public string TempText => _d.TempC.HasValue ? $"{_d.TempC:0} °C" : "—";
        public Brush TempBrush => new SolidColorBrush(
            !_d.TempC.HasValue ? Color.FromRgb(0x9A, 0x8F, 0xB8)
            : _d.TempC >= 70 ? Color.FromRgb(0xFF, 0x6B, 0x6B)
            : _d.TempC >= 60 ? Color.FromRgb(0xFF, 0xB4, 0x54)
            : Color.FromRgb(0x5B, 0xE3, 0xA0));

        // ---- detail fields ----
        public string RemainingLife => _d.RemainingLifePercent.HasValue ? $"{_d.RemainingLifePercent:0}%" : "—";
        public string PowerOnHours => _d.PowerOnHours.HasValue ? $"{_d.PowerOnHours:N0} h" : "—";
        public string PowerCycles => _d.PowerCycles.HasValue ? $"{_d.PowerCycles:N0}" : "—";
        public string HostReads => _d.HostReadsGb.HasValue ? FormatGb(_d.HostReadsGb.Value) : "—";
        public string HostWrites => _d.HostWritesGb.HasValue ? FormatGb(_d.HostWritesGb.Value) : "—";
        public string ReallocatedSectors => _d.ReallocatedSectors.HasValue ? $"{_d.ReallocatedSectors:N0}" : "—";
        public string MediaErrors => _d.MediaErrors.HasValue ? $"{_d.MediaErrors:N0}" : "—";
        public string CriticalWarning => string.IsNullOrEmpty(_d.CriticalWarning) ? "None" : _d.CriticalWarning!;

        // ---- formatting helpers ----
        private static string Show(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

        private static string FormatGb(double gb)
            => gb >= 1024 ? $"{gb / 1024:0.00} TB" : $"{gb:0.0} GB";

        public static string FormatBytes(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double s = bytes; int i = 0;
            while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
            return $"{s:0.#} {u[i]}";
        }

        // Decimal (1000-based) — matches advertised capacity and CrystalDiskInfo.
        public static string FormatBytesDecimal(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double s = bytes; int i = 0;
            while (s >= 1000 && i < u.Length - 1) { s /= 1000; i++; }
            return $"{s:0.#} {u[i]}";
        }

        private static readonly string[] _props =
        {
            nameof(Model), nameof(Serial), nameof(Firmware), nameof(Interface), nameof(MediaType),
            nameof(Capacity), nameof(HealthValue), nameof(HealthText), nameof(StatusText),
            nameof(StatusBrush), nameof(TempText), nameof(TempBrush), nameof(RemainingLife),
            nameof(PowerOnHours), nameof(PowerCycles), nameof(HostReads), nameof(HostWrites),
            nameof(ReallocatedSectors), nameof(MediaErrors), nameof(CriticalWarning),
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
